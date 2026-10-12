using System;
using System.Collections.Generic;
using GameFramework;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// v2 技能进度、独立奖励池与收藏的配置适配入口。
    /// </summary>
    public static class ProjectV2RewardConfig
    {
        /// <summary>
        /// 按身份取得加权进度规则。
        /// </summary>
        /// <param name="id">进度规则身份。</param>
        /// <returns>规则行；表未加载或身份缺失时为空。</returns>
        public static CfgRogueProgressRow Progress(int id) => GameTables.CfgRogueProgress?.GetById(id);

        /// <summary>
        /// 按身份取得 Boss 奖励规则。
        /// </summary>
        /// <param name="id">Boss 奖励规则身份。</param>
        /// <returns>规则行；表未加载或身份缺失时为空。</returns>
        public static CfgBossRewardsRow BossReward(int id) => GameTables.CfgBossRewards?.GetById(id);

        /// <summary>
        /// 按池配置顺序取得独立技能池，不包含物品候选。
        /// </summary>
        /// <param name="poolId">技能池身份。</param>
        /// <param name="error">缺表、缺引用或非法池的原因。</param>
        /// <returns>有效技能列表；失败时为空。</returns>
        public static IReadOnlyList<CfgRogueRewardsRow> SkillPool(string poolId, out string error)
        {
            error = null;
            if (GameTables.CfgRoguePools == null || GameTables.CfgRogueRewards == null || string.IsNullOrWhiteSpace(poolId))
            {
                error = "技能池配置未加载或身份为空。";
                return null;
            }
            var result = new List<CfgRogueRewardsRow>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (CfgRoguePoolsRow row in GameTables.CfgRoguePools.GetAll())
            {
                if (row.PoolID != poolId) continue;
                CfgRogueRewardsRow reward = GameTables.CfgRogueRewards.GetById(row.RewardID);
                if (reward == null || !Enum.IsDefined(typeof(RogueRewardKind), reward.Kind) || !ids.Add(reward.ID))
                {
                    error = "技能池引用缺失、重复或分类无效：" + poolId;
                    return null;
                }
                result.Add(reward);
            }
            if (result.Count == 0) error = "技能池为空：" + poolId;
            foreach (CfgRogueRewardsRow reward in result)
                if (!string.IsNullOrEmpty(reward.PrerequisiteID) && !ids.Contains(reward.PrerequisiteID))
                    error = "技能池缺少前置：" + poolId + "/" + reward.ID;
            return error == null ? result.AsReadOnly() : null;
        }

        /// <summary>
        /// 取得一份与生成表隔离的收藏定义。
        /// </summary>
        /// <param name="id">收藏身份，以收藏表为唯一来源。</param>
        /// <returns>收藏定义；未配置时为空。</returns>
        public static ProjectCollectionDefinition Collection(string id)
        {
            if (GameTables.CfgCollections == null || string.IsNullOrWhiteSpace(id)) return null;
            CfgCollectionsRow row = GameTables.CfgCollections.GetById(id);
            return row == null ? null : new ProjectCollectionDefinition
            {
                Id = row.ID, Name = row.Name, Series = row.Series, Source = row.Source, SellGold = row.SellGold,
                RequiredLevel = row.RequiredLevel, Quality = (ProjectItemQuality)row.Quality
            };
        }

        /// <summary>
        /// 取得完整的独立收藏定义列表。
        /// </summary>
        /// <returns>收藏列表；表未加载时为空。</returns>
        public static IReadOnlyList<ProjectCollectionDefinition> CollectionDefinitions()
        {
            if (GameTables.CfgCollections == null) return null;
            var result = new List<ProjectCollectionDefinition>();
            foreach (CfgCollectionsRow row in GameTables.CfgCollections.GetAll()) result.Add(Collection(row.ID));
            return result.AsReadOnly();
        }

        /// <summary>
        /// 取得完整的独立收藏成就定义列表。
        /// </summary>
        /// <param name="error">成就数组不匹配或未加载的原因。</param>
        /// <returns>成就列表；失败时为空。</returns>
        public static IReadOnlyList<ProjectCollectionAchievementDefinition> CollectionAchievements(out string error)
        {
            error = null;
            if (GameTables.CfgCollectionAchievements == null)
            {
                error = "收藏成就表未加载。";
                return null;
            }
            var result = new List<ProjectCollectionAchievementDefinition>();
            foreach (CfgCollectionAchievementsRow row in GameTables.CfgCollectionAchievements.GetAll())
            {
                if (row.CollectionIDs == null || row.Attributes == null || row.Values == null || row.Attributes.Length != row.Values.Length)
                {
                    error = "收藏成就数组不完整：" + row.ID;
                    return null;
                }
                var definition = new ProjectCollectionAchievementDefinition
                {
                    Id = row.ID, Name = row.Name, RequiredCollectionIds = new List<string>(row.CollectionIDs)
                };
                for (int index = 0; index < row.Attributes.Length; index++)
                {
                    double value = row.Values[index];
                    if (double.IsNaN(value) || double.IsInfinity(value) || value > ProjectEquipmentRules.MaxAttributeValue || value <= 0)
                    {
                        error = "收藏成就属性数值无效：" + row.ID;
                        return null;
                    }
                    definition.AttributeRewards.Add(new ProjectAttributeValue
                    {
                        Attribute = (ProjectAttributeType)row.Attributes[index], Value = (float)value
                    });
                }
                result.Add(definition);
            }
            return result.AsReadOnly();
        }

        /// <summary>
        /// 生成本轮三个完整且独立的装备与收藏实例，点击不再抽取随机数。
        /// </summary>
        /// <param name="random">由单局保存其游标的随机源。</param>
        /// <param name="lootLevel">掉落等级。</param>
        /// <param name="rewardId">Boss 奖励规则身份。</param>
        /// <param name="batchId">稳定的奖励批次身份。</param>
        /// <param name="round">从一开始的当前轮次。</param>
        /// <param name="claimedCollectionIds">本批次已领取的收藏身份。</param>
        /// <returns>三个冻结物品；缺配置或无法满足本轮构成时为空。</returns>
        public static IReadOnlyList<ProjectItem> CreateBossCandidates(Random random, int lootLevel, int rewardId,
            string batchId, int round, IReadOnlyCollection<string> claimedCollectionIds)
        {
            IReadOnlyList<ProjectItem> result = CreateBossCandidates(random, lootLevel, rewardId, batchId, round, claimedCollectionIds, out string error);
            if (error != null) GameEntry.Get<ILogModule>()?.Error("BossReward", error);
            return result;
        }

        /// <summary>
        /// 先校验全部候选来源，再按配置抽取并冻结本轮物品。
        /// </summary>
        /// <param name="random">由单局保存其游标的随机源。</param>
        /// <param name="lootLevel">掉落等级。</param>
        /// <param name="rewardId">Boss 奖励规则身份。</param>
        /// <param name="batchId">稳定的奖励批次身份。</param>
        /// <param name="round">从一开始的当前轮次。</param>
        /// <param name="claimedCollectionIds">本批次已领取的收藏身份。</param>
        /// <param name="error">缺配置或候选来源不足的原因。</param>
        /// <returns>三个冻结物品；失败时为空。</returns>
        public static IReadOnlyList<ProjectItem> CreateBossCandidates(Random random, int lootLevel, int rewardId,
            string batchId, int round, IReadOnlyCollection<string> claimedCollectionIds, out string error)
        {
            error = "Boss 奖励输入或配置无效。";
            CfgBossRewardsRow rule = BossReward(rewardId);
            if (random == null || string.IsNullOrWhiteSpace(batchId) || lootLevel < 1 || lootLevel > 1000 ||
                claimedCollectionIds == null || !ValidBossRule(rule) || round < 1 || round > rule.Rounds ||
                GameTables.CfgLootPool == null || GameTables.CfgLootQualities == null || GameTables.CfgItemDefinitions == null ||
                GameTables.CfgCollections == null || GameTables.CfgProfile == null || ProjectRulesConfig.Profile == null ||
                GameTables.CfgQualities == null || GameTables.CfgAffixes == null || GameTables.CfgAdvancedAffixes == null ||
                GameTables.CfgCombatAugments == null) return null;
            var equipment = new List<CfgLootPoolRow>();
            long equipmentWeight = 0;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                if (row.PoolID != rule.EquipmentPoolID || lootLevel < row.MinLevel || lootLevel > row.MaxLevel) continue;
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (row.Weight <= 0 || definition == null || definition.StarterEquipped ||
                    definition.Type != (int)ProjectItemType.Weapon && definition.Type != (int)ProjectItemType.Armor) return null;
                equipment.Add(row);
                equipmentWeight += row.Weight;
            }
            var qualities = new List<CfgLootQualitiesRow>();
            long qualityWeight = 0;
            foreach (CfgLootQualitiesRow row in GameTables.CfgLootQualities.GetAll())
            {
                if (row.PoolID != rule.EquipmentPoolID || row.Quality < rule.MinQuality || row.Quality > rule.MaxQuality) continue;
                if (row.Weight < 0 || ProjectRulesConfig.Quality((ProjectItemQuality)row.Quality) == null) return null;
                if (row.Weight == 0) continue;
                qualities.Add(row);
                qualityWeight += row.Weight;
            }
            var claimed = new HashSet<string>(claimedCollectionIds, StringComparer.Ordinal);
            var collections = new List<ProjectCollectionDefinition>();
            foreach (string id in rule.CollectionIDs)
            {
                ProjectCollectionDefinition definition = Collection(id);
                if (definition == null) return null;
                if (ProjectCollectionRules.Validate(new[] { definition }, Array.Empty<ProjectCollectionAchievementDefinition>()) != null) return null;
                if (definition.RequiredLevel <= lootLevel && (int)definition.Quality >= rule.MinQuality &&
                    (int)definition.Quality <= rule.MaxQuality && (rule.AllowRepeatCollections || !claimed.Contains(id))) collections.Add(definition);
            }
            if (equipment.Count == 0 || equipmentWeight <= 0 || equipmentWeight > int.MaxValue || qualityWeight <= 0 ||
                qualityWeight > int.MaxValue || collections.Count < rule.MinCollectionCandidates)
            {
                error = "Boss 奖励池无法满足本轮装备、品质或收藏构成：" + rewardId + "/" + round;
                return null;
            }
            var result = new List<ProjectItem>(3);
            int equipmentCount = 0;
            int collectionCount = 0;
            for (int index = 0; index < 3; index++)
            {
                bool chooseEquipment;
                if (equipmentCount < rule.MinEquipmentCandidates) chooseEquipment = true;
                else if (collectionCount < rule.MinCollectionCandidates) chooseEquipment = false;
                else chooseEquipment = collections.Count == 0 || random.Next(rule.EquipmentWeight + rule.CollectionWeight) < rule.EquipmentWeight;
                ProjectItem item;
                if (chooseEquipment)
                {
                    int roll = random.Next((int)equipmentWeight);
                    CfgLootPoolRow selected = equipment[0];
                    foreach (CfgLootPoolRow row in equipment)
                    {
                        if (roll < row.Weight)
                        {
                            selected = row;
                            break;
                        }
                        roll -= row.Weight;
                    }
                    roll = random.Next((int)qualityWeight);
                    int quality = qualities[0].Quality;
                    foreach (CfgLootQualitiesRow row in qualities)
                    {
                        if (roll < row.Weight)
                        {
                            quality = row.Quality;
                            break;
                        }
                        roll -= row.Weight;
                    }
                    item = ProjectEquipmentRules.CreateConfiguredItem(random, lootLevel, (ProjectItemQuality)quality, selected.DefinitionId);
                    equipmentCount++;
                }
                else
                {
                    int chosen = random.Next(collections.Count);
                    item = ProjectCollectionRules.CreateItem(random, collections[chosen]);
                    collections.RemoveAt(chosen);
                    collectionCount++;
                }
                if (item == null || ProjectEquipmentRules.ValidateItem(item) != null)
                {
                    error = "Boss 完整物品生成失败：" + rewardId;
                    return null;
                }
                result.Add(item);
            }
            for (int index = result.Count - 1; index > 0; index--)
            {
                int swap = random.Next(index + 1);
                ProjectItem item = result[index];
                result[index] = result[swap];
                result[swap] = item;
            }
            error = null;
            return result.AsReadOnly();
        }

        /// <summary>
        /// 校验新奖励表、收藏来源及关卡对奖励池的引用。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>所有 v2 奖励配置可启用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = "v2 奖励配置表未全部加载。";
            if (GameTables.CfgRogueProgress == null || GameTables.CfgRoguePools == null || GameTables.CfgBossRewards == null ||
                GameTables.CfgCollections == null || GameTables.CfgCollectionAchievements == null || GameTables.CfgRogueRewards == null ||
                GameTables.CfgLootPool == null || GameTables.CfgLootQualities == null || GameTables.CfgItemDefinitions == null ||
                GameTables.CfgLevels == null || GameTables.CfgBosses == null) return false;
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements = CollectionAchievements(out error);
            if (error != null) return false;
            IReadOnlyList<ProjectCollectionDefinition> definitions = CollectionDefinitions();
            error = ProjectCollectionRules.Validate(definitions, achievements);
            if (error != null) return false;
            if (definitions.Count == 0 || GameTables.CfgRogueProgress.GetAll().Count == 0 || GameTables.CfgBossRewards.GetAll().Count == 0)
            {
                error = "v2 收藏、进度或 Boss 奖励表为空。";
                return false;
            }
            foreach (CfgRogueProgressRow row in GameTables.CfgRogueProgress.GetAll())
                if (row.ID < 1 || row.NormalContribution < 1 || row.EliteContribution < 1 || row.Threshold < 1)
                {
                    error = "加权技能进度配置无效：" + row.ID;
                    return false;
                }
            var pools = new HashSet<string>(StringComparer.Ordinal);
            foreach (CfgRoguePoolsRow row in GameTables.CfgRoguePools.GetAll())
            {
                if (string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.PoolID) || string.IsNullOrWhiteSpace(row.RewardID))
                {
                    error = "技能池身份无效。";
                    return false;
                }
                pools.Add(row.PoolID);
            }
            foreach (string poolId in pools)
                if (SkillPool(poolId, out error) == null) return false;
            foreach (CfgItemDefinitionsRow row in GameTables.CfgItemDefinitions.GetAll())
                if (row.Type == (int)ProjectItemType.Collection ? Collection(row.CollectionID) == null : !string.IsNullOrEmpty(row.CollectionID))
                {
                    error = "物品模板收藏引用无效：" + row.ID;
                    return false;
                }
            foreach (CfgBossRewardsRow row in GameTables.CfgBossRewards.GetAll())
            {
                if (!ValidBossRule(row))
                {
                    error = "Boss 奖励规则无效：" + row.ID;
                    return false;
                }
                foreach (string id in row.CollectionIDs)
                {
                    ProjectCollectionDefinition definition = Collection(id);
                    if (definition == null || (int)definition.Quality < row.MinQuality || (int)definition.Quality > row.MaxQuality)
                    {
                        error = "Boss 收藏引用或固定品质无效：" + row.ID + "/" + id;
                        return false;
                    }
                }
                if (!row.AllowRepeatCollections && row.CollectionIDs.Length < row.Rounds - 1 + row.MinCollectionCandidates)
                {
                    error = "Boss 禁止跨轮重复时收藏数量不足：" + row.ID;
                    return false;
                }
                bool hasEquipment = false;
                bool hasQuality = false;
                foreach (CfgLootPoolRow loot in GameTables.CfgLootPool.GetAll())
                {
                    if (loot.PoolID != row.EquipmentPoolID) continue;
                    CfgItemDefinitionsRow item = ProjectRulesConfig.Item(loot.DefinitionId);
                    if (item == null || item.StarterEquipped || item.Type != (int)ProjectItemType.Weapon && item.Type != (int)ProjectItemType.Armor)
                    {
                        error = "Boss 装备池包含非装备或初始穿戴物品：" + row.ID;
                        return false;
                    }
                    if (loot.Weight > 0) hasEquipment = true;
                }
                foreach (CfgLootQualitiesRow quality in GameTables.CfgLootQualities.GetAll())
                    if (quality.PoolID == row.EquipmentPoolID && quality.Quality >= row.MinQuality &&
                        quality.Quality <= row.MaxQuality && quality.Weight > 0) hasQuality = true;
                if (!hasEquipment || !hasQuality)
                {
                    error = "Boss 装备或品质池为空：" + row.ID;
                    return false;
                }
            }
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                if (Progress(row.ProgressRuleID) == null || SkillPool(row.SkillPoolID, out error) == null)
                {
                    if (error == null) error = "关卡进度规则缺失：" + row.ID;
                    return false;
                }
                foreach (CfgBossesRow boss in GameTables.CfgBosses.GetAll())
                {
                    if (boss.PoolID != row.BossPoolID && boss.PoolID != row.FinalBossPoolID) continue;
                    CfgBossRewardsRow reward = BossReward(boss.RewardID);
                    if (reward == null || !ProjectEquipmentRules.HasLoot(reward.EquipmentPoolID, row.LootLevel))
                    {
                        error = "关卡 Boss 奖励或掉落等级缺失：" + row.ID + "/" + boss.ID;
                        return false;
                    }
                    int available = 0;
                    foreach (string id in reward.CollectionIDs)
                        if (Collection(id).RequiredLevel <= row.LootLevel) available++;
                    int needed = reward.AllowRepeatCollections ? reward.MinCollectionCandidates : reward.Rounds - 1 + reward.MinCollectionCandidates;
                    if (available < needed)
                    {
                        error = "关卡 Boss 收藏等级覆盖不足：" + row.ID;
                        return false;
                    }
                }
            }
            foreach (CfgBossesRow boss in GameTables.CfgBosses.GetAll())
            {
                if (BossReward(boss.RewardID) == null)
                {
                    error = "Boss 奖励规则引用缺失：" + boss.ID;
                    return false;
                }
                if (boss.CollectionPreviewIDs == null)
                {
                    error = "Boss 收藏预览未配置：" + boss.ID;
                    return false;
                }
                var previews = new HashSet<string>(StringComparer.Ordinal);
                foreach (string id in boss.CollectionPreviewIDs)
                    if (Collection(id) == null || !previews.Add(id))
                    {
                        error = "Boss 收藏预览身份缺失或重复：" + boss.ID;
                        return false;
                    }
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 检查单轮构成、权重、品质范围和收藏身份。
        /// </summary>
        /// <param name="row">Boss 奖励规则。</param>
        /// <returns>结构能够产生三候选时为真。</returns>
        private static bool ValidBossRule(CfgBossRewardsRow row)
        {
            if (row == null || row.ID < 1 || row.Rounds < 1 || row.Rounds > 1000 || string.IsNullOrWhiteSpace(row.EquipmentPoolID) ||
                row.CollectionIDs == null || row.EquipmentWeight < 1 || row.CollectionWeight < 1 ||
                (long)row.EquipmentWeight + row.CollectionWeight > int.MaxValue || row.MinEquipmentCandidates < 1 ||
                row.MinCollectionCandidates < 1 || row.MinEquipmentCandidates + (long)row.MinCollectionCandidates > 3 ||
                !Enum.IsDefined(typeof(ProjectItemQuality), row.MinQuality) || !Enum.IsDefined(typeof(ProjectItemQuality), row.MaxQuality) ||
                row.MinQuality > row.MaxQuality || row.CollectionIDs.Length < row.MinCollectionCandidates) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in row.CollectionIDs)
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) return false;
            return true;
        }
    }
}
