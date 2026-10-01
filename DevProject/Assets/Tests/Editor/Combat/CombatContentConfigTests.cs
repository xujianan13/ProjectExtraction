using GameLogic.Gameplay.Combat;
using GameLogic.Tables;
using NUnit.Framework;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 使用真实导出表验证战斗目录构建和配置错误门禁。
    /// </summary>
    [NonParallelizable]
    public sealed class CombatContentConfigTests
    {
        /// <summary>
        /// 真实表可构建包含三种技能、飞弹命中链与装备范围的目录。
        /// </summary>
        [Test]
        public void BuildsCatalogFromExportedTables()
        {
            Assert.That(CombatContentConfig.Validate(out string error), Is.True, error);
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(2.4f);
            Assert.That(catalog.Skills.Count, Is.EqualTo(3));
            Assert.That(catalog.Skills[1].StartRange, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(catalog.Skills[1].Nodes[2].Effects[0].Radius, Is.EqualTo(2.4f).Within(0.0001f));
            Assert.That(catalog.Projectiles[1].OnHit.Count, Is.EqualTo(2));
            Assert.That(catalog.Projectiles[1].OnHit[1].LinkedId, Is.EqualTo(1));
            Assert.That(catalog.Buffs[1].OnTick[0].DefinitionId, Is.EqualTo(5));
        }

        /// <summary>
        /// 修改已加载表行会影响后建目录，旧目录保持原值。
        /// </summary>
        [Test]
        public void LaterCatalogUsesChangedTableValues()
        {
            CombatCatalog original = CombatContentConfig.CreateCatalog(2f);
            CfgCombatSkillsRow skill = GameTables.CfgCombatSkills.GetById(2);
            CfgCombatEffectsRow effect = GameTables.CfgCombatEffects.GetById(5);
            int oldCooldown = skill.CooldownTicks;
            double oldCoefficient = effect.AttackCoefficient;
            try
            {
                skill.CooldownTicks += 7;
                effect.AttackCoefficient += 0.25;
                CombatCatalog changed = CombatContentConfig.CreateCatalog(2f);
                Assert.That(changed.Skills[2].CooldownTicks, Is.EqualTo(oldCooldown + 7));
                Assert.That(changed.Skills[2].Nodes[1].Effects[0].AttackCoefficient, Is.EqualTo((float)(oldCoefficient + 0.25)));
                Assert.That(original.Skills[2].CooldownTicks, Is.EqualTo(oldCooldown));
                Assert.That(original.Skills[2].Nodes[1].Effects[0].AttackCoefficient, Is.EqualTo((float)oldCoefficient));
            }
            finally
            {
                skill.CooldownTicks = oldCooldown;
                effect.AttackCoefficient = oldCoefficient;
            }
        }

        /// <summary>
        /// 时间轴越界被总校验拒绝，恢复行数据后重新通过。
        /// </summary>
        [Test]
        public void RejectsOutOfRangeTimeline()
        {
            CfgCombatTimelineNodesRow node = GameTables.CfgCombatTimelineNodes.GetById(3);
            int oldTick = node.Tick;
            try
            {
                node.Tick = GameTables.CfgCombatSkills.GetById(node.SkillID).DurationTicks;
                Assert.That(CombatContentConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("时间轴"));
            }
            finally
            {
                node.Tick = oldTick;
            }
            Assert.That(CombatContentConfig.Validate(out string restoredError), Is.True, restoredError);
        }

        /// <summary>
        /// 效果关联的飞弹或 Buff 外键缺失时校验失败。
        /// </summary>
        [Test]
        public void RejectsMissingEffectReference()
        {
            CfgCombatEffectsRow effect = GameTables.CfgCombatEffects.GetById(9);
            int oldLinkedId = effect.LinkedID;
            try
            {
                effect.LinkedID = 999999;
                Assert.That(CombatContentConfig.Validate(out string error), Is.False);
                Assert.That(error, Does.Contain("关联定义不存在"));
            }
            finally
            {
                effect.LinkedID = oldLinkedId;
            }
            Assert.That(CombatContentConfig.Validate(out string restoredError), Is.True, restoredError);
        }
    }
}
