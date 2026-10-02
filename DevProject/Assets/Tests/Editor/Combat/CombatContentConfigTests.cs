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
        /// 真实表可构建包含全部技能、飞弹命中链与装备范围的目录。
        /// </summary>
        [Test]
        public void BuildsCatalogFromExportedTables()
        {
            Assert.That(CombatContentConfig.Validate(out string error), Is.True, error);
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(2.4f);
            Assert.That(catalog.Skills.Count, Is.EqualTo(GameTables.CfgCombatSkills.GetAll().Count));
            Assert.That(catalog.Skills.ContainsKey(7), Is.True, "Boss 阶段冲刺技能必须进入战斗目录。");
            Assert.That(catalog.Skills[1].StartRange, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(catalog.Skills[1].Nodes[2].Effects[0].Radius, Is.EqualTo(2.4f).Within(0.0001f));
            Assert.That(catalog.Projectiles[1].OnHit.Count, Is.EqualTo(2));
            Assert.That(catalog.Projectiles[1].OnHit[1].LinkedId, Is.EqualTo(1));
            Assert.That(catalog.Buffs[1].OnTick[0].DefinitionId, Is.EqualTo(5));
        }

        /// <summary>
        /// 高级词条效果映射生成独立快照，缺失编号时整体拒绝。
        /// </summary>
        [Test]
        public void TryCreateEffects_RejectsMissingIdsAndReturnsIndependentCopies()
        {
            Assert.That(CombatContentConfig.TryCreateEffects(new[] { 5 }, 2f, out var first), Is.True);
            Assert.That(CombatContentConfig.TryCreateEffects(new[] { 5 }, 2f, out var second), Is.True);
            Assert.That(first.Count, Is.EqualTo(1));
            Assert.That(first[0], Is.Not.SameAs(second[0]));
            Assert.That(CombatContentConfig.TryCreateEffects(new[] { 5, int.MaxValue }, 2f, out var missing), Is.False);
            Assert.That(missing, Is.Null);
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
        /// <summary>
        /// 敌人技能复制后与源表隔离，生命阶段缺口和超出起手距离的配置被拒绝。
        /// </summary>
        [Test]
        public void EnemySkillProfilesAreCopiedAndInvalidConditionsAreRejected()
        {
            Assert.That(CombatEnemySkillConfig.Validate(out string error), Is.True, error);
            CombatEnemySkillDefinition[] copied = CombatEnemySkillConfig.Create(1);
            CfgEnemySkillsRow row = GameTables.CfgEnemySkills.GetById(1);
            double oldMinimum = row.MinHealthRatio, oldDistance = row.MaxDistance;
            try
            {
                row.MinHealthRatio = 0.2;
                Assert.That(CombatEnemySkillConfig.Validate(out error), Is.False);
                Assert.That(error, Does.Contain("生命阶段"));
                Assert.That(copied[0].MinHealthRatio, Is.Zero);
                row.MinHealthRatio = oldMinimum;
                row.MaxDistance = GameTables.CfgCombatSkills.GetById(row.SkillID).StartRange + 1;
                Assert.That(CombatEnemySkillConfig.Validate(out error), Is.False);
                Assert.That(error, Does.Contain("技能选择配置无效"));
            }
            finally
            {
                row.MinHealthRatio = oldMinimum;
                row.MaxDistance = oldDistance;
            }
            Assert.That(CombatEnemySkillConfig.Validate(out error), Is.True, error);
        }
    }
}
