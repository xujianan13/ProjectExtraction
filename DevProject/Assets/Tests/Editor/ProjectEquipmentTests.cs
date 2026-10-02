using System;
using System.Collections.Generic;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证装备品质、词条、角色属性与旧存档迁移。
    /// </summary>
    public sealed class ProjectEquipmentTests
    {
        /// <summary>
        /// 六品质生成固定数量且互不重复的已注册词条。
        /// </summary>
        [Test]
        public void SixQualitiesProduceDistinctColorsAndAffixCounts()
        {
            var colors = new HashSet<Color>();
            string[] names = { "普通", "精良", "优秀", "史诗", "传说", "神器" };
            for (int index = 0; index < 6; index++)
            {
                ProjectItemQuality quality = (ProjectItemQuality)index;
                ProjectItem item = ProjectEquipmentRules.CreateEquipment(new System.Random(17), ProjectItemType.Weapon, 3, quality);
                Assert.AreEqual(names[index], ProjectEquipmentRules.QualityName(quality));
                Assert.IsTrue(colors.Add(ProjectEquipmentRules.QualityColor(quality)));
                Assert.AreEqual(index, item.Affixes.Count);
                Assert.AreEqual(GameTables.CfgQualities.GetById(index).AdvancedAffixCount, item.AdvancedAffixes.Count);
                Assert.AreEqual(item.AdvancedAffixes.Count,
                    new HashSet<string>(item.AdvancedAffixes.ConvertAll(value => value.DefinitionId)).Count);
                Assert.AreEqual(index, new HashSet<ProjectAttributeType>(item.Affixes.ConvertAll(value => value.Attribute)).Count);
                Assert.IsNull(ProjectEquipmentRules.ValidateItem(item));
            }
        }

        /// <summary>
        /// 属性只在创建时随机，副本中的基础属性和词条独立。
        /// </summary>
        [Test]
        public void FrozenEquipmentAndCopyDoNotShareAttributes()
        {
            ProjectItem item = ProjectEquipmentRules.CreateEquipment(new System.Random(9), ProjectItemType.Armor, 4, ProjectItemQuality.Artifact);
            ProjectItem copy = item.Copy();
            float originalBase = item.BaseAttributes[0].Value;
            float originalAffix = item.Affixes[0].Value;
            float originalAdvanced = item.AdvancedAffixes[0].Value;
            copy.BaseAttributes[0].Value += 100;
            copy.Affixes[0].Value += 100;
            copy.Affixes.Clear();
            copy.AdvancedAffixes[0].Value += 1f;
            copy.AdvancedAffixes.Clear();
            Assert.AreEqual(originalBase, item.BaseAttributes[0].Value);
            Assert.AreEqual(originalAffix, item.Affixes[0].Value);
            Assert.AreEqual(originalAdvanced, item.AdvancedAffixes[0].Value);
            Assert.AreEqual(2, item.AdvancedAffixes.Count);
            Assert.AreEqual(5, item.Affixes.Count);
            Assert.IsNull(ProjectEquipmentRules.ValidateItem(item));
        }

        /// <summary>
        /// 新装备只读新属性，卸下后不再增加角色数值。
        /// </summary>
        [Test]
        public void CharacterStatsApplyEquippedAttributesOnce()
        {
            ProjectItem weapon = ProjectEquipmentRules.CreateEquipment(new System.Random(5), ProjectItemType.Weapon, 1, ProjectItemQuality.Common);
            weapon.Equipped = true;
            weapon.Damage = 999;
            weapon.BaseAttributes[0].Value = 16;
            weapon.Range = 2f;
            ProjectItem armor = ProjectEquipmentRules.CreateEquipment(new System.Random(8), ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            armor.Equipped = true;
            armor.BaseAttributes[0].Value = 20;
            armor.BaseAttributes[1].Value = 4;
            ProjectCharacterStats stats = ProjectEquipmentRules.Calculate(new[] { weapon, armor }, 1, 1, 1);
            Assert.AreEqual(17.6f, stats.Attack, 0.001f);
            Assert.AreEqual(135, stats.MaxHealth);
            Assert.AreEqual(4, stats.Defense);
            Assert.AreEqual(2, stats.AttackRange);
            Assert.AreEqual(5.25f, stats.MoveSpeed, 0.001f);
            armor.Equipped = false;
            Assert.AreEqual(115, ProjectEquipmentRules.Calculate(new[] { weapon, armor }, 0, 1, 0).MaxHealth);
            weapon.Equipped = false;
            Assert.AreEqual(10, ProjectEquipmentRules.Calculate(new[] { weapon, armor }, 0, 0, 0).Attack);
        }

        /// <summary>
        /// 旧仓库、托管、终局和结算清单全部按原数值迁为普通品质。
        /// </summary>
        [Test]
        public void MigrationPreservesLegacyRollsAcrossAllContainers()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            ProjectItem weapon = data.Warehouse[0];
            weapon.EquipmentVersion = 0;
            weapon.BaseAttributes.Clear();
            var armor = new ProjectItem
            {
                Id = "armor", Name = "旧护甲", Type = ProjectItemType.Equipment, RequiredLevel = 1,
                BonusHealth = 17, Equipped = true
            };
            data.Warehouse.Add(armor);
            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, "legacy-v2", 1, 77,
                new List<ProjectItem> { weapon.Copy(), armor.Copy() }, out _));
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, "legacy-v2", out _));
            var outcome = new ProjectRunOutcome
            {
                RunId = "legacy-v2", LevelId = 1, Seed = 77, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Tick = 1, Extracted = true, Items = data.EntryItems.ConvertAll(item => item.Copy())
            };
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            ProjectSaveData active = data.Copy();
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            var codec = new ProjectSaveCodec();
            ProjectSaveData migratedActive = Migrate(codec, active);
            ProjectSaveData migratedSettled = Migrate(codec, data);
            AssertMigrated(migratedActive.EntryItems[0], ProjectItemType.Weapon, 16);
            AssertMigrated(migratedActive.EntryItems[1], ProjectItemType.Armor, 17);
            AssertMigrated(migratedActive.Outcome.Items[1], ProjectItemType.Armor, 17);
            AssertMigrated(migratedSettled.Warehouse[1], ProjectItemType.Armor, 17);
            AssertMigrated(migratedSettled.LastResult.Outcome.Items[1], ProjectItemType.Armor, 17);
            AssertMigrated(migratedSettled.LastResult.RetainedItems[1], ProjectItemType.Armor, 17);
            Assert.AreEqual("legacy-v2", migratedSettled.LastSettledRunId);
        }

        /// <summary>
        /// 非有限值、重复词条和同槽装备不能进入存档。
        /// </summary>
        [Test]
        public void ValidationRejectsInvalidAffixesAndDuplicateSlots()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            ProjectItem extraWeapon = data.Warehouse[0].Copy();
            extraWeapon.Id = "extra";
            data.Warehouse.Add(extraWeapon);
            Assert.IsNotNull(ProjectSaveData.Validate(data));
            data.Warehouse.Remove(extraWeapon);
            data.ActiveRunId = "slot-conflict";
            data.Stage = ProjectRunStage.Running;
            data.EntryItems.Add(extraWeapon);
            Assert.IsNotNull(ProjectSaveData.Validate(data));
            data.EntryItems.Clear();
            data.ActiveRunId = "";
            data.Stage = ProjectRunStage.None;
            var item = ProjectEquipmentRules.CreateEquipment(new System.Random(4), ProjectItemType.Armor, 1, ProjectItemQuality.Rare);
            item.Affixes[1].Attribute = item.Affixes[0].Attribute;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(item));
            item.Affixes[1].Attribute = ProjectAttributeType.DamageReduction;
            item.Affixes[1].DefinitionId = "stat.DamageReduction";
            item.Affixes[1].Value = float.PositiveInfinity;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(item));
            item.Affixes[1].Value = ProjectEquipmentRules.MaxAttributeValue + 1;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(item));
            ProjectItem disguisedLegacy = data.Warehouse[0].Copy();
            disguisedLegacy.EquipmentVersion = 0;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(disguisedLegacy));
            disguisedLegacy.BaseAttributes.Clear();
            disguisedLegacy.Quality = ProjectItemQuality.Rare;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(disguisedLegacy));
            disguisedLegacy.Quality = ProjectItemQuality.Common;
            Assert.IsNull(ProjectEquipmentRules.ValidateItem(disguisedLegacy));
            var wrongArmor = ProjectEquipmentRules.CreateEquipment(new System.Random(3), ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            wrongArmor.Type = ProjectItemType.Equipment;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(wrongArmor));
        }

        /// <summary>
        /// 新高级词条经过存档编码和物品副本后保持定义、版本及随机值。
        /// </summary>
        [Test]
        public void AdvancedAffixesRoundTripWithoutReroll()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            ProjectItem item = ProjectEquipmentRules.CreateEquipment(new System.Random(43), ProjectItemType.Armor, 4, ProjectItemQuality.Artifact);
            data.Warehouse.Add(item);
            var codec = new ProjectSaveCodec();
            ProjectSaveData saved = codec.Decode(codec.Encode(data));
            ProjectItem restored = saved.Warehouse.Find(value => value.Id == item.Id);
            Assert.IsNotNull(restored);
            Assert.AreEqual(ProjectEquipmentRules.EquipmentVersion, restored.EquipmentVersion);
            Assert.AreEqual(item.AdvancedAffixes.Count, restored.AdvancedAffixes.Count);
            for (int index = 0; index < item.AdvancedAffixes.Count; index++)
            {
                Assert.AreEqual(item.AdvancedAffixes[index].DefinitionId, restored.AdvancedAffixes[index].DefinitionId);
                Assert.AreEqual(item.AdvancedAffixes[index].AugmentId, restored.AdvancedAffixes[index].AugmentId);
                Assert.AreEqual(item.AdvancedAffixes[index].Version, restored.AdvancedAffixes[index].Version);
                Assert.AreEqual(item.AdvancedAffixes[index].Value, restored.AdvancedAffixes[index].Value);
            }
            Assert.IsNull(ProjectSaveData.Validate(saved));
        }

        /// <summary>
        /// 版本一装备跨编码往返时保持普通词条且不产生高级词条。
        /// </summary>
        [Test]
        public void VersionOneEquipmentRoundTripsWithoutAdvancedReroll()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            ProjectItem item = ProjectEquipmentRules.CreateEquipment(new System.Random(23), ProjectItemType.Weapon, 3, ProjectItemQuality.Epic);
            item.EquipmentVersion = ProjectEquipmentRules.LegacyEquipmentVersion;
            item.AdvancedAffixes = null;
            data.Warehouse.Add(item);
            var codec = new ProjectSaveCodec();
            ProjectSaveData restored = codec.Decode(codec.Encode(data));
            ProjectItem saved = restored.Warehouse.Find(value => value.Id == item.Id);
            Assert.IsNotNull(saved);
            Assert.AreEqual(ProjectEquipmentRules.LegacyEquipmentVersion, saved.EquipmentVersion);
            Assert.IsTrue(saved.AdvancedAffixes == null || saved.AdvancedAffixes.Count == 0);
            Assert.AreEqual(item.Affixes.Count, saved.Affixes.Count);
            Assert.AreEqual(item.BaseAttributes[0].Value, saved.BaseAttributes[0].Value);
            Assert.IsNull(ProjectEquipmentRules.ValidateItem(saved));
            Assert.IsNull(ProjectSaveData.Validate(restored));
        }

        /// <summary>
        /// 调整新掉落区间和品质数量后，已保存的冻结装备仍可读取。
        /// </summary>
        [Test]
        public void ExistingAdvancedAffixesSurviveCurrentRollRangeAndCountChanges()
        {
            ProjectItem existing = ProjectEquipmentRules.CreateEquipment(new System.Random(51), ProjectItemType.Armor, 4, ProjectItemQuality.Artifact);
            CfgQualitiesRow quality = GameTables.CfgQualities.GetById((int)ProjectItemQuality.Artifact);
            CfgAdvancedAffixesRow first = GameTables.CfgAdvancedAffixes.GetById(existing.AdvancedAffixes[0].DefinitionId);
            CfgAdvancedAffixesRow second = GameTables.CfgAdvancedAffixes.GetById(existing.AdvancedAffixes[1].DefinitionId);
            int originalCount = quality.AdvancedAffixCount;
            float firstValue = existing.AdvancedAffixes[0].Value, secondValue = existing.AdvancedAffixes[1].Value;
            double firstMin = first.ValueMin, firstMax = first.ValueMax, secondMin = second.ValueMin, secondMax = second.ValueMax;
            try
            {
                quality.AdvancedAffixCount = 1;
                first.ValueMin = second.ValueMin = 0.01;
                first.ValueMax = second.ValueMax = 0.02;
                Assert.IsNull(ProjectEquipmentRules.ValidateItem(existing));
                Assert.AreEqual(firstValue, existing.AdvancedAffixes[0].Value);
                Assert.AreEqual(secondValue, existing.AdvancedAffixes[1].Value);
                ProjectItem next = ProjectEquipmentRules.CreateEquipment(new System.Random(52), ProjectItemType.Armor, 4, ProjectItemQuality.Artifact);
                Assert.AreEqual(1, next.AdvancedAffixes.Count);
                Assert.That(next.AdvancedAffixes[0].Value, Is.InRange(0.01f, 0.02f));
                Assert.IsNull(ProjectEquipmentRules.ValidateItem(next));
            }
            finally
            {
                quality.AdvancedAffixCount = originalCount;
                first.ValueMin = firstMin;
                first.ValueMax = firstMax;
                second.ValueMin = secondMin;
                second.ValueMax = secondMax;
            }
        }

        /// <summary>
        /// 高级词条来源、效果版本、冻结值和数量不允许被伪造。
        /// </summary>
        [Test]
        public void ValidationRejectsInvalidAdvancedAffixIdentityVersionAndValue()
        {
            ProjectItem valid = ProjectEquipmentRules.CreateEquipment(new System.Random(31), ProjectItemType.Weapon, 4, ProjectItemQuality.Artifact);
            Assert.IsNull(ProjectEquipmentRules.ValidateItem(valid));
            ProjectItem invalid = valid.Copy();
            invalid.AdvancedAffixes[0].DefinitionId = "missing.advanced";
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
            invalid = valid.Copy();
            invalid.AdvancedAffixes[0].Version++;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
            invalid = valid.Copy();
            invalid.AdvancedAffixes[0].Value = float.PositiveInfinity;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
            invalid = valid.Copy();
            invalid.AdvancedAffixes[0].Value = -0.1f;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
            invalid = valid.Copy();
            invalid.AdvancedAffixes[0].DefinitionId = invalid.AdvancedAffixes[1].DefinitionId;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
            invalid = valid.Copy();
            invalid.AdvancedAffixes = null;
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
            invalid = valid.Copy();
            invalid.AdvancedAffixes[0].AugmentId = "missing.augment";
            Assert.IsNotNull(ProjectEquipmentRules.ValidateItem(invalid));
        }

        /// <summary>
        /// 执行版本二装备迁移并模拟实际存档编码往返。
        /// </summary>
        /// <param name="codec">项目编解码器。</param>
        /// <param name="data">版本二数据。</param>
        /// <returns>通过当前校验的版本三数据。</returns>
        private static ProjectSaveData Migrate(ProjectSaveCodec codec, ProjectSaveData data)
        {
            string json = codec.Encode(data);
            var migration = ProjectEquipmentRules.CreateEquipmentMigration();
            Assert.IsNull(migration.ValidateSource(json));
            ProjectSaveData migrated = codec.Decode(migration.Migrate(json));
            Assert.IsNull(ProjectSaveData.Validate(migrated));
            return codec.Decode(codec.Encode(migrated));
        }

        /// <summary>
        /// 检查旧实例数值被直接保留且没有生成随机词条。
        /// </summary>
        /// <param name="item">迁移后的实例。</param>
        /// <param name="type">预期分类。</param>
        /// <param name="value">预期原数值。</param>
        private static void AssertMigrated(ProjectItem item, ProjectItemType type, float value)
        {
            Assert.AreEqual(type, item.Type);
            Assert.AreEqual(ProjectItemQuality.Common, item.Quality);
            Assert.AreEqual(ProjectEquipmentRules.LegacyEquipmentVersion, item.EquipmentVersion);
            Assert.AreEqual(0, item.Affixes.Count);
            Assert.AreEqual(value, item.BaseAttributes[0].Value);
        }
    }
}
