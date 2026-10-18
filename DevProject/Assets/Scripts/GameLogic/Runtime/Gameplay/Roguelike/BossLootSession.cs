using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GameLogic.Project;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// 一次 Boss 掉落的冻结物品与整批领取事实。
    /// </summary>
    [Serializable]
    public sealed class BossLootBatchState
    {
        public string BatchId;
        public int Revision;
        public bool Claimed;
        public List<ProjectItem> Items = new();

        /// <summary>
        /// 创建不共享物品及属性集合的批次副本。
        /// </summary>
        /// <returns>独立的整批掉落状态。</returns>
        public BossLootBatchState Copy() => new()
        { BatchId = BatchId, Revision = Revision, Claimed = Claimed, Items = Items.ConvertAll(item => item.Copy()) };
    }

    /// <summary>
    /// 整关 Boss 掉落的保存状态，已领取批次继续保留以阻止重复发奖。
    /// </summary>
    [Serializable]
    public sealed class BossLootState
    {
        public int Version = 2;
        public string RunId;
        public string ActiveBatchId;
        public bool Cancelled;
        public List<BossLootBatchState> Batches = new();

        /// <summary>
        /// 创建交给统一保存入口的完整深拷贝。
        /// </summary>
        /// <returns>与运行会话隔离的整批掉落保存状态。</returns>
        public BossLootState Copy() => new()
        {
            Version = Version, RunId = RunId, ActiveBatchId = ActiveBatchId, Cancelled = Cancelled,
            Batches = Batches.ConvertAll(batch => batch.Copy())
        };
    }

    /// <summary>
    /// 当前尚未领取的整批掉落视图，物品读取始终返回只读深拷贝。
    /// </summary>
    public sealed class BossLootSnapshot
    {
        private readonly List<ProjectItem> items;

        /// <summary>
        /// 本次 Boss 掉落的稳定批次身份。
        /// </summary>
        public string BatchId { get; }

        /// <summary>
        /// 当前冻结掉落的代次。
        /// </summary>
        public int Revision { get; }

        /// <summary>
        /// 本批所有完整物品，每次读取均与内部状态隔离。
        /// </summary>
        public IReadOnlyList<ProjectItem> Items => new ReadOnlyCollection<ProjectItem>(items.ConvertAll(item => item.Copy()));

        /// <summary>
        /// 从已经校验的整批掉落建立独立展示视图。
        /// </summary>
        /// <param name="batch">当前尚未领取的冻结批次。</param>
        internal BossLootSnapshot(BossLootBatchState batch)
        {
            BatchId = batch.BatchId;
            Revision = batch.Revision;
            items = batch.Items.ConvertAll(item => item.Copy());
        }
    }

    /// <summary>
    /// 冻结调用方生成的 Boss 掉落并一次领取整批，不执行随机生成或账号保存。
    /// </summary>
    public sealed class BossLootSession
    {
        private readonly string runId;
        private readonly List<BossLootBatchState> batches = new();
        private readonly HashSet<string> itemIds = new(StringComparer.Ordinal);
        private BossLootBatchState activeBatch;
        private bool cancelled;
        private bool applying;

        /// <summary>
        /// 创建绑定稳定单局身份的 Boss 掉落会话。
        /// </summary>
        /// <param name="runId">不可在会话内更换的单局身份。</param>
        public BossLootSession(string runId) => this.runId = runId;

        /// <summary>
        /// 当前批次尚未整批领取，且会话没有取消。
        /// </summary>
        public bool HasPending => !cancelled && activeBatch != null && !activeBatch.Claimed;

        /// <summary>
        /// 最近建立的掉落批次已经成功整批领取。
        /// </summary>
        public bool IsComplete => activeBatch != null && activeBatch.Claimed;

        /// <summary>
        /// 尚未领取的整批视图，已领取、未创建或取消时为空。
        /// </summary>
        public BossLootSnapshot Active => HasPending ? new BossLootSnapshot(activeBatch) : null;

        /// <summary>
        /// 首次冻结一个 Boss 掉落批次，不覆盖待领物品或重复使用既有实例身份。
        /// </summary>
        /// <param name="batchId">当前单局内唯一的 Boss 掉落身份。</param>
        /// <param name="items">调用方已经生成的非空完整物品列表。</param>
        /// <returns>整批物品首次冻结成功时为真。</returns>
        public bool TryBegin(string batchId, IReadOnlyList<ProjectItem> items)
        {
            if (applying || cancelled || HasPending || string.IsNullOrWhiteSpace(runId) || string.IsNullOrWhiteSpace(batchId) ||
                batches.Exists(batch => batch.BatchId == batchId) || !ValidItems(items, itemIds)) return false;
            var batch = new BossLootBatchState { BatchId = batchId, Revision = 1 };
            foreach (ProjectItem item in items)
            {
                batch.Items.Add(item.Copy());
                itemIds.Add(item.Id);
            }
            batches.Add(batch);
            activeBatch = batch;
            return true;
        }

        /// <summary>
        /// 校验当前批次及代次，将整批冻结物品只交给调用方原子兑现一次。
        /// </summary>
        /// <param name="batchId">展示方持有的当前掉落批次身份。</param>
        /// <param name="revision">展示方持有的冻结代次。</param>
        /// <param name="apply">同步原子应用整批只读物品的回调，返回假时不得保留部分应用。</param>
        /// <returns>本批首次成功应用并标记领取时为真，回调失败不会消费掉落。</returns>
        public bool TryClaim(string batchId, int revision, Func<IReadOnlyList<ProjectItem>, bool> apply)
        {
            if (applying || !HasPending || apply == null || activeBatch.BatchId != batchId || activeBatch.Revision != revision) return false;
            BossLootBatchState batch = activeBatch;
            bool accepted;
            applying = true;
            try { accepted = apply(new ReadOnlyCollection<ProjectItem>(batch.Items.ConvertAll(item => item.Copy()))); }
            finally { applying = false; }
            if (!accepted) return false;
            batch.Claimed = true;
            return true;
        }

        /// <summary>
        /// 取消后续领取，保留冻结物品与既有领取事实，应用回调期间拒绝取消。
        /// </summary>
        public void Cancel()
        {
            if (!applying) cancelled = true;
        }

        /// <summary>
        /// 读取所有已经整批领取的物品，不共享内部实例。
        /// </summary>
        /// <returns>按批次及物品原有顺序排列的只读深拷贝。</returns>
        public IReadOnlyList<ProjectItem> ClaimedItems()
        {
            var claimed = new List<ProjectItem>();
            foreach (BossLootBatchState batch in batches)
                if (batch.Claimed) claimed.AddRange(batch.Items.ConvertAll(item => item.Copy()));
            return new ReadOnlyCollection<ProjectItem>(claimed);
        }

        /// <summary>
        /// 读取指定批次已经领取的完整物品。
        /// </summary>
        /// <param name="batchId">当前单局内的稳定掉落批次身份。</param>
        /// <returns>该批已领取物品的只读深拷贝，未知或尚未领取时为空列表。</returns>
        public IReadOnlyList<ProjectItem> ClaimedItems(string batchId)
        {
            BossLootBatchState batch = batches.Find(value => value.BatchId == batchId);
            return new ReadOnlyCollection<ProjectItem>(batch != null && batch.Claimed ? batch.Items.ConvertAll(item => item.Copy()) : new List<ProjectItem>());
        }

        /// <summary>
        /// 冻结全部掉落、领取和取消事实，供统一业务检查点保存。
        /// </summary>
        /// <returns>与运行对象完全隔离的保存状态。</returns>
        public BossLootState CaptureState() => new()
        {
            RunId = runId, ActiveBatchId = activeBatch?.BatchId, Cancelled = cancelled,
            Batches = batches.ConvertAll(batch => batch.Copy())
        };

        /// <summary>
        /// 完整校验掉落顺序和物品唯一性后恢复，失败不改变当前会话且不重放领取回调。
        /// </summary>
        /// <param name="state">已经持久确认的第二版整批掉落状态。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>身份、代次、物品及批次领取顺序均合法时为真。</returns>
        public bool TryRestore(BossLootState state, out string error)
        {
            error = "Boss 整批掉落保存状态无效。";
            if (applying || state == null || state.Version != 2 || state.RunId != runId || string.IsNullOrWhiteSpace(runId) ||
                state.Batches == null) return false;
            var restoredBatchIds = new HashSet<string>(StringComparer.Ordinal);
            var restoredItemIds = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < state.Batches.Count; index++)
            {
                BossLootBatchState batch = state.Batches[index];
                if (batch == null || string.IsNullOrWhiteSpace(batch.BatchId) || !restoredBatchIds.Add(batch.BatchId) || batch.Revision != 1 ||
                    !ValidItems(batch.Items, restoredItemIds) || index < state.Batches.Count - 1 && !batch.Claimed) return false;
                foreach (ProjectItem item in batch.Items) restoredItemIds.Add(item.Id);
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
        /// 校验整批物品非空、属性有效且实例身份未被任何批次占用。
        /// </summary>
        /// <param name="items">需要冻结或恢复的完整掉落。</param>
        /// <param name="existingIds">其他批次已经占用的实例身份。</param>
        /// <returns>本批所有物品均可独立保存和领取时为真。</returns>
        private static bool ValidItems(IReadOnlyList<ProjectItem> items, HashSet<string> existingIds)
        {
            if (items == null || items.Count == 0) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectItem item in items)
                if (ProjectEquipmentRules.ValidateItem(item) != null || item.Equipped || existingIds.Contains(item.Id) || !ids.Add(item.Id)) return false;
            return true;
        }
    }
}
