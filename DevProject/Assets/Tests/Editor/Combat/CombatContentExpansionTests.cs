using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 使用正式内容表验证新增敌人的时间轴命中、闪避空间和地面预警。
    /// </summary>
    public sealed class CombatContentExpansionTests
    {
        private readonly List<CombatWorld> worlds = new List<CombatWorld>();
        private readonly List<CombatEvent> events = new List<CombatEvent>();
        private readonly List<CombatTelegraphSnapshot> telegraphs = new List<CombatTelegraphSnapshot>();

        /// <summary>
        /// 释放本测试创建的战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (CombatWorld world in worlds) world.Dispose();
            worlds.Clear();
        }

        /// <summary>
        /// 连斩的两个正式伤害节点各命中一次，节点之间和施法结束前不产生额外伤害，预警切换到下一节点。
        /// </summary>
        [Test]
        public void TwinSlash_HitsOnlyAtBothDamageNodesAndAdvancesTelegraph()
        {
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(2f);
            CombatSkillDefinition skill = catalog.Skills[8];
            Assert.That(skill.DurationTicks, Is.EqualTo(38));
            Assert.That(skill.CooldownTicks, Is.EqualTo(70));
            Assert.That(skill.StartRange, Is.EqualTo(2.2f).Within(0.0001f));
            Assert.That(skill.Nodes.Count, Is.EqualTo(3));
            Assert.That(skill.Nodes[0].Tick, Is.Zero);
            Assert.That(skill.Nodes[0].Effects[0].DefinitionId, Is.EqualTo(80));
            Assert.That(skill.Nodes[1].Tick, Is.EqualTo(14));
            Assert.That(skill.Nodes[2].Tick, Is.EqualTo(24));
            Assert.That(skill.Nodes[1].Effects[0].DefinitionId, Is.EqualTo(81));
            Assert.That(skill.Nodes[2].Effects[0].DefinitionId, Is.EqualTo(81));
            Assert.That(skill.Nodes[1].Effects[0].AttackCoefficient, Is.EqualTo(0.6f).Within(0.0001f));
            Assert.That(skill.Nodes[1].Effects[0].RequireLineOfSight, Is.True);
            CreateWorld(catalog, 5, 1.2f, out CombatWorld world, out _, out _);

            world.Step();
            Assert.That(Damages(world), Is.Empty);
            CombatTelegraphSnapshot firstWarning = SingleTelegraph(world);
            AssertWarning(firstWarning, 1.8f, 100f);
            for (int tick = 1; tick < 14; tick++)
            {
                world.Step();
                Assert.That(Damages(world), Is.Empty, "首次命中前不得提前伤害。");
            }
            world.Step();
            AssertSingleDamage(world, 15, 81);
            CombatTelegraphSnapshot secondWarning = SingleTelegraph(world);
            Assert.That(secondWarning.Cast, Is.EqualTo(firstWarning.Cast));
            AssertWarning(secondWarning, 1.8f, 100f);
            Assert.That(secondWarning.Progress, Is.GreaterThan(0f));
            for (int tick = 15; tick < 24; tick++)
            {
                world.Step();
                Assert.That(Damages(world), Is.Empty, "两次节点之间不得额外伤害。");
            }
            world.Step();
            AssertSingleDamage(world, 25, 81);
            world.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs, Is.Empty);
            for (int tick = 25; tick <= skill.DurationTicks; tick++)
            {
                world.Step();
                Assert.That(Damages(world), Is.Empty, "施法剩余时间不得额外伤害。");
            }
            Assert.That(world.Faulted, Is.False, world.FaultReason);
        }

        /// <summary>
        /// 连斩首击后离开扇形范围，第二个正式伤害节点可以躲开。
        /// </summary>
        [Test]
        public void TwinSlash_LeavingRangeAfterFirstHitAvoidsSecond()
        {
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(2f);
            CreateWorld(catalog, 5, 1.2f, out CombatWorld world, out CombatEntityRef player, out _);
            for (int tick = 1; tick <= 15; tick++) world.Step();
            AssertSingleDamage(world, 15, 81);
            float radius = catalog.Skills[8].Nodes[1].Effects[0].Radius;
            Assert.That(world.TrySetPosition(player, new Vector3(0f, 0f, radius + 0.5f)), Is.True);
            for (int tick = 16; tick <= 25; tick++)
            {
                world.Step();
                Assert.That(Damages(world), Is.Empty, "离开连斩范围后第二击必须落空。");
            }
            Assert.That(world.Faulted, Is.False, world.FaultReason);
        }

        /// <summary>
        /// 长柄守卫的正前方三米可命中，侧移与施法后设置的墙体均阻挡实际伤害节点。
        /// </summary>
        [Test]
        public void PolearmGuard_HitsFrontButMissesSideAndBlockedTarget()
        {
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(2f);
            CombatSkillDefinition skill = catalog.Skills[9];
            Assert.That(skill.DurationTicks, Is.EqualTo(50));
            Assert.That(skill.CooldownTicks, Is.EqualTo(90));
            Assert.That(skill.StartRange, Is.EqualTo(3.7f).Within(0.0001f));
            Assert.That(skill.Nodes.Count, Is.EqualTo(2));
            Assert.That(skill.Nodes[0].Tick, Is.Zero);
            Assert.That(skill.Nodes[0].Effects[0].DefinitionId, Is.EqualTo(90));
            Assert.That(skill.Nodes[1].Tick, Is.EqualTo(32));
            CombatEffectDefinition hit = skill.Nodes[1].Effects[0];
            Assert.That(hit.DefinitionId, Is.EqualTo(91));
            Assert.That(hit.AttackCoefficient, Is.EqualTo(1.3f).Within(0.0001f));
            Assert.That(hit.RequireLineOfSight, Is.True);

            CreateWorld(catalog, 6, 3f, out CombatWorld front, out _, out _);
            front.Step();
            Assert.That(Damages(front), Is.Empty);
            AssertWarning(SingleTelegraph(front), 3.7f, 55f);
            for (int tick = 2; tick < 33; tick++)
            {
                front.Step();
                Assert.That(Damages(front), Is.Empty);
            }
            front.Step();
            AssertSingleDamage(front, 33, 91);

            CreateWorld(catalog, 6, 3f, out CombatWorld side, out CombatEntityRef sidePlayer, out _);
            side.Step();
            Assert.That(side.TrySetPosition(sidePlayer, new Vector3(3f, 0f, 0f)), Is.True);
            for (int tick = 2; tick <= 33; tick++) side.Step();
            Assert.That(Damages(side), Is.Empty, "施法方向侧面不得命中。");

            CreateWorld(catalog, 6, 3f, out CombatWorld blocked, out _, out _);
            blocked.Step();
            Assert.That(blocked.AddObstacle(1, new Rect(-0.4f, 1.4f, 0.8f, 0.2f)), Is.True);
            for (int tick = 2; tick <= 33; tick++) blocked.Step();
            Assert.That(Damages(blocked), Is.Empty, "实体墙应阻挡要求视线的伤害。");
            Assert.That(front.Faulted || side.Faulted || blocked.Faulted, Is.False);
        }

        /// <summary>
        /// 从正式表创建静止角色和单技能敌人；玩家不配置自动普攻以隔离敌人伤害。
        /// </summary>
        /// <param name="catalog">由正式表生成的战斗目录。</param>
        /// <param name="enemyId">敌人配置编号。</param>
        /// <param name="distance">玩家位于敌人前方的距离。</param>
        /// <param name="world">创建的战斗世界。</param>
        /// <param name="player">创建的玩家引用。</param>
        /// <param name="enemy">创建的敌人引用。</param>
        private void CreateWorld(CombatCatalog catalog, int enemyId, float distance, out CombatWorld world,
            out CombatEntityRef player, out CombatEntityRef enemy)
        {
            CfgEnemiesRow row = GameTables.CfgEnemies.GetById(enemyId);
            Assert.That(row, Is.Not.Null);
            CombatEnemySkillDefinition[] rules = CombatEnemySkillConfig.Create(enemyId);
            Assert.That(rules, Has.Length.EqualTo(1));
            Assert.That(rules[0].SkillId, Is.EqualTo(enemyId + 3));
            Assert.That(row.SkillID, Is.EqualTo(enemyId + 3));
            Assert.That(rules[0].MinHealthRatio, Is.Zero);
            Assert.That(rules[0].MaxHealthRatio, Is.EqualTo(1f));
            world = new CombatWorld(catalog, 123);
            worlds.Add(world);
            Assert.That(world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f)), Is.True);
            player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = new Vector3(0f, 0f, distance),
                MaxHealth = 10000f, Radius = 0.3f, BasicSkillId = 0
            });
            enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.zero,
                MaxHealth = (float)row.BaseHealth, Attack = (float)row.Attack, MoveSpeed = 0f,
                Radius = (float)row.Radius, AttackRange = (float)row.AttackRange,
                AggroRange = (float)row.AggroRange, BasicSkillId = row.SkillID, EnemySkills = rules
            });
            Assert.That(player.Id, Is.Not.Zero);
            Assert.That(enemy.Id, Is.Not.Zero);
        }

        /// <summary>
        /// 清空世界事件并只返回本次伤害事件。
        /// </summary>
        /// <param name="world">待读取世界。</param>
        /// <returns>自上次读取后产生的伤害事件。</returns>
        private List<CombatEvent> Damages(CombatWorld world)
        {
            events.Clear();
            world.DrainEvents(events);
            return events.FindAll(item => item.Kind == CombatEventKind.Damage);
        }

        /// <summary>
        /// 断言本帧只有一次来自指定节点的玩家伤害。
        /// </summary>
        /// <param name="world">待读取世界。</param>
        /// <param name="tick">预期逻辑帧。</param>
        /// <param name="definitionId">伤害效果定义编号。</param>
        private void AssertSingleDamage(CombatWorld world, int tick, int definitionId)
        {
            List<CombatEvent> damages = Damages(world);
            Assert.That(damages, Has.Count.EqualTo(1));
            Assert.That(damages[0].Tick, Is.EqualTo(tick));
            Assert.That(damages[0].ActorId, Is.EqualTo(2));
            Assert.That(damages[0].TargetActorId, Is.EqualTo(1));
            Assert.That(damages[0].DefinitionId, Is.EqualTo(definitionId));
            Assert.That(damages[0].Value, Is.GreaterThan(0f));
        }

        /// <summary>
        /// 获取当前唯一的敌人预警。
        /// </summary>
        /// <param name="world">待读取世界。</param>
        /// <returns>真实时间轴上的下一次伤害预警。</returns>
        private CombatTelegraphSnapshot SingleTelegraph(CombatWorld world)
        {
            world.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs, Has.Count.EqualTo(1));
            return telegraphs[0];
        }

        /// <summary>
        /// 核对真实伤害节点导出的扇形范围与施法方向。
        /// </summary>
        /// <param name="warning">待检查的预警快照。</param>
        /// <param name="radius">期望的半径。</param>
        /// <param name="angle">期望的扇形角度。</param>
        private static void AssertWarning(CombatTelegraphSnapshot warning, float radius, float angle)
        {
            Assert.That(warning.ActorId, Is.EqualTo(2));
            Assert.That(warning.IsPlayer || warning.IsProjectile, Is.False);
            Assert.That(warning.Shape, Is.EqualTo(CombatHitShape.Sector));
            Assert.That(warning.Origin, Is.EqualTo(Vector3.zero));
            Assert.That(warning.Direction, Is.EqualTo(Vector3.forward));
            Assert.That(warning.Radius, Is.EqualTo(radius).Within(0.0001f));
            Assert.That(warning.Angle, Is.EqualTo(angle).Within(0.0001f));
        }
    }
}
