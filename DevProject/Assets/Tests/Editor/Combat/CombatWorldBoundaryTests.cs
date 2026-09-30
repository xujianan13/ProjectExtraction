using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 周期 Buff、连续扫掠及因果预算的战斗边界测试。
    /// </summary>
    public sealed class CombatWorldBoundaryTests
    {
        /// <summary>
        /// 验证 Buff 叠加上限、重置时长与半开到期边界。
        /// </summary>
        [Test]
        public void Buff_StackResetAndExpiry_AreDeterministic()
        {
            var catalog = CombatCatalog.CreatePrototype();
            var stack = new CombatBuffDefinition
            {
                BuffId = 21, DurationTicks = 4, IntervalTicks = 1, MaxStacks = 2,
                Merge = CombatBuffMerge.AddStack, PeriodicDamagePerStack = 1f
            };
            var reset = new CombatBuffDefinition
            {
                BuffId = 22, DurationTicks = 4, IntervalTicks = 2, MaxStacks = 1,
                Merge = CombatBuffMerge.ResetDuration, PeriodicDamagePerStack = 1f
            };
            catalog.Buffs.Add(21, stack);
            catalog.Buffs.Add(22, reset);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 30f, MoveSpeed = 0f, Radius = 0.3f
            });
            Assert.That(world.ApplyBuff(player, 21, 2), Is.True);
            Assert.That(world.ApplyBuff(player, 21, 2), Is.True);
            Assert.That(world.ApplyBuff(player, 21, 2), Is.True);
            Assert.That(world.ApplyBuff(player, 22, 2), Is.True);
            world.Step();
            Assert.That(world.ApplyBuff(player, 22, 2), Is.True);
            for (var i = 0; i < 4; i++) world.Step();
            Assert.That(world.TryGetActor(player, out var result), Is.True);
            Assert.That(result.Health, Is.EqualTo(22f));
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.BuffExpired).Count, Is.EqualTo(2));
        }

        /// <summary>
        /// 验证高速弹先命中近处角色，墙体阻止命中墙后目标。
        /// </summary>
        [Test]
        public void ProjectileSweep_HitsNearTargetBeforeWall()
        {
            var catalog = CombatCatalog.CreatePrototype();
            var fast = new CombatProjectileDefinition
            {
                ProjectileId = 31, Motion = ProjectileMotionKind.Straight, Speed = 600f, MaxSpeed = 600f,
                LifetimeTicks = 2, MaxDistance = 20f, Radius = 0.1f, MaxDistinctTargets = 2
            };
            fast.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, Value = 10f });
            catalog.Projectiles.Add(31, fast);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-5f, -5f, 20f, 10f));
            world.AddObstacle(1, new Rect(5f, -1f, 0.2f, 2f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            var near = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(3f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            var behind = world.CreateActor(new CombatActorSpec
            {
                ActorId = 3, RoomId = 1, Team = 2, Position = new Vector3(7f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            Assert.That(world.TrySpawnProjectile(player, 31, default, Vector3.zero, Vector3.right), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(near, out var nearState), Is.True);
            Assert.That(world.TryGetActor(behind, out var behindState), Is.True);
            Assert.That(nearState.Health, Is.EqualTo(90f));
            Assert.That(behindState.Health, Is.EqualTo(100f));
        }

        /// <summary>
        /// 验证瞬时飞弹互相生成的零等待链被预算阻止。
        /// </summary>
        [Test]
        public void RecursiveProjectileEffects_EnterFault()
        {
            var catalog = CombatCatalog.CreatePrototype();
            var loop = new CombatProjectileDefinition
            {
                ProjectileId = 41, Motion = ProjectileMotionKind.Instant, Radius = 1f, MaxDistinctTargets = 1
            };
            loop.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 41 });
            catalog.Projectiles.Add(41, loop);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-5f, -5f, 10f, 10f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(0.5f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            Assert.That(world.TrySpawnProjectile(player, 41, default, Vector3.zero, Vector3.right), Is.True);
            world.Step();
            Assert.That(world.IsFaulted, Is.True);
        }
    }
}
