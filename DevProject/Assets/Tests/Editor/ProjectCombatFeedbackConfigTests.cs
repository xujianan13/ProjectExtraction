using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证事件显示样式匹配、治疗边界和 Buff 配置覆盖。
    /// </summary>
    public sealed class ProjectCombatFeedbackConfigTests
    {
        /// <summary>
        /// 恢复测试期间改动过的配置行。
        /// </summary>
        [TearDown]
        public void RestoreTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 通用样式和具体衍生伤害分别匹配，治疗不能产生受击效果。
        /// </summary>
        [Test]
        public void EventStylesDistinguishHealingAndDerivedDamage()
        {
            Assert.IsTrue(ProjectCombatFeedbackConfig.Validate(out string error), error);
            var heal = ProjectCombatFeedbackConfig.Event(new CombatEvent(CombatEventKind.Heal, 1, 1, 1, 1, 1003, 0.25f, Vector3.zero));
            Assert.AreEqual("+{value}", heal.Text);
            Assert.IsFalse(heal.ShowHit || heal.ShowFlash);
            Assert.Greater(heal.ColorG, heal.ColorR);
            var bleed = ProjectCombatFeedbackConfig.Event(new CombatEvent(CombatEventKind.Damage, 1, 1, 1, 2, 1001, 3f, Vector3.zero));
            Assert.That(bleed.Text, Does.Contain("流血"));
            var critical = ProjectCombatFeedbackConfig.Event(new CombatEvent(CombatEventKind.Damage, 1, 1, 1, 2, 0, 10f, Vector3.zero, true));
            Assert.That(critical.Text, Does.Contain("暴击"));
            Assert.AreEqual("流血", ProjectCombatFeedbackConfig.Status(1001).Text);
            Assert.IsNull(ProjectCombatFeedbackConfig.Event(new CombatEvent(CombatEventKind.Death, 1, 1, 1, 2, 0, 0f, Vector3.zero)));
        }

        /// <summary>
        /// 错误效果引用及治疗受击开关应阻止启用显示配置。
        /// </summary>
        [Test]
        public void RejectsInvalidReferenceAndHealingHitEffects()
        {
            var row = GameTables.CfgCombatFeedback.GetById("bleed_damage");
            row.DefinitionID = 99999;
            Assert.IsFalse(ProjectCombatFeedbackConfig.Validate(out string invalidReference));
            Assert.That(invalidReference, Does.Contain("引用"));
            row.DefinitionID = 1001;
            GameTables.CfgCombatFeedback.GetById("heal").ShowHit = true;
            Assert.IsFalse(ProjectCombatFeedbackConfig.Validate(out _));
        }

        /// <summary>
        /// 重复匹配键和非有限颜色不允许被静默采用。
        /// </summary>
        [Test]
        public void RejectsDuplicateStyleAndInvalidColor()
        {
            var row = GameTables.CfgCombatFeedback.GetById("critical");
            row.Critical = false;
            Assert.IsFalse(ProjectCombatFeedbackConfig.Validate(out _));
            row.Critical = true;
            row.ColorG = double.NaN;
            Assert.IsFalse(ProjectCombatFeedbackConfig.Validate(out _));
        }
    }
}
