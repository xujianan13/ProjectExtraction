using System;
using System.Collections.Generic;
using GameFramework;
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
        public const int EquipmentVersion = 1;
        public const float MaxAttributeValue = 1000000f;
        public const float MaxAttackRange = 100f;
        private static readonly ProjectAttributeType[] AffixPool =
        {
            ProjectAttributeType.MaxHealth, ProjectAttributeType.Attack, ProjectAttributeType.Defense,
            ProjectAttributeType.CriticalRating, ProjectAttributeType.CriticalDamage, ProjectAttributeType.DodgeChance,
            ProjectAttributeType.DamageIncrease, ProjectAttributeType.DamageReduction
        };
        private static readonly int[] QualityWeights = { 60, 22, 10, 5, 2, 1 };
        private static readonly int[] QualityAffixCounts = { 0, 1, 2, 3, 4, 5 };
        private const int WeaponAttackRandomExclusive = 6;
        private const int ArmorHealthRandomExclusive = 6;
        private const int ArmorDefenseRandomMin = 1;
        private const int ArmorDefenseRandomMaxExclusive = 4;
        private static readonly float[] QualityScaleMin = { 0, 0.7f, 1f, 1.3f, 1.7f, 2.2f };
        private static readonly float[] QualityScaleMax = { 0, 1.1f, 1.5f, 1.9f, 2.4f, 3f };
        private static readonly Dictionary<ProjectAttributeType, float> AttributeScale = new()
        {
            [ProjectAttributeType.MaxHealth] = 5f, [ProjectAttributeType.Attack] = 2f,
            [ProjectAttributeType.Defense] = 1.5f, [ProjectAttributeType.CriticalRating] = 3f,
            [ProjectAttributeType.CriticalDamage] = 0.04f, [ProjectAttributeType.DodgeChance] = 0.015f,
            [ProjectAttributeType.DamageIncrease] = 0.025f, [ProjectAttributeType.DamageReduction] = 0.02f
        };

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
        public static string QualityName(ProjectItemQuality quality) => quality switch
        {
            ProjectItemQuality.Common => "普通", ProjectItemQuality.Uncommon => "精良", ProjectItemQuality.Rare => "优秀",
            ProjectItemQuality.Epic => "史诗", ProjectItemQuality.Legendary => "传说", ProjectItemQuality.Artifact => "神器",
            _ => "未知"
        };

        /// <summary>
        /// 获取品质的界面显示颜色。
        /// </summary>
        /// <param name="quality">物品品质。</param>
        /// <returns>品质显示色。</returns>
        public static Color QualityColor(ProjectItemQuality quality) => quality switch
        {
            ProjectItemQuality.Common => Color.white,
            ProjectItemQuality.Uncommon => new Color(0.28f, 0.82f, 0.35f),
            ProjectItemQuality.Rare => new Color(0.30f, 0.56f, 1f),
            ProjectItemQuality.Epic => new Color(0.68f, 0.36f, 0.92f),
            ProjectItemQuality.Legendary => new Color(1f, 0.62f, 0.20f),
            ProjectItemQuality.Artifact => new Color(0.96f, 0.22f, 0.24f),
            _ => Color.white
        };

        /// <summary>
        /// 按三类原型掉落权重和六品质权重生成物品。
        /// </summary>
        /// <param name="random">本局独立随机源。</param>
        /// <param name="level">掉落等级。</param>
        /// <returns>已冻结属性的物品实例。</returns>
        public static ProjectItem CreateLoot(System.Random random, int level)
        {
            if (random == null) return null;
            int typeRoll = random.Next(3);
            ProjectItemType type = typeRoll == 0 ? ProjectItemType.Weapon : typeRoll == 1 ? ProjectItemType.Armor : ProjectItemType.Item;
            return CreateLoot(random, level, type);
        }

        /// <summary>
        /// 按指定分类和现有品质权重生成一件冻结属性的奖励。
        /// </summary>
        /// <param name="random">奖励独立随机源。</param>
        /// <param name="level">掉落等级。</param>
        /// <param name="type">武器、护甲或普通物品分类。</param>
        /// <returns>已冻结的奖励物品，输入无效时为空。</returns>
        public static ProjectItem CreateLoot(System.Random random, int level, ProjectItemType type)
        {
            if (random == null || level < 1 || level > 1000 ||
                type != ProjectItemType.Weapon && type != ProjectItemType.Armor && type != ProjectItemType.Item) return null;
            int qualityRoll = random.Next(100);
            int qualityIndex = 0;
            while (qualityIndex < QualityWeights.Length - 1 && qualityRoll >= QualityWeights[qualityIndex])
            {
                qualityRoll -= QualityWeights[qualityIndex];
                qualityIndex++;
            }
            ProjectItemQuality quality = (ProjectItemQuality)qualityIndex;
            if (type != ProjectItemType.Item) return CreateEquipment(random, type, level, quality);
            return new ProjectItem
            {
                Id = Guid.NewGuid().ToString("N"), Name = "遗迹零件", Type = ProjectItemType.Item,
                Quality = quality, EquipmentVersion = EquipmentVersion, RequiredLevel = Math.Max(1, level),
                SellGold = 8 + Math.Max(1, level) * 4 + random.Next(8) + qualityIndex * 6
            };
        }

        /// <summary>
        /// 创建只含已注册基础属性词条的武器或护甲。
        /// </summary>
        /// <param name="random">本局独立随机源。</param>
        /// <param name="type">武器或护甲分类。</param>
        /// <param name="level">物品等级。</param>
        /// <param name="quality">物品品质。</param>
        /// <returns>已冻结的装备；输入无效时为空。</returns>
        public static ProjectItem CreateEquipment(System.Random random, ProjectItemType type, int level, ProjectItemQuality quality)
        {
            if (random == null || type != ProjectItemType.Weapon && !IsArmor(type) || level < 1 ||
                !Enum.IsDefined(typeof(ProjectItemQuality), quality) || level > 1000) return null;
            bool weapon = type == ProjectItemType.Weapon;
            var item = new ProjectItem
            {
                Id = Guid.NewGuid().ToString("N"), Name = weapon ? "遗迹短刃" : "旧制护甲",
                Type = weapon ? ProjectItemType.Weapon : ProjectItemType.Armor, Quality = quality,
                EquipmentVersion = EquipmentVersion, RequiredLevel = level,
                SellGold = 8 + level * 4 + random.Next(8) + (int)quality * 6
            };
            if (weapon)
            {
                float attack = 12 + level * 2 + random.Next(WeaponAttackRandomExclusive);
                item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Attack, Value = attack });
                item.Range = 1.6f + (float)random.NextDouble() * 0.5f;
                item.Damage = attack;
            }
            else
            {
                float health = 5 + level * 3 + random.Next(ArmorHealthRandomExclusive);
                item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.MaxHealth, Value = health });
                item.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Defense, Value = level + random.Next(ArmorDefenseRandomMin, ArmorDefenseRandomMaxExclusive) });
                item.BonusHealth = (int)health;
            }
            var pool = new List<ProjectAttributeType>(AffixPool);
            for (int index = 0; index < QualityAffixCounts[(int)quality]; index++)
            {
                int chosen = random.Next(pool.Count);
                ProjectAttributeType attribute = pool[chosen];
                pool.RemoveAt(chosen);
                float scale = QualityScaleMin[(int)quality] +
                    (float)random.NextDouble() * (QualityScaleMax[(int)quality] - QualityScaleMin[(int)quality]);
                float value = AttributeScale[attribute] * scale * (1 + (level - 1) * 0.12f);
                item.Affixes.Add(new ProjectEquipmentAffix
                {
                    DefinitionId = DefinitionId(attribute), Attribute = attribute, Value = (float)Math.Round(value, 3)
                });
            }
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
                        if (affix != null && affix.DefinitionId == DefinitionId(affix.Attribute))
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
        /// <returns>用于战斗接入的独立角色属性。</returns>
        public static ProjectCharacterStats Calculate(IReadOnlyList<ProjectItem> items, int powerRank, int vitalityRank, int agilityRank)
        {
            var stats = new ProjectCharacterStats
            {
                MaxHealth = 100, CriticalDamage = 1.5f, AttackRange = 1.5f, MoveSpeed = 5
            };
            bool weaponEquipped = false;
            if (items != null)
                foreach (ProjectItem item in items)
                {
                    if (item == null || !item.Equipped) continue;
                    if (item.Type == ProjectItemType.Weapon)
                    {
                        weaponEquipped = true;
                        stats.AttackRange = item.Range > 0 ? item.Range : 1.5f;
                    }
                    foreach (ProjectAttributeValue attribute in GetItemAttributes(item)) Apply(stats, attribute);
                }
            if (!weaponEquipped) stats.Attack += 10;
            stats.Attack *= 1 + Math.Min(PrototypeRules.SkillMaxRank, Math.Max(0, powerRank)) * 0.1f;
            stats.MaxHealth += Math.Min(PrototypeRules.SkillMaxRank, Math.Max(0, vitalityRank)) * 15;
            stats.MoveSpeed *= 1 + Math.Min(PrototypeRules.SkillMaxRank, Math.Max(0, agilityRank)) * 0.05f;
            stats.MaxHealth = Math.Min(MaxAttributeValue, Math.Max(1, stats.MaxHealth));
            stats.Attack = Math.Min(MaxAttributeValue, Math.Max(0, stats.Attack));
            stats.Defense = Math.Min(MaxAttributeValue, Math.Max(0, stats.Defense));
            stats.CriticalRating = Math.Min(MaxAttributeValue, Math.Max(0, stats.CriticalRating));
            stats.CriticalDamage = Math.Min(MaxAttributeValue, Math.Max(1, stats.CriticalDamage));
            stats.DodgeChance = Math.Min(0.75f, Math.Max(0, stats.DodgeChance));
            stats.DamageIncrease = Math.Min(MaxAttributeValue, Math.Max(0, stats.DamageIncrease));
            stats.DamageReduction = Math.Min(0.75f, Math.Max(0, stats.DamageReduction));
            stats.AttackRange = Math.Min(MaxAttackRange, Math.Max(0, stats.AttackRange));
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
            if (item.EquipmentVersion == 0)
            {
                if (item.Quality != ProjectItemQuality.Common || item.Type == ProjectItemType.Armor ||
                    item.BaseAttributes != null && item.BaseAttributes.Count != 0 ||
                    item.Affixes != null && item.Affixes.Count != 0) return "旧版物品包含新装备字段。";
                return null;
            }
            if (item.Type == ProjectItemType.Equipment) return "新版护甲分类无效。";
            if (item.EquipmentVersion != EquipmentVersion || !Enum.IsDefined(typeof(ProjectItemQuality), item.Quality) ||
                item.BaseAttributes == null || item.Affixes == null) return "物品装备版本或品质无效。";
            if (item.Type == ProjectItemType.Item && (item.BaseAttributes.Count != 0 || item.Affixes.Count != 0)) return "材料不能携带装备属性。";
            var bases = new HashSet<ProjectAttributeType>();
            foreach (ProjectAttributeValue value in item.BaseAttributes)
                if (value == null || !Enum.IsDefined(typeof(ProjectAttributeType), value.Attribute) ||
                    !FiniteNonNegative(value.Value) || value.Value > MaxAttributeValue || !bases.Add(value.Attribute)) return "装备基础属性重复或无效。";
            var affixes = new HashSet<ProjectAttributeType>();
            foreach (ProjectEquipmentAffix affix in item.Affixes)
                if (affix == null || !Enum.IsDefined(typeof(ProjectAttributeType), affix.Attribute) ||
                    affix.DefinitionId != DefinitionId(affix.Attribute) || !FiniteNonNegative(affix.Value) ||
                    affix.Value > MaxAttributeValue || !affixes.Add(affix.Attribute)) return "装备词条重复或未注册。";
            if (item.Affixes.Count > QualityAffixCounts[(int)item.Quality]) return "装备词条超过品质上限。";
            return null;
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
                item.EquipmentVersion = EquipmentVersion;
                item.BaseAttributes = new List<ProjectAttributeValue>();
                item.Affixes = new List<ProjectEquipmentAffix>();
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
        /// 获取当前已注册基础属性词条定义身份。
        /// </summary>
        /// <param name="attribute">属性种类。</param>
        /// <returns>稳定定义身份。</returns>
        private static string DefinitionId(ProjectAttributeType attribute) => "stat." + attribute.ToString();

        /// <summary>
        /// 判断随机和存档数值有限且非负。
        /// </summary>
        /// <param name="value">待检数值。</param>
        /// <returns>数值是否有效。</returns>
        private static bool FiniteNonNegative(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value >= 0;
    }
}
