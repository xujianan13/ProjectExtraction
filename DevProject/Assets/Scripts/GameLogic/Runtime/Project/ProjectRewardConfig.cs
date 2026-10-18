using System;
using System.Collections.Generic;
using System.Globalization;
using GameFramework;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// Boss 奖励、收藏和收藏成就的配置适配入口。
    /// </summary>
    public static class ProjectRewardConfig
    {
        /// <summary>
        /// 按身份取得 Boss 奖励规则。
        /// </summary>
        /// <param name="id">Boss 奖励规则身份。</param>
        /// <returns>规则行；表未加载或身份缺失时为空。</returns>
        public static CfgBossRewardsRow BossReward(int id) => GameTables.CfgBossRewards?.GetById(id);

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
        /// 按配置一次生成整箱实际物品，领取时不再抽取随机数。
        /// </summary>
        /// <param name="random">由单局保存其游标的随机源。</param>
        /// <param name="lootLevel">掉落等级。</param>
        /// <param name="rewardId">Boss 奖励规则身份。</param>
        /// <param name="batchId">稳定的奖励批次身份。</param>
        /// <returns>整箱冻结物品；缺配置或无法满足构成时为空。</returns>
        public static IReadOnlyList<ProjectItem> CreateBossLoot(Random random, int lootLevel, int rewardId, string batchId)
        {
            IReadOnlyList<ProjectItem> result = CreateBossLoot(random, lootLevel, rewardId, batchId, out string error);
            if (error != null) GameEntry.Get<ILogModule>()?.Error("BossReward", error);
            return result;
        }

        /// <summary>
        /// 先完整校验物品来源，再按数量、最低组成和权重冻结一批实际掉落。
        /// </summary>
        /// <param name="random">由单局保存其游标的随机源。</param>
        /// <param name="lootLevel">掉落等级。</param>
        /// <param name="rewardId">Boss 奖励规则身份。</param>
        /// <param name="batchId">稳定的奖励批次身份。</param>
        /// <param name="error">缺配置或掉落来源不足的原因。</param>
        /// <returns>数量等于配置且身份稳定的整箱物品；失败时为空。</returns>
        public static IReadOnlyList<ProjectItem> CreateBossLoot(Random random, int lootLevel, int rewardId, string batchId, out string error)
        {
            error = "Boss 奖励输入或配置无效。";
            CfgBossRewardsRow rule = BossReward(rewardId);
            if (random == null || string.IsNullOrWhiteSpace(batchId) || lootLevel < 1 || lootLevel > 1000 ||
                !ValidBossRule(rule) ||
                GameTables.CfgLootPool == null || GameTables.CfgLootQualities == null || GameTables.CfgItemDefinitions == null ||
                GameTables.CfgCollections == null || GameTables.CfgProfile == null || ProjectRulesConfig.Profile == null ||
                GameTables.CfgQualities == null || GameTables.CfgAffixes == null || GameTables.CfgAdvancedAffixes == null ||
                GameTables.CfgCombatAugments == null) return null;
            if (!ProjectRulesConfig.Validate(out error)) return null;
            var equipment = new List<CfgLootPoolRow>();
            long equipmentWeight = 0;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                if (row.PoolID != rule.EquipmentPoolID || lootLevel < row.MinLevel || lootLevel > row.MaxLevel) continue;
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (row.Weight < 0 || definition == null || definition.StarterEquipped ||
                    definition.Type != (int)ProjectItemType.Weapon && definition.Type != (int)ProjectItemType.Armor)
                { error = "Boss 装备掉落来源无效：" + row.ID; return null; }
                if (row.Weight == 0) continue;
                equipment.Add(row);
                equipmentWeight += row.Weight;
            }
            var qualities = new List<CfgLootQualitiesRow>();
            long qualityWeight = 0;
            foreach (CfgLootQualitiesRow row in GameTables.CfgLootQualities.GetAll())
            {
                if (row.PoolID != rule.EquipmentPoolID || row.Quality < rule.MinQuality || row.Quality > rule.MaxQuality) continue;
                if (row.Weight < 0 || ProjectRulesConfig.Quality((ProjectItemQuality)row.Quality) == null)
                { error = "Boss 品质掉落来源无效：" + row.ID; return null; }
                if (row.Weight == 0) continue;
                qualities.Add(row);
                qualityWeight += row.Weight;
            }
            var collections = new List<ProjectCollectionDefinition>();
            foreach (string id in rule.CollectionIDs)
            {
                ProjectCollectionDefinition definition = Collection(id);
                if (definition == null || ProjectCollectionRules.Validate(new[] { definition }, Array.Empty<ProjectCollectionAchievementDefinition>()) != null)
                { error = "Boss 收藏掉落来源无效：" + id; return null; }
                if (definition.RequiredLevel <= lootLevel && (int)definition.Quality >= rule.MinQuality &&
                    (int)definition.Quality <= rule.MaxQuality) collections.Add(definition);
            }
            if (equipment.Count == 0 || equipmentWeight <= 0 || equipmentWeight > int.MaxValue || qualityWeight <= 0 ||
                qualityWeight > int.MaxValue || collections.Count < RequiredUniqueCollections(rule))
            {
                error = "Boss 奖励池无法满足装备、品质或收藏构成：" + rewardId;
                return null;
            }
            var result = new List<ProjectItem>(rule.ItemCount);
            int equipmentCount = 0;
            int collectionCount = 0;
            for (int index = 0; index < rule.ItemCount; index++)
            {
                bool chooseEquipment;
                if (equipmentCount < rule.MinEquipmentItems) chooseEquipment = true;
                else if (collectionCount < rule.MinCollectionItems) chooseEquipment = false;
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
                    if (!rule.AllowRepeatCollections) collections.RemoveAt(chosen);
                    collectionCount++;
                }
                if (item == null || ProjectEquipmentRules.ValidateItem(item) != null)
                {
                    error = "Boss 完整物品生成失败：" + rewardId;
                    return null;
                }
                result.Add(item);
            }
            for (int index = 0; index < result.Count; index++) result[index].Id = batchId + ":item:" + index.ToString(CultureInfo.InvariantCulture);
            error = null;
            return result.AsReadOnly();
        }

        /// <summary>
        /// 校验新奖励表、收藏来源及关卡对奖励池的引用。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>全部 Boss 奖励和收藏配置可启用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = "Boss 奖励与收藏配置表未全部加载。";
            if (GameTables.CfgBossRewards == null || GameTables.CfgCollections == null || GameTables.CfgCollectionAchievements == null ||
                GameTables.CfgLootPool == null || GameTables.CfgLootQualities == null || GameTables.CfgItemDefinitions == null ||
                GameTables.CfgLevels == null || GameTables.CfgBosses == null || GameTables.CfgRooms == null) return false;
            IReadOnlyList<ProjectCollectionAchievementDefinition> achievements = CollectionAchievements(out error);
            if (error != null) return false;
            IReadOnlyList<ProjectCollectionDefinition> definitions = CollectionDefinitions();
            error = ProjectCollectionRules.Validate(definitions, achievements);
            if (error != null) return false;
            if (definitions.Count == 0 || GameTables.CfgBossRewards.GetAll().Count == 0)
            {
                error = "收藏或 Boss 奖励表为空。";
                return false;
            }
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
            foreach (CfgLevelsRow level in GameTables.CfgLevels.GetAll())
            {
                if (level.RoomIDs == null || level.RoomIDs.Length == 0)
                { error = "关卡房间序列缺失：" + level.ID; return false; }
                foreach (int roomId in level.RoomIDs)
                {
                    CfgRoomsRow room = GameTables.CfgRooms.GetById(roomId);
                    if (room == null) { error = "关卡房间引用缺失：" + level.ID + "/" + roomId; return false; }
                    if (room.Kind != (int)GameLogic.Gameplay.Level.ExpeditionRoomKind.Boss) continue;
                    CfgBossesRow boss = GameTables.CfgBosses.GetById(room.BossID);
                    CfgBossRewardsRow reward = boss == null ? null : BossReward(boss.RewardID);
                    if (reward == null || !ProjectEquipmentRules.HasLoot(reward.EquipmentPoolID, level.LootLevel))
                    { error = "关卡 Boss 奖励或掉落等级缺失：" + level.ID + "/" + roomId; return false; }
                    int available = 0;
                    foreach (string id in reward.CollectionIDs)
                        if (Collection(id).RequiredLevel <= level.LootLevel) available++;
                    int needed = RequiredUniqueCollections(reward);
                    if (available < needed)
                    { error = "关卡 Boss 收藏等级覆盖不足：" + level.ID; return false; }
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
        /// 检查整箱数量、最低组成、权重、品质范围和收藏身份。
        /// </summary>
        /// <param name="row">Boss 奖励规则。</param>
        /// <returns>结构能够产生配置数量的实际物品时为真。</returns>
        private static bool ValidBossRule(CfgBossRewardsRow row)
        {
            if (row == null || row.ID < 1 || row.ItemCount < 1 || row.ItemCount > 20 || string.IsNullOrWhiteSpace(row.EquipmentPoolID) ||
                row.CollectionIDs == null || row.EquipmentWeight < 1 || row.CollectionWeight < 1 ||
                (long)row.EquipmentWeight + row.CollectionWeight > int.MaxValue || row.MinEquipmentItems < 0 ||
                row.MinCollectionItems < 0 || row.MinEquipmentItems + (long)row.MinCollectionItems > row.ItemCount ||
                !Enum.IsDefined(typeof(ProjectItemQuality), row.MinQuality) || !Enum.IsDefined(typeof(ProjectItemQuality), row.MaxQuality) ||
                row.MinQuality > row.MaxQuality || row.CollectionIDs.Length < RequiredUniqueCollections(row)) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in row.CollectionIDs)
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) return false;
            return true;
        }

        /// <summary>
        /// 计算本批最低收藏组成所需的不同收藏来源数。
        /// </summary>
        /// <param name="row">已经检查数量和组成的奖励规则。</param>
        /// <returns>允许重复时最多需要一个来源，否则等于最低收藏件数。</returns>
        private static int RequiredUniqueCollections(CfgBossRewardsRow row) => row.AllowRepeatCollections
            ? row.MinCollectionItems > 0 ? 1 : 0 : row.MinCollectionItems;
    }
}
