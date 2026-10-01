using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.Project;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// 本局强化的效果分类。
    /// </summary>
    public enum RogueRewardKind { AttributeBonus, RandomLoot }

    /// <summary>
    /// 可配置但每局创建时会复制的选择功能开关。
    /// </summary>
    public sealed class RogueChoicePolicy
    {
        public bool RefreshEnabled = false;
        public bool ExtraChoiceEnabled = false;
        public int MaxRefreshes = 1;

        /// <summary>
        /// 创建互不共享配置字段的副本。
        /// </summary>
        /// <returns>独立策略。</returns>
        public RogueChoicePolicy Copy() => (RogueChoicePolicy)MemberwiseClone();
    }

    /// <summary>
    /// 当前仅表达本局属性或随机战利品，应用由调用方完成。
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
        public RogueRewardDefinition(string id, string title, string description, RogueRewardKind kind,
            ProjectAttributeType attribute = default, float amount = 0, ProjectItemType lootType = default)
        {
            Id = id;
            Title = title;
            Description = description;
            Kind = kind;
            Attribute = attribute;
            Amount = amount;
            LootType = lootType;
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
        private string offerId;
        private string sourceId;
        private int revision;
        private int refreshCount;
        private int offerSerial;
        private bool extraUnlocked;
        private bool cancelled;
        private bool applying;
        private RogueChoiceAccessRequest pendingRequest;

        private static readonly RogueRewardDefinition[] Pool =
        {
            new("hp", "生命强化", "本局生命上限和当前生命均 +20", RogueRewardKind.AttributeBonus, ProjectAttributeType.MaxHealth, 20),
            new("attack", "攻击强化", "本局攻击 +4", RogueRewardKind.AttributeBonus, ProjectAttributeType.Attack, 4),
            new("defense", "防御强化", "本局防御 +8", RogueRewardKind.AttributeBonus, ProjectAttributeType.Defense, 8),
            new("critical", "暴击强化", "本局暴击值 +12", RogueRewardKind.AttributeBonus, ProjectAttributeType.CriticalRating, 12),
            new("critical_damage", "暴伤强化", "本局暴击伤害 +20 个百分点", RogueRewardKind.AttributeBonus, ProjectAttributeType.CriticalDamage, 0.2f),
            new("dodge", "闪避强化", "本局闪避率 +5%", RogueRewardKind.AttributeBonus, ProjectAttributeType.DodgeChance, 0.05f),
            new("increase", "增伤强化", "本局增伤 +10%", RogueRewardKind.AttributeBonus, ProjectAttributeType.DamageIncrease, 0.1f),
            new("reduction", "减伤强化", "本局减伤 +5%", RogueRewardKind.AttributeBonus, ProjectAttributeType.DamageReduction, 0.05f),
            new("weapon", "随机武器", "本局获得随机武器", RogueRewardKind.RandomLoot, lootType: ProjectItemType.Weapon),
            new("armor", "随机护甲", "本局获得随机护甲", RogueRewardKind.RandomLoot, lootType: ProjectItemType.Armor),
            new("item", "随机物品", "本局获得随机物品", RogueRewardKind.RandomLoot, lootType: ProjectItemType.Item)
        };

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
            Deal();
            return true;
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
            refreshCount++;
            this.revision++;
            extraUnlocked = false;
            pendingRequest = null;
            Deal();
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
            bool applied;
            applying = true;
            try
            {
                applied = apply(options[optionIndex].Reward);
            }
            finally
            {
                applying = false;
            }
            if (!applied) return false;
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
        /// 从独立随机流抽取互不重复的三个基础项和备用第四项。
        /// </summary>
        private void Deal()
        {
            options.Clear();
            var indices = new List<int>();
            for (int index = 0; index < Pool.Length; index++) indices.Add(index);
            for (int index = 0; index < 4; index++)
            {
                int chosen = random.Next(indices.Count);
                RogueRewardDefinition reward = Pool[indices[chosen]];
                indices.RemoveAt(chosen);
                if (index < 3) options.Add(new RogueChoiceOption(index, false, reward));
                else if (policy.ExtraChoiceEnabled) options.Add(new RogueChoiceOption(3, true, reward));
            }
        }
    }
}
