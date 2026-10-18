using System.Collections.Generic;

namespace GameLogic.Project
{
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 已经实际入库登记的收藏身份副本。
        /// </summary>
        public IReadOnlyList<string> CollectedCollectionIds => new List<string>(profile.CollectedCollectionIds);

        /// <summary>
        /// 已完成且持久化的收藏成就身份副本。
        /// </summary>
        public IReadOnlyList<string> AchievedCollectionIds => new List<string>(profile.AchievedCollectionIds);

        /// <summary>
        /// 当前永久收藏成就的逐项属性来源。
        /// </summary>
        public IReadOnlyList<ProjectCollectionBonusSource> CollectionBonusSources => ProjectCollectionRules.GetBonusSources(
            profile, ProjectRewardConfig.CollectionDefinitions(), ProjectRewardConfig.CollectionAchievements(out _), out _);

        /// <summary>
        /// 使用同一成长与收藏成就口径计算任意装备候选。
        /// </summary>
        /// <param name="items">需要计算的物品集合。</param>
        /// <returns>独立的完整基础属性。</returns>
        public ProjectCharacterStats CalculateCharacterStats(IReadOnlyList<ProjectItem> items) => ProjectEquipmentRules.Calculate(
            items, profile.PowerRank, profile.VitalityRank, profile.AgilityRank, ProjectCollectionRules.GetBonuses(
                profile, ProjectRewardConfig.CollectionDefinitions(), ProjectRewardConfig.CollectionAchievements(out _), out _));
    }
}
