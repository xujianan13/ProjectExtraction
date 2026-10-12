using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证 v2 奖励配置池、收藏掉落与完整 Boss 候选的实际边界。
    /// </summary>
    public sealed class V2RewardConfigTests
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
        /// 独立技能池只返回技能，错误引用或缺失池明确失败。
        /// </summary>
        [Test]
        public void SkillPool_UsesConfiguredMembershipAndRejectsMissingReferences()
        {
            IReadOnlyList<CfgRogueRewardsRow> pool = ProjectV2RewardConfig.SkillPool("expedition", out string error);
            Assert.That(pool, Is.Not.Null, error);
            Assert.That(pool.All(row => row.Kind == (int)RogueRewardKind.AttributeBonus || row.Kind == (int)RogueRewardKind.CombatAugment), Is.True);
            Assert.That(ProjectV2RewardConfig.SkillPool("missing", out error), Is.Null);
            Assert.That(error, Is.Not.Empty);
            GameTables.CfgRoguePools.GetAll()[0].RewardID = "missing";
            Assert.That(ProjectV2RewardConfig.SkillPool("expedition", out error), Is.Null);
            Assert.That(ProjectV2RewardConfig.Validate(out error), Is.False);
        }

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
            ProjectCollectionDefinition definition = ProjectV2RewardConfig.Collection("explorer_token");
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
        /// 每轮配置生成三个独立合法候选，装备与收藏同时存在且品质受规则限制。
        /// </summary>
        [Test]
        public void BossCandidates_ContainLegalCompleteItemsAndHonorNonRepeatRounds()
        {
            var random = new PersistentRandom(102);
            CfgBossRewardsRow rule = ProjectV2RewardConfig.BossReward(3);
            var claimed = new HashSet<string>();
            var itemIds = new HashSet<string>();
            for (int round = 1; round <= rule.Rounds; round++)
            {
                IReadOnlyList<ProjectItem> candidates = ProjectV2RewardConfig.CreateBossCandidates(random, 1, rule.ID, "batch", round, claimed, out string error);
                Assert.That(candidates, Is.Not.Null, error);
                Assert.That(candidates.Count, Is.EqualTo(3));
                Assert.That(candidates.Count(item => item.Type == ProjectItemType.Collection), Is.GreaterThanOrEqualTo(rule.MinCollectionCandidates));
                Assert.That(candidates.Count(item => item.Type == ProjectItemType.Weapon || item.Type == ProjectItemType.Armor),
                    Is.GreaterThanOrEqualTo(rule.MinEquipmentCandidates));
                foreach (ProjectItem item in candidates)
                {
                    Assert.That(itemIds.Add(item.Id), Is.True);
                    Assert.That(ProjectEquipmentRules.ValidateItem(item), Is.Null);
                    Assert.That((int)item.Quality, Is.InRange(rule.MinQuality, rule.MaxQuality));
                    if (item.Type == ProjectItemType.Collection) Assert.That(claimed.Contains(item.CollectionId), Is.False);
                }
                claimed.Add(candidates.First(item => item.Type == ProjectItemType.Collection).CollectionId);
            }
            Assert.That(ProjectV2RewardConfig.CreateBossCandidates(random, 1, 999, "batch", 1, claimed, out string missing), Is.Null);
            Assert.That(missing, Is.Not.Empty);
            rule.CollectionIDs = new[] { "missing" };
            long state = random.State;
            Assert.That(ProjectV2RewardConfig.CreateBossCandidates(random, 1, rule.ID, "batch", 1, claimed, out _), Is.Null);
            Assert.That(random.State, Is.EqualTo(state));
        }

        /// <summary>
        /// Boss 点击只领取已冻结实例，保存随机游标后后续候选属性与抽取位置一致。
        /// </summary>
        [Test]
        public void BossSelection_DoesNotDrawAndRestoredRandomKeepsNextRound()
        {
            var random = new PersistentRandom(711);
            IReadOnlyList<ProjectItem> first = ProjectV2RewardConfig.CreateBossCandidates(random, 1, 3, "batch", 1, new string[0], out string error);
            Assert.That(first, Is.Not.Null, error);
            var session = new BossLootChoiceSession("run");
            Assert.That(session.TryBegin("batch", 3, first), Is.True);
            long frozenState = random.State;
            BossLootChoiceSnapshot offer = session.Active;
            int selected = offer.Options.ToList().FindIndex(item => item.Type == ProjectItemType.Collection);
            Assert.That(session.TrySelect(offer.OfferId, offer.Revision, selected, item => true), Is.True);
            Assert.That(session.TrySelect(offer.OfferId, offer.Revision, selected, item => true), Is.False);
            Assert.That(random.State, Is.EqualTo(frozenState));
            Assert.That(PersistentRandom.TryRestore(frozenState, out PersistentRandom restored), Is.True);
            string[] claimed = session.ClaimedItems().Where(item => item.Type == ProjectItemType.Collection).Select(item => item.CollectionId).ToArray();
            IReadOnlyList<ProjectItem> next = ProjectV2RewardConfig.CreateBossCandidates(random, 1, 3, "batch", 2, claimed, out error);
            IReadOnlyList<ProjectItem> replay = ProjectV2RewardConfig.CreateBossCandidates(restored, 1, 3, "batch", 2, claimed, out error);
            Assert.That(random.State, Is.EqualTo(restored.State));
            Assert.That(next.Select(Signature), Is.EqualTo(replay.Select(Signature)));
            Assert.That(next.Select(item => item.Id).Intersect(replay.Select(item => item.Id)), Is.Empty);
        }

        /// <summary>
        /// 数组不匹配、品质矛盾和不足的跨轮收藏会在配置边界拒绝。
        /// </summary>
        [Test]
        public void ConfigValidation_RejectsUnusableBossAndAchievementDefinitions()
        {
            Assert.That(ProjectV2RewardConfig.Validate(out string error), Is.True, error);
            CfgCollectionAchievementsRow achievement = GameTables.CfgCollectionAchievements.GetAll()[0];
            achievement.Values = new double[0];
            Assert.That(ProjectV2RewardConfig.Validate(out error), Is.False);
            TestGameTables.LoadAll();
            CfgBossRewardsRow rule = ProjectV2RewardConfig.BossReward(3);
            rule.CollectionIDs = new[] { "core_fragment" };
            Assert.That(ProjectV2RewardConfig.Validate(out error), Is.False);
            TestGameTables.LoadAll();
            ProjectV2RewardConfig.BossReward(1).MaxQuality = 1;
            Assert.That(ProjectV2RewardConfig.Validate(out error), Is.False);
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
