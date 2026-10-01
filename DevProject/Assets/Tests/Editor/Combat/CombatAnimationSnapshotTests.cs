using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 验证战斗动画只读快照的时间和移动事实。
    /// </summary>
    public sealed class CombatAnimationSnapshotTests
    {
        private readonly List<CombatWorld> worlds = new List<CombatWorld>();

        /// <summary>
        /// 释放本测试创建的战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var world in worlds) world.Dispose();
            worlds.Clear();
        }

        /// <summary>
        /// 验证敌人的动作提示随房间时钟冻结并在回访后续播。
        /// </summary>
        [Test]
        public void EnemyCue_FollowsRoomClock()
        {
            var catalog = CombatCatalog.CreatePrototype();
            catalog.Skills[1].Nodes[0].Tick = 1;
            var world = CreateWorld(catalog);
            var player = CreateActor(world, 1, 1, true, Vector3.zero, 0f);
            var enemy = CreateActor(world, 2, 1, false, new Vector3(0f, 0f, 2f), 0f);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var first), Is.True);
            Assert.That(first.CueId, Is.EqualTo(1));
            Assert.That(first.CueElapsedTicks, Is.Zero);
            Assert.That(first.CueDurationTicks, Is.EqualTo(16));
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            for (var i = 0; i < 25; i++) world.Step();
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var frozen), Is.True);
            Assert.That(frozen.ClockTick, Is.EqualTo(first.ClockTick));
            Assert.That(frozen.CueElapsedTicks, Is.Zero);
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var resumed), Is.True);
            Assert.That(resumed.CueElapsedTicks, Is.EqualTo(1));
            for (var i = 0; i < 15; i++) world.Step();
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var expired), Is.True);
            Assert.That(expired.CueId, Is.Zero);
            Assert.That(expired.CueDurationTicks, Is.Zero);
        }

        /// <summary>
        /// 验证快照速度来自真实位移，传送及换图立即清零。
        /// </summary>
        [Test]
        public void Velocity_UsesActualMovementAndClearsOnTeleport()
        {
            var world = CreateWorld(CombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-2f, -2f, 4f, 4f));
            var player = CreateActor(world, 1, 1, true, new Vector3(1.75f, 0f, 0f), 4f, radius: 0.25f);
            Assert.That(world.SetMove(player, Vector2.right), Is.True);
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(player, out var blocked), Is.True);
            Assert.That(blocked.Velocity.sqrMagnitude, Is.Zero);
            Assert.That(world.TrySetPosition(player, Vector3.zero), Is.True);
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(player, out var moved), Is.True);
            Assert.That(moved.Velocity.x, Is.GreaterThan(0f));
            Assert.That(world.TrySetPosition(player, Vector3.zero), Is.True);
            Assert.That(world.TryGetAnimationSnapshot(player, out var teleported), Is.True);
            Assert.That(teleported.Velocity, Is.EqualTo(Vector3.zero));
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            Assert.That(world.TryGetAnimationSnapshot(player, out var transferred), Is.True);
            Assert.That(transferred.Velocity, Is.EqualTo(Vector3.zero));
        }

        /// <summary>
        /// 验证正伤害与死亡在发生帧产生零时差。
        /// </summary>
        [Test]
        public void HitAndDeath_RecordClockWithoutChangingDamage()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 2, StartRange = 2f };
            var cue = new CombatTimelineNode { Tick = 1 };
            cue.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 9 });
            var hit = new CombatTimelineNode { Tick = 2 };
            hit.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 10, Value = 20f,
                Shape = CombatHitShape.Circle, Radius = 3f, Height = 2f, CanDodge = false, CanCritical = false });
            skill.Nodes.Add(cue);
            skill.Nodes.Add(hit);
            catalog.Skills.Add(1, skill);
            var world = CreateWorld(catalog);
            var player = CreateActor(world, 1, 1, true, Vector3.zero, 0f);
            var enemy = CreateActor(world, 2, 1, false, new Vector3(0f, 0f, 1f), 0f, 10f);
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var before), Is.True);
            Assert.That(before.HitElapsedTicks, Is.EqualTo(-1));
            Assert.That(before.DeathElapsedTicks, Is.EqualTo(-1));
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var dead), Is.True);
            Assert.That(dead.Dead, Is.True);
            Assert.That(dead.HitElapsedTicks, Is.Zero);
            Assert.That(dead.DeathElapsedTicks, Is.Zero);
            Assert.That(world.TryGetActor(enemy, out var actor), Is.True);
            Assert.That(actor.Health, Is.Zero);
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(enemy, out var later), Is.True);
            Assert.That(later.HitElapsedTicks, Is.EqualTo(1));
            Assert.That(later.DeathElapsedTicks, Is.EqualTo(1));
            Assert.That(CreateWorld(catalog).TryGetAnimationSnapshot(enemy, out _), Is.False);
        }

        /// <summary>
        /// 创建并登记待释放的战斗世界。
        /// </summary>
        /// <param name="catalog">战斗配置集合。</param>
        /// <returns>新建的战斗世界。</returns>
        private CombatWorld CreateWorld(CombatCatalog catalog)
        {
            var world = new CombatWorld(catalog);
            worlds.Add(world);
            return world;
        }

        /// <summary>
        /// 创建测试角色。
        /// </summary>
        /// <param name="world">目标战斗世界。</param>
        /// <param name="actorId">稳定角色编号。</param>
        /// <param name="roomId">所在地图编号。</param>
        /// <param name="isPlayer">是否为玩家。</param>
        /// <param name="position">初始位置。</param>
        /// <param name="moveSpeed">移动速度。</param>
        /// <param name="health">最大生命值。</param>
        /// <param name="radius">角色占位半径。</param>
        /// <returns>创建成功的实体引用。</returns>
        private static CombatEntityRef CreateActor(CombatWorld world, int actorId, int roomId, bool isPlayer,
            Vector3 position, float moveSpeed, float health = 100f, float radius = 0.3f) => world.CreateActor(new CombatActorSpec
            {
                ActorId = actorId, RoomId = roomId, Team = isPlayer ? 1 : 2, IsPlayer = isPlayer,
                Position = position, MaxHealth = health, MoveSpeed = moveSpeed, Radius = radius
            });
    }
}
