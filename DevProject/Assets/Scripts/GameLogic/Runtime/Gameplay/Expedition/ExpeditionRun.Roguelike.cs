using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private RogueChoiceSession rogueChoices;
        private RogueProgressSession rogueProgress;
        private BossLootChoiceSession bossChoices;
        private PersistentRandom bossRewardRandom;
        private IReadOnlyList<CfgRogueRewardsRow> configuredSkillPool;
        private IRogueChoiceAccessProvider rogueAccessProvider;
        private readonly CancellationTokenSource rogueLifetime = new();
        private readonly Queue<int> pendingBossRegions = new();
        private int normalProgressContribution;
        private int eliteProgressContribution;
        private long activeRogueQualification;
        private bool requestingRogueAccess;
        private bool rewardOffersVisible;

        public event Action RogueChoiceChanged;
        public event Action BossChoiceChanged;

        /// <summary>
        /// 只展示持久确认过的技能候选。
        /// </summary>
        public RogueChoiceSnapshot PendingRogueChoice => rewardOffersVisible && !IsSavingRun ? rogueChoices.Active : null;

        /// <summary>
        /// 只展示持久确认过的 Boss 完整物品候选。
        /// </summary>
        public BossLootChoiceSnapshot PendingBossChoice => rewardOffersVisible && !IsSavingRun ? bossChoices.Active : null;

        /// <summary>
        /// 技能会话是否存在未完成选择，兼容技能窗口的独立门禁。
        /// </summary>
        public bool IsChoosingRogue => rogueChoices.HasPending;

        /// <summary>
        /// Boss 批次尚有未领取轮次，包含正在保存下一轮候选的阶段。
        /// </summary>
        public bool IsChoosingBoss => bossChoices.HasPending;

        /// <summary>
        /// 待领资格、Boss 批次及候选统一冻结战斗和局内操作。
        /// </summary>
        public bool IsChoosingReward => rogueChoices.HasPending || bossChoices.HasPending ||
            rogueProgress.PendingCount > 0 || pendingBossRegions.Count > 0;

        /// <summary>
        /// 尚未兑换成资格的技能进度余量。
        /// </summary>
        public int CurrentRogueProgress => rogueProgress.RemainingProgress;

        /// <summary>
        /// 下一份技能资格需要的配置阈值。
        /// </summary>
        public int NextRogueThreshold => rogueProgress.Threshold;

        /// <summary>
        /// 构筑已经耗尽，不再累计技能进度。
        /// </summary>
        public bool IsRogueBuildComplete => rogueProgress.IsExhausted;

        /// <summary>
        /// 尚未兑现的技能资格数，包含当前正在选择的资格。
        /// </summary>
        public int PendingRogueQualifications => rogueProgress.PendingCount;

        public string RogueMessage { get; private set; } = string.Empty;
        private bool CanAcceptRewardInput => IsRunning && IsReady && !paused && ActivePanel == ExpeditionPanelKind.None &&
            !disposed && !stopped && !IsSavingRun && !GameEntry.IsInBackground && !session.HasUncommittedSave &&
            World.TryGetActor(Player, out CombatActorSnapshot player) && !player.Dead;

        /// <summary>
        /// 是否可以请求解锁本轮已冻结的第四选项。
        /// </summary>
        public bool CanRequestExtraRogueChoice => CanAcceptRewardInput && !requestingRogueAccess && rogueAccessProvider != null &&
            PendingRogueChoice is RogueChoiceSnapshot offer && offer.Options.Count > 3 && !offer.ExtraUnlocked;

        /// <summary>
        /// 从关卡引用配置创建整关共用的技能进度与独立 Boss 随机流。
        /// </summary>
        /// <param name="seed">整关初始随机种子。</param>
        /// <param name="skillPoolId">关卡技能池身份。</param>
        /// <param name="progressRuleId">关卡加权进度规则。</param>
        /// <param name="policy">可选技能功能策略。</param>
        /// <param name="access">可选第四选项授权适配器。</param>
        private void InitializeRewardSessions(int seed, string skillPoolId, int progressRuleId,
            RogueChoicePolicy policy, IRogueChoiceAccessProvider access)
        {
            configuredSkillPool = ProjectV2RewardConfig.SkillPool(skillPoolId, out _);
            CfgRogueProgressRow rule = ProjectV2RewardConfig.Progress(progressRuleId);
            normalProgressContribution = rule.NormalContribution;
            eliteProgressContribution = rule.EliteContribution;
            rogueChoices = new RogueChoiceSession(RunId, seed ^ 0x4E71, configuredSkillPool, policy);
            rogueProgress = new RogueProgressSession(rule.Threshold);
            bossChoices = new BossLootChoiceSession(RunId);
            bossRewardRandom = new PersistentRandom(seed ^ 0x47A3);
            rogueAccessProvider = access;
        }

        /// <summary>
        /// 累计已经由区域生命身份确认的普通或精英死亡贡献，Boss 不进入技能进度。
        /// </summary>
        /// <param name="kind">已确认死亡的敌人职责。</param>
        /// <returns>本次实际接纳的贡献；Boss、构筑耗尽或提交失败时为零。</returns>
        private int RecordProgressDeath(RegionEnemyKind kind)
        {
            if (kind == RegionEnemyKind.Boss || rogueProgress.IsExhausted) return 0;
            int contribution = kind == RegionEnemyKind.Elite ? eliteProgressContribution : normalProgressContribution;
            if (rogueProgress.TryAddProgress(contribution)) return contribution;
            HandleFault("技能进度提交失败，已冻结本关。");
            return 0;
        }

        /// <summary>
        /// 登记已击败区域的唯一 Boss 奖励批次，不提前领取或解锁出口。
        /// </summary>
        /// <param name="regionId">已经确认 Boss 死亡的区域身份。</param>
        private void QueueBossReward(int regionId)
        {
            if (!pendingBossRegions.Contains(regionId)) pendingBossRegions.Enqueue(regionId);
        }

        /// <summary>
        /// 按技能资格先于 Boss 的顺序冻结下一次候选，并在确认保存后开放窗口。
        /// </summary>
        /// <returns>奖励或保存门禁已接管当前帧时为真。</returns>
        private bool TryBeginPendingReward()
        {
            if (IsSavingRun) return true;
            if (!CanAcceptRewardInput) return IsChoosingReward;
            if (rogueChoices.HasPending) return true;
            if (!rogueProgress.IsExhausted && rogueChoices.IsExhausted)
            {
                rogueProgress.CloseExhausted();
                activeRogueQualification = 0;
                SaveRewardMutation("技能构筑已完成，后续击杀不再累计强化进度。");
                return true;
            }
            if (rogueProgress.TryPeek(out long qualification))
            {
                if (!BeginRogueQualification(qualification)) HandleFault("技能候选创建失败，已保留待领资格。");
                return true;
            }
            if (bossChoices.HasPending && !bossChoices.NeedsCandidates) return true;
            if (pendingBossRegions.Count == 0) return false;
            int regionId = pendingBossRegions.Peek();
            RegionGenerationRequest request = regionRequests.Find(value => value.RegionId == regionId);
            CfgBossRewardsRow rule = BossRule(request);
            string batchId = BossBatchId(regionId);
            BossLootChoiceState state = bossChoices.CaptureState();
            int round = bossChoices.NeedsCandidates ? state.Batches[state.Batches.Count - 1].Rounds.Count + 1 : 1;
            var claimed = new List<string>();
            foreach (ProjectItem item in bossChoices.ClaimedItems(batchId))
                if (item.Type == ProjectItemType.Collection) claimed.Add(item.CollectionId);
            IReadOnlyList<ProjectItem> candidates = ProjectV2RewardConfig.CreateBossCandidates(bossRewardRandom, LootLevel,
                rule.ID, batchId, round, claimed, out string error);
            bool created = candidates != null && (round == 1 ? bossChoices.TryBegin(batchId, rule.Rounds, candidates) :
                bossChoices.TryBeginNextRound(candidates));
            if (!created)
            {
                HandleFault(error ?? "Boss 候选批次提交失败，已保留待领状态。");
                return true;
            }
            SaveRewardMutation("请选择一件完整的 Boss 战利品（第 " + round + "/" + rule.Rounds + " 轮）。");
            return true;
        }

        /// <summary>
        /// 只允许当前队首资格的稳定来源创建技能候选。
        /// </summary>
        /// <param name="sourceId">与当前技能资格对应的稳定来源。</param>
        /// <returns>首次冻结该资格并进入保存时为真。</returns>
        public bool TryBeginRogueChoice(string sourceId)
        {
            return CanAcceptRewardInput && !rogueChoices.HasPending && !bossChoices.HasPending &&
                rogueProgress.TryPeek(out long qualification) && sourceId == RogueSourceId(qualification) && BeginRogueQualification(qualification);
        }

        /// <summary>
        /// 为队首资格冻结技能选项，不提前消耗进度资格。
        /// </summary>
        /// <param name="qualification">队首技能资格身份。</param>
        /// <returns>技能候选已创建并开始持久提交时为真。</returns>
        private bool BeginRogueQualification(long qualification)
        {
            if (!rogueChoices.TryBegin(RogueSourceId(qualification))) return false;
            activeRogueQualification = qualification;
            SaveRewardMutation("战斗已暂停，请选择一项技能强化。");
            return true;
        }

        /// <summary>
        /// 同次提交技能效果、已选等级、资格消费和实际选择次数。
        /// </summary>
        /// <param name="offerId">当前候选批次身份。</param>
        /// <param name="revision">当前候选代次。</param>
        /// <param name="index">选中的候选序号。</param>
        /// <returns>选择首次生效并进入持久提交时为真。</returns>
        public bool TryChooseRogue(string offerId, int revision, int index)
        {
            if (!CanAcceptRewardInput || !rewardOffersVisible || requestingRogueAccess || statistics.RogueChoices == int.MaxValue ||
                !rogueProgress.TryPeek(out long qualification) || qualification != activeRogueQualification ||
                !rogueChoices.TrySelect(offerId, revision, index, ApplyRogueReward)) return false;
            if (!rogueProgress.TryComplete(qualification))
            {
                HandleFault("技能已生效但资格未能确认，已冻结本关。");
                return false;
            }
            activeRogueQualification = 0;
            statistics.RogueChoices++;
            if (rogueChoices.IsExhausted) rogueProgress.CloseExhausted();
            SaveRewardMutation(rogueProgress.IsExhausted ? "技能构筑已完成，继续探索。" : "已获得技能强化。");
            return true;
        }

        /// <summary>
        /// 领取冻结的完整物品，同次保存本轮领取结果及最终出口解锁。
        /// </summary>
        /// <param name="offerId">当前 Boss 候选身份。</param>
        /// <param name="revision">候选代次。</param>
        /// <param name="index">选中的完整物品序号。</param>
        /// <returns>本轮首次领取并开始持久提交时为真。</returns>
        public bool TryChooseBoss(string offerId, int revision, int index)
        {
            if (!CanAcceptRewardInput || !rewardOffersVisible || pendingBossRegions.Count == 0 ||
                statistics.BossLootRounds == int.MaxValue ||
                pendingBossRegions.Peek() != CurrentRegion.Plan.Request.RegionId || !CurrentRegion.BossDefeated ||
                CurrentRegion.BossRewardsCompleted || !bossChoices.TrySelect(offerId, revision, index, ApplyBossLoot)) return false;
            statistics.BossLootRounds++;
            bool complete = bossChoices.IsComplete;
            if (complete)
            {
                if (!CurrentRegion.TryCompleteBossRewards())
                {
                    HandleFault("Boss 奖励已领取但出口状态未能确认，已冻结本关。");
                    return false;
                }
                pendingBossRegions.Dequeue();
            }
            SaveRewardMutation(complete ? "Boss 奖励已全部领取，区域出口已开启。" : "本轮战利品已领取。", complete && IsFinalRegion);
            return true;
        }

        /// <summary>
        /// 添加来自已冻结候选的独立物品，重复实例身份不会再次入包。
        /// </summary>
        /// <param name="item">已校验且复制的候选物品。</param>
        /// <returns>物品首次进入本局背包时为真。</returns>
        private bool ApplyBossLoot(ProjectItem item)
        {
            if (inventory.Exists(existing => existing.Id == item.Id)) return false;
            inventory.Add(item);
            Message = "获得 Boss 战利品：" + item.Name;
            return true;
        }

        /// <summary>
        /// 在允许刷新时创建下一代冻结候选，保存确认后替换窗口。
        /// </summary>
        /// <param name="offerId">当前候选身份。</param>
        /// <param name="revision">按钮绑定的候选代次。</param>
        /// <returns>刷新首次生效并开始保存时为真。</returns>
        public bool TryRefreshRogue(string offerId, int revision)
        {
            if (!CanAcceptRewardInput || !rewardOffersVisible || requestingRogueAccess || !rogueChoices.TryRefresh(offerId, revision)) return false;
            SaveRewardMutation("技能候选已刷新，请选择一项。");
            return true;
        }

        /// <summary>
        /// 先保存唯一授权请求，再调用外部适配器，并保存解锁结果后返回。
        /// </summary>
        /// <param name="offerId">当前候选身份。</param>
        /// <param name="revision">当前候选代次。</param>
        /// <returns>本轮第四项获得授权且保存确认时为真。</returns>
        public async UniTask<bool> RequestExtraRogueChoiceAsync(string offerId, int revision)
        {
            if (!CanRequestExtraRogueChoice || !rogueChoices.TryCreateExtraAccessRequest(offerId, revision, out var request)) return false;
            requestingRogueAccess = true;
            try
            {
                if (!await CommitRewardMutationAsync("正在确认额外选项资格。")) return false;
                RogueChoiceAccessReceipt receipt = await rogueAccessProvider.RequestAsync(request, rogueLifetime.Token)
                    .AttachExternalCancellation(rogueLifetime.Token);
                if (disposed || stopped || !IsRunning) return false;
                bool granted = rogueChoices.TryGrantExtraAccess(request, receipt);
                bool saved = await CommitRewardMutationAsync(granted ? "第四选项已解锁，请选择一项。" : "未获得授权，可继续选择基础强化。");
                return granted && saved;
            }
            catch (OperationCanceledException) when (rogueLifetime.IsCancellationRequested)
            {
                return false;
            }
            catch (Exception error)
            {
                if (!disposed && !stopped && IsRunning)
                {
                    rogueChoices.TryGrantExtraAccess(request, null);
                    GameEntry.Get<ILogModule>()?.Error("Roguelike", "额外选项授权失败：" + error.Message);
                    SaveRewardMutation("授权未完成，可重试或选择基础强化。");
                }
                return false;
            }
            finally
            {
                requestingRogueAccess = false;
                if (!disposed && !stopped) PublishRewardState();
            }
        }

        /// <summary>
        /// 将一次技能选择应用到现有战斗事实中，不生成战利品。
        /// </summary>
        /// <param name="reward">已经冻结的技能奖励。</param>
        /// <returns>整个技能效果成功应用时为真。</returns>
        private bool ApplyRogueReward(RogueRewardDefinition reward)
        {
            if (reward.Kind == RogueRewardKind.AttributeBonus)
            {
                if (!TryMapRogueAttribute(reward.Attribute, out CombatAttributeKind attribute) ||
                    !World.TryAddAttributeBonus(Player, attribute, reward.Amount)) return false;
                Message = "本局强化：" + reward.Title;
                return true;
            }
            return reward.Kind == RogueRewardKind.CombatAugment && ApplyCombatAugmentReward(reward);
        }

        /// <summary>
        /// 同次冻结奖励相关完整事实，持久确认前不暴露新候选。
        /// </summary>
        /// <param name="message">保存后显示的奖励反馈。</param>
        /// <param name="finishRun">是否在本次确认后结束最终区域。</param>
        private void SaveRewardMutation(string message, bool finishRun = false)
        {
            rewardOffersVisible = false;
            ClearPlayerInput();
            RogueMessage = message;
            SaveMutation(message, () =>
            {
                if (disposed || stopped || !IsRunning) return;
                if (finishRun) EndRun(true);
                else PublishRewardState();
            });
        }

        /// <summary>
        /// 等待同一次奖励事务的持久确认，供外部授权流程串联。
        /// </summary>
        /// <param name="message">授权提交过程的反馈。</param>
        /// <returns>确认时该单局仍在运行则为真。</returns>
        private UniTask<bool> CommitRewardMutationAsync(string message)
        {
            var confirmed = new UniTaskCompletionSource<bool>();
            rewardOffersVisible = false;
            ClearPlayerInput();
            RogueMessage = message;
            SaveMutation(message, () =>
            {
                bool active = !disposed && !stopped && IsRunning;
                if (active) PublishRewardState();
                confirmed.TrySetResult(active);
            });
            return confirmed.Task.AttachExternalCancellation(rogueLifetime.Token);
        }

        /// <summary>
        /// 恢复已确认候选可见性，并在资格尚无候选时进入下一次完整保存。
        /// </summary>
        private void PublishRewardState()
        {
            rewardOffersVisible = IsRunning && !IsSavingRun && !IsAwaitingDeathChoice && !disposed && !stopped;
            if (rewardOffersVisible && !paused && ActivePanel == ExpeditionPanelKind.None &&
                ProjectServices.Coordinator.Phase == ProjectRunPhase.Playing)
            {
                if (IsFinalRegion && CurrentRegion.BossRewardsCompleted && !IsChoosingReward)
                {
                    EndRun(true);
                    return;
                }
                if (!rogueChoices.HasPending && (!bossChoices.HasPending || bossChoices.NeedsCandidates) && TryBeginPendingReward()) return;
            }
            RogueChoiceChanged?.Invoke();
            BossChoiceChanged?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>
        /// 在协调器进入正式探索阶段后续接已持久确认的奖励或最终通关。
        /// </summary>
        internal void ResumePendingRewards() => PublishRewardState();

        /// <summary>
        /// 将奖励会话和随机游标纳入同一运行快照。
        /// </summary>
        /// <param name="snapshot">由统一入口创建的本次完整快照。</param>
        private void CaptureRewardState(ExpeditionRunSnapshot snapshot)
        {
            snapshot.Rogue = rogueChoices.CaptureState();
            snapshot.Progress = rogueProgress.CaptureState();
            snapshot.BossLoot = bossChoices.CaptureState();
            snapshot.BossRandomState = bossRewardRandom.State;
            snapshot.ActiveRogueQualification = activeRogueQualification;
            snapshot.PendingBossRegions = new List<int>(pendingBossRegions);
        }

        /// <summary>
        /// 先验证独立会话与跨会话归属，再原子替换奖励运行态，不重放战斗或入包效果。
        /// </summary>
        /// <param name="snapshot">已从完整持久修订恢复的运行快照。</param>
        /// <param name="error">奖励状态错误原因。</param>
        /// <returns>奖励资格、Boss 批次与随机游标全部合法时为真。</returns>
        private bool RestoreRewardState(ExpeditionRunSnapshot snapshot, out string error)
        {
            error = "单局奖励状态不完整。";
            if (snapshot.Rogue == null || snapshot.Progress == null || snapshot.BossLoot == null || snapshot.PendingBossRegions == null ||
                !PersistentRandom.TryRestore(snapshot.BossRandomState, out PersistentRandom restoredRandom)) return false;
            var restoredRogue = new RogueChoiceSession(RunId, Seed, configuredSkillPool, rogueChoices.CaptureState().Policy);
            var restoredProgress = new RogueProgressSession(rogueProgress.Threshold);
            var restoredBoss = new BossLootChoiceSession(RunId);
            if (!restoredRogue.TryRestore(snapshot.Rogue, out error) || !restoredProgress.TryRestore(snapshot.Progress, out error) ||
                !restoredBoss.TryRestore(snapshot.BossLoot, out error)) return false;
            error = "技能资格或 Boss 领取归属不一致。";
            bool pendingSkill = restoredProgress.TryPeek(out long qualification);
            if (restoredRogue.HasPending ? !pendingSkill || snapshot.ActiveRogueQualification != qualification ||
                restoredRogue.Active.SourceId != RogueSourceId(qualification) : snapshot.ActiveRogueQualification != 0) return false;
            if (snapshot.Rogue.UsedSources.Count != restoredProgress.SelectedChoices + (restoredRogue.HasPending ? 1 : 0) ||
                restoredProgress.IsExhausted != restoredRogue.IsExhausted ||
                snapshot.Statistics == null || snapshot.Statistics.RogueChoices != restoredProgress.SelectedChoices) return false;
            long selectedRanks = 0;
            foreach (RogueRewardState reward in snapshot.Rogue.SelectedRewards) selectedRanks += reward.Rank;
            if (selectedRanks != restoredProgress.SelectedChoices || restoredBoss.HasPending && pendingSkill) return false;
            var validSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (string source in snapshot.Rogue.UsedSources) validSources.Add(source);
            for (long id = 1; id <= restoredProgress.SelectedChoices; id++)
                if (!validSources.Contains(RogueSourceId(id))) return false;
            var pendingRegions = new HashSet<int>();
            int previousIndex = -1;
            foreach (int regionId in snapshot.PendingBossRegions)
            {
                RegionRuntimeState region = snapshot.Regions.Find(value => value.Plan.Request.RegionId == regionId);
                if (region == null || !region.BossDefeated || region.BossRewardsCompleted || !pendingRegions.Add(regionId) ||
                    region.Plan.Request.RegionIndex <= previousIndex || region.Plan.Request.RegionIndex > snapshot.CurrentRegionIndex) return false;
                previousIndex = region.Plan.Request.RegionIndex;
            }
            var batchRegions = new HashSet<int>();
            int selectedBossRounds = 0;
            foreach (BossLootBatchState batch in snapshot.BossLoot.Batches)
            {
                RegionGenerationRequest request = regionRequests.Find(value => BossBatchId(value.RegionId) == batch.BatchId);
                RegionRuntimeState region = request == null ? null : snapshot.Regions.Find(value => value.Plan.Request.RegionId == request.RegionId);
                if (region == null || !region.BossDefeated || !batchRegions.Add(request.RegionId) ||
                    batch.TotalRounds != BossRule(request).Rounds || !ValidBossBatch(request, batch, snapshot.Inventory)) return false;
                bool complete = batch.Rounds.Count == batch.TotalRounds && batch.Rounds[batch.Rounds.Count - 1].SelectedIndex >= 0;
                if (complete != region.BossRewardsCompleted || !complete && !pendingRegions.Contains(request.RegionId)) return false;
                foreach (BossLootRoundState round in batch.Rounds) if (round.SelectedIndex >= 0) selectedBossRounds++;
            }
            if (snapshot.Statistics.BossLootRounds != selectedBossRounds) return false;
            foreach (RegionRuntimeState region in snapshot.Regions)
                if (region.BossRewardsCompleted ? !batchRegions.Contains(region.Plan.Request.RegionId) :
                    region.BossDefeated && !pendingRegions.Contains(region.Plan.Request.RegionId)) return false;
            if (restoredBoss.HasPending && (snapshot.PendingBossRegions.Count == 0 ||
                snapshot.BossLoot.ActiveBatchId != BossBatchId(snapshot.PendingBossRegions[0]))) return false;
            rogueChoices = restoredRogue;
            rogueProgress = restoredProgress;
            bossChoices = restoredBoss;
            bossRewardRandom = restoredRandom;
            activeRogueQualification = snapshot.ActiveRogueQualification;
            pendingBossRegions.Clear();
            foreach (int regionId in snapshot.PendingBossRegions) pendingBossRegions.Enqueue(regionId);
            rewardOffersVisible = false;
            error = null;
            return true;
        }

        /// <summary>
        /// 验证已保存 Boss 候选仍符合该区域规则及跨轮收藏领取事实。
        /// </summary>
        /// <param name="request">固定区域 Boss 请求。</param>
        /// <param name="batch">已保存完整轮次和候选。</param>
        /// <param name="items">本局实际持有物品。</param>
        /// <returns>候选构成、品质和未领取实例归属合法时为真。</returns>
        private static bool ValidBossBatch(RegionGenerationRequest request, BossLootBatchState batch, IReadOnlyList<ProjectItem> items)
        {
            CfgBossRewardsRow rule = BossRule(request);
            var collectionIds = new HashSet<string>(rule.CollectionIDs, StringComparer.Ordinal);
            var claimed = new HashSet<string>(StringComparer.Ordinal);
            foreach (BossLootRoundState round in batch.Rounds)
            {
                int equipment = 0;
                int collections = 0;
                var roundCollections = new HashSet<string>(StringComparer.Ordinal);
                for (int index = 0; index < round.Options.Count; index++)
                {
                    ProjectItem item = round.Options[index];
                    if ((int)item.Quality < rule.MinQuality || (int)item.Quality > rule.MaxQuality) return false;
                    if (index != round.SelectedIndex)
                        foreach (ProjectItem owned in items)
                            if (owned.Id == item.Id) return false;
                    if (item.Type == ProjectItemType.Collection)
                    {
                        ProjectCollectionDefinition definition = ProjectV2RewardConfig.Collection(item.CollectionId);
                        if (!collectionIds.Contains(item.CollectionId) || !roundCollections.Add(item.CollectionId) ||
                            !rule.AllowRepeatCollections && claimed.Contains(item.CollectionId) || definition == null ||
                            item.Name != definition.Name || item.Quality != definition.Quality || item.SellGold != definition.SellGold ||
                            item.RequiredLevel != definition.RequiredLevel || definition.RequiredLevel > request.LootLevel) return false;
                        collections++;
                    }
                    else
                    {
                        if (item.Type != ProjectItemType.Weapon && item.Type != ProjectItemType.Armor || item.RequiredLevel != request.LootLevel) return false;
                        bool known = false;
                        foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                            if (row.PoolID == rule.EquipmentPoolID && row.DefinitionId == item.DefinitionId && row.Weight > 0 &&
                                request.LootLevel >= row.MinLevel && request.LootLevel <= row.MaxLevel) known = true;
                        if (!known) return false;
                        equipment++;
                    }
                }
                if (equipment < rule.MinEquipmentCandidates || collections < rule.MinCollectionCandidates) return false;
                if (round.SelectedIndex >= 0 && round.Options[round.SelectedIndex].Type == ProjectItemType.Collection)
                    claimed.Add(round.Options[round.SelectedIndex].CollectionId);
            }
            return true;
        }

        /// <summary>
        /// 生成整关技能资格的唯一来源身份。
        /// </summary>
        /// <param name="qualification">由进度会话递增的资格序号。</param>
        /// <returns>技能选择的稳定来源身份。</returns>
        private string RogueSourceId(long qualification) => RunId + ":progress:" + qualification;

        /// <summary>
        /// 生成区域 Boss 的唯一批次身份。
        /// </summary>
        /// <param name="regionId">固定区域身份。</param>
        /// <returns>该区域唯一的 Boss 奖励批次身份。</returns>
        private string BossBatchId(int regionId) => RunId + ":boss:" + regionId;

        /// <summary>
        /// 从固定区域 Boss 配置取得其奖励规则。
        /// </summary>
        /// <param name="request">已经冻结的区域请求。</param>
        /// <returns>开局已校验的 Boss 奖励规则。</returns>
        private static CfgBossRewardsRow BossRule(RegionGenerationRequest request) =>
            ProjectV2RewardConfig.BossReward(GameTables.CfgBosses.GetById(request.BossDefinitionId).RewardID);

        /// <summary>
        /// 将项目强化属性映射到独立战斗层属性。
        /// </summary>
        /// <param name="attribute">项目属性种类。</param>
        /// <param name="mapped">对应战斗属性。</param>
        /// <returns>属性存在明确映射时为真。</returns>
        private static bool TryMapRogueAttribute(ProjectAttributeType attribute, out CombatAttributeKind mapped)
        {
            mapped = default;
            switch (attribute)
            {
                case ProjectAttributeType.MaxHealth: mapped = CombatAttributeKind.MaxHealth; break;
                case ProjectAttributeType.Attack: mapped = CombatAttributeKind.Attack; break;
                case ProjectAttributeType.Defense: mapped = CombatAttributeKind.Defense; break;
                case ProjectAttributeType.CriticalRating: mapped = CombatAttributeKind.CriticalRating; break;
                case ProjectAttributeType.CriticalDamage: mapped = CombatAttributeKind.CriticalDamage; break;
                case ProjectAttributeType.DodgeChance: mapped = CombatAttributeKind.DodgeChance; break;
                case ProjectAttributeType.DamageIncrease: mapped = CombatAttributeKind.DamageIncrease; break;
                case ProjectAttributeType.DamageReduction: mapped = CombatAttributeKind.DamageReduction; break;
                default: return false;
            }
            return true;
        }

        /// <summary>
        /// 释放授权等待及事件；已经冻结的奖励事实不在释放时改写。
        /// </summary>
        private void DisposeRewardSessions()
        {
            if (!rogueLifetime.IsCancellationRequested) rogueLifetime.Cancel();
            rogueLifetime.Dispose();
            RogueChoiceChanged = null;
            BossChoiceChanged = null;
        }
    }
}
