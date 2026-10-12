using System;
using System.Collections.Generic;
using GameLogic.Project;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// 可序列化的完整强化候选，不依赖窗口或配置行引用。
    /// </summary>
    [Serializable]
    public sealed class RogueRewardState
    {
        public string Id;
        public string Title;
        public string Description;
        public RogueRewardKind Kind;
        public ProjectAttributeType Attribute;
        public float Amount;
        public string AugmentId;
        public int Rank;
        public int MaxRank;

        /// <summary>
        /// 创建不共享可写字段的候选副本。
        /// </summary>
        /// <returns>独立候选副本。</returns>
        public RogueRewardState Copy() => (RogueRewardState)MemberwiseClone();

        /// <summary>
        /// 从已冻结的运行时定义创建保存数据。
        /// </summary>
        /// <param name="reward">已冻结强化定义。</param>
        /// <returns>可序列化的完整候选。</returns>
        public static RogueRewardState FromReward(RogueRewardDefinition reward) => new()
        {
            Id = reward.Id, Title = reward.Title, Description = reward.Description, Kind = reward.Kind, Attribute = reward.Attribute,
            Amount = reward.Amount, AugmentId = reward.AugmentId, Rank = reward.Rank, MaxRank = reward.MaxRank
        };

        /// <summary>
        /// 重建不可变的运行时强化定义。
        /// </summary>
        /// <returns>不共享可写状态的强化定义。</returns>
        public RogueRewardDefinition ToReward() => new(Id, Title, Description, Kind, Attribute, Amount, AugmentId, Rank, MaxRank);
    }

    /// <summary>
    /// 保存互斥组当前已经选定的强化身份。
    /// </summary>
    [Serializable]
    public sealed class RogueSelectedGroupState
    {
        public string GroupId;
        public string RewardId;

        /// <summary>
        /// 创建独立互斥组副本。
        /// </summary>
        /// <returns>独立互斥组副本。</returns>
        public RogueSelectedGroupState Copy() => (RogueSelectedGroupState)MemberwiseClone();
    }

    /// <summary>
    /// 强化选择会话的完整保存状态，列表顺序保持展示及随机推进顺序。
    /// </summary>
    [Serializable]
    public sealed class RogueChoiceState
    {
        public int Version = 1;
        public string RunId;
        public long State;
        public RogueChoicePolicy Policy;
        public List<string> PoolIds = new();
        public List<string> UsedSources = new();
        public List<string> UsedReceipts = new();
        public List<RogueRewardState> SelectedRewards = new();
        public List<RogueSelectedGroupState> SelectedGroups = new();
        public string OfferId;
        public string SourceId;
        public int Revision;
        public int RefreshCount;
        public int OfferSerial;
        public bool ExtraUnlocked;
        public bool Cancelled;
        public string PendingRequestId;
        public List<RogueRewardState> Options = new();

        /// <summary>
        /// 创建用于统一存档候选的完整深拷贝。
        /// </summary>
        /// <returns>不共享策略或列表的保存状态。</returns>
        public RogueChoiceState Copy()
        {
            var copy = (RogueChoiceState)MemberwiseClone();
            copy.Policy = Policy.Copy();
            copy.PoolIds = new List<string>(PoolIds);
            copy.UsedSources = new List<string>(UsedSources);
            copy.UsedReceipts = new List<string>(UsedReceipts);
            copy.SelectedRewards = SelectedRewards.ConvertAll(reward => reward.Copy());
            copy.SelectedGroups = SelectedGroups.ConvertAll(group => group.Copy());
            copy.Options = Options.ConvertAll(reward => reward.Copy());
            return copy;
        }
    }
}
