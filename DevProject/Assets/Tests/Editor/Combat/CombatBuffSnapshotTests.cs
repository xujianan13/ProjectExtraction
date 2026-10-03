using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// Buff 状态快照的时钟、生命周期与副本边界测试。
    /// </summary>
    public sealed class CombatBuffSnapshotTests
    {
        private CombatWorld world;

        /// <summary>
        /// 每项测试结束时释放隔离战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            world?.Dispose();
            world = null;
        }

        /// <summary>
        /// 验证叠层沿用实例和到期时间，而重置合并更新剩余时间。
        /// </summary>
        [Test]
        public void Merge_ProjectsStacksAndResetClock()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            catalog.Buffs.Add(22, new CombatBuffDefinition
            {
                BuffId = 22, DurationTicks = 5, IntervalTicks = 10, MaxStacks = 1, Merge = CombatBuffMerge.ResetDuration
            });
            world = CreateWorld(catalog, out var player, out _);
            var output = new List<CombatBuffSnapshot>();
            Assert.That(world.ApplyBuff(player, 1, 9), Is.True);
            world.GetBuffsSnapshot(output);
            var original = output[0];
            Assert.That(world.ApplyBuff(player, 1, 9), Is.True);
            Assert.That(world.ApplyBuff(player, 22, 9), Is.True);
            world.Step();
            Assert.That(world.ApplyBuff(player, 22, 9), Is.True);
            world.GetBuffsSnapshot(output);
            Assert.That(output.Count, Is.EqualTo(2));
            Assert.That(output[0].InstanceId, Is.EqualTo(original.InstanceId));
            Assert.That(output[0].Stacks, Is.EqualTo(2));
            Assert.That(output[0].RemainingTicks, Is.EqualTo(179));
            Assert.That(output[1].RemainingTicks, Is.EqualTo(5));
            Assert.That(original.Stacks, Is.EqualTo(1));
            Assert.That(original.RemainingTicks, Is.EqualTo(180));
            Assert.That(typeof(CombatBuffSnapshot).GetField(nameof(CombatBuffSnapshot.Stacks)).IsInitOnly, Is.True);
        }

        /// <summary>
        /// 验证离房期间玩家按全局钟推进，原房间敌人按房间钟冻结。
        /// </summary>
        [Test]
        public void RoomTransfer_ProjectsOwnerClockAndActiveRoom()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            catalog.Buffs.Add(23, new CombatBuffDefinition { BuffId = 23, DurationTicks = 5, IntervalTicks = 10 });
            world = CreateWorld(catalog, out var player, out var enemy);
            world.SetRoomBounds(2, new Rect(-10f, -10f, 20f, 20f));
            var output = new List<CombatBuffSnapshot>();
            Assert.That(world.ApplyBuff(player, 23, 9), Is.True);
            Assert.That(world.ApplyBuff(enemy, 23, 9), Is.True);
            world.Step();
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            world.GetBuffsSnapshot(output);
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(output[0].ActorId, Is.EqualTo(1));
            for (var index = 0; index < 2; index++) world.Step();
            world.GetBuffsSnapshot(output);
            Assert.That(output[0].RemainingTicks, Is.EqualTo(2));
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            world.GetBuffsSnapshot(output);
            Assert.That(output.Count, Is.EqualTo(2));
            Assert.That(output.Find(item => item.ActorId == 1).RemainingTicks, Is.EqualTo(2));
            Assert.That(output.Find(item => item.ActorId == 2).RemainingTicks, Is.EqualTo(4));
            for (var index = 0; index < 2; index++) world.Step();
            world.GetBuffsSnapshot(output);
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(output[0].ActorId, Is.EqualTo(2));
            Assert.That(output[0].RemainingTicks, Is.EqualTo(2));
        }

        /// <summary>
        /// 验证目标死亡后快照即刻不再显示其 Buff，且复用列表会被清空。
        /// </summary>
        [Test]
        public void Death_OmitsBuffAndClearsOutput()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            catalog.Projectiles.Add(24, new CombatProjectileDefinition
            {
                ProjectileId = 24, Motion = ProjectileMotionKind.Instant, SpawnAnchor = ProjectileSpawnAnchor.TargetEntityAtSpawn,
                Radius = 1f, MaxDistinctTargets = 1
            });
            catalog.Projectiles[24].OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, Value = 200f });
            world = CreateWorld(catalog, out var player, out var enemy);
            var output = new List<CombatBuffSnapshot>();
            Assert.That(world.ApplyBuff(enemy, 1, 1), Is.True);
            world.GetBuffsSnapshot(output);
            Assert.That(output.Count, Is.EqualTo(1));
            Assert.That(world.TrySpawnProjectile(player, 24, enemy, Vector3.zero, Vector3.forward), Is.True);
            world.Step();
            world.GetBuffsSnapshot(output);
            Assert.That(output, Is.Empty);
        }

        /// <summary>
        /// 创建两名静止角色所在的战斗世界。
        /// </summary>
        /// <param name="catalog">隔离的测试目录。</param>
        /// <param name="player">输出玩家实体。</param>
        /// <param name="enemy">输出敌人实体。</param>
        /// <returns>已设置房间和角色的战斗世界。</returns>
        private static CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef player, out CombatEntityRef enemy)
        {
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(2f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
            });
            return world;
        }
    }
}
