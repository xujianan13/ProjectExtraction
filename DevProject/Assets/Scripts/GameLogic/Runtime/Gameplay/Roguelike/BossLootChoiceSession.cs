using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameLogic.Project;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// Boss 一轮战利品的冻结候选与领取结果。
    /// </summary>
    [Serializable]
    public sealed class BossLootRoundState
    {
        public int Revision;
        public int SelectedIndex = -1;
        public List<ProjectItem> Options = new();

        /// <summary>
        /// 创建候选及领取结果的深拷贝。
        /// </summary>
        /// <returns>不共享物品实例的轮次状态。</returns>
        public BossLootRoundState Copy() => new()
        {
            Revision = Revision, SelectedIndex = SelectedIndex, Options = Options.ConvertAll(item => item.Copy())
        };
    }

    /// <summary>
    /// 一次 Boss 击败奖励批次，保留已经生成的各轮候选和领取事实。
    /// </summary>
    [Serializable]
    public sealed class BossLootBatchState
    {
        public string BatchId;
        public int TotalRounds;
        public List<BossLootRoundState> Rounds = new();

        /// <summary>
        /// 创建独立奖励批次。
        /// </summary>
        /// <returns>不共享轮次或物品的批次副本。</returns>
        public BossLootBatchState Copy() => new()
        {
            BatchId = BatchId, TotalRounds = TotalRounds, Rounds = Rounds.ConvertAll(round => round.Copy())
        };
    }

    /// <summary>
    /// 整关 Boss 战利品选择的可序列化状态，已完成批次防止重复击败事件发奖。
    /// </summary>
    [Serializable]
    public sealed class BossLootChoiceState
    {
        public int Version = 1;
        public string RunId;
        public string ActiveBatchId;
        public bool Cancelled;
        public List<BossLootBatchState> Batches = new();

        /// <summary>
        /// 创建交给统一保存入口的完整深拷贝。
        /// </summary>
        /// <returns>与运行会话隔离的完整选择状态。</returns>
        public BossLootChoiceState Copy() => new()
        {
            Version = Version, RunId = RunId, ActiveBatchId = ActiveBatchId, Cancelled = Cancelled,
            Batches = Batches.ConvertAll(batch => batch.Copy())
        };
    }

    /// <summary>
    /// Boss 当前轮次的只读视图，物品访问始终返回深拷贝。
    /// </summary>
    public sealed class BossLootChoiceSnapshot
    {
        private readonly List<ProjectItem> options;

        /// <summary>
        /// 当前选择的稳定身份。
        /// </summary>
        public string OfferId { get; }

        /// <summary>
        /// 本次 Boss 击败的稳定批次身份。
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 当前候选代次。
        /// </summary>
        public int Revision { get; }

        /// <summary>
        /// 从一开始的当前轮次。
        /// </summary>
        public int Round { get; }

        /// <summary>
        /// 本批次配置的总轮数。
        /// </summary>
        public int TotalRounds { get; }

        /// <summary>
        /// 当前轮次已经冻结的具体物品。
        /// </summary>
        public IReadOnlyList<ProjectItem> Options => new ReadOnlyCollection<ProjectItem>(options.ConvertAll(item => item.Copy()));

        /// <summary>
        /// 冻结当前轮次视图。
        /// </summary>
        /// <param name="offerId">当前选择身份。</param>
        /// <param name="batchId">Boss 奖励批次。</param>
        /// <param name="round">当前轮次。</param>
        /// <param name="totalRounds">总轮数。</param>
        /// <param name="state">内部已经冻结的轮次。</param>
        internal BossLootChoiceSnapshot(string offerId, string batchId, int round, int totalRounds, BossLootRoundState state)
        {
            OfferId = offerId;
            BatchId = batchId;
            Round = round;
            TotalRounds = totalRounds;
            Revision = state.Revision;
            options = state.Options.ConvertAll(item => item.Copy());
        }
    }

    /// <summary>
    /// 管理调用方预先生成的 Boss 完整物品候选，不执行随机生成或账户保存。
    /// </summary>
    public sealed class BossLootChoiceSession
    {
        private readonly string runId;
        private readonly List<BossLootBatchState> batches = new();
        private readonly HashSet<string> itemIds = new(StringComparer.Ordinal);
        private BossLootBatchState activeBatch;
        private bool cancelled;
        private bool applying;

        /// <summary>
        /// 创建整关 Boss 奖励会话。
        /// </summary>
        /// <param name="runId">稳定单局身份。</param>
        public BossLootChoiceSession(string runId) => this.runId = runId;

        /// <summary>
        /// 当前批次仍有未完成轮次，包括等待调用方提供下一轮候选的间隙。
        /// </summary>
        public bool HasPending => !cancelled && activeBatch != null && !BatchComplete(activeBatch);

        /// <summary>
        /// 已完成上一轮，需调用方冻结并提交下一轮完整候选。
        /// </summary>
        public bool NeedsCandidates => HasPending && activeBatch.Rounds[activeBatch.Rounds.Count - 1].SelectedIndex >= 0;

        /// <summary>
        /// 当前批次所有轮次已经成功兑现。
        /// </summary>
        public bool IsComplete => activeBatch != null && BatchComplete(activeBatch);

        /// <summary>
        /// 尚未兑现的当前轮视图，没有候选或已经结束时为空。
        /// </summary>
        public BossLootChoiceSnapshot Active
        {
            get
            {
                if (!HasPending || NeedsCandidates) return null;
                int round = activeBatch.Rounds.Count;
                BossLootRoundState state = activeBatch.Rounds[round - 1];
                return new BossLootChoiceSnapshot(OfferId(activeBatch.BatchId, round), activeBatch.BatchId, round, activeBatch.TotalRounds, state);
            }
        }

        /// <summary>
        /// 创建尚未处理的 Boss 批次并复制第一轮完整候选。
        /// </summary>
        /// <param name="batchId">稳定 Boss 击败批次身份。</param>
        /// <param name="totalRounds">调用方配置的正整数总轮数。</param>
        /// <param name="frozenCandidates">展示前已经生成的完整物品候选。</param>
        /// <returns>批次和首轮候选首次创建成功时为真。</returns>
        public bool TryBegin(string batchId, int totalRounds, IReadOnlyList<ProjectItem> frozenCandidates)
        {
            if (applying || cancelled || HasPending || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(batchId) ||
                totalRounds < 1 || batches.Exists(batch => batch.BatchId == batchId) || !ValidCandidates(frozenCandidates, itemIds)) return false;
            activeBatch = new BossLootBatchState { BatchId = batchId, TotalRounds = totalRounds };
            batches.Add(activeBatch);
            AppendRound(frozenCandidates);
            return true;
        }

        /// <summary>
        /// 上一轮成功后加入下一轮完整候选，不重抽或覆盖未领取轮次。
        /// </summary>
        /// <param name="frozenCandidates">调用方已冻结的新轮次物品。</param>
        /// <returns>下一轮候选首次加入成功时为真。</returns>
        public bool TryBeginNextRound(IReadOnlyList<ProjectItem> frozenCandidates)
        {
            if (applying || !NeedsCandidates || !ValidCandidates(frozenCandidates, itemIds)) return false;
            AppendRound(frozenCandidates);
            return true;
        }

        /// <summary>
        /// 校验当前轮次与代次，仅将选中的冻结物品交给调用方兑现一次。
        /// </summary>
        /// <param name="offerId">客户端持有的当前选择身份。</param>
        /// <param name="revision">客户端持有的候选代次。</param>
        /// <param name="optionIndex">选中的候选序号。</param>
        /// <param name="apply">应用具体物品的同步回调，失败返回假以保留原候选。</param>
        /// <returns>本轮首次成功兑现时为真。</returns>
        public bool TrySelect(string offerId, int revision, int optionIndex, Func<ProjectItem, bool> apply)
        {
            if (applying || !HasPending || NeedsCandidates || apply == null || offerId != OfferId(activeBatch.BatchId, activeBatch.Rounds.Count))
                return false;
            BossLootRoundState round = activeBatch.Rounds[activeBatch.Rounds.Count - 1];
            if (revision != round.Revision || optionIndex < 0 || optionIndex >= round.Options.Count) return false;
            applying = true;
            bool accepted = apply(round.Options[optionIndex].Copy());
            applying = false;
            if (!accepted) return false;
            round.SelectedIndex = optionIndex;
            return true;
        }

        /// <summary>
        /// 终止后续发奖并保留已经冻结的批次事实。
        /// </summary>
        public void Cancel()
        {
            if (!applying) cancelled = true;
        }

        /// <summary>
        /// 取得已领取的完整物品结果，不共享内部实例。
        /// </summary>
        /// <returns>按批次及轮次顺序排列的已领取物品。</returns>
        public IReadOnlyList<ProjectItem> ClaimedItems()
        {
            var claimed = new List<ProjectItem>();
            foreach (BossLootBatchState batch in batches)
                foreach (BossLootRoundState round in batch.Rounds)
                    if (round.SelectedIndex >= 0) claimed.Add(round.Options[round.SelectedIndex].Copy());
            return new ReadOnlyCollection<ProjectItem>(claimed);
        }

        /// <summary>
        /// 取得指定批次的已领取物品，用于仅在该批次内排除跨轮重复收藏。
        /// </summary>
        /// <param name="batchId">稳定奖励批次身份。</param>
        /// <returns>该批次按轮次排序的独立物品；批次不存在时为空列表。</returns>
        public IReadOnlyList<ProjectItem> ClaimedItems(string batchId)
        {
            var claimed = new List<ProjectItem>();
            foreach (BossLootBatchState batch in batches)
            {
                if (batch.BatchId != batchId) continue;
                foreach (BossLootRoundState round in batch.Rounds)
                    if (round.SelectedIndex >= 0) claimed.Add(round.Options[round.SelectedIndex].Copy());
                break;
            }
            return new ReadOnlyCollection<ProjectItem>(claimed);
        }

        /// <summary>
        /// 冻结当前批次、轮次、候选及已领结果，供统一持久提交。
        /// </summary>
        /// <returns>完整且不共享物品实例的保存状态。</returns>
        public BossLootChoiceState CaptureState() => new()
        {
            RunId = runId, ActiveBatchId = activeBatch?.BatchId, Cancelled = cancelled,
            Batches = batches.ConvertAll(batch => batch.Copy())
        };

        /// <summary>
        /// 校验批次顺序、物品身份与领取结果后恢复，失败不修改已有会话。
        /// </summary>
        /// <param name="state">已经持久确认的完整 Boss 奖励状态。</param>
        /// <param name="error">恢复失败原因。</param>
        /// <returns>所有轮次和物品归属有效时为真。</returns>
        public bool TryRestore(BossLootChoiceState state, out string error)
        {
            error = "Boss 战利品选择保存状态无效。";
            if (applying || state == null || state.Version != 1 || state.RunId != runId || string.IsNullOrWhiteSpace(runId) ||
                state.Batches == null) return false;
            var restoredBatchIds = new HashSet<string>(StringComparer.Ordinal);
            var restoredItemIds = new HashSet<string>(StringComparer.Ordinal);
            for (int batchIndex = 0; batchIndex < state.Batches.Count; batchIndex++)
            {
                BossLootBatchState batch = state.Batches[batchIndex];
                if (batch == null || string.IsNullOrWhiteSpace(batch.BatchId) || !restoredBatchIds.Add(batch.BatchId) ||
                    batch.TotalRounds < 1 || batch.Rounds == null || batch.Rounds.Count < 1 || batch.Rounds.Count > batch.TotalRounds) return false;
                for (int index = 0; index < batch.Rounds.Count; index++)
                {
                    BossLootRoundState round = batch.Rounds[index];
                    if (round == null || round.Revision != 0 || !ValidCandidates(round.Options, restoredItemIds) ||
                        round.SelectedIndex < -1 || round.SelectedIndex >= round.Options.Count ||
                        index < batch.Rounds.Count - 1 && round.SelectedIndex < 0) return false;
                    foreach (ProjectItem item in round.Options) restoredItemIds.Add(item.Id);
                }
                if (batchIndex < state.Batches.Count - 1 && !BatchComplete(batch)) return false;
            }
            if (state.Batches.Count == 0)
            {
                if (!string.IsNullOrEmpty(state.ActiveBatchId)) return false;
            }
            else if (state.ActiveBatchId != state.Batches[state.Batches.Count - 1].BatchId) return false;
            batches.Clear();
            batches.AddRange(state.Batches.ConvertAll(batch => batch.Copy()));
            itemIds.Clear();
            itemIds.UnionWith(restoredItemIds);
            activeBatch = batches.Count == 0 ? null : batches[batches.Count - 1];
            cancelled = state.Cancelled;
            error = null;
            return true;
        }

        /// <summary>
        /// 检查候选为三件不同且未归其他轮次的有效物品。
        /// </summary>
        /// <param name="candidates">边界输入的完整候选。</param>
        /// <param name="existingIds">其他轮次已经拥有的物品身份。</param>
        /// <returns>候选可以独立冻结时为真。</returns>
        private static bool ValidCandidates(IReadOnlyList<ProjectItem> candidates, HashSet<string> existingIds)
        {
            if (candidates == null || candidates.Count != 3) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectItem item in candidates)
                if (ProjectEquipmentRules.ValidateItem(item) != null || item.Equipped || existingIds.Contains(item.Id) || !ids.Add(item.Id)) return false;
            return true;
        }

        /// <summary>
        /// 复制已校验候选并登记唯一实例身份。
        /// </summary>
        /// <param name="candidates">已经校验的完整候选。</param>
        private void AppendRound(IReadOnlyList<ProjectItem> candidates)
        {
            var round = new BossLootRoundState();
            foreach (ProjectItem item in candidates)
            {
                round.Options.Add(item.Copy());
                itemIds.Add(item.Id);
            }
            activeBatch.Rounds.Add(round);
        }

        /// <summary>
        /// 判断批次全部轮次都已经实际兑现。
        /// </summary>
        /// <param name="batch">已经创建的奖励批次。</param>
        /// <returns>最后一轮已选且达到总轮数时为真。</returns>
        private static bool BatchComplete(BossLootBatchState batch) => batch.Rounds.Count == batch.TotalRounds &&
            batch.Rounds[batch.Rounds.Count - 1].SelectedIndex >= 0;

        /// <summary>
        /// 生成不依赖表现对象或临时计数的轮次选择身份。
        /// </summary>
        /// <param name="batchId">稳定 Boss 击败批次身份。</param>
        /// <param name="round">从一开始的轮次。</param>
        /// <returns>当前单局内唯一的选择身份。</returns>
        private string OfferId(string batchId, int round) => runId + ":boss:" + batchId.Length + ":" + batchId + ":" + round;
    }
}
