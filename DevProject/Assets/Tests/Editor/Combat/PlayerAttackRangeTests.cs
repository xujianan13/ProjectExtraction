using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 使用正式装备和普攻配置验证射程、伤害边界及墙体遮挡。
    /// </summary>
    public sealed class PlayerAttackRangeTests
    {
        private CombatWorld world;

        /// <summary>
        /// 释放用例创建的战斗世界，前置断言失败时允许尚未创建世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            world?.Dispose();
            world = null;
        }

        /// <summary>
        /// 空手和各武器最短射程均覆盖普通与精英敌人的起手范围，并能实际命中边界目标。
        /// </summary>
        /// <param name="definitionId">武器模板编号；空字符串表示空手。</param>
        [TestCase("")]
        [TestCase("starter_weapon")]
        [TestCase("ruin_weapon")]
        [TestCase("ruin_heavy_blade")]
        [TestCase("ruin_poleblade")]
        [TestCase("guard_blade")]
        public void EveryPlayerWeapon_OutrangesNormalAndEliteEnemiesAndHitsAtSearchLimit(string definitionId)
        {
            ProjectCharacterStats stats = PlayerStats(definitionId);
            int skillId = PlayerBasicSkill(definitionId);
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(stats.AttackRange);
            double minimumRange = definitionId.Length == 0 ? ProjectRulesConfig.Profile.BaseAttackRange :
                ProjectRulesConfig.Item(definitionId).RangeBase;
            foreach (CfgEnemySkillsRow skill in GameTables.CfgEnemySkills.GetAll())
                if (GameTables.CfgEnemies.GetById(skill.EnemyID).Role < 2)
                    Assert.That(minimumRange, Is.GreaterThan(skill.MaxDistance), definitionId + ": " + skill.EnemyID);

            world = CreateWorld(stats, out _, out CombatEntityRef enemy, stats.AttackRange, skillId);
            world.Step();
            var telegraphs = new List<CombatTelegraphSnapshot>();
            world.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs.Count, Is.EqualTo(1));
            Assert.That(telegraphs[0].Radius, Is.EqualTo(stats.AttackRange).Within(0.0001f));
            CombatEffectDefinition damage = catalog.Skills[skillId].Nodes.SelectMany(node => node.Effects)
                .First(effect => effect.Kind == CombatEffectKind.Damage);
            Assert.That(damage.Shape, Is.EqualTo(definitionId == "ruin_poleblade" ? CombatHitShape.ForwardBox : CombatHitShape.Sector));
            Assert.That(catalog.Skills[skillId].Nodes.SelectMany(node => node.Effects).Any(effect => effect.Kind == CombatEffectKind.Displace), Is.False);
            AdvanceToHit(world, skillId);
            Assert.That(world.TryGetActor(enemy, out CombatActorSnapshot target), Is.True);
            Assert.That(target.Health, Is.LessThan(target.MaxHealth));
        }

        /// <summary>
        /// 自动普攻在角色射程外不因技能额外起手余量而提前触发。
        /// </summary>
        [Test]
        public void AutoAttack_DoesNotStartOutsideCharacterRange()
        {
            ProjectCharacterStats stats = PlayerStats("starter_weapon");
            world = CreateWorld(stats, out _, out CombatEntityRef enemy, stats.AttackRange + 0.01f);
            world.Step();
            var telegraphs = new List<CombatTelegraphSnapshot>();
            world.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs, Is.Empty);
            AdvanceToHit(world);
            Assert.That(world.TryGetActor(enemy, out CombatActorSnapshot target), Is.True);
            Assert.That(target.Health, Is.EqualTo(target.MaxHealth));
        }

        /// <summary>
        /// 正式扇形普攻在真实半径内命中，在半径外不命中，且不附带强制前冲。
        /// </summary>
        /// <param name="offset">目标相对命中边界的距离偏差。</param>
        /// <param name="expectDamage">是否应产生伤害。</param>
        [TestCase(-0.01f, true)]
        [TestCase(0.01f, false)]
        public void Damage_RespectsActualSectorRangeWithoutLunge(float offset, bool expectDamage)
        {
            ProjectCharacterStats stats = PlayerStats("starter_weapon");
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(stats.AttackRange);
            Assert.That(catalog.Skills[ProjectContentConfig.Player.SkillID].Nodes.SelectMany(node => node.Effects)
                .Any(effect => effect.Kind == CombatEffectKind.Displace), Is.False);
            world = CreateWorld(stats, out CombatEntityRef player, out CombatEntityRef enemy, stats.AttackRange + offset);
            Assert.That(world.TrySetPosition(enemy, new Vector3(0f, 0f, stats.AttackRange + offset)), Is.True);
            Assert.That(world.TryStartSkill(player, ProjectContentConfig.Player.SkillID, default), Is.True);
            world.Step();
            AdvanceToHit(world);
            Assert.That(world.TryGetActor(enemy, out CombatActorSnapshot target), Is.True);
            Assert.That(target.Health < target.MaxHealth, Is.EqualTo(expectDamage));
            Assert.That(world.TryGetActor(player, out var attacker), Is.True);
            Assert.That(attacker.Position, Is.EqualTo(Vector3.zero));
        }

        /// <summary>
        /// 墙体在索敌之前或施法之后出现，均不能让扩大射程的普攻穿墙伤害。
        /// </summary>
        /// <param name="afterCast">是否在已经发起普攻之后放置墙体。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Attack_WallBlocksSelectionAndDamage(bool afterCast)
        {
            ProjectCharacterStats stats = PlayerStats("starter_weapon");
            world = CreateWorld(stats, out _, out CombatEntityRef enemy, stats.AttackRange - 0.1f);
            if (afterCast) world.Step();
            world.AddObstacle(1, new Rect(2f, -1f, 0.5f, 2f));
            if (!afterCast) world.Step();
            var telegraphs = new List<CombatTelegraphSnapshot>();
            world.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs.Count, Is.EqualTo(afterCast ? 1 : 0));
            AdvanceToHit(world);
            Assert.That(world.TryGetActor(enemy, out CombatActorSnapshot target), Is.True);
            Assert.That(target.Health, Is.EqualTo(target.MaxHealth));
        }

        /// <summary>
        /// 用真实模板创建装备并执行正式角色属性汇总。
        /// </summary>
        /// <param name="definitionId">武器模板编号；空字符串表示空手。</param>
        /// <returns>可以直接接入战斗的角色属性。</returns>
        private static ProjectCharacterStats PlayerStats(string definitionId)
        {
            var items = new List<ProjectItem>();
            if (definitionId.Length > 0)
            {
                ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(37), 1, ProjectItemQuality.Common, definitionId);
                Assert.That(item, Is.Not.Null);
                item.Equipped = true;
                items.Add(item);
            }
            return ProjectEquipmentRules.Calculate(items, 0, 0, 0);
        }

        /// <summary>
        /// 经正式武器映射读取当前普攻入口。
        /// </summary>
        /// <param name="definitionId">装备模板，空字符串表示徒手。</param>
        /// <returns>当前武器的普攻技能编号。</returns>
        private static int PlayerBasicSkill(string definitionId)
        {
            var items = new List<ProjectItem>();
            if (definitionId.Length > 0)
            {
                ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(37), 1, ProjectItemQuality.Common, definitionId);
                item.Equipped = true;
                items.Add(item);
            }
            Assert.That(ProjectWeaponCombat.TryGetBasicSkill(items, out int skillId), Is.True);
            return skillId;
        }

        /// <summary>
        /// 创建正式普攻目录和静止敌人，隔离敌人技能与移动干扰。
        /// </summary>
        /// <param name="stats">正式汇总后的玩家属性。</param>
        /// <param name="player">创建的玩家引用。</param>
        /// <param name="enemy">创建的目标引用。</param>
        /// <param name="distance">玩家与目标的初始距离。</param>
        /// <param name="skillId">本次武器对应的普攻技能编号。</param>
        /// <returns>需要由调用方释放的战斗世界。</returns>
        private static CombatWorld CreateWorld(ProjectCharacterStats stats, out CombatEntityRef player, out CombatEntityRef enemy,
            float distance, int skillId = 1)
        {
            var world = new CombatWorld(CombatContentConfig.CreateCatalog(stats.AttackRange));
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = stats.MaxHealth, Attack = stats.Attack, Radius = (float)ProjectContentConfig.Player.Radius,
                AttackRange = stats.AttackRange, BasicSkillId = skillId
            });
            enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(distance, 0f, 0f), MaxHealth = 1000f, Radius = 0.3f
            });
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            return world;
        }

        /// <summary>
        /// 从普攻第一帧推进到正式配置的首个伤害节点。
        /// </summary>
        /// <param name="world">已经开始普攻的战斗世界。</param>
        /// <param name="skillId">本次普攻技能编号。</param>
        private static void AdvanceToHit(CombatWorld world, int skillId = 1)
        {
            int hitTick = int.MaxValue;
            foreach (CfgCombatTimelineNodesRow node in GameTables.CfgCombatTimelineNodes.GetAll())
                if (node.SkillID == skillId)
                    foreach (int effectId in node.EffectIDs)
                        if (GameTables.CfgCombatEffects.GetById(effectId).Kind == (int)CombatEffectKind.Damage)
                            hitTick = Math.Min(hitTick, node.Tick);
            Assert.That(hitTick, Is.Not.EqualTo(int.MaxValue));
            for (int tick = 0; tick < hitTick; tick++) world.Step();
        }
    }
}
