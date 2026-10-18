using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证收藏掉落与整箱 Boss 实际物品的配置边界。
    /// </summary>
    public sealed class ProjectRewardConfigTests
    {
        /// <summary>
        /// 加载当前导出配置。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 恢复测试变更过的配置行。
        /// </summary>
        [TearDown]
        public void RestoreTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 收藏通过普通掉落入口生成，固定品质且只消耗模板抽取随机数。
        /// </summary>
        [Test]
        public void CollectionLoot_UsesFixedQualityWithoutEquipmentRandomDraws()
        {
            var random = new PersistentRandom(51);
            var expected = new PersistentRandom(51);
            CfgLootPoolRow row = GameTables.CfgLootPool.GetAll().Single(loot => loot.PoolID == "region_chest");
            expected.Next(row.Weight);
            ProjectItem item = ProjectEquipmentRules.CreateLootFromPool(random, 1, "region_chest");
            ProjectCollectionDefinition definition = ProjectRewardConfig.Collection("explorer_token");
            Assert.That(item, Is.Not.Null);
            Assert.That(item.Type, Is.EqualTo(ProjectItemType.Collection));
            Assert.That(item.CollectionId, Is.EqualTo(definition.Id));
            Assert.That(item.Quality, Is.EqualTo(definition.Quality));
            Assert.That(item.SellGold, Is.EqualTo(definition.SellGold));
            Assert.That(item.Affixes, Is.Empty);
            Assert.That(item.AdvancedAffixes, Is.Empty);
            Assert.That(random.State, Is.EqualTo(expected.State));
            Assert.That(ProjectEquipmentRules.ValidateItem(item), Is.Null);
            ProjectItem direct = ProjectEquipmentRules.CreateConfiguredItem(random, 100, ProjectItemQuality.Artifact, "explorer_token");
            Assert.That(direct.Quality, Is.EqualTo(definition.Quality));
            Assert.That(direct.RequiredLevel, Is.EqualTo(definition.RequiredLevel));
            Assert.That(random.State, Is.EqualTo(expected.State));
            Assert.That(ProjectRulesConfig.Validate(out string error), Is.True, error);
            GameTables.CfgItemDefinitions.GetById("explorer_token").CollectionID = "missing";
            Assert.That(ProjectEquipmentRules.CreateConfiguredItem(random, 1, ProjectItemQuality.Common, "explorer_token"), Is.Null);
            Assert.That(ProjectRulesConfig.Validate(out error), Is.False);
        }

        /// <summary>
        /// 每条配置均一次生成三件实际掉落，最低组成、品质和稳定实例身份正确。
        /// </summary>
        [Test]
        public void BossLoot_ContainsConfiguredItemsAndStableBatchIds()
        {
            foreach (CfgBossRewardsRow rule in GameTables.CfgBossRewards.GetAll())
            for (int seed = 1; seed <= 32; seed++)
            {
                IReadOnlyList<ProjectItem> items = ProjectRewardConfig.CreateBossLoot(new PersistentRandom(seed), 1, rule.ID, "batch", out string error);
                Assert.That(items, Is.Not.Null, error);
                Assert.That(items.Count, Is.EqualTo(rule.ItemCount));
                Assert.That(rule.ItemCount, Is.EqualTo(3), "当前原型一次实际掉落三件，不按旧轮次乘出九件。");
                Assert.That(items.Count(item => item.Type == ProjectItemType.Collection), Is.GreaterThanOrEqualTo(rule.MinCollectionItems));
                Assert.That(items.Count(item => item.Type == ProjectItemType.Weapon || item.Type == ProjectItemType.Armor),
                    Is.GreaterThanOrEqualTo(rule.MinEquipmentItems));
                var collectionIds = new HashSet<string>();
                for (int index = 0; index < items.Count; index++)
                {
                    ProjectItem item = items[index];
                    Assert.That(item.Id, Is.EqualTo("batch:item:" + index.ToString(CultureInfo.InvariantCulture)));
                    Assert.That(ProjectEquipmentRules.ValidateItem(item), Is.Null);
                    Assert.That((int)item.Quality, Is.InRange(rule.MinQuality, rule.MaxQuality));
                    if (item.Type == ProjectItemType.Collection && !rule.AllowRepeatCollections)
                        Assert.That(collectionIds.Add(item.CollectionId), Is.True);
                }
            }
        }

        /// <summary>
        /// 配置无效时整批失败，不能消耗调用方的随机游标。
        /// </summary>
        [Test]
        public void BossLoot_InvalidSourcesAndCompositionFailBeforeRandomDraws()
        {
            var random = new PersistentRandom(102);
            long before = random.State;
            Assert.That(ProjectRewardConfig.CreateBossLoot(random, 1, 999, "batch", out string missing), Is.Null);
            Assert.That(missing, Is.Not.Empty);
            CfgBossRewardsRow rule = ProjectRewardConfig.BossReward(3);
            rule.CollectionIDs = new[] { "missing" };
            Assert.That(ProjectRewardConfig.CreateBossLoot(random, 1, rule.ID, "batch", out string error), Is.Null);
            Assert.That(error, Is.Not.Empty);
            Assert.That(random.State, Is.EqualTo(before));
            TestGameTables.LoadAll();
            rule = ProjectRewardConfig.BossReward(3);
            rule.MinEquipmentItems = rule.MinCollectionItems = 2;
            Assert.That(ProjectRewardConfig.CreateBossLoot(random, 1, rule.ID, "batch", out error), Is.Null);
            Assert.That(error, Is.Not.Empty);
            Assert.That(random.State, Is.EqualTo(before));
        }

        /// <summary>
        /// 禁止重复收藏时来源耗尽转为装备，仍满足整箱件数与最低组成。
        /// </summary>
        [Test]
        public void BossLoot_NonRepeatCollectionExhaustionUsesEquipment()
        {
            CfgBossRewardsRow rule = ProjectRewardConfig.BossReward(3);
            rule.ItemCount = 5;
            rule.CollectionIDs = new[] { "core_fragment" };
            rule.AllowRepeatCollections = false;
            IReadOnlyList<ProjectItem> items = ProjectRewardConfig.CreateBossLoot(new PreferCollectionsRandom(), 1, rule.ID, "batch", out string error);
            Assert.That(items, Is.Not.Null, error);
            Assert.That(items.Count, Is.EqualTo(5));
            Assert.That(items.Count(item => item.Type == ProjectItemType.Collection), Is.EqualTo(1));
            Assert.That(items.Count(item => item.Type == ProjectItemType.Weapon || item.Type == ProjectItemType.Armor), Is.EqualTo(4));
        }

        /// <summary>
        /// 允许同批重复收藏时，一个有效来源可满足多件最低收藏组成。
        /// </summary>
        [Test]
        public void BossLoot_RepeatCollectionsCanSatisfyConfiguredMinimum()
        {
            CfgBossRewardsRow rule = ProjectRewardConfig.BossReward(3);
            rule.ItemCount = 4;
            rule.MinCollectionItems = 2;
            rule.CollectionIDs = new[] { "core_fragment" };
            rule.AllowRepeatCollections = true;
            IReadOnlyList<ProjectItem> items = ProjectRewardConfig.CreateBossLoot(new PreferCollectionsRandom(), 1, rule.ID, "batch", out string error);
            Assert.That(items, Is.Not.Null, error);
            Assert.That(items.Count(item => item.Type == ProjectItemType.Collection), Is.EqualTo(3));
            Assert.That(items.Where(item => item.Type == ProjectItemType.Collection).Select(item => item.CollectionId).Distinct(),
                Is.EqualTo(new[] { "core_fragment" }));
        }

        /// <summary>
        /// 一键领取只提交已冻结整批物品，恢复随机游标可重现同批身份和全部属性。
        /// </summary>
        [Test]
        public void BossLoot_ClaimDoesNotDrawAndRestoredRandomKeepsWholeBatch()
        {
            var random = new PersistentRandom(711);
            long before = random.State;
            IReadOnlyList<ProjectItem> first = ProjectRewardConfig.CreateBossLoot(random, 1, 3, "batch", out string error);
            Assert.That(first, Is.Not.Null, error);
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("batch", first), Is.True);
            long frozenState = random.State;
            BossLootSnapshot offer = session.Active;
            int claimed = 0;
            Assert.That(session.TryClaim(offer.BatchId, offer.Revision, items =>
            {
                Assert.That(items.Select(item => item.Id), Is.EqualTo(first.Select(item => item.Id)));
                claimed += items.Count;
                return true;
            }), Is.True);
            Assert.That(session.TryClaim(offer.BatchId, offer.Revision, items => true), Is.False);
            Assert.That(claimed, Is.EqualTo(first.Count));
            Assert.That(random.State, Is.EqualTo(frozenState));
            Assert.That(PersistentRandom.TryRestore(before, out PersistentRandom restored), Is.True);
            IReadOnlyList<ProjectItem> replay = ProjectRewardConfig.CreateBossLoot(restored, 1, 3, "batch", out error);
            Assert.That(random.State, Is.EqualTo(restored.State));
            Assert.That(first.Select(Signature), Is.EqualTo(replay.Select(Signature)));
            Assert.That(first.Select(item => item.Id), Is.EqualTo(replay.Select(item => item.Id)));
        }

        /// <summary>
        /// 数组不匹配、品质矛盾和不足的同批收藏来源会在配置边界拒绝。
        /// </summary>
        [Test]
        public void ConfigValidation_RejectsUnusableBossAndAchievementDefinitions()
        {
            Assert.That(ProjectRewardConfig.Validate(out string error), Is.True, error);
            CfgCollectionAchievementsRow achievement = GameTables.CfgCollectionAchievements.GetAll()[0];
            achievement.Values = new double[0];
            Assert.That(ProjectRewardConfig.Validate(out error), Is.False);
            TestGameTables.LoadAll();
            CfgBossRewardsRow rule = ProjectRewardConfig.BossReward(3);
            rule.CollectionIDs = new[] { "core_fragment" };
            rule.MinCollectionItems = 2;
            Assert.That(ProjectRewardConfig.Validate(out error), Is.False);
            TestGameTables.LoadAll();
            ProjectRewardConfig.BossReward(1).MaxQuality = 1;
            Assert.That(ProjectRewardConfig.Validate(out error), Is.False);
        }

        /// <summary>
        /// 固定优先抽取收藏，避免以概率偶然性验证收藏耗尽路径。
        /// </summary>
        private sealed class PreferCollectionsRandom : System.Random
        {
            /// <summary>
            /// 返回合法范围末端以优先命中收藏权重。
            /// </summary>
            /// <param name="maxValue">不包含的上界。</param>
            /// <returns>范围内最大整数。</returns>
            public override int Next(int maxValue) => maxValue - 1;

            /// <summary>
            /// 返回属性抽样区间末端。
            /// </summary>
            /// <param name="minValue">包含的下界。</param>
            /// <param name="maxValue">不包含的上界。</param>
            /// <returns>范围内最大整数。</returns>
            public override int Next(int minValue, int maxValue) => maxValue - 1;

            /// <summary>
            /// 为连续属性提供固定合法抽样值。
            /// </summary>
            /// <returns>区间中点。</returns>
            public override double NextDouble() => 0.5d;
        }

        /// <summary>
        /// 生成不含实例身份的冻结属性签名，用于比较随机游标恢复结果。
        /// </summary>
        /// <param name="item">已生成物品。</param>
        /// <returns>模板、品质、售价与全部属性的稳定文本。</returns>
        private static string Signature(ProjectItem item) => item.DefinitionId + ":" + item.CollectionId + ":" + item.Quality + ":" +
            item.RequiredLevel + ":" + item.SellGold + ":" + item.Range + ":" +
            string.Join(";", item.BaseAttributes.Select(value => value.Attribute + "=" + value.Value)) + ":" +
            string.Join(";", item.Affixes.Select(value => value.DefinitionId + "=" + value.Value)) + ":" +
            string.Join(";", item.AdvancedAffixes.Select(value => value.DefinitionId + "=" + value.Value));
    }
}
