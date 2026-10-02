using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 高级强化的表关联、倍率边界与快照转换验证。
    /// </summary>
    public sealed class ProjectAugmentConfigTests
    {
        /// <summary>
        /// 每项测试恢复真实配置，避免故障注入污染后续用例。
        /// </summary>
        [TearDown]
        public void RestoreTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 正式配置可加载，效果倍率只修改独立快照且治疗量可按等级增长。
        /// </summary>
        [Test]
        public void ConfiguredAugments_CreateIndependentSnapshots()
        {
            Assert.That(ProjectRulesConfig.Validate(out string error), Is.True, error);
            Assert.That(ProjectAugmentConfig.TryCreate("kill_leech", "reward", 2, 2, out var heal), Is.True);
            Assert.That(heal.Effects[0].Kind, Is.EqualTo(CombatEffectKind.Heal));
            Assert.That(heal.Effects[0].Value, Is.EqualTo(8));
            heal.Effects[0].Value = 999;
            Assert.That(ProjectAugmentConfig.TryCreate("kill_leech", "gear", 1, 2, out var copy), Is.True);
            Assert.That(copy.Effects[0].Value, Is.EqualTo(4));
            Assert.That(GameTables.CfgCombatEffects.GetById(1003).Value, Is.EqualTo(4));
            Assert.That(ProjectAugmentConfig.TryCreate("wide_blade", "range", 0.3f, 2, out var range), Is.True);
            Assert.That(range.RangeMultiplier, Is.EqualTo(1.3f).Within(0.0001f));
        }

        /// <summary>
        /// 无效技能倍率、缺失效果、奖励前置循环和不足的品质池均阻止启动。
        /// </summary>
        [Test]
        public void InvalidReferencesAndUnreachableRewardsFailBeforeRun()
        {
            GameTables.CfgAdvancedAffixes.GetById("weapon_swift").ValueMax = 0.99;
            Assert.That(ProjectAugmentConfig.Validate(out _), Is.False);
            TestGameTables.LoadAll();
            GameTables.CfgCombatAugments.GetById("bleed").EffectIDs = new[] { 999999 };
            Assert.That(ProjectAugmentConfig.Validate(out _), Is.False);
            TestGameTables.LoadAll();
            var bleed = GameTables.CfgRogueRewards.GetById("bleed");
            bleed.PrerequisiteID = "bloodlust";
            bleed.PrerequisiteRank = 1;
            Assert.That(ProjectAugmentConfig.Validate(out string cycle), Is.False);
            Assert.That(cycle, Does.Contain("前置"));
            TestGameTables.LoadAll();
            GameTables.CfgQualities.GetById(5).AdvancedAffixCount = 3;
            Assert.That(ProjectAugmentConfig.Validate(out string pool), Is.False);
            Assert.That(pool, Does.Contain("词条池"));
        }
    }
}
