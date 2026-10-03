using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 战斗世界固定帧与跨图边界测试。
    /// </summary>
    public sealed class CombatWorldTests
    {
        /// <summary>
        /// 验证 0、5、10 时间轴在固定帧分别触发且第十帧造成伤害。
        /// </summary>
        [Test]
        public void Timeline_ZeroFiveTen_UsesFixedTicks()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 4f, Radius = 0.3f, AttackRange = 2.2f, BasicSkillId = 1
            });
            var enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(2.1f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f, AttackRange = 0f
            });
            world.Step();
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Cue), Is.True);
            for (var i = 0; i < 9; i++) world.Step();
            Assert.That(world.TryGetActor(enemy, out var before), Is.True);
            Assert.That(before.Health, Is.EqualTo(100f));
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var after), Is.True);
            Assert.That(after.Health, Is.LessThan(100f));
            Assert.That(world.TryGetActor(player, out var attacker), Is.True);
            Assert.That(attacker.Position.x, Is.GreaterThan(0f));
        }

        /// <summary>
        /// 验证敌方施法在非活跃地图冻结，返场按剩余时间继续。
        /// </summary>
        [Test]
        public void EnemyCast_UsesRoomClock()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            world.SetRoomBounds(2, new Rect(-20f, -20f, 40f, 40f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            var enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(1f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
            world.Step();
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            Assert.That(world.TrySetPosition(player, new Vector3(5f, 0f, 0f)), Is.True);
            for (var i = 0; i < 30; i++) world.Step();
            Assert.That(world.TryGetActor(enemy, out var frozen), Is.True);
            Assert.That(frozen.Health, Is.EqualTo(100f));
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.TrySetPosition(player, Vector3.zero), Is.True);
            world.Step();
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage), Is.False);
        }

        /// <summary>
        /// 验证帧间手动施法的零帧和一帧节点各执行一次。
        /// </summary>
        /// <param name="playerCasts">是否由玩家施法；否则由敌人施法。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void ManualCast_ZeroAndOneTickNodesExecuteOnce(bool playerCasts)
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 81, DurationTicks = 3, CooldownTicks = 3 };
            var start = new CombatTimelineNode { Tick = 0 };
            start.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 810 });
            var next = new CombatTimelineNode { Tick = 1 };
            next.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 811 });
            skill.Nodes.Add(start);
            skill.Nodes.Add(next);
            catalog.Skills.Add(81, skill);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(new CombatActorSpec { ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true,
                Position = Vector3.zero, MaxHealth = 100f, Radius = 0.3f });
            var enemy = world.CreateActor(new CombatActorSpec { ActorId = 2, RoomId = 1, Team = 2,
                Position = new Vector3(2f, 0f, 0f), MaxHealth = 100f, Radius = 0.3f });
            Assert.That(world.TryStartSkill(playerCasts ? player : enemy, 81, default), Is.True);
            world.Step();
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Cue && item.DefinitionId == 810).Count, Is.EqualTo(1));
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Cue && item.DefinitionId == 811).Count, Is.EqualTo(1));
            world.Step();
            events.Clear();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Cue).Count, Is.Zero);
            world.Dispose();
        }

        /// <summary>
        /// 验证玩家零帧节点尚未执行就换房时，取消后返场不会补发。
        /// </summary>
        [Test]
        public void ManualCast_TransferBeforeStepCancelsZeroTickNode()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 82, DurationTicks = 3, CooldownTicks = 3 };
            var start = new CombatTimelineNode { Tick = 0 };
            start.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 820 });
            skill.Nodes.Add(start);
            catalog.Skills.Add(82, skill);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.SetRoomBounds(2, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(new CombatActorSpec { ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true,
                Position = Vector3.zero, MaxHealth = 100f, Radius = 0.3f });
            Assert.That(world.TryStartSkill(player, 82, default), Is.True);
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            world.Step();
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            world.Step();
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Cue && item.DefinitionId == 820), Is.False);
            world.Dispose();
        }

        /// <summary>
        /// 验证瞬时飞弹允许零速度并在目标点当次命中。
        /// </summary>
        [Test]
        public void InstantProjectile_HitsTargetPointWithoutFlight()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            var instant = new CombatProjectileDefinition
            {
                ProjectileId = 9, Motion = ProjectileMotionKind.Instant,
                SpawnAnchor = ProjectileSpawnAnchor.TargetEntityAtSpawn, Radius = 1f, MaxDistinctTargets = 1
            };
            instant.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, Value = 11f });
            catalog.Projectiles.Add(9, instant);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            var enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(8f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            Assert.That(world.TrySpawnProjectile(player, 9, enemy, Vector3.zero, Vector3.forward), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var result), Is.True);
            Assert.That(result.Health, Is.EqualTo(89f));
        }

        /// <summary>
        /// 验证死亡事实只发一次并保留致死攻击者归因。
        /// </summary>
        [Test]
        public void Death_PublishedOnceWithInstigator()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            catalog.Skills[1].Nodes[2].Effects[0].Value = 120f;
            catalog.Skills[1].Nodes[1].Effects[0].Value = 0f;
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f, AttackRange = 2.2f, BasicSkillId = 1
            });
            world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(1f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            for (var i = 0; i < 20; i++) world.Step();
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            var deaths = events.FindAll(item => item.Kind == CombatEventKind.Death);
            Assert.That(deaths.Count, Is.EqualTo(1));
            Assert.That(deaths[0].ActorId, Is.EqualTo(1));
            Assert.That(deaths[0].TargetActorId, Is.EqualTo(2));
            Assert.That(deaths[0].DefinitionId, Is.GreaterThan(0));
        }
    }
}
