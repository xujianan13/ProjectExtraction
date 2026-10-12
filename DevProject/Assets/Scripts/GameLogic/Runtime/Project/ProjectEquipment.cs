using System;
using System.Collections.Generic;
using GameFramework;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Project
{
    /// <summary>
    /// 物品品质，从普通到神器逐级提升。
    /// </summary>
    public enum ProjectItemQuality { Common, Uncommon, Rare, Epic, Legendary, Artifact }

    /// <summary>
    /// 首版可直接计算的基础属性种类。
    /// </summary>
    public enum ProjectAttributeType
    {
        MaxHealth, Attack, Defense, CriticalRating, CriticalDamage, DodgeChance, DamageIncrease, DamageReduction
    }

    /// <summary>
    /// 装备的基础属性数值。
    /// </summary>
    [Serializable]
    public sealed class ProjectAttributeValue
    {
        public ProjectAttributeType Attribute;
        public float Value;

        /// <summary>
        /// 创建独立属性副本。
        /// </summary>
        /// <returns>独立属性数值。</returns>
        public ProjectAttributeValue Copy() => (ProjectAttributeValue)MemberwiseClone();
    }

    /// <summary>
    /// 已冻结的随机词条；定义身份用于后续扩展处理器。
    /// </summary>
    [Serializable]
    public sealed class ProjectEquipmentAffix
    {
        public string DefinitionId;
        public ProjectAttributeType Attribute;
        public float Value;

        /// <summary>
        /// 创建独立词条副本。
        /// </summary>
        /// <returns>独立词条。</returns>
        public ProjectEquipmentAffix Copy() => (ProjectEquipmentAffix)MemberwiseClone();
    }

    /// <summary>
    /// 当前已生效的角色属性快照。
    /// </summary>
    public sealed class ProjectCharacterStats
    {
        public float MaxHealth;
        public float Attack;
        public float Defense;
        public float CriticalRating;
        public float CriticalDamage;
        public float DodgeChance;
        public float DamageIncrease;
        public float DamageReduction;
        public float AttackRange;
        public float MoveSpeed;
    }

    /// <summary>
    /// 冻结装备生成、词条注册、属性汇总与旧数据迁移规则。
    /// </summary>
    public static class ProjectEquipmentRules
    {
        public const int EquipmentVersion = 2;
        public const int LegacyEquipmentVersion = 1;
        public const float MaxAttributeValue = 1000000f;
        public const float MaxAttackRange = 100f;
        /// <summary>
        /// 创建版本二到版本三的装备迁移步骤。
        /// </summary>
        /// <returns>版本二装备迁移定义。</returns>
        public static SaveMigration CreateEquipmentMigration() => new(2, ValidateLegacyEquipmentJson, MigrateLegacyEquipmentJson);

        /// <summary>
        /// 校验旧版物品记录未被破坏。
        /// </summary>
        /// <param name="json">版本二存档 JSON。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string ValidateLegacyEquipmentJson(string json) =>
            string.IsNullOrWhiteSpace(json) ? "旧版装备存档为空。" : ProjectSaveData.Validate(new ProjectSaveCodec().Decode(json));

        /// <summary>
        /// 转换版本二旧数值和分类，不重新随机词条。
        /// </summary>
        /// <param name="json">版本二存档 JSON。</param>
        /// <returns>版本三存档 JSON。</returns>
        public static string MigrateLegacyEquipmentJson(string json)
        {
            var codec = new ProjectSaveCodec();
            ProjectSaveData data = codec.Decode(json);
            MigrateLegacyItems(data);
            return codec.Encode(data);
        }

        /// <summary>
        /// 将所有存档容器及历史结果的旧物品迁为普通品质。
        /// </summary>
        /// <param name="data">独立迁移候选。</param>
        public static void MigrateLegacyItems(ProjectSaveData data)
        {
            if (data == null) return;
            MigrateItems(data.Warehouse);
            MigrateItems(data.EntryItems);
            MigrateItems(data.Outcome?.Items);
            MigrateItems(data.LastResult?.Outcome?.Items);
            MigrateItems(data.LastResult?.RetainedItems);
            MigrateItems(data.LastResult?.LostItems);
        }

        /// <summary>
        /// 判断旧护甲类型和新护甲类型。
        /// </summary>
        /// <param name="type">物品分类。</param>
        /// <returns>是否为护甲。</returns>
        public static bool IsArmor(ProjectItemType type) => type == ProjectItemType.Equipment || type == ProjectItemType.Armor;

        /// <summary>
        /// 获取品质的中文名称。
        /// </summary>
        /// <param name="quality">物品品质。</param>
        /// <returns>品质名称。</returns>
        public static string QualityName(ProjectItemQuality quality) => ProjectRulesConfig.Quality(quality)?.Name ?? "未知";

        /// <summary>
        /// 获取品质的界面显示颜色。
        /// </summary>
        /// <param name="quality">物品品质。</param>
        /// <returns>品质显示色。</returns>
        public static Color QualityColor(ProjectItemQuality quality)
        {
            CfgQualitiesRow row = ProjectRulesConfig.Quality(quality);
            return row == null ? Color.white : new Color((float)row.ColorR, (float)row.ColorG, (float)row.ColorB);
        }

        /// <summary>
        /// 按默认掉落池的模板和品质权重生成物品。
        /// </summary>
        /// <param name="random">本局独立随机源。</param>
        /// <param name="level">掉落等级。</param>
        /// <returns>已冻结属性的物品实例。</returns>
        public static ProjectItem CreateLoot(System.Random random, int level) =>
            CreateLootFromPool(random, level, ProjectRulesConfig.Profile.DefaultLootPoolID);

        /// <summary>
        /// 从指定掉落池按权重生成冻结物品。
        /// </summary>
        /// <param name="random">本局独立随机源。</param>
        /// <param name="level">掉落等级。</param>
        /// <param name="poolId">掉落池身份。</param>
        /// <returns>生成的物品；没有可用条目时为空。</returns>
        public static ProjectItem CreateLootFromPool(System.Random random, int level, string poolId)
        {
            if (random == null || level < 1 || level > PrototypeRules.LegacySkillRankLimit || string.IsNullOrWhiteSpace(poolId)) return null;
            if (GameTables.CfgLootPool == null || GameTables.CfgItemDefinitions == null)
            {
                GameEntry.Get<ILogModule>()?.Error("Equipment", "掉落池或物品模板表未加载。");
                return null;
            }
            var eligible = new List<CfgLootPoolRow>();
            int total = 0;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (row.PoolID == poolId && level >= row.MinLevel && level <= row.MaxLevel && row.Weight > 0)
                {
                    eligible.Add(row);
                    total += row.Weight;
                }
            if (total <= 0) return null;
            int roll = random.Next(total);
            foreach (CfgLootPoolRow row in eligible)
            {
                if (roll < row.Weight)
                {
                    CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                    if (definition == null) return null;
                    if (definition.Type == (int)ProjectItemType.Collection)
                        return CreateGenerated(random, level, default, definition);
                    if (!TryRollQuality(random, poolId, out ProjectItemQuality quality)) return null;
                    return CreateGenerated(random, level, quality, definition);
                }
                roll -= row.Weight;
            }
            return null;
        }

        /// <summary>
        /// 检查指定等级与可选类型在掉落池中有可用模板。
        /// </summary>
        /// <param name="poolId">掉落池身份。</param>
        /// <param name="level">掉落等级。</param>
        /// <param name="type">可选物品类型。</param>
        /// <returns>是否有正权重的可用条目。</returns>
        public static bool HasLoot(string poolId, int level, ProjectItemType? type = null)
        {
            if (GameTables.CfgLootPool == null || string.IsNullOrWhiteSpace(poolId) || level < 1) return false;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                if (row.PoolID != poolId || row.Weight <= 0 || level < row.MinLevel || level > row.MaxLevel) continue;
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (definition != null && (!type.HasValue || definition.Type == (int)type.Value)) return true;
            }
            return false;
        }

        /// <summary>
        /// 按指定分类从默认掉落池生成一件冻结属性的奖励。
        /// </summary>
        /// <param name="random">奖励独立随机源。</param>
        /// <param name="level">掉落等级。</param>
        /// <param name="type">武器、护甲或普通物品分类。</param>
        /// <returns>生成的物品；没有可用条目时为空。</returns>
        public static ProjectItem CreateLoot(System.Random random, int level, ProjectItemType type)
        {
            if (random == null || level < 1 || level > PrototypeRules.LegacySkillRankLimit) return null;
            var eligible = new List<CfgLootPoolRow>();
            int total = 0;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (row.PoolID != ProjectRulesConfig.Profile.DefaultLootPoolID || definition == null ||
                    definition.Type != (int)type || level < row.MinLevel || level > row.MaxLevel || row.Weight <= 0) continue;
                eligible.Add(row);
                total += row.Weight;
            }
            if (total <= 0) return null;
            int roll = random.Next(total);
            foreach (CfgLootPoolRow row in eligible)
            {
                if (roll < row.Weight)
                {
                    CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                    if (definition.Type == (int)ProjectItemType.Collection) return CreateGenerated(random, level, default, definition);
                    if (!TryRollQuality(random, ProjectRulesConfig.Profile.DefaultLootPoolID, out ProjectItemQuality quality)) return null;
                    return CreateGenerated(random, level, quality, definition);
                }
                roll -= row.Weight;
            }
            return null;
        }

        /// <summary>
        /// 按品质和默认掉落池的模板生成装备。
        /// </summary>
        /// <param name="random">本局独立随机源。</param>
        /// <param name="type">装备分类。</param>
        /// <param name="level">物品等级。</param>
        /// <param name="quality">指定品质。</param>
        /// <returns>冻结属性的装备；没有对应模板时为空。</returns>
        public static ProjectItem CreateEquipment(System.Random random, ProjectItemType type, int level, ProjectItemQuality quality)
        {
            if (random == null || level < 1 || level > PrototypeRules.LegacySkillRankLimit ||
                !Enum.IsDefined(typeof(ProjectItemQuality), quality) || type != ProjectItemType.Weapon && !IsArmor(type)) return null;
            ProjectItemType normalizedType = IsArmor(type) ? ProjectItemType.Armor : ProjectItemType.Weapon;
            var eligible = new List<CfgLootPoolRow>();
            int total = 0;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
            {
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (row.PoolID != ProjectRulesConfig.Profile.DefaultLootPoolID || definition == null ||
                    definition.Type != (int)normalizedType || level < row.MinLevel || level > row.MaxLevel || row.Weight <= 0) continue;
                eligible.Add(row);
                total += row.Weight;
            }
            if (total <= 0) return null;
            int roll = random.Next(total);
            foreach (CfgLootPoolRow row in eligible)
            {
                if (roll < row.Weight) return CreateGenerated(random, level, quality, ProjectRulesConfig.Item(row.DefinitionId));
                roll -= row.Weight;
            }
            return null;
        }

        /// <summary>
        /// 根据指定模板和随机源创建已冻结属性的物品。
        /// </summary>
        /// <param name="random">商品属性使用的随机源。</param>
        /// <param name="level">商品物品等级。</param>
        /// <param name="quality">商品品质。</param>
        /// <param name="definitionId">物品模板身份。</param>
        /// <returns>独立物品实例；参数或模板无效时为空。</returns>
        public static ProjectItem CreateConfiguredItem(System.Random random, int level, ProjectItemQuality quality, string definitionId)
        {
            if (random == null || level < 1 || level > PrototypeRules.LegacySkillRankLimit ||
                !Enum.IsDefined(typeof(ProjectItemQuality), quality) || string.IsNullOrWhiteSpace(definitionId)) return null;
            return CreateGenerated(random, level, quality, ProjectRulesConfig.Item(definitionId));
        }

        /// <summary>
        /// 使用明确收藏定义创建实例，不进入装备模板或词条生成流程。
        /// </summary>
        /// <param name="random">调用方持有的物品随机源，收藏字段固定不额外抽取。</param>
        /// <param name="definition">已经确定品质、系列与售价的收藏定义。</param>
        /// <returns>独立收藏物品；定义无效时为空。</returns>
        public static ProjectItem CreateConfiguredItem(System.Random random, ProjectCollectionDefinition definition) =>
            ProjectCollectionRules.CreateItem(random, definition);

        /// <summary>
        /// 从初始模板生成新档装备。
        /// </summary>
        /// <returns>已经冻结数值的新手装备。</returns>
        public static ProjectItem CreateStarterItem() =>
            CreateGenerated(new System.Random(), ProjectRulesConfig.Profile.StartingLevel, (ProjectItemQuality)ProjectRulesConfig.Profile.StarterQuality,
                ProjectRulesConfig.Item(ProjectRulesConfig.Profile.StarterDefinitionId));

        /// <summary>
        /// 按指定掉落池的品质权重抽取品质。
        /// </summary>
        /// <param name="random">随机源。</param>
        /// <param name="poolId">掉落池身份。</param>
        /// <param name="quality">抽取的品质。</param>
        /// <returns>品质池完整且有正权重时为真。</returns>
        private static bool TryRollQuality(System.Random random, string poolId, out ProjectItemQuality quality)
        {
            quality = ProjectItemQuality.Common;
            if (GameTables.CfgLootQualities == null)
            {
                GameEntry.Get<ILogModule>()?.Error("Equipment", "掉落品质表未加载。");
                return false;
            }
            long total = 0;
            var rows = new List<CfgLootQualitiesRow>();
            var qualities = new HashSet<int>();
            foreach (CfgLootQualitiesRow row in GameTables.CfgLootQualities.GetAll())
            {
                if (row == null || row.PoolID != poolId) continue;
                if (!Enum.IsDefined(typeof(ProjectItemQuality), row.Quality) || row.Weight < 0 || !qualities.Add(row.Quality))
                {
                    GameEntry.Get<ILogModule>()?.Error("Equipment", "掉落池品质配置无效：" + poolId);
                    return false;
                }
                if (row.Weight <= 0) continue;
                rows.Add(row);
                total += row.Weight;
            }
            if (qualities.Count != Enum.GetValues(typeof(ProjectItemQuality)).Length || total <= 0 || total > int.MaxValue)
            {
                GameEntry.Get<ILogModule>()?.Error("Equipment", "掉落池品质缺失或权重无效：" + poolId);
                return false;
            }
            int roll = random.Next((int)total);
            foreach (CfgLootQualitiesRow row in rows)
            {
                if (roll < row.Weight)
                {
                    quality = (ProjectItemQuality)row.Quality;
                    return true;
                }
                roll -= row.Weight;
            }
            return false;
        }

        /// <summary>
        /// 按模板创建带稳定来源和冻结数值的物品。
        /// </summary>
        /// <param name="random">随机源。</param>
        /// <param name="level">物品等级。</param>
        /// <param name="quality">物品品质。</param>
        /// <param name="definition">物品模板。</param>
        /// <returns>新物品实例；无模板时为空。</returns>
        private static ProjectItem CreateGenerated(System.Random random, int level, ProjectItemQuality quality, CfgItemDefinitionsRow definition)
        {
            if (definition == null || random == null) return null;
            if (definition.Type == (int)ProjectItemType.Collection)
            {
                ProjectCollectionDefinition collection = ProjectV2RewardConfig.Collection(definition.CollectionID);
                ProjectItem retained = ProjectCollectionRules.CreateItem(random, collection);
                if (retained != null) retained.DefinitionId = definition.ID;
                return retained;
            }
            int qualityIndex = (int)quality;
            var item = new ProjectItem
            {
                Id = Guid.NewGuid().ToString("N"), DefinitionId = definition.ID, Name = definition.Name,
                Type = (ProjectItemType)definition.Type, Quality = quality, EquipmentVersion = EquipmentVersion,
                RequiredLevel = level, Equipped = definition.StarterEquipped,
                SellGold = definition.SellBase + level * definition.SellPerLevel +
                    (definition.SellRandomExclusive > 0 ? random.Next(definition.SellRandomExclusive) : 0) +
                    qualityIndex * definition.SellPerQuality
            };
            if (item.Type == ProjectItemType.Weapon)
            {
                float attack = (float)(definition.AttackBase + level * definition.AttackPerLevel +
                    (definition.AttackRandomExclusive > 0 ? random.Next(definition.AttackRandomExclusive) : 0));
                item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Attack, Value = attack });
                item.Range = (float)(definition.RangeBase + random.NextDouble() * definition.RangeRandom);
                item.Damage = attack;
                if (definition.HealthBase > 0 || definition.HealthPerLevel > 0 || definition.HealthRandomExclusive > 0)
                {
                    float health = (float)(definition.HealthBase + level * definition.HealthPerLevel +
                        (definition.HealthRandomExclusive > 0 ? random.Next(definition.HealthRandomExclusive) : 0));
                    item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.MaxHealth, Value = health });
                }
                if (definition.DefenseBase > 0 || definition.DefensePerLevel > 0 || definition.DefenseRandomMin > 0 ||
                    definition.DefenseRandomExclusive > definition.DefenseRandomMin)
                {
                    float defense = (float)(definition.DefenseBase + level * definition.DefensePerLevel +
                        (definition.DefenseRandomExclusive > definition.DefenseRandomMin ?
                            random.Next(definition.DefenseRandomMin, definition.DefenseRandomExclusive) : definition.DefenseRandomMin));
                    item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Defense, Value = defense });
                }
            }
            else if (IsArmor(item.Type))
            {
                float health = (float)(definition.HealthBase + level * definition.HealthPerLevel +
                    (definition.HealthRandomExclusive > 0 ? random.Next(definition.HealthRandomExclusive) : 0));
                item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.MaxHealth, Value = health });
                float defense = (float)(definition.DefenseBase + level * definition.DefensePerLevel +
                    (definition.DefenseRandomExclusive > definition.DefenseRandomMin ?
                        random.Next(definition.DefenseRandomMin, definition.DefenseRandomExclusive) : definition.DefenseRandomMin));
                item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Defense, Value = defense });
                item.BonusHealth = (int)health;
            }
            if (item.Type == ProjectItemType.Item) return item;
            CfgQualitiesRow qualityRow = ProjectRulesConfig.Quality(quality);
            var pool = new List<CfgAffixesRow>(GameTables.CfgAffixes.GetAll());
            for (int index = 0; index < qualityRow.AffixCount; index++)
            {
                int chosen = random.Next(pool.Count);
                CfgAffixesRow affix = pool[chosen];
                pool.RemoveAt(chosen);
                float scale = (float)(qualityRow.ScaleMin + random.NextDouble() * (qualityRow.ScaleMax - qualityRow.ScaleMin));
                float value = (float)(affix.Scale * scale * (1 + (level - 1) * ProjectRulesConfig.Profile.AffixLevelScale));
                item.Affixes.Add(new ProjectEquipmentAffix
                {
                    DefinitionId = affix.ID, Attribute = (ProjectAttributeType)affix.Attribute, Value = (float)Math.Round(value, 3)
                });
            }
            int advancedCount = item.Type == ProjectItemType.Item ? 0 : qualityRow.AdvancedAffixCount;
            if (!ProjectAdvancedAffixRules.TryRoll(random, item.Type, quality, advancedCount, out List<ProjectAdvancedAffix> advanced))
            {
                GameEntry.Get<ILogModule>()?.Error("Equipment", "高级词条配置不足：" + item.Type + "/" + quality);
                return null;
            }
            item.AdvancedAffixes = advanced;
            return item;
        }

        /// <summary>
        /// 汇总单个物品已生效的基础属性和注册词条。
        /// </summary>
        /// <param name="item">物品实例。</param>
        /// <returns>按属性合并的独立属性列表。</returns>
        public static IReadOnlyList<ProjectAttributeValue> GetItemAttributes(ProjectItem item)
        {
            var values = new Dictionary<ProjectAttributeType, float>();
            if (item == null) return new List<ProjectAttributeValue>();
            if (item.EquipmentVersion == 0)
            {
                if (item.Type == ProjectItemType.Weapon) values[ProjectAttributeType.Attack] = item.Damage;
                if (IsArmor(item.Type)) values[ProjectAttributeType.MaxHealth] = item.BonusHealth;
            }
            else
            {
                if (item.BaseAttributes != null)
                    foreach (ProjectAttributeValue attribute in item.BaseAttributes)
                        if (attribute != null) Add(values, attribute.Attribute, attribute.Value);
                if (item.Affixes != null)
                    foreach (ProjectEquipmentAffix affix in item.Affixes)
                        if (affix != null && KnownFrozenAffix(affix))
                            Add(values, affix.Attribute, affix.Value);
            }
            var result = new List<ProjectAttributeValue>();
            foreach (KeyValuePair<ProjectAttributeType, float> value in values)
                result.Add(new ProjectAttributeValue { Attribute = value.Key, Value = value.Value });
            return result;
        }

        /// <summary>
        /// 汇总已穿戴装备和局外技能的角色属性。
        /// </summary>
        /// <param name="items">可携带物品。</param>
        /// <param name="powerRank">力量技能等级。</param>
        /// <param name="vitalityRank">生存技能等级。</param>
        /// <param name="agilityRank">敏捷技能等级。</param>
        /// <param name="persistentBonuses">已经完成收藏成就的独立属性来源。</param>
        /// <returns>用于战斗接入的独立角色属性。</returns>
        public static ProjectCharacterStats Calculate(IReadOnlyList<ProjectItem> items, int powerRank, int vitalityRank, int agilityRank,
            IReadOnlyList<ProjectAttributeValue> persistentBonuses = null)
        {
            var stats = new ProjectCharacterStats
            {
                MaxHealth = (float)ProjectRulesConfig.Profile.BaseMaxHealth, CriticalDamage = (float)ProjectRulesConfig.Profile.BaseCriticalDamage,
                AttackRange = (float)ProjectRulesConfig.Profile.BaseAttackRange, MoveSpeed = (float)ProjectRulesConfig.Profile.BaseMoveSpeed
            };
            bool weaponEquipped = false;
            if (items != null)
                foreach (ProjectItem item in items)
                {
                    if (item == null || !item.Equipped) continue;
                    if (item.Type == ProjectItemType.Weapon)
                    {
                        weaponEquipped = true;
                        stats.AttackRange = item.Range > 0 ? item.Range : (float)ProjectRulesConfig.Profile.BaseAttackRange;
                    }
                    foreach (ProjectAttributeValue attribute in GetItemAttributes(item)) Apply(stats, attribute);
                }
            if (!weaponEquipped) stats.Attack += (float)ProjectRulesConfig.Profile.UnarmedAttack;
            stats.Attack *= 1 + Math.Min(ProjectRulesConfig.Skill("power").MaxRank, Math.Max(0, powerRank)) * (float)ProjectRulesConfig.Skill("power").EffectPerRank;
            stats.MaxHealth += Math.Min(ProjectRulesConfig.Skill("vitality").MaxRank, Math.Max(0, vitalityRank)) * (float)ProjectRulesConfig.Skill("vitality").EffectPerRank;
            stats.MoveSpeed *= 1 + Math.Min(ProjectRulesConfig.Skill("agility").MaxRank, Math.Max(0, agilityRank)) * (float)ProjectRulesConfig.Skill("agility").EffectPerRank;
            if (persistentBonuses != null)
                foreach (ProjectAttributeValue bonus in persistentBonuses) Apply(stats, bonus);
            stats.MaxHealth = Math.Min((float)ProjectRulesConfig.Profile.MaxAttributeValue, Math.Max(1, stats.MaxHealth));
            stats.Attack = Math.Min((float)ProjectRulesConfig.Profile.MaxAttributeValue, Math.Max(0, stats.Attack));
            stats.Defense = Math.Min((float)ProjectRulesConfig.Profile.MaxAttributeValue, Math.Max(0, stats.Defense));
            stats.CriticalRating = Math.Min((float)ProjectRulesConfig.Profile.MaxAttributeValue, Math.Max(0, stats.CriticalRating));
            stats.CriticalDamage = Math.Min((float)ProjectRulesConfig.Profile.MaxAttributeValue, Math.Max(1, stats.CriticalDamage));
            stats.DodgeChance = Math.Min((float)ProjectRulesConfig.Profile.MaxDodgeChance, Math.Max(0, stats.DodgeChance));
            stats.DamageIncrease = Math.Min((float)ProjectRulesConfig.Profile.MaxAttributeValue, Math.Max(0, stats.DamageIncrease));
            stats.DamageReduction = Math.Min((float)ProjectRulesConfig.Profile.MaxDamageReduction, Math.Max(0, stats.DamageReduction));
            stats.AttackRange = Math.Min((float)ProjectRulesConfig.Profile.MaxAttackRange, Math.Max(0, stats.AttackRange));
            return stats;
        }

        /// <summary>
        /// 校验实例、品质、基础属性与词条注册身份。
        /// </summary>
        /// <param name="item">待校验物品。</param>
        /// <returns>错误信息，通过时为空。</returns>
        public static string ValidateItem(ProjectItem item)
        {
            if (item == null || string.IsNullOrEmpty(item.Id) || item.RequiredLevel < 1 || item.RequiredLevel > 1000 ||
                item.SellGold < 0 || item.Type == ProjectItemType.Item && item.Equipped ||
                item.BonusHealth < 0 || item.BonusHealth > MaxAttributeValue ||
                !Enum.IsDefined(typeof(ProjectItemType), item.Type) || !FiniteNonNegative(item.Damage) ||
                item.Damage > MaxAttributeValue || !FiniteNonNegative(item.Range) || item.Range > MaxAttackRange) return "物品基础字段无效。";
            if (item.Type == ProjectItemType.Collection) return ProjectCollectionRules.ValidateItem(item);
            if (!string.IsNullOrEmpty(item.CollectionId)) return "非收藏物品不能携带收藏身份。";
            if (item.EquipmentVersion == 0)
            {
                if (item.Quality != ProjectItemQuality.Common || item.Type == ProjectItemType.Armor ||
                    item.BaseAttributes != null && item.BaseAttributes.Count != 0 ||
                    item.Affixes != null && item.Affixes.Count != 0 ||
                    item.AdvancedAffixes != null && item.AdvancedAffixes.Count != 0) return "旧版物品包含新装备字段。";
                return null;
            }
            if (item.Type == ProjectItemType.Equipment) return "新版护甲分类无效。";
            if (item.EquipmentVersion != LegacyEquipmentVersion && item.EquipmentVersion != EquipmentVersion ||
                !Enum.IsDefined(typeof(ProjectItemQuality), item.Quality) ||
                item.BaseAttributes == null || item.Affixes == null) return "物品装备版本或品质无效。";
            if (item.Type == ProjectItemType.Item && (item.BaseAttributes.Count != 0 || item.Affixes.Count != 0)) return "材料不能携带装备属性。";
            var bases = new HashSet<ProjectAttributeType>();
            foreach (ProjectAttributeValue value in item.BaseAttributes)
                if (value == null || !Enum.IsDefined(typeof(ProjectAttributeType), value.Attribute) ||
                    !FiniteNonNegative(value.Value) || value.Value > MaxAttributeValue || !bases.Add(value.Attribute)) return "装备基础属性重复或无效。";
            var affixes = new HashSet<ProjectAttributeType>();
            foreach (ProjectEquipmentAffix affix in item.Affixes)
                if (affix == null || !Enum.IsDefined(typeof(ProjectAttributeType), affix.Attribute) ||
                    !KnownFrozenAffix(affix) || !FiniteNonNegative(affix.Value) ||
                    affix.Value > MaxAttributeValue || !affixes.Add(affix.Attribute)) return "装备词条重复或未注册。";
            if (item.Affixes.Count > Enum.GetValues(typeof(ProjectAttributeType)).Length) return "装备词条超过品质上限。";
            if (item.EquipmentVersion == LegacyEquipmentVersion)
                return item.AdvancedAffixes == null || item.AdvancedAffixes.Count == 0 ? null : "旧版装备不能包含高级词条。";
            return ProjectAdvancedAffixRules.Validate(item);
        }

        /// <summary>
        /// 将容器内旧实例按已保存数值转换为新属性。
        /// </summary>
        /// <param name="items">旧物品集合。</param>
        private static void MigrateItems(List<ProjectItem> items)
        {
            if (items == null) return;
            foreach (ProjectItem item in items)
            {
                if (item == null || item.EquipmentVersion != 0) continue;
                if (IsArmor(item.Type)) item.Type = ProjectItemType.Armor;
                item.Quality = ProjectItemQuality.Common;
                item.EquipmentVersion = LegacyEquipmentVersion;
                item.BaseAttributes = new List<ProjectAttributeValue>();
                item.Affixes = new List<ProjectEquipmentAffix>();
                item.AdvancedAffixes = new List<ProjectAdvancedAffix>();
                if (item.Type == ProjectItemType.Weapon)
                    item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Attack, Value = item.Damage });
                else if (IsArmor(item.Type) && item.BonusHealth > 0)
                    item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.MaxHealth, Value = item.BonusHealth });
            }
        }

        /// <summary>
        /// 将注册属性数值累加到物品合并视图。
        /// </summary>
        /// <param name="values">待写入的属性集合。</param>
        /// <param name="attribute">属性种类。</param>
        /// <param name="amount">附加数值。</param>
        private static void Add(Dictionary<ProjectAttributeType, float> values, ProjectAttributeType attribute, float amount)
        {
            values.TryGetValue(attribute, out float current);
            values[attribute] = current + amount;
        }

        /// <summary>
        /// 应用已合并的基础属性。
        /// </summary>
        /// <param name="stats">可变角色属性。</param>
        /// <param name="attribute">单项属性。</param>
        private static void Apply(ProjectCharacterStats stats, ProjectAttributeValue attribute)
        {
            switch (attribute.Attribute)
            {
                case ProjectAttributeType.MaxHealth: stats.MaxHealth += attribute.Value; break;
                case ProjectAttributeType.Attack: stats.Attack += attribute.Value; break;
                case ProjectAttributeType.Defense: stats.Defense += attribute.Value; break;
                case ProjectAttributeType.CriticalRating: stats.CriticalRating += attribute.Value; break;
                case ProjectAttributeType.CriticalDamage: stats.CriticalDamage += attribute.Value; break;
                case ProjectAttributeType.DodgeChance: stats.DodgeChance += attribute.Value; break;
                case ProjectAttributeType.DamageIncrease: stats.DamageIncrease += attribute.Value; break;
                case ProjectAttributeType.DamageReduction: stats.DamageReduction += attribute.Value; break;
            }
        }

        /// <summary>
        /// 识别已冻结且带来源身份的词条，不依赖当前词条表。
        /// </summary>
        /// <param name="affix">已保存词条。</param>
        /// <returns>词条身份与属性种类是否具备稳定结构。</returns>
        private static bool KnownFrozenAffix(ProjectEquipmentAffix affix) =>
            !string.IsNullOrWhiteSpace(affix.DefinitionId) && Enum.IsDefined(typeof(ProjectAttributeType), affix.Attribute);

        /// <summary>
        /// 判断随机和存档数值有限且非负。
        /// </summary>
        /// <param name="value">待检数值。</param>
        /// <returns>数值是否有效。</returns>
        private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
    }
}
