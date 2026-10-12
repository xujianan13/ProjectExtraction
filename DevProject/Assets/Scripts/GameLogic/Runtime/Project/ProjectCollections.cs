using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameLogic.Project
{
    /// <summary>
    /// 收藏的稳定内容定义，由项目配置适配层提供。
    /// </summary>
    [Serializable]
    public sealed class ProjectCollectionDefinition
    {
        public string Id;
        public string Name;
        public string Series;
        public string Source;
        public int SellGold;
        public int RequiredLevel = 1;
        public ProjectItemQuality Quality;
    }

    /// <summary>
    /// 以明确收藏身份集合为条件的一次性成就定义。
    /// </summary>
    [Serializable]
    public sealed class ProjectCollectionAchievementDefinition
    {
        public string Id;
        public string Name;
        public List<string> RequiredCollectionIds = new();
        public List<ProjectAttributeValue> AttributeRewards = new();
    }

    /// <summary>
    /// 已完成收藏成就提供的独立属性来源，不直接写入永久累计字段。
    /// </summary>
    public sealed class ProjectCollectionBonusSource
    {
        private readonly List<ProjectAttributeValue> attributes;

        /// <summary>
        /// 稳定成就来源身份。
        /// </summary>
        public string AchievementId { get; }

        /// <summary>
        /// 来源显示名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 不共享配置或其他来源的属性快照。
        /// </summary>
        public IReadOnlyList<ProjectAttributeValue> Attributes =>
            new ReadOnlyCollection<ProjectAttributeValue>(attributes.ConvertAll(value => value.Copy()));

        /// <summary>
        /// 从已完成成就冻结一份属性来源。
        /// </summary>
        /// <param name="definition">已经验证的成就定义。</param>
        internal ProjectCollectionBonusSource(ProjectCollectionAchievementDefinition definition)
        {
            AchievementId = definition.Id;
            Name = definition.Name;
            attributes = definition.AttributeRewards.ConvertAll(value => value.Copy());
        }
    }

    /// <summary>
    /// 收藏实例、入库登记及成就来源规则，不依赖未生成的配置表或结算时机。
    /// </summary>
    public static class ProjectCollectionRules
    {
        /// <summary>
        /// 校验收藏定义、成就引用与属性奖励。
        /// </summary>
        /// <param name="definitions">明确的收藏定义列表。</param>
        /// <param name="achievements">明确的成就定义列表。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string Validate(IReadOnlyList<ProjectCollectionDefinition> definitions,
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements)
        {
            if (definitions == null || achievements == null) return "收藏与成就定义未提供。";
            var collectionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectCollectionDefinition definition in definitions)
            {
                string error = ValidateDefinition(definition);
                if (error != null) return error;
                if (!collectionIds.Add(definition.Id)) return "收藏定义身份重复：" + definition.Id;
            }
            var achievementIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectCollectionAchievementDefinition achievement in achievements)
            {
                if (achievement == null || string.IsNullOrWhiteSpace(achievement.Id) || string.IsNullOrWhiteSpace(achievement.Name) ||
                    !achievementIds.Add(achievement.Id) || achievement.RequiredCollectionIds == null ||
                    achievement.RequiredCollectionIds.Count == 0 || achievement.AttributeRewards == null || achievement.AttributeRewards.Count == 0)
                    return "收藏成就身份、条件或属性奖励无效。";
                var requiredIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in achievement.RequiredCollectionIds)
                    if (string.IsNullOrWhiteSpace(id) || !requiredIds.Add(id) || !collectionIds.Contains(id))
                        return "收藏成就引用缺失或重复：" + achievement.Id;
                var attributes = new HashSet<ProjectAttributeType>();
                foreach (ProjectAttributeValue reward in achievement.AttributeRewards)
                    if (reward == null || !Enum.IsDefined(typeof(ProjectAttributeType), reward.Attribute) || !attributes.Add(reward.Attribute) ||
                        float.IsNaN(reward.Value) || float.IsInfinity(reward.Value) || reward.Value <= 0 ||
                        reward.Value > ProjectEquipmentRules.MaxAttributeValue) return "收藏成就属性奖励无效：" + achievement.Id;
            }
            return null;
        }

        /// <summary>
        /// 按明确收藏定义生成独立实例，固定字段不随机成装备属性。
        /// </summary>
        /// <param name="random">调用方持有的物品随机源，固定收藏字段不额外抽取。</param>
        /// <param name="definition">已经指定品质、等级与售价的收藏定义。</param>
        /// <returns>独立收藏实例；随机源或定义无效时为空。</returns>
        public static ProjectItem CreateItem(Random random, ProjectCollectionDefinition definition)
        {
            if (random == null || ValidateDefinition(definition) != null) return null;
            return new ProjectItem
            {
                Id = Guid.NewGuid().ToString("N"), DefinitionId = definition.Id, CollectionId = definition.Id, Name = definition.Name,
                Type = ProjectItemType.Collection, Quality = definition.Quality, RequiredLevel = definition.RequiredLevel,
                SellGold = definition.SellGold, EquipmentVersion = ProjectEquipmentRules.EquipmentVersion
            };
        }

        /// <summary>
        /// 校验收藏物品只携带稳定收藏身份，不能穿戴或拥有装备属性词条。
        /// </summary>
        /// <param name="item">输入或已保存的物品实例。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string ValidateItem(ProjectItem item)
        {
            if (item == null || item.Type != ProjectItemType.Collection || string.IsNullOrWhiteSpace(item.Id) ||
                string.IsNullOrWhiteSpace(item.DefinitionId) || string.IsNullOrWhiteSpace(item.CollectionId) || string.IsNullOrWhiteSpace(item.Name) ||
                item.RequiredLevel < 1 || item.RequiredLevel > PrototypeRules.LegacySkillRankLimit || item.SellGold <= 0 ||
                !Enum.IsDefined(typeof(ProjectItemQuality), item.Quality) || item.EquipmentVersion != ProjectEquipmentRules.EquipmentVersion ||
                item.Equipped || item.Damage != 0 || item.Range != 0 || item.BonusHealth != 0 ||
                item.BaseAttributes == null || item.BaseAttributes.Count != 0 || item.Affixes == null || item.Affixes.Count != 0 ||
                item.AdvancedAffixes == null || item.AdvancedAffixes.Count != 0) return "收藏物品身份、品质或非装备字段无效。";
            return null;
        }

        /// <summary>
        /// 校验保存身份列表的基本结构，不依赖配置是否已经加载。
        /// </summary>
        /// <param name="data">输入或已保存的玩家状态。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string ValidateSavedState(ProjectSaveData data)
        {
            if (data == null || data.CollectedCollectionIds == null || data.AchievedCollectionIds == null) return "收藏保存列表缺失。";
            var collectedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in data.CollectedCollectionIds)
                if (string.IsNullOrWhiteSpace(id) || !collectedIds.Add(id)) return "已登记收藏身份为空或重复。";
            var achievedIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in data.AchievedCollectionIds)
                if (string.IsNullOrWhiteSpace(id) || !achievedIds.Add(id)) return "已完成收藏成就身份为空或重复。";
            return null;
        }

        /// <summary>
        /// 校验保存身份引用有效，已完成成就满足其收藏条件。
        /// </summary>
        /// <param name="data">已保存状态或事务候选。</param>
        /// <param name="definitions">本版本收藏定义。</param>
        /// <param name="achievements">本版本成就定义。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string ValidateState(ProjectSaveData data, IReadOnlyList<ProjectCollectionDefinition> definitions,
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements)
        {
            string error = Validate(definitions, achievements);
            if (error != null) return error;
            error = ValidateSavedState(data);
            if (error != null) return error;
            var definitionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectCollectionDefinition definition in definitions) definitionIds.Add(definition.Id);
            var collectedIds = new HashSet<string>(data.CollectedCollectionIds, StringComparer.Ordinal);
            foreach (string id in collectedIds)
                if (!definitionIds.Contains(id)) return "已登记收藏定义缺失：" + id;
            var achievementById = new Dictionary<string, ProjectCollectionAchievementDefinition>(StringComparer.Ordinal);
            foreach (ProjectCollectionAchievementDefinition achievement in achievements) achievementById.Add(achievement.Id, achievement);
            foreach (string id in data.AchievedCollectionIds)
            {
                if (!achievementById.TryGetValue(id, out ProjectCollectionAchievementDefinition achievement)) return "已完成收藏成就定义缺失：" + id;
                foreach (string requiredId in achievement.RequiredCollectionIds)
                    if (!collectedIds.Contains(requiredId)) return "已完成收藏成就的条件不满足：" + id;
            }
            return null;
        }

        /// <summary>
        /// 将调用方确认实际保留入库的物品登记到候选，收藏与成就身份各自只记录一次。
        /// </summary>
        /// <param name="data">统一存档入口持有的未提交候选。</param>
        /// <param name="retainedItems">调用方已确认成功带回的物品，不由本方法决定结算资格。</param>
        /// <param name="definitions">本版本明确收藏定义。</param>
        /// <param name="achievements">本版本明确成就定义。</param>
        /// <param name="error">配置、物品或保存状态无效时的原因。</param>
        /// <returns>完整校验并原子登记成功时为真，重复登记也成功但不新增身份。</returns>
        public static bool RegisterRetainedItems(ProjectSaveData data, IReadOnlyList<ProjectItem> retainedItems,
            IReadOnlyList<ProjectCollectionDefinition> definitions, IReadOnlyList<ProjectCollectionAchievementDefinition> achievements, out string error)
        {
            error = ValidateState(data, definitions, achievements);
            if (error != null) return false;
            if (retainedItems == null) { error = "实际保留物品列表缺失。"; return false; }
            var definitionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (ProjectCollectionDefinition definition in definitions) definitionIds.Add(definition.Id);
            var collectedIds = new HashSet<string>(data.CollectedCollectionIds, StringComparer.Ordinal);
            var achievedIds = new HashSet<string>(data.AchievedCollectionIds, StringComparer.Ordinal);
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            var newCollections = new List<string>();
            var newAchievements = new List<string>();
            foreach (ProjectItem item in retainedItems)
            {
                error = ProjectEquipmentRules.ValidateItem(item);
                if (error != null) return false;
                if (!itemIds.Add(item.Id)) { error = "实际保留物品实例重复：" + item.Id; return false; }
                if (item.Type != ProjectItemType.Collection) continue;
                if (!definitionIds.Contains(item.CollectionId)) { error = "物品引用的收藏定义缺失：" + item.CollectionId; return false; }
                if (collectedIds.Add(item.CollectionId)) newCollections.Add(item.CollectionId);
            }
            foreach (ProjectCollectionAchievementDefinition achievement in achievements)
            {
                if (achievedIds.Contains(achievement.Id)) continue;
                bool complete = true;
                foreach (string id in achievement.RequiredCollectionIds)
                    if (!collectedIds.Contains(id)) { complete = false; break; }
                if (complete && achievedIds.Add(achievement.Id)) newAchievements.Add(achievement.Id);
            }
            data.CollectedCollectionIds.AddRange(newCollections);
            data.AchievedCollectionIds.AddRange(newAchievements);
            error = null;
            return true;
        }

        /// <summary>
        /// 使用无回执形式调用同一登记规则，调用方可改用带错误原因的重载处理失败。
        /// </summary>
        /// <param name="data">统一存档入口持有的未提交候选。</param>
        /// <param name="retainedItems">实际保留入库的物品列表。</param>
        /// <param name="definitions">本版本收藏定义。</param>
        /// <param name="achievements">本版本成就定义。</param>
        /// <returns>完整登记成功时为真。</returns>
        public static bool RegisterRetainedItems(ProjectSaveData data, IReadOnlyList<ProjectItem> retainedItems,
            IReadOnlyList<ProjectCollectionDefinition> definitions, IReadOnlyList<ProjectCollectionAchievementDefinition> achievements) =>
            RegisterRetainedItems(data, retainedItems, definitions, achievements, out _);

        /// <summary>
        /// 根据已经保存的成就身份重建属性来源，不重新授予或累加永久属性。
        /// </summary>
        /// <param name="data">已经保存或准备提交的状态。</param>
        /// <param name="definitions">本版本收藏定义。</param>
        /// <param name="achievements">本版本成就定义。</param>
        /// <param name="error">状态或配置无效时的原因。</param>
        /// <returns>独立属性来源列表；校验失败时为空引用。</returns>
        public static IReadOnlyList<ProjectCollectionBonusSource> GetBonusSources(ProjectSaveData data,
            IReadOnlyList<ProjectCollectionDefinition> definitions, IReadOnlyList<ProjectCollectionAchievementDefinition> achievements, out string error)
        {
            error = ValidateState(data, definitions, achievements);
            if (error != null) return null;
            var achievedIds = new HashSet<string>(data.AchievedCollectionIds, StringComparer.Ordinal);
            var sources = new List<ProjectCollectionBonusSource>();
            foreach (ProjectCollectionAchievementDefinition achievement in achievements)
                if (achievedIds.Contains(achievement.Id)) sources.Add(new ProjectCollectionBonusSource(achievement));
            return new ReadOnlyCollection<ProjectCollectionBonusSource>(sources);
        }

        /// <summary>
        /// 按属性合并已经完成成就的奖励，结果作为独立来源交给角色属性计算。
        /// </summary>
        /// <param name="data">已经保存或准备提交的状态。</param>
        /// <param name="definitions">本版本收藏定义。</param>
        /// <param name="achievements">本版本成就定义。</param>
        /// <param name="error">状态或配置无效时的原因。</param>
        /// <returns>按属性种类排序的独立奖励；校验失败时为空引用。</returns>
        public static IReadOnlyList<ProjectAttributeValue> GetBonuses(ProjectSaveData data, IReadOnlyList<ProjectCollectionDefinition> definitions,
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements, out string error)
        {
            IReadOnlyList<ProjectCollectionBonusSource> sources = GetBonusSources(data, definitions, achievements, out error);
            if (sources == null) return null;
            var totals = new Dictionary<ProjectAttributeType, float>();
            foreach (ProjectCollectionBonusSource source in sources)
                foreach (ProjectAttributeValue reward in source.Attributes)
                    totals[reward.Attribute] = (totals.TryGetValue(reward.Attribute, out float value) ? value : 0) + reward.Value;
            var result = new List<ProjectAttributeValue>();
            foreach (KeyValuePair<ProjectAttributeType, float> total in totals)
                result.Add(new ProjectAttributeValue { Attribute = total.Key, Value = total.Value });
            result.Sort((left, right) => left.Attribute.CompareTo(right.Attribute));
            return new ReadOnlyCollection<ProjectAttributeValue>(result);
        }

        /// <summary>
        /// 取得已完成成就的属性汇总，错误处理可使用带原因的重载。
        /// </summary>
        /// <param name="data">已经保存或准备提交的状态。</param>
        /// <param name="definitions">本版本收藏定义。</param>
        /// <param name="achievements">本版本成就定义。</param>
        /// <returns>独立属性奖励；校验失败时为空引用。</returns>
        public static IReadOnlyList<ProjectAttributeValue> GetBonuses(ProjectSaveData data, IReadOnlyList<ProjectCollectionDefinition> definitions,
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements) => GetBonuses(data, definitions, achievements, out _);

        /// <summary>
        /// 校验独立收藏定义具备展示、实例生成和出售所需字段。
        /// </summary>
        /// <param name="definition">来自配置适配层的收藏定义。</param>
        /// <returns>错误信息，通过时为空。</returns>
        private static string ValidateDefinition(ProjectCollectionDefinition definition) => definition == null ||
            string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.Name) ||
            string.IsNullOrWhiteSpace(definition.Series) || string.IsNullOrWhiteSpace(definition.Source) || definition.SellGold <= 0 ||
            definition.RequiredLevel < 1 || definition.RequiredLevel > PrototypeRules.LegacySkillRankLimit ||
            !Enum.IsDefined(typeof(ProjectItemQuality), definition.Quality) ? "收藏定义身份、来源、品质或出售字段无效。" : null;
    }
}
