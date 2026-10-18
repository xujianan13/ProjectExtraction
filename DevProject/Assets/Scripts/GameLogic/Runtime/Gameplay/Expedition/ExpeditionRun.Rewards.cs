using System;
using System.Collections.Generic;
using GameFramework;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private BossLootChoiceSession bossChoices;
        private PersistentRandom bossRewardRandom;
        private readonly Queue<int> pendingBossRegions = new();
        private bool rewardOffersVisible;

        public event Action BossChoiceChanged;
        public BossLootChoiceSnapshot PendingBossChoice => rewardOffersVisible && !IsSavingRun ? bossChoices.Active : null;
        public bool IsChoosingBoss => bossChoices.HasPending;
        public bool IsChoosingReward => bossChoices.HasPending || pendingBossRegions.Count > 0;
        public string RewardMessage { get; private set; } = string.Empty;
        private bool CanAcceptRewardInput => IsRunning && IsReady && !paused && ActivePanel == ExpeditionPanelKind.None &&
            !disposed && !stopped && !IsSavingRun && !GameEntry.IsInBackground && !session.HasUncommittedSave &&
            World.TryGetActor(Player, out var player) && !player.Dead;

        /// <summary>
        /// 建立最终 Boss 奖励会话及独立随机流。
        /// </summary>
        /// <param name="seed">本关固定种子。</param>
        private void InitializeRewardSessions(int seed)
        {
            bossChoices = new BossLootChoiceSession(RunId);
            bossRewardRandom = new PersistentRandom(seed ^ 0x47A3);
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
        /// 冻结最终 Boss 的下一轮候选，并在确认保存后开放窗口。
        /// </summary>
        /// <returns>奖励或保存门禁已接管当前帧时为真。</returns>
        private bool TryBeginPendingReward()
        {
            if (IsSavingRun) return true;
            if (!CanAcceptRewardInput) return IsChoosingReward;
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
            IReadOnlyList<ProjectItem> candidates = ProjectRewardConfig.CreateBossCandidates(bossRewardRandom, LootLevel,
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
        /// 领取冻结的完整物品，同次保存本轮领取结果，最终全领奖后自动通关。
        /// </summary>
        /// <param name="offerId">当前 Boss 候选身份。</param>
        /// <param name="revision">候选代次。</param>
        /// <param name="index">选中的完整物品序号。</param>
        /// <returns>本轮首次领取并开始持久提交时为真。</returns>
        public bool TryChooseBoss(string offerId, int revision, int index)
        {
            if (!CanAcceptRewardInput || !rewardOffersVisible || pendingBossRegions.Count == 0 ||
                statistics.BossLootRounds == int.MaxValue ||
                pendingBossRegions.Peek() != CurrentRoom.Plan.Request.RegionId || !CurrentRoom.BossDefeated ||
                CurrentRoom.BossRewardsCompleted || !bossChoices.TrySelect(offerId, revision, index, ApplyBossLoot)) return false;
            statistics.BossLootRounds++;
            bool complete = bossChoices.IsComplete;
            if (complete)
            {
                if (!CurrentRoom.TryCompleteBossRewards())
                {
                    HandleFault("Boss 奖励已领取但出口状态未能确认，已冻结本关。");
                    return false;
                }
                pendingBossRegions.Dequeue();
            }
            SaveRewardMutation(complete ? "Boss 奖励已全部领取，正在确认通关。" : "本轮战利品已领取。", complete && IsFinalRoom);
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
        /// 同次冻结奖励相关完整事实，持久确认前不暴露新候选。
        /// </summary>
        /// <param name="message">保存后显示的奖励反馈。</param>
        /// <param name="finishRun">是否在本次确认后结束最终区域。</param>
        private void SaveRewardMutation(string message, bool finishRun = false)
        {
            rewardOffersVisible = false;
            ClearPlayerInput();
            RewardMessage = message;
            SaveMutation(message, () =>
            {
                if (disposed || stopped || !IsRunning) return;
                if (finishRun) EndRun(ProjectRunExitKind.Completed);
                else PublishRewardState();
            });
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
                if (IsFinalRoom && CurrentRoom.BossRewardsCompleted && !IsChoosingReward)
                {
                    EndRun(ProjectRunExitKind.Completed);
                    return;
                }
                if ((!bossChoices.HasPending || bossChoices.NeedsCandidates) && TryBeginPendingReward()) return;
            }
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
            snapshot.BossLoot = bossChoices.CaptureState();
            snapshot.BossRandomState = bossRewardRandom.State;
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
            if (snapshot.BossLoot == null || snapshot.PendingBossRegions == null ||
                !PersistentRandom.TryRestore(snapshot.BossRandomState, out PersistentRandom restoredRandom)) return false;
            var restoredBoss = new BossLootChoiceSession(RunId);
            if (!restoredBoss.TryRestore(snapshot.BossLoot, out error)) return false;
            error = "Boss 奖励领取记录不一致。";
            var pendingRegions = new HashSet<int>();
            int previousIndex = -1;
            foreach (int regionId in snapshot.PendingBossRegions)
            {
                ExpeditionRoomCheckpoint region = snapshot.Rooms.Find(value => value.Plan.Request.RegionId == regionId);
                if (region == null || !region.BossDefeated || region.BossRewardsCompleted || !pendingRegions.Add(regionId) ||
                    region.Plan.Request.RegionIndex <= previousIndex || region.Plan.Request.RegionIndex > snapshot.CurrentRoomIndex) return false;
                previousIndex = region.Plan.Request.RegionIndex;
            }
            var batchRegions = new HashSet<int>();
            int selectedBossRounds = 0;
            foreach (BossLootBatchState batch in snapshot.BossLoot.Batches)
            {
                RegionGenerationRequest request = regionRequests.Find(value => BossBatchId(value.RegionId) == batch.BatchId);
                ExpeditionRoomCheckpoint region = request == null ? null : snapshot.Rooms.Find(value => value.Plan.Request.RegionId == request.RegionId);
                if (region == null || !region.BossDefeated || !batchRegions.Add(request.RegionId) ||
                    batch.TotalRounds != BossRule(request).Rounds || !ValidBossBatch(request, batch, snapshot.Inventory)) return false;
                bool complete = batch.Rounds.Count == batch.TotalRounds && batch.Rounds[batch.Rounds.Count - 1].SelectedIndex >= 0;
                if (complete != region.BossRewardsCompleted || !complete && !pendingRegions.Contains(request.RegionId)) return false;
                foreach (BossLootRoundState round in batch.Rounds) if (round.SelectedIndex >= 0) selectedBossRounds++;
            }
            if (snapshot.Statistics.BossLootRounds != selectedBossRounds) return false;
            foreach (ExpeditionRoomCheckpoint region in snapshot.Rooms)
                if (region.BossRewardsCompleted ? !batchRegions.Contains(region.Plan.Request.RegionId) :
                    region.BossDefeated && !pendingRegions.Contains(region.Plan.Request.RegionId)) return false;
            if (restoredBoss.HasPending && (snapshot.PendingBossRegions.Count == 0 ||
                snapshot.BossLoot.ActiveBatchId != BossBatchId(snapshot.PendingBossRegions[0]))) return false;
            bossChoices = restoredBoss;
            bossRewardRandom = restoredRandom;
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
                        ProjectCollectionDefinition definition = ProjectRewardConfig.Collection(item.CollectionId);
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
            ProjectRewardConfig.BossReward(GameTables.CfgBosses.GetById(request.BossDefinitionId).RewardID);

        /// <summary>
        /// 释放奖励事件订阅。
        /// </summary>
        private void DisposeRewardSessions() => BossChoiceChanged = null;
    }
}

