using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// 本局强化的效果分类。
    /// </summary>
    public enum RogueRewardKind { AttributeBonus, RandomLoot, CombatAugment }

    /// <summary>
    /// 可配置但每局创建时会复制的选择功能开关。
    /// </summary>
    public sealed class RogueChoicePolicy
    {
        public bool RefreshEnabled;
        public bool ExtraChoiceEnabled;
        public int MaxRefreshes;
        public int SelectedUpgradeWeightMultiplier = 1;
        public int UnlockedComboWeightMultiplier = 1;

        /// <summary>
        /// 从当前内容配置复制默认策略。
        /// </summary>
        public RogueChoicePolicy()
        {
            CfgRoguePolicyRow row = ProjectRulesConfig.RoguePolicy;
            RefreshEnabled = row.RefreshEnabled;
            ExtraChoiceEnabled = row.ExtraChoiceEnabled;
            MaxRefreshes = row.MaxRefreshes;
            SelectedUpgradeWeightMultiplier = row.SelectedUpgradeWeightMultiplier;
            UnlockedComboWeightMultiplier = row.UnlockedComboWeightMultiplier;
        }

        /// <summary>
        /// 创建互不共享配置字段的副本。
        /// </summary>
        /// <returns>独立策略。</returns>
        public RogueChoicePolicy Copy() => (RogueChoicePolicy)MemberwiseClone();
    }

    /// <summary>
    /// 表达本局属性、随机战利品或战斗强化，应用由调用方完成。
    /// </summary>
    public sealed class RogueRewardDefinition
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public RogueRewardKind Kind { get; }
        public ProjectAttributeType Attribute { get; }
        public float Amount { get; }
        public ProjectItemType LootType { get; }
        public string AugmentId { get; }
        public int Rank { get; }
        public int MaxRank { get; }

        /// <summary>
        /// 创建不可变奖励定义。
        /// </summary>
        /// <param name="id">奖励身份。</param>
        /// <param name="title">显示名称。</param>
        /// <param name="description">显示说明。</param>
        /// <param name="kind">奖励分类。</param>
        /// <param name="attribute">本局属性种类。</param>
        /// <param name="amount">本局属性增量。</param>
        /// <param name="lootType">随机战利品类型。</param>
        /// <param name="augmentId">战斗强化身份。</param>
        /// <param name="rank">本次获得后的等级。</param>
        /// <param name="maxRank">允许的最高等级，零表示不限。</param>
        public RogueRewardDefinition(string id, string title, string description, RogueRewardKind kind,
            ProjectAttributeType attribute = default, float amount = 0, ProjectItemType lootType = default,
            string augmentId = null, int rank = 1, int maxRank = 0)
        {
            Id = id;
            Title = title;
            Description = description;
            Kind = kind;
            Attribute = attribute;
            Amount = amount;
            LootType = lootType;
            AugmentId = augmentId;
            Rank = rank;
            MaxRank = maxRank;
        }
    }

    /// <summary>
    /// 单局已获得强化的不可变快照。
    /// </summary>
    public sealed class RogueUpgradeSnapshot
    {
        public string Id { get; }
        public string Title { get; }
        public string Description { get; }
        public int Rank { get; }
        public int MaxRank { get; }
        public RogueRewardKind Kind { get; }
        public string AugmentId { get; }
        public float Amount { get; }

        /// <summary>
        /// 冻结一次已获得的强化等级。
        /// </summary>
        /// <param name="reward">成功应用的奖励。</param>
        public RogueUpgradeSnapshot(RogueRewardDefinition reward)
        {
            Id = reward.Id;
            Title = reward.Title;
            Description = reward.Description;
            Rank = reward.Rank;
            MaxRank = reward.MaxRank;
            Kind = reward.Kind;
            AugmentId = reward.AugmentId;
            Amount = reward.Amount;
        }
    }

    /// <summary>
    /// 不可变选择项。
    /// </summary>
    public sealed class RogueChoiceOption
    {
        public int Index { get; }
        public bool IsExtra { get; }
        public RogueRewardDefinition Reward { get; }

        /// <summary>
        /// 创建独立选择项。
        /// </summary>
        /// <param name="index">当前选项序号。</param>
        /// <param name="isExtra">是否为授权第四项。</param>
        /// <param name="reward">不可变奖励定义。</param>
        public RogueChoiceOption(int index, bool isExtra, RogueRewardDefinition reward)
        {
            Index = index;
            IsExtra = isExtra;
            Reward = reward;
        }
    }

    /// <summary>
    /// 不共享可写集合的当前选择快照。
    /// </summary>
    public sealed class RogueChoiceSnapshot
    {
        public string OfferId { get; }
        public string SourceId { get; }
        public int Revision { get; }
        public int RefreshCount { get; }
        public bool CanRefresh { get; }
        public bool ExtraUnlocked { get; }
        public IReadOnlyList<RogueChoiceOption> Options { get; }

        /// <summary>
        /// 创建已封闭集合的快照。
        /// </summary>
        /// <param name="offerId">本次选择身份。</param>
        /// <param name="sourceId">来源身份。</param>
        /// <param name="revision">当前代次。</param>
        /// <param name="refreshCount">已刷新次数。</param>
        /// <param name="canRefresh">是否仍可刷新。</param>
        /// <param name="extraUnlocked">第四项是否已授权。</param>
        /// <param name="options">当前可见选项。</param>
        public RogueChoiceSnapshot(string offerId, string sourceId, int revision, int refreshCount, bool canRefresh,
            bool extraUnlocked, IList<RogueChoiceOption> options)
        {
            OfferId = offerId;
            SourceId = sourceId;
            Revision = revision;
            RefreshCount = refreshCount;
            CanRefresh = canRefresh;
            ExtraUnlocked = extraUnlocked;
            Options = new ReadOnlyCollection<RogueChoiceOption>(new List<RogueChoiceOption>(options));
        }
    }

    /// <summary>
    /// 第四项授权请求的稳定身份。
    /// </summary>
    public sealed class RogueChoiceAccessRequest
    {
        public string RunId { get; }
        public string OfferId { get; }
        public int Revision { get; }
        public string RequestId { get; }

        /// <summary>
        /// 创建不可变授权请求。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <param name="offerId">选择身份。</param>
        /// <param name="revision">选择代次。</param>
        /// <param name="requestId">请求身份。</param>
        public RogueChoiceAccessRequest(string runId, string offerId, int revision, string requestId)
        {
            RunId = runId;
            OfferId = offerId;
            Revision = revision;
            RequestId = requestId;
        }
    }

    /// <summary>
    /// 外部授权提供者返回的不可变回执。
    /// </summary>
    public sealed class RogueChoiceAccessReceipt
    {
        public string RunId { get; }
        public string OfferId { get; }
        public int Revision { get; }
        public string RequestId { get; }
        public bool Granted { get; }
        public string ReceiptId { get; }

        /// <summary>
        /// 创建不可变授权回执。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <param name="offerId">选择身份。</param>
        /// <param name="revision">选择代次。</param>
        /// <param name="requestId">请求身份。</param>
        /// <param name="granted">是否授权。</param>
        /// <param name="receiptId">回执唯一身份。</param>
        public RogueChoiceAccessReceipt(string runId, string offerId, int revision, string requestId, bool granted, string receiptId)
        {
            RunId = runId;
            OfferId = offerId;
            Revision = revision;
            RequestId = requestId;
            Granted = granted;
            ReceiptId = receiptId;
        }
    }

    /// <summary>
    /// 未来外部授权实现的异步契约。
    /// </summary>
    public interface IRogueChoiceAccessProvider
    {
        /// <summary>
        /// 请求一次第四项授权。
        /// </summary>
        /// <param name="request">会话签发的稳定请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>包含匹配身份的授权回执。</returns>
        UniTask<RogueChoiceAccessReceipt> RequestAsync(RogueChoiceAccessRequest request, CancellationToken ct);
    }

    /// <summary>
    /// 使用独立随机源管理本局三选一和可选第四项授权。
    /// </summary>
    public sealed class RogueChoiceSession
    {
        private readonly string runId;
        private readonly System.Random random;
        private readonly RogueChoicePolicy policy;
        private readonly HashSet<string> usedSources = new();
        private readonly HashSet<string> usedReceipts = new();
        private readonly List<RogueChoiceOption> options = new();
        private readonly Dictionary<string, int> selectedRanks = new();
        private readonly Dictionary<string, string> selectedGroups = new();
        private readonly Dictionary<string, RogueUpgradeSnapshot> upgrades = new();
        private string offerId;
        private string sourceId;
        private int revision;
        private int refreshCount;
        private int offerSerial;
        private bool extraUnlocked;
        private bool cancelled;
        private bool applying;
        private RogueChoiceAccessRequest pendingRequest;

        /// <summary>
        /// 创建与其他玩法随机流独立的选择会话。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <param name="seed">选择专属随机种子。</param>
        /// <param name="policy">可选功能策略。</param>
        public RogueChoiceSession(string runId, int seed, RogueChoicePolicy policy = null)
        {
            this.runId = runId;
            random = new System.Random(seed);
            this.policy = (policy ?? new RogueChoicePolicy()).Copy();
        }

        /// <summary>
        /// 当前是否有待处理选择，避免高频检查时复制快照。
        /// </summary>
        public bool HasPending => offerId != null;

        /// <summary>
        /// 取得与内部集合隔离的当前选择。
        /// </summary>
        public RogueChoiceSnapshot Active => offerId == null ? null : new RogueChoiceSnapshot(offerId, sourceId, revision,
            refreshCount, policy.RefreshEnabled && refreshCount < policy.MaxRefreshes, extraUnlocked, options);

        /// <summary>
        /// 获取已兑现且不含随机战利品的只读强化快照。
        /// </summary>
        public IReadOnlyList<RogueUpgradeSnapshot> Upgrades
        {
            get
            {
                var snapshot = new List<RogueUpgradeSnapshot>(upgrades.Values);
                snapshot.Sort((left, right) => string.CompareOrdinal(left.Id, right.Id));
                return new ReadOnlyCollection<RogueUpgradeSnapshot>(snapshot);
            }
        }

        /// <summary>
        /// 每个来源在本局只创建一次选择。
        /// </summary>
        /// <param name="sourceId">触发来源身份。</param>
        /// <returns>是否创建新的三选一。</returns>
        public bool TryBegin(string sourceId)
        {
            if (cancelled || offerId != null || string.IsNullOrWhiteSpace(sourceId) || !usedSources.Add(sourceId)) return false;
            this.sourceId = sourceId;
            offerId = runId + ":" + ++offerSerial;
            revision = 0;
            refreshCount = 0;
            extraUnlocked = false;
            pendingRequest = null;
            if (Deal()) return true;
            usedSources.Remove(sourceId);
            CloseActive();
            return false;
        }

        /// <summary>
        /// 刷新当前选项并废弃旧代次和旧授权请求。
        /// </summary>
        /// <param name="offerId">当前选择身份。</param>
        /// <param name="revision">客户端持有的代次。</param>
        /// <returns>是否刷新成功。</returns>
        public bool TryRefresh(string offerId, int revision)
        {
            if (applying || !Current(offerId, revision) || !policy.RefreshEnabled || refreshCount >= policy.MaxRefreshes) return false;
            if (!Deal()) return false;
            refreshCount++;
            this.revision++;
            extraUnlocked = false;
            pendingRequest = null;
            return true;
        }

        /// <summary>
        /// 兑现选中项；发奖失败时保留同一选择供重试。
        /// </summary>
        /// <param name="offerId">当前选择身份。</param>
        /// <param name="revision">客户端持有的代次。</param>
        /// <param name="optionIndex">选项序号。</param>
        /// <param name="apply">调用方一次性应用本局奖励。</param>
        /// <returns>是否成功兑现并关闭选择。</returns>
        public bool TrySelect(string offerId, int revision, int optionIndex, Func<RogueRewardDefinition, bool> apply)
        {
            if (!Current(offerId, revision) || applying || apply == null || optionIndex < 0 || optionIndex >= options.Count ||
                (options[optionIndex].IsExtra && !extraUnlocked)) return false;
            RogueRewardDefinition reward = options[optionIndex].Reward;
            bool applied;
            applying = true;
            try
            {
                applied = apply(reward);
            }
            finally
            {
                applying = false;
            }
            if (!applied) return false;
            selectedRanks[reward.Id] = reward.Rank;
            CfgRogueRewardsRow row = GameTables.CfgRogueRewards.GetById(reward.Id);
            if (!string.IsNullOrEmpty(row?.ExclusiveGroup)) selectedGroups[row.ExclusiveGroup] = reward.Id;
            if (reward.Kind != RogueRewardKind.RandomLoot) upgrades[reward.Id] = new RogueUpgradeSnapshot(reward);
            CloseActive();
            return true;
        }

        /// <summary>
        /// 生成或复用当前唯一的第四项授权请求。
        /// </summary>
        /// <param name="offerId">当前选择身份。</param>
        /// <param name="revision">客户端持有的代次。</param>
        /// <param name="request">签发的不可变请求。</param>
        /// <returns>是否能请求第四项授权。</returns>
        public bool TryCreateExtraAccessRequest(string offerId, int revision, out RogueChoiceAccessRequest request)
        {
            request = null;
            if (applying || !Current(offerId, revision) || !policy.ExtraChoiceEnabled || extraUnlocked) return false;
            pendingRequest ??= new RogueChoiceAccessRequest(runId, offerId, revision, Guid.NewGuid().ToString("N"));
            request = pendingRequest;
            return true;
        }

        /// <summary>
        /// 仅接受自身签发、当前代次且未使用的授权回执。
        /// </summary>
        /// <param name="request">先前签发的请求。</param>
        /// <param name="receipt">外部授权回执。</param>
        /// <returns>是否解锁第四项。</returns>
        public bool TryGrantExtraAccess(RogueChoiceAccessRequest request, RogueChoiceAccessReceipt receipt)
        {
            if (applying || !policy.ExtraChoiceEnabled || pendingRequest == null || !ReferenceEquals(request, pendingRequest) ||
                !Current(request.OfferId, request.Revision)) return false;
            if (receipt == null)
            {
                pendingRequest = null;
                return false;
            }
            if (receipt.RunId != runId ||
                receipt.OfferId != offerId || receipt.Revision != revision || receipt.RequestId != request.RequestId ||
                string.IsNullOrWhiteSpace(receipt.ReceiptId) || usedReceipts.Contains(receipt.ReceiptId)) return false;
            pendingRequest = null;
            if (!receipt.Granted) return false;
            usedReceipts.Add(receipt.ReceiptId);
            extraUnlocked = true;
            return true;
        }

        /// <summary>
        /// 永久终止本局选择会话，并令旧身份不可再兑现。
        /// </summary>
        public void Cancel()
        {
            cancelled = true;
            CloseActive();
        }

        /// <summary>
        /// 关闭已经兑现或取消的当前选择。
        /// </summary>
        private void CloseActive()
        {
            offerId = null;
            sourceId = null;
            pendingRequest = null;
            options.Clear();
            extraUnlocked = false;
        }

        /// <summary>
        /// 检查请求仍属于当前选择代次。
        /// </summary>
        /// <param name="id">选择身份。</param>
        /// <param name="version">选择代次。</param>
        /// <returns>身份是否匹配。</returns>
        private bool Current(string id, int version) => offerId != null && offerId == id && revision == version;

        /// <summary>
        /// 按等级上限、前置和互斥组过滤后，抽取不重复选项。
        /// </summary>
        /// <returns>至少可以生成三个基础选项时为真。</returns>
        private bool Deal()
        {
            if (policy.SelectedUpgradeWeightMultiplier < 1 || policy.SelectedUpgradeWeightMultiplier > 16 ||
                policy.UnlockedComboWeightMultiplier < 1 || policy.UnlockedComboWeightMultiplier > 16) return false;
            var pool = new List<CfgRogueRewardsRow>();
            foreach (CfgRogueRewardsRow row in GameTables.CfgRogueRewards.GetAll())
            {
                int rank = selectedRanks.TryGetValue(row.ID, out int current) ? current : 0;
                if (row.MaxRank > 0 && rank >= row.MaxRank) continue;
                if (!string.IsNullOrEmpty(row.PrerequisiteID) &&
                    (!selectedRanks.TryGetValue(row.PrerequisiteID, out int prerequisite) || prerequisite < row.PrerequisiteRank)) continue;
                if (!string.IsNullOrEmpty(row.ExclusiveGroup) && selectedGroups.TryGetValue(row.ExclusiveGroup, out string chosen) &&
                    chosen != row.ID) continue;
                pool.Add(row);
            }
            if (pool.Count < 3) return false;
            var dealt = new List<RogueChoiceOption>();
            int count = Math.Min(4, pool.Count);
            for (int index = 0; index < count; index++)
            {
                long total = 0;
                foreach (CfgRogueRewardsRow candidate in pool) total += EffectiveWeight(candidate);
                if (total <= 0 || total > int.MaxValue) return false;
                long roll = random.Next((int)total);
                int chosen = 0;
                for (; chosen < pool.Count - 1; chosen++)
                {
                    long weight = EffectiveWeight(pool[chosen]);
                    if (roll < weight) break;
                    roll -= weight;
                }
                CfgRogueRewardsRow row = pool[chosen];
                pool.RemoveAt(chosen);
                int rank = selectedRanks.TryGetValue(row.ID, out int current) ? current + 1 : 1;
                var reward = new RogueRewardDefinition(row.ID, row.Title, row.Description, (RogueRewardKind)row.Kind,
                    (ProjectAttributeType)row.Attribute, (float)row.Amount, (ProjectItemType)row.LootType,
                    row.AugmentID, rank, row.MaxRank);
                if (index < 3 || policy.ExtraChoiceEnabled) dealt.Add(new RogueChoiceOption(index, index == 3, reward));
            }
            options.Clear();
            options.AddRange(dealt);
            return true;
        }

        /// <summary>
        /// 对已选未满级战斗强化和已解锁组合应用配置权重。
        /// </summary>
        /// <param name="row">已经通过资格过滤的奖励。</param>
        /// <returns>本次无放回抽取使用的长整型正权重。</returns>
        private long EffectiveWeight(CfgRogueRewardsRow row)
        {
            if (row.Kind != (int)RogueRewardKind.CombatAugment) return row.Weight;
            if (selectedRanks.ContainsKey(row.ID)) return (long)row.Weight * policy.SelectedUpgradeWeightMultiplier;
            if (!string.IsNullOrEmpty(row.PrerequisiteID)) return (long)row.Weight * policy.UnlockedComboWeightMultiplier;
            return row.Weight;
        }
    }
}
