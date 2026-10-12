using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证收藏独立实例、幂等登记、成就来源与保存边界。
    /// </summary>
    public sealed class CollectionRulesTests
    {
        /// <summary>
        /// 为现有物品模板和仓库规则加载真实配置。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 清理测试中的模板分类变更。
        /// </summary>
        [TearDown]
        public void RestoreTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 相同藏品定义产生不同物品实例，字段固定且不能携带装备能力。
        /// </summary>
        [Test]
        public void CollectionItems_HaveIndependentIdentityAndNoEquipmentAffixes()
        {
            ProjectCollectionDefinition definition = Definition("a");
            var random = new System.Random(11);
            ProjectItem first = ProjectCollectionRules.CreateItem(random, definition);
            ProjectItem second = ProjectEquipmentRules.CreateConfiguredItem(random, definition);
            Assert.That(first.Id, Is.Not.EqualTo(second.Id));
            Assert.That(first.CollectionId, Is.EqualTo(second.CollectionId));
            Assert.That(first.Type, Is.EqualTo(ProjectItemType.Collection));
            Assert.That(first.Quality, Is.EqualTo(definition.Quality));
            Assert.That(first.SellGold, Is.EqualTo(definition.SellGold));
            Assert.That(first.Equipped, Is.False);
            Assert.That(first.BaseAttributes, Is.Empty);
            Assert.That(first.Affixes, Is.Empty);
            Assert.That(first.AdvancedAffixes, Is.Empty);
            Assert.That(ProjectEquipmentRules.ValidateItem(first), Is.Null);
            ProjectItem copy = first.Copy();
            copy.CollectionId = "changed";
            Assert.That(first.CollectionId, Is.EqualTo("a"));
            first.Equipped = true;
            Assert.That(ProjectEquipmentRules.ValidateItem(first), Is.Not.Null);
            first.Equipped = false;
            first.BaseAttributes.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Attack, Value = 1 });
            Assert.That(ProjectEquipmentRules.ValidateItem(first), Is.Not.Null);
            first.BaseAttributes.Clear();
            first.AdvancedAffixes.Add(new ProjectAdvancedAffix());
            Assert.That(ProjectEquipmentRules.ValidateItem(first), Is.Not.Null);
        }

        /// <summary>
        /// 缺少收藏定义的模板明确失败，已配置收藏通过普通模板入口采用固定品质。
        /// </summary>
        [Test]
        public void CollectionGeneration_DoesNotUseEquipmentTemplatePath()
        {
            GameTables.CfgItemDefinitions.GetById("ruin_weapon").Type = (int)ProjectItemType.Collection;
            Assert.That(ProjectEquipmentRules.CreateConfiguredItem(new System.Random(4), 1, ProjectItemQuality.Epic, "ruin_weapon"), Is.Null);
            Assert.That(ProjectEquipmentRules.CreateConfiguredItem(new System.Random(4), Definition("a")), Is.Not.Null);
            ProjectItem configured = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(4), 100, ProjectItemQuality.Artifact, "gate_sigil");
            Assert.That(configured.Type, Is.EqualTo(ProjectItemType.Collection));
            Assert.That(configured.CollectionId, Is.EqualTo("gate_sigil"));
            Assert.That(configured.Quality, Is.EqualTo(ProjectV2RewardConfig.Collection("gate_sigil").Quality));
            Assert.That(configured.Affixes, Is.Empty);
            ProjectItem ordinary = new()
            {
                Id = "ordinary", Name = "普通物品", Type = ProjectItemType.Item, RequiredLevel = 1, CollectionId = "a"
            };
            Assert.That(ProjectEquipmentRules.ValidateItem(ordinary), Is.Not.Null);
        }

        /// <summary>
        /// 不同实例的同一收藏只登记一次，完成成就的属性来源在重复登记和出售后保持稳定。
        /// </summary>
        [Test]
        public void Registration_IsIdempotentAndBonusesPersistAfterSellingDuplicate()
        {
            ProjectCollectionDefinition[] definitions = { Definition("a"), Definition("b") };
            ProjectCollectionAchievementDefinition[] achievements =
            {
                Achievement("first", new[] { "a" }, 2), Achievement("series", new[] { "a", "b" }, 3)
            };
            achievements[1].AttributeRewards.Add(new ProjectAttributeValue { Attribute = ProjectAttributeType.Defense, Value = 1 });
            var data = new ProjectSaveData { Level = 1, PowerRank = 7 };
            ProjectItem first = ProjectCollectionRules.CreateItem(new System.Random(1), definitions[0]);
            ProjectItem duplicate = ProjectCollectionRules.CreateItem(new System.Random(2), definitions[0]);
            Assert.That(ProjectCollectionRules.RegisterRetainedItems(data, new[] { first, duplicate }, definitions, achievements, out string error), Is.True, error);
            Assert.That(data.CollectedCollectionIds, Is.EqualTo(new[] { "a" }));
            Assert.That(data.AchievedCollectionIds, Is.EqualTo(new[] { "first" }));
            Assert.That(data.Warehouse, Is.Empty);
            Assert.That(ProjectCollectionRules.RegisterRetainedItems(data, new[] { first, duplicate }, definitions, achievements, out error), Is.True, error);
            Assert.That(data.CollectedCollectionIds.Count, Is.EqualTo(1));
            Assert.That(data.AchievedCollectionIds.Count, Is.EqualTo(1));
            ProjectItem second = ProjectCollectionRules.CreateItem(new System.Random(3), definitions[1]);
            Assert.That(ProjectCollectionRules.RegisterRetainedItems(data, new[] { second }, definitions, achievements, out error), Is.True, error);
            Assert.That(data.AchievedCollectionIds, Is.EqualTo(new[] { "first", "series" }));
            Assert.That(data.PowerRank, Is.EqualTo(7));
            IReadOnlyList<ProjectAttributeValue> bonuses = ProjectCollectionRules.GetBonuses(data, definitions, achievements, out error);
            Assert.That(error, Is.Null);
            Assert.That(bonuses.Single(value => value.Attribute == ProjectAttributeType.Attack).Value, Is.EqualTo(5));
            Assert.That(bonuses.Single(value => value.Attribute == ProjectAttributeType.Defense).Value, Is.EqualTo(1));
            IReadOnlyList<ProjectCollectionBonusSource> sources = ProjectCollectionRules.GetBonusSources(data, definitions, achievements, out error);
            Assert.That(sources.Select(source => source.AchievementId), Is.EqualTo(new[] { "first", "series" }));
            sources[0].Attributes[0].Value = 99;
            Assert.That(ProjectCollectionRules.GetBonuses(data, definitions, achievements).Single(value => value.Attribute == ProjectAttributeType.Attack).Value,
                Is.EqualTo(5));
            data.Warehouse.AddRange(new[] { first.Copy(), duplicate.Copy(), second.Copy() });
            Assert.That(ProjectWarehouseRules.TrySell(data, new[] { duplicate.Id }, out int gold, out error), Is.True, error);
            Assert.That(gold, Is.EqualTo(duplicate.SellGold));
            Assert.That(data.CollectedCollectionIds.Count, Is.EqualTo(2));
            Assert.That(data.AchievedCollectionIds.Count, Is.EqualTo(2));
            Assert.That(ProjectCollectionRules.GetBonuses(data, definitions, achievements).Single(value => value.Attribute == ProjectAttributeType.Attack).Value,
                Is.EqualTo(5));
        }

        /// <summary>
        /// 未知收藏或重复物品实例导致完整登记失败，不留下部分收藏或成就。
        /// </summary>
        [Test]
        public void Registration_RejectsInvalidBatchWithoutPartialChanges()
        {
            ProjectCollectionDefinition[] definitions = { Definition("a") };
            ProjectCollectionAchievementDefinition[] achievements = { Achievement("first", new[] { "a" }, 2) };
            var data = new ProjectSaveData();
            ProjectItem valid = ProjectCollectionRules.CreateItem(new System.Random(1), definitions[0]);
            ProjectItem unknown = ProjectCollectionRules.CreateItem(new System.Random(2), Definition("unknown"));
            Assert.That(ProjectCollectionRules.RegisterRetainedItems(data, new[] { valid, unknown }, definitions, achievements, out string error), Is.False);
            Assert.That(error, Does.Contain("缺失"));
            Assert.That(data.CollectedCollectionIds, Is.Empty);
            Assert.That(data.AchievedCollectionIds, Is.Empty);
            Assert.That(ProjectCollectionRules.RegisterRetainedItems(data, new[] { valid, valid }, definitions, achievements, out error), Is.False);
            Assert.That(error, Does.Contain("重复"));
            Assert.That(data.CollectedCollectionIds, Is.Empty);
        }

        /// <summary>
        /// 定义缺失、重复成就条件、无效属性及不满足条件的保存成就都明确失败。
        /// </summary>
        [Test]
        public void Validation_RejectsBadDefinitionsAndUnearnedAchievement()
        {
            ProjectCollectionDefinition[] definitions = { Definition("a") };
            ProjectCollectionAchievementDefinition achievement = Achievement("first", new[] { "missing" }, 2);
            Assert.That(ProjectCollectionRules.Validate(definitions, new[] { achievement }), Does.Contain("引用"));
            achievement.RequiredCollectionIds = new List<string> { "a", "a" };
            Assert.That(ProjectCollectionRules.Validate(definitions, new[] { achievement }), Does.Contain("重复"));
            achievement.RequiredCollectionIds = new List<string> { "a" };
            achievement.AttributeRewards[0].Value = float.NaN;
            Assert.That(ProjectCollectionRules.Validate(definitions, new[] { achievement }), Does.Contain("属性"));
            achievement.AttributeRewards[0].Value = 2;
            var data = new ProjectSaveData();
            data.AchievedCollectionIds.Add("first");
            Assert.That(ProjectCollectionRules.ValidateState(data, definitions, new[] { achievement }), Does.Contain("条件"));
            Assert.That(ProjectCollectionRules.GetBonuses(data, definitions, new[] { achievement }, out string error), Is.Null);
            Assert.That(error, Does.Contain("条件"));
            data.AchievedCollectionIds.Clear();
            data.CollectedCollectionIds.AddRange(new[] { "a", "a" });
            Assert.That(ProjectCollectionRules.ValidateSavedState(data), Does.Contain("重复"));
            Assert.That(ProjectSaveData.Validate(data), Is.Not.Null);
        }

        /// <summary>
        /// 保存完整收藏身份并深拷贝列表，旧正文缺席的新列表恢复为空集合。
        /// </summary>
        [Test]
        public void CollectionState_CodecRoundTripAndCopyPreserveIndependentLists()
        {
            var codec = new ProjectSaveCodec();
            var data = new ProjectSaveData { Level = 1 };
            data.Warehouse.Add(ProjectCollectionRules.CreateItem(new System.Random(8), Definition("a")));
            data.CollectedCollectionIds.Add("a");
            data.AchievedCollectionIds.Add("first");
            ProjectSaveData restored = codec.Decode(codec.Encode(data));
            Assert.That(restored.Warehouse.Single().CollectionId, Is.EqualTo("a"));
            Assert.That(restored.CollectedCollectionIds, Is.EqualTo(new[] { "a" }));
            Assert.That(restored.AchievedCollectionIds, Is.EqualTo(new[] { "first" }));
            ProjectSaveData copy = restored.Copy();
            copy.CollectedCollectionIds.Clear();
            copy.AchievedCollectionIds.Clear();
            copy.Warehouse[0].CollectionId = "changed";
            Assert.That(restored.CollectedCollectionIds.Count, Is.EqualTo(1));
            Assert.That(restored.AchievedCollectionIds.Count, Is.EqualTo(1));
            Assert.That(restored.Warehouse[0].CollectionId, Is.EqualTo("a"));
            ProjectSaveData old = codec.Decode("{\"Level\":1,\"Warehouse\":[],\"EntryItems\":[]}");
            Assert.That(old.CollectedCollectionIds, Is.Not.Null.And.Empty);
            Assert.That(old.AchievedCollectionIds, Is.Not.Null.And.Empty);
        }

        /// <summary>
        /// 创建测试程序集内的明确收藏定义。
        /// </summary>
        /// <param name="id">收藏稳定身份。</param>
        /// <returns>独立测试收藏定义。</returns>
        private static ProjectCollectionDefinition Definition(string id) => new()
        {
            Id = id, Name = "测试藏品 " + id, Series = "test-series", Source = "测试首领", SellGold = 10,
            RequiredLevel = 1, Quality = ProjectItemQuality.Rare
        };

        /// <summary>
        /// 创建带攻击奖励的测试收藏成就。
        /// </summary>
        /// <param name="id">成就稳定身份。</param>
        /// <param name="requiredIds">所需收藏身份。</param>
        /// <param name="attack">攻击属性奖励。</param>
        /// <returns>独立测试成就定义。</returns>
        private static ProjectCollectionAchievementDefinition Achievement(string id, IReadOnlyList<string> requiredIds, float attack) => new()
        {
            Id = id, Name = "测试成就 " + id, RequiredCollectionIds = new List<string>(requiredIds),
            AttributeRewards = new List<ProjectAttributeValue> { new() { Attribute = ProjectAttributeType.Attack, Value = attack } }
        };
    }
}
