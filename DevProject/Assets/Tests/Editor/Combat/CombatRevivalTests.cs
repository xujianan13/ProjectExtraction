using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 验证玩家原地复活的死亡消费边界、重复死亡和完整状态恢复。
    /// </summary>
    public sealed class CombatRevivalTests
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
        /// 多次真实致命伤害后复活保留身份和位置，并能继续移动攻击且每次仅发布一次死亡。
        /// </summary>
        [Test]
        public void Revive_RepeatedDeathsRemainUniqueAndPlayerCanMoveAndAttack()
        {
            var world = CreateWorld(CreateCatalog(), out var player, out var enemy);
            int previousDeathId = 0;
            for (int cycle = 0; cycle < 3; cycle++)
            {
                Assert.That(world.SetMove(player, Vector2.right), Is.True);
                Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
                world.Step();
                Assert.That(world.TryGetActor(player, out var dead), Is.True);
                Assert.That(dead.Dead && dead.Health == 0f, Is.True);
                Assert.That(world.TryRevivePlayer(player), Is.False, "死亡事实消费前不得重新开放玩家行动。");
                var facts = Drain(world);
                var deaths = facts.FindAll(fact => fact.Kind == CombatEventKind.Death);
                Assert.That(deaths.Count, Is.EqualTo(1));
                Assert.That(deaths[0].TargetActorId, Is.EqualTo(1));
                Assert.That(deaths[0].DefinitionId, Is.GreaterThan(previousDeathId));
                previousDeathId = deaths[0].DefinitionId;
                Assert.That(world.TryRevivePlayer(player), Is.True);
                Assert.That(world.TryGetActor(player, out var revived), Is.True);
                Assert.That(revived.Entity, Is.EqualTo(player));
                Assert.That(revived.ActorId, Is.EqualTo(dead.ActorId));
                Assert.That(revived.Position, Is.EqualTo(dead.Position));
                Assert.That(revived.Health, Is.EqualTo(revived.MaxHealth));
                Assert.That(revived.Dead, Is.False);
                world.Step();
                Assert.That(world.TryGetActor(player, out var idle), Is.True);
                Assert.That(idle.Position, Is.EqualTo(dead.Position), "旧移动输入不得在复活后继续执行。");
                Assert.That(Drain(world).Exists(fact => fact.Kind == CombatEventKind.Death || fact.Kind == CombatEventKind.RoomCleared), Is.False);
                Assert.That(world.SetMove(player, Vector2.right), Is.True);
                Assert.That(world.TryStartSkill(player, 3, enemy), Is.True);
                world.Step();
                Assert.That(world.TryGetActor(player, out var moved), Is.True);
                Assert.That(moved.Position.x, Is.GreaterThan(idle.Position.x));
                Assert.That(Drain(world).Exists(fact => fact.Kind == CombatEventKind.Damage && fact.ActorId == 1 && fact.TargetActorId == 2), Is.True);
            }
        }

        /// <summary>
        /// 原地复活清除导航、死亡与受击姿势以及已终止的施法、位移和 Buff，同时保留永久强化、词条和冷却。
        /// </summary>
        [Test]
        public void Revive_ClearsTransientActionsAndPreservesPermanentStateAndCooldown()
        {
            var world = CreateWorld(CreateCatalog(), out var player, out var enemy);
            Assert.That(world.TryAddAttributeBonus(player, CombatAttributeKind.MaxHealth, 50f), Is.True);
            Assert.That(world.TryAddAttributeBonus(player, CombatAttributeKind.Attack, 12f), Is.True);
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { new CombatAugmentDefinition
                { Id = "equipped", Trigger = CombatTriggerKind.None, SkillId = 1 } }), Is.True);
            Assert.That(world.ApplyBuff(player, 1, 1), Is.True);
            Assert.That(world.SetNavigationWaypoint(player, new Vector3(5f, 0f, 0f)), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetAnimationSnapshot(player, out var action), Is.True);
            Assert.That(action.CueId, Is.EqualTo(9));
            Assert.That(world.TryGetMovementStatus(player, out _, out bool displaced), Is.True);
            Assert.That(displaced, Is.True);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Drain(world);
            Assert.That(world.TryExportState(out var dead, out string error), Is.True, error);
            int cooldown = dead.Actors.Find(actor => actor.Spec.IsPlayer).SkillCooldowns.Find(skill => skill.SkillId == 1).RemainingTicks;
            Assert.That(dead.Casts.Count, Is.Zero);
            Assert.That(dead.Displacements.Count, Is.Zero);
            Assert.That(dead.Buffs.Count, Is.Zero);
            Assert.That(world.TryRevivePlayer(player), Is.True);
            Assert.That(world.TryGetAnimationSnapshot(player, out var idle), Is.True);
            Assert.That(idle.Dead, Is.False);
            Assert.That(idle.CueId, Is.Zero);
            Assert.That(idle.CueDirection, Is.EqualTo(Vector3.zero));
            Assert.That(idle.Velocity, Is.EqualTo(Vector3.zero));
            Assert.That(idle.HitElapsedTicks, Is.EqualTo(-1));
            Assert.That(idle.DeathElapsedTicks, Is.EqualTo(-1));
            Assert.That(world.TryGetMovementStatus(player, out float speed, out displaced), Is.True);
            Assert.That(speed, Is.EqualTo(6f));
            Assert.That(displaced, Is.False);
            Assert.That(world.TryExportState(out var revived, out error), Is.True, error);
            var state = revived.Actors.Find(actor => actor.Spec.IsPlayer);
            Assert.That(state.Key, Is.EqualTo(dead.PlayerKey));
            Assert.That(state.Health, Is.EqualTo(150f));
            Assert.That(state.Spec.Attack, Is.EqualTo(12f));
            Assert.That(state.PermanentBonuses, Is.EqualTo(dead.Actors.Find(actor => actor.Spec.IsPlayer).PermanentBonuses));
            Assert.That(state.AugmentGroups.Count, Is.EqualTo(1));
            Assert.That(state.SkillCooldowns.Find(skill => skill.SkillId == 1).RemainingTicks, Is.EqualTo(cooldown));
            Assert.That(state.LastInstigatorActorId, Is.Zero);
            Assert.That(state.CueClock, Is.EqualTo(-1));
            Assert.That(state.DeathPublished, Is.False);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.False, "复活保留原施法冷却。");
            world.Step();
            Assert.That(world.TryGetActor(player, out var after), Is.True);
            Assert.That(after.Position, Is.EqualTo(state.Position), "旧路点和位移不得继续执行。");
        }

        /// <summary>
        /// 死亡快照可恢复后再复活，复活快照再次恢复不会重放死亡或清空本局身份和增益。
        /// </summary>
        [Test]
        public void Revive_DeathAndRevivedSnapshotsRoundTripWithoutReplayingEvents()
        {
            var catalog = CreateCatalog();
            var world = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(world.TryAddAttributeBonus(player, CombatAttributeKind.MaxHealth, 25f), Is.True);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            var firstDeath = Drain(world).Find(fact => fact.Kind == CombatEventKind.Death);
            Assert.That(world.TryExportState(out var saved, out string error), Is.True, error);
            var restored = Restore(catalog, saved);
            Assert.That(restored.TryRevivePlayer(player), Is.False);
            Assert.That(restored.TryFindActor(1, out var restoredPlayer), Is.True);
            Assert.That(restored.TryRevivePlayer(restoredPlayer), Is.True);
            Assert.That(restored.TryExportState(out var revived, out error), Is.True, error);
            var roundTrip = Restore(catalog, revived);
            Assert.That(Drain(roundTrip), Is.Empty);
            Assert.That(roundTrip.TryFindActor(1, out var currentPlayer), Is.True);
            Assert.That(roundTrip.TryGetActor(currentPlayer, out var actor), Is.True);
            Assert.That(actor.Health, Is.EqualTo(125f));
            Assert.That(actor.Position, Is.EqualTo(saved.Actors.Find(value => value.Spec.IsPlayer).Position));
            Assert.That(roundTrip.TryRevivePlayer(currentPlayer), Is.False);
            Assert.That(roundTrip.TryFindActor(2, out var currentEnemy), Is.True);
            Assert.That(roundTrip.TryStartSkill(currentEnemy, 2, currentPlayer), Is.True);
            roundTrip.Step();
            var deaths = Drain(roundTrip).FindAll(fact => fact.Kind == CombatEventKind.Death);
            Assert.That(deaths.Count, Is.EqualTo(1));
            Assert.That(deaths[0].DefinitionId, Is.GreaterThan(firstDeath.DefinitionId));
            Assert.That(roundTrip.TryRevivePlayer(currentPlayer), Is.True);
        }

        /// <summary>
        /// 拒绝活玩家、敌人、外部引用、非活动地图和死亡尚未发布的快照状态。
        /// </summary>
        [Test]
        public void Revive_RejectsInvalidActorsAndIncompleteDeath()
        {
            var catalog = CreateCatalog();
            var world = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(world.TryRevivePlayer(default), Is.False);
            Assert.That(world.TryRevivePlayer(player), Is.False);
            Assert.That(world.TryRevivePlayer(enemy), Is.False);
            var other = CreateWorld(catalog, out var otherPlayer, out _);
            Assert.That(world.TryRevivePlayer(otherPlayer), Is.False);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Drain(world);
            Assert.That(world.TryExportState(out var dead, out string error), Is.True, error);
            dead.Actors.Find(actor => actor.Spec.IsPlayer).DeathPublished = false;
            var incomplete = Restore(catalog, dead);
            Assert.That(incomplete.TryFindActor(1, out var pendingPlayer), Is.True);
            Assert.That(incomplete.TryRevivePlayer(pendingPlayer), Is.False);
            Assert.That(other.SetActiveRoom(2, otherPlayer), Is.True);
            Assert.That(other.TryExportState(out var otherState, out error), Is.True, error);
            dead.Rooms.Add(otherState.Rooms.Find(room => room.RoomId == 2));
            dead.ActiveRoomId = 2;
            dead.Actors.Find(actor => actor.Spec.IsPlayer).DeathPublished = true;
            var inactive = Restore(catalog, dead);
            Assert.That(inactive.TryFindActor(1, out var inactivePlayer), Is.True);
            Assert.That(inactive.TryRevivePlayer(inactivePlayer), Is.False);
            Assert.That(other.SetActiveRoom(1, otherPlayer), Is.True);
            Assert.That(other.TryFindActor(2, out var otherEnemy), Is.True);
            Assert.That(other.TryStartSkill(otherPlayer, 2, otherEnemy), Is.True);
            other.Step();
            Drain(other);
            Assert.That(other.TryRevivePlayer(otherEnemy), Is.False, "敌人死亡保留原有回收和地图刷新流程。");
            Assert.That(other.TryRetireDeadActor(otherEnemy), Is.True);
        }

        /// <summary>
        /// 原点被存活角色或新障碍占用时拒绝复活，清除占用后仍可原地复活。
        /// </summary>
        [Test]
        public void Revive_RequiresLegalOriginalOccupancy()
        {
            var world = CreateWorld(CreateCatalog(), out var player, out var enemy);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Drain(world);
            Assert.That(world.TrySetPosition(enemy, Vector3.zero), Is.True);
            Assert.That(world.TryRevivePlayer(player), Is.False);
            Assert.That(world.TrySetPosition(enemy, new Vector3(0f, 0f, 2f)), Is.True);
            Assert.That(world.TryRevivePlayer(player), Is.True);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Drain(world);
            Assert.That(world.AddObstacle(1, new Rect(-0.1f, -0.1f, 0.2f, 0.2f)), Is.True);
            Assert.That(world.TryRevivePlayer(player), Is.False);
            Assert.That(world.TryGetActor(player, out var blocked), Is.True);
            Assert.That(blocked.Dead && blocked.Health == 0f, Is.True);
        }

        /// <summary>
        /// 创建并登记隔离战斗世界与双方角色。
        /// </summary>
        /// <param name="catalog">测试战斗配置。</param>
        /// <param name="player">创建的玩家引用。</param>
        /// <param name="enemy">创建的敌人引用。</param>
        /// <returns>包含测试双方角色的世界。</returns>
        private CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef player, out CombatEntityRef enemy)
        {
            var world = new CombatWorld(catalog);
            worlds.Add(world);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            player = world.CreateActor(new CombatActorSpec { ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true,
                Position = Vector3.zero, MaxHealth = 100f, MoveSpeed = 6f, Radius = 0.3f });
            enemy = world.CreateActor(new CombatActorSpec { ActorId = 2, RoomId = 1, Team = 2,
                Position = new Vector3(0f, 0f, 2f), MaxHealth = 1000f, Radius = 0.3f });
            return world;
        }

        /// <summary>
        /// 通过序列化往返恢复并登记隔离世界。
        /// </summary>
        /// <param name="catalog">与状态对应的测试配置。</param>
        /// <param name="snapshot">完整战斗状态。</param>
        /// <returns>恢复成功的新世界。</returns>
        private CombatWorld Restore(CombatCatalog catalog, CombatWorldSnapshot snapshot)
        {
            var decoded = JsonUtility.FromJson<CombatWorldSnapshot>(JsonUtility.ToJson(snapshot));
            Assert.That(CombatWorld.TryRestore(catalog, decoded, out var world, out string error), Is.True, error);
            worlds.Add(world);
            return world;
        }

        /// <summary>
        /// 消费全部战斗事实并交给测试检查。
        /// </summary>
        /// <param name="world">待消费事件的世界。</param>
        /// <returns>本次消费的战斗事实。</returns>
        private static List<CombatEvent> Drain(CombatWorld world)
        {
            var facts = new List<CombatEvent>();
            world.DrainEvents(facts);
            return facts;
        }

        /// <summary>
        /// 创建动作、必定致命伤害、普通攻击和临时移速增益的隔离配置。
        /// </summary>
        /// <returns>只用于复活机制验证的测试配置。</returns>
        private static CombatCatalog CreateCatalog()
        {
            var catalog = new CombatCatalog();
            var action = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 30, CooldownTicks = 80, StartRange = 10f };
            var cue = new CombatTimelineNode { Tick = 1 };
            cue.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 9, DurationTicks = 30 });
            cue.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Displace, DefinitionId = 8, Value = 3f, DurationTicks = 20 });
            action.Nodes.Add(cue);
            catalog.Skills.Add(1, action);
            for (int id = 2; id <= 3; id++)
            {
                var skill = new CombatSkillDefinition { CombatSkillId = id, DurationTicks = 1, StartRange = 10f };
                var node = new CombatTimelineNode { Tick = 1 };
                node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = id,
                    Value = id == 2 ? 2000f : 10f, Shape = CombatHitShape.Circle, Radius = 10f, Height = 2f,
                    CanDodge = false, CanCritical = false });
                skill.Nodes.Add(node);
                catalog.Skills.Add(id, skill);
            }
            catalog.Buffs.Add(1, new CombatBuffDefinition { BuffId = 1, DurationTicks = 120, IntervalTicks = 60,
                MaxStacks = 1, MoveSpeedAddPerStack = 2f });
            return catalog;
        }
    }
}
