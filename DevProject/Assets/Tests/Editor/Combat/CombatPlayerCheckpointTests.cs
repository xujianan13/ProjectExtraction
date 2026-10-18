using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 独立玩家检查点的属性、技能、来源隔离与跨世界恢复测试。
    /// </summary>
    public sealed class CombatPlayerCheckpointTests
    {
        private readonly List<CombatWorld> worlds = new();

        /// <summary>
        /// 释放测试创建的隔离战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (var world in worlds) world.Dispose();
            worlds.Clear();
        }

        /// <summary>
        /// 装备基础属性、永久强化与损伤跨世界保留，恢复后再次换装仍正确叠加强化。
        /// </summary>
        [Test]
        public void RoundTrip_PreservesEquipmentBonusesHealthAndSkills()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out _);
            Assert.That(source.TryReplaceBaseAttributes(player, new CombatActorSpec
            { MaxHealth = 200f, Attack = 30f, Defense = 5f, CriticalDamage = 1.8f, MoveSpeed = 4f, AttackRange = 2f }), Is.True);
            Assert.That(source.TryAddAttributeBonus(player, CombatAttributeKind.MaxHealth, 20f), Is.True);
            Assert.That(source.TryAddAttributeBonus(player, CombatAttributeKind.Attack, 5f), Is.True);
            Assert.That(source.TryReplaceAugments(player, "equipment", new[]
            { new CombatAugmentDefinition { Id = "range", AffectsBasicAttack = true, RangeMultiplier = 2f } }), Is.True);
            Assert.That(source.SetMove(player, Vector2.right), Is.True);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            Assert.That(captured.Spec.RoomId, Is.Zero);
            Assert.That(captured.Spec.Position, Is.EqualTo(Vector3.zero));
            Assert.That(captured.BaseSpec.MaxHealth, Is.EqualTo(200f));
            var decoded = JsonUtility.FromJson<CombatPlayerCheckpoint>(JsonUtility.ToJson(captured));
            var restored = CreateEmptyWorld(catalog);
            var position = new Vector3(0f, 0f, 4f);
            Assert.That(restored.TryRestorePlayerCheckpoint(decoded, 2, position, out var current, out error), Is.True, error);
            Assert.That(restored.TryGetActor(current, out var actor), Is.True);
            Assert.That(actor.Health, Is.EqualTo(120f));
            Assert.That(actor.MaxHealth, Is.EqualTo(220f));
            Assert.That(actor.Attack, Is.EqualTo(35f));
            Assert.That(actor.Position, Is.EqualTo(position));
            var skills = new List<CombatSkillSnapshot>();
            restored.GetSkillsSnapshot(current, skills);
            Assert.That(skills.ConvertAll(skill => skill.SkillId), Is.EqualTo(new[] { 1, 2, 3 }));
            restored.Step();
            Drain(restored);
            Assert.That(restored.TryGetActor(current, out actor), Is.True);
            Assert.That(actor.Position, Is.EqualTo(position), "恢复不得接续旧移动输入。");
            Assert.That(restored.TryReplaceBaseAttributes(current, new CombatActorSpec
            { MaxHealth = 250f, Attack = 40f, MoveSpeed = 6f, AttackRange = 3f }), Is.True);
            Assert.That(restored.TryGetActor(current, out actor), Is.True);
            Assert.That(actor.MaxHealth, Is.EqualTo(270f));
            Assert.That(actor.Attack, Is.EqualTo(45f));
            Assert.That(actor.Health, Is.EqualTo(120f));
            Assert.That(restored.TryCapturePlayerCheckpoint(current, out var later, out error), Is.True, error);
            Assert.That(later.AugmentGroups[0].GroupId, Is.EqualTo("equipment"));
        }

        /// <summary>
        /// 玩家检查点不携带敌人、进行中的施法、战斗位移或飞弹，技能剩余冷却仍然保留。
        /// </summary>
        [Test]
        public void RoundTrip_DropsTransientEntitiesAndPreservesCooldown()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out _);
            Assert.That(source.TryStartSkill(player, 3, player), Is.True);
            Assert.That(source.TrySpawnProjectile(player, 1, default, Vector3.forward * 8f, Vector3.forward), Is.True);
            source.Step();
            Drain(source);
            Assert.That(source.TryExportState(out var complete, out string error), Is.True, error);
            Assert.That(complete.Casts.Count, Is.EqualTo(1));
            Assert.That(complete.Displacements.Count, Is.EqualTo(1));
            Assert.That(complete.Projectiles.Count, Is.EqualTo(1));
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out error), Is.True, error);
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            Assert.That(restored.TryExportState(out var isolated, out error), Is.True, error);
            Assert.That(isolated.Actors.Count, Is.EqualTo(1));
            Assert.That(isolated.Casts, Is.Empty);
            Assert.That(isolated.Projectiles, Is.Empty);
            Assert.That(isolated.Displacements, Is.Empty);
            Assert.That(restored.TryFindActor(2, out _), Is.False);
            var skills = new List<CombatSkillSnapshot>();
            restored.GetSkillsSnapshot(current, skills);
            var skill = skills.Find(value => value.SkillId == 3);
            Assert.That(skill.CooldownTicks, Is.EqualTo(captured.SkillCooldowns.Find(value => value.SkillId == 3).RemainingTicks));
            Assert.That(skill.IsCasting, Is.False);
            restored.Step();
            Drain(restored);
            Assert.That(restored.TryGetActor(current, out var actor), Is.True);
            Assert.That(actor.Position, Is.EqualTo(Vector3.zero));
        }

        /// <summary>
        /// 自身 Buff 的层数、周期与已应用强化保留，外部及缺失来源 Buff 不被假冒为自身状态。
        /// </summary>
        [Test]
        public void Buffs_RestoreOnlySelfSourceWithoutReplayingOnApply()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out _);
            Assert.That(source.ApplyBuff(player, 1, 1), Is.True);
            Assert.That(source.ApplyBuff(player, 1, 1), Is.True);
            Assert.That(source.ApplyBuff(player, 2, 2), Is.True);
            Assert.That(source.ApplyBuff(player, 2, 999), Is.True);
            source.Step();
            Drain(source);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            Assert.That(captured.Buffs.Count, Is.EqualTo(1));
            Assert.That(captured.Buffs[0].Stacks, Is.EqualTo(2));
            captured.Health = 50f;
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            Assert.That(Drain(restored), Is.Empty);
            Assert.That(restored.TryGetActor(current, out var actor), Is.True);
            Assert.That(actor.Attack, Is.EqualTo(12f), "首次加攻击效果不能因恢复再次应用。");
            var buffs = new List<CombatBuffSnapshot>();
            restored.GetBuffsSnapshot(buffs);
            Assert.That(buffs.Count, Is.EqualTo(1));
            Assert.That(buffs[0].SourceActorId, Is.EqualTo(1));
            Assert.That(buffs[0].RemainingTicks, Is.EqualTo(captured.Buffs[0].RemainingTicks));
            Assert.That(restored.TryExportState(out var isolated, out error), Is.True, error);
            Assert.That(isolated.Actors[0].MoveBonus, Is.EqualTo(4f));
            for (int tick = 0; tick < captured.Buffs[0].NextTickIn; tick++)
            {
                restored.Step();
                var events = Drain(restored);
                Assert.That(events.Exists(value => value.Kind == CombatEventKind.Heal), Is.EqualTo(tick + 1 == captured.Buffs[0].NextTickIn));
            }
            Assert.That(restored.TryGetActor(current, out actor), Is.True);
            Assert.That(actor.Health, Is.EqualTo(52f));
        }

        /// <summary>
        /// 常驻技能的启用状态与自身控制 Buff 恢复，不执行首次施放或初次 Buff 效果。
        /// </summary>
        [Test]
        public void PersistentSkill_PreservesActivationAndController()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out _);
            Assert.That(source.TrySetSkillLoadout(player, 1, new[] { 1, 2, 3, 4 }), Is.True);
            Assert.That(source.TryStartSkill(player, 4, player), Is.True);
            source.Step();
            Drain(source);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            Assert.That(captured.EnabledPersistentSkills, Is.EqualTo(new[] { 4 }));
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            var skills = new List<CombatSkillSnapshot>();
            restored.GetSkillsSnapshot(current, skills);
            Assert.That(skills.Find(value => value.SkillId == 4).IsPersistentActive, Is.True);
            restored.Step();
            Assert.That(Drain(restored).Exists(value => value.Kind == CombatEventKind.BuffApplied), Is.False);
            var buffs = new List<CombatBuffSnapshot>();
            restored.GetBuffsSnapshot(buffs);
            Assert.That(buffs.Count, Is.EqualTo(1));
            Assert.That(buffs[0].Lifetime, Is.EqualTo(CombatBuffLifetime.Persistent));
        }

        /// <summary>
        /// 命中词条剩余冷却以相对帧保存，并从目的世界的当前时钟继续递减。
        /// </summary>
        [Test]
        public void AugmentCooldown_PreservesRemainingTicks()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(source.TryReplaceAugments(player, "rogue", new[]
            {
                new CombatAugmentDefinition { Id = "heal", Trigger = CombatTriggerKind.Hit, CooldownTicks = 8, TargetSelf = true,
                    Effects = new List<CombatEffectDefinition> { new() { Kind = CombatEffectKind.Heal, Value = 5f } } }
            }), Is.True);
            Assert.That(source.TryStartSkill(player, 1, enemy), Is.True);
            source.Step();
            Drain(source);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            Assert.That(captured.AugmentGroups[0].Cooldowns[0].RemainingTicks, Is.EqualTo(8));
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            Assert.That(restored.TryCapturePlayerCheckpoint(current, out var before, out error), Is.True, error);
            Assert.That(before.AugmentGroups[0].Cooldowns[0].RemainingTicks, Is.EqualTo(8));
            restored.Step();
            Drain(restored);
            Assert.That(restored.TryCapturePlayerCheckpoint(current, out var later, out error), Is.True, error);
            Assert.That(later.AugmentGroups[0].Cooldowns[0].RemainingTicks, Is.EqualTo(7));
        }

        /// <summary>
        /// 已消费死亡的玩家可恢复并显式复活，旧死亡不重放且下次死亡编号不会倒退。
        /// </summary>
        [Test]
        public void Death_RestoresPendingStateWithoutReplayingDeath()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(source.TryStartSkill(enemy, 2, player), Is.True);
            source.Step();
            var death = Drain(source).Find(value => value.Kind == CombatEventKind.Death && value.TargetActorId == 1);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            Assert.That(Drain(restored), Is.Empty);
            restored.Step();
            Assert.That(Drain(restored).Exists(value => value.Kind == CombatEventKind.Death), Is.False);
            Assert.That(restored.TryGetActor(current, out var dead), Is.True);
            Assert.That(dead.Dead && dead.Health == 0f, Is.True);
            Assert.That(restored.TryRevivePlayer(current), Is.True);
            var newEnemy = restored.CreateActor(new CombatActorSpec
            { ActorId = 2, RoomId = 2, Team = 2, Position = Vector3.forward * 2f, MaxHealth = 100f, Radius = 0.3f });
            Assert.That(restored.TryStartSkill(newEnemy, 2, current), Is.True);
            restored.Step();
            var nextDeath = Drain(restored).Find(value => value.Kind == CombatEventKind.Death && value.TargetActorId == 1);
            Assert.That(nextDeath.DefinitionId, Is.GreaterThan(death.DefinitionId));
        }

        /// <summary>
        /// 死亡玩家同步永久属性后仍保持零生命，不重复发布死亡，显式复活按新最大生命恢复。
        /// </summary>
        [Test]
        public void RefreshBaseAttributes_DeadPlayerStaysDeadUntilExplicitRevive()
        {
            var world = CreateWorld(CreateCatalog(), out var player, out var enemy);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Assert.That(Drain(world).FindAll(value => value.Kind == CombatEventKind.Death && value.TargetActorId == 1).Count, Is.EqualTo(1));
            Assert.That(world.TryRefreshPlayerBaseAttributes(player, new CombatActorSpec
            { MaxHealth = 240f, Attack = 35f, MoveSpeed = 4f, AttackRange = 10f }), Is.True);
            Assert.That(world.TryGetActor(player, out var actor), Is.True);
            Assert.That(actor.Dead, Is.True);
            Assert.That(actor.Health, Is.Zero);
            Assert.That(actor.MaxHealth, Is.EqualTo(240f));
            Assert.That(actor.Attack, Is.EqualTo(35f));
            Assert.That(Drain(world), Is.Empty);
            world.Step();
            Assert.That(Drain(world).Exists(value => value.Kind == CombatEventKind.Death), Is.False);
            Assert.That(world.TryCapturePlayerCheckpoint(player, out var checkpoint, out string error), Is.True, error);
            Assert.That(checkpoint.Health, Is.Zero);
            Assert.That(checkpoint.BaseSpec.MaxHealth, Is.EqualTo(240f));
            Assert.That(world.TryRevivePlayer(player), Is.True);
            Assert.That(world.TryGetActor(player, out actor), Is.True);
            Assert.That(actor.Dead, Is.False);
            Assert.That(actor.Health, Is.EqualTo(240f));
            Assert.That(actor.Attack, Is.EqualTo(35f));
            Assert.That(Drain(world), Is.Empty);
        }

        /// <summary>
        /// 存活玩家同步永久属性不自动回血，检查点基础配置保留玩家身份及原有技能入口。
        /// </summary>
        [Test]
        public void RefreshBaseAttributes_LivingPlayerPreservesHealthAndCheckpointIdentity()
        {
            var world = CreateWorld(CreateCatalog(), out var player, out var enemy);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
            world.Step();
            Drain(world);
            Assert.That(world.TryCapturePlayerCheckpoint(player, out var before, out string error), Is.True, error);
            Assert.That(before.Health, Is.EqualTo(80f));
            Assert.That(world.TryRefreshPlayerBaseAttributes(player, new CombatActorSpec
            { MaxHealth = 180f, Attack = 28f, MoveSpeed = 6f, AttackRange = 12f }), Is.True);
            Assert.That(world.TryGetActor(player, out var actor), Is.True);
            Assert.That(actor.Dead, Is.False);
            Assert.That(actor.Health, Is.EqualTo(before.Health));
            Assert.That(actor.MaxHealth, Is.EqualTo(180f));
            Assert.That(actor.Attack, Is.EqualTo(28f));
            Assert.That(world.TryCapturePlayerCheckpoint(player, out var after, out error), Is.True, error);
            Assert.That(after.BaseSpec.ActorId, Is.EqualTo(before.BaseSpec.ActorId));
            Assert.That(after.BaseSpec.Team, Is.EqualTo(before.BaseSpec.Team));
            Assert.That(after.BaseSpec.IsPlayer, Is.EqualTo(before.BaseSpec.IsPlayer));
            Assert.That(after.BaseSpec.BasicSkillId, Is.EqualTo(before.BaseSpec.BasicSkillId));
            Assert.That(after.BaseSpec.SkillIds, Is.EqualTo(before.BaseSpec.SkillIds));
            Assert.That(after.BaseSpec.MaxHealth, Is.EqualTo(180f));
            Assert.That(after.BaseSpec.Attack, Is.EqualTo(28f));
            Assert.That(Drain(world), Is.Empty);
        }

        /// <summary>
        /// 捕获和恢复均复制数组、词条定义及效果集合，修改检查点不能影响任一运行世界。
        /// </summary>
        [Test]
        public void Copies_IsolateSourceCheckpointAndRestoredWorld()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out _);
            Assert.That(source.TryReplaceAugments(player, "equipment", new[]
            {
                new CombatAugmentDefinition { Id = "heal", Trigger = CombatTriggerKind.Hit, TargetSelf = true,
                    Effects = new List<CombatEffectDefinition> { new() { Kind = CombatEffectKind.Heal, Value = 5f } } }
            }), Is.True);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            captured.Spec.SkillIds[0] = 999;
            captured.PermanentBonuses[1] = 200f;
            captured.AugmentGroups[0].Definitions[0].Effects[0].Value = 90f;
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var original, out error), Is.True, error);
            Assert.That(restored.TryCapturePlayerCheckpoint(current, out var copy, out error), Is.True, error);
            foreach (var checkpoint in new[] { original, copy })
            {
                Assert.That(checkpoint.Spec.SkillIds[0], Is.EqualTo(1));
                Assert.That(checkpoint.PermanentBonuses[1], Is.Zero);
                Assert.That(checkpoint.AugmentGroups[0].Definitions[0].Effects[0].Value, Is.EqualTo(5f));
            }
        }

        /// <summary>
        /// 外部检查点非法时原子拒绝，目的世界不残留玩家且仍可恢复合法检查点。
        /// </summary>
        /// <param name="invalidPart">需要破坏的检查点字段。</param>
        [TestCase("health")]
        [TestCase("buff")]
        [TestCase("cooldown")]
        [TestCase("persistent")]
        public void Restore_RejectsInvalidDataWithoutCreatingPlayer(string invalidPart)
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out _);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var valid, out string error), Is.True, error);
            var invalid = JsonUtility.FromJson<CombatPlayerCheckpoint>(JsonUtility.ToJson(valid));
            if (invalidPart == "health") invalid.Health = float.NaN;
            if (invalidPart == "buff") invalid.Buffs.Add(new CombatPlayerBuffCheckpoint { BuffId = 999 });
            if (invalidPart == "cooldown") invalid.SkillCooldowns.Add(new CombatSkillCooldownState { SkillId = 1, RemainingTicks = -1 });
            if (invalidPart == "persistent") invalid.EnabledPersistentSkills.Add(1);
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(invalid, 2, Vector3.zero, out var rejected, out error), Is.False);
            Assert.That(rejected.Id, Is.Zero);
            Assert.That(error, Is.Not.Empty);
            Assert.That(restored.TryFindActor(1, out _), Is.False);
            Assert.That(Drain(restored), Is.Empty);
            Assert.That(restored.TryRestorePlayerCheckpoint(valid, 2, Vector3.zero, out _, out error), Is.True, error);
        }

        /// <summary>
        /// 未消费效果或事件、错误玩家及重复恢复均拒绝，合法边界不会受之前拒绝影响。
        /// </summary>
        [Test]
        public void Boundary_RejectsPendingWorkWrongActorAndDuplicatePlayer()
        {
            var catalog = CreateCatalog();
            var source = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(source.TryCapturePlayerCheckpoint(enemy, out _, out _), Is.False);
            Assert.That(source.ApplyBuff(player, 1, 1), Is.True);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out _, out _), Is.False);
            Drain(source);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out _, out _), Is.False);
            source.Step();
            Drain(source);
            Assert.That(source.TryCapturePlayerCheckpoint(player, out var captured, out string error), Is.True, error);
            var restored = CreateEmptyWorld(catalog);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.zero, out var current, out error), Is.True, error);
            Assert.That(restored.TryRestorePlayerCheckpoint(captured, 2, Vector3.forward, out _, out _), Is.False);
            Assert.That(restored.TryGetActor(current, out _), Is.True);
        }

        /// <summary>
        /// 创建只含配置与两张地图几何的隔离世界。
        /// </summary>
        /// <param name="catalog">独立测试配置。</param>
        /// <returns>尚未创建角色的战斗世界。</returns>
        private CombatWorld CreateEmptyWorld(CombatCatalog catalog)
        {
            var world = new CombatWorld(catalog);
            worlds.Add(world);
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            world.SetRoomBounds(2, new Rect(-20f, -20f, 40f, 40f));
            return world;
        }

        /// <summary>
        /// 创建玩家和静止敌人，所有测试技能都由测试显式触发。
        /// </summary>
        /// <param name="catalog">独立测试配置。</param>
        /// <param name="player">输出玩家引用。</param>
        /// <param name="enemy">输出敌人引用。</param>
        /// <returns>准备好的隔离战斗世界。</returns>
        private CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef player, out CombatEntityRef enemy)
        {
            var world = CreateEmptyWorld(catalog);
            player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, MaxHealth = 100f, Attack = 10f,
                MoveSpeed = 4f, Radius = 0.3f, AttackRange = 10f, BasicSkillId = 1, SkillIds = new[] { 1, 2, 3 }
            });
            enemy = world.CreateActor(new CombatActorSpec
            { ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.right * 2f, MaxHealth = 100f, Radius = 0.3f });
            Assert.That(player.Id, Is.Not.Zero);
            Assert.That(enemy.Id, Is.Not.Zero);
            return world;
        }

        /// <summary>
        /// 消费全部战斗事实，建立可捕获的完整事件边界。
        /// </summary>
        /// <param name="world">待消费事件的世界。</param>
        /// <returns>本次消费的独立事件集合。</returns>
        private static List<CombatEvent> Drain(CombatWorld world)
        {
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            return events;
        }

        /// <summary>
        /// 创建仅供测试的伤害、长施法、常驻技能和自身回复状态定义。
        /// </summary>
        /// <returns>不依赖正式配置表的测试战斗目录。</returns>
        private static CombatCatalog CreateCatalog()
        {
            var catalog = new CombatCatalog { InitialAttackRange = 10f };
            for (int id = 1; id <= 4; id++)
                catalog.Skills.Add(id, new CombatSkillDefinition
                {
                    CombatSkillId = id, CastMode = CombatSkillCastMode.Manual, DurationTicks = 1, CooldownTicks = 1, StartRange = 10f,
                    SkillType = id == 1 ? CombatSkillType.BasicAttack : CombatSkillType.Normal
                });
            for (int id = 1; id <= 2; id++)
            {
                var node = new CombatTimelineNode();
                node.Effects.Add(new CombatEffectDefinition
                { Kind = CombatEffectKind.Damage, Value = id == 1 ? 20f : 200f, Radius = 10f, CanCritical = false, CanDodge = false });
                catalog.Skills[id].Nodes.Add(node);
            }
            var displacement = new CombatTimelineNode();
            displacement.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Displace, Value = 2f, DurationTicks = 10 });
            catalog.Skills[3].Nodes.Add(displacement);
            catalog.Skills[3].TargetKind = CombatSkillTargetKind.Self;
            catalog.Skills[3].DurationTicks = 20;
            catalog.Skills[3].CooldownTicks = 40;
            catalog.Skills[4].CastMode = CombatSkillCastMode.Persistent;
            catalog.Skills[4].TargetKind = CombatSkillTargetKind.Self;
            catalog.Skills[4].MaintainBuffId = 3;
            catalog.Skills[4].OccupiesAction = false;
            catalog.Buffs.Add(1, new CombatBuffDefinition
            { BuffId = 1, DurationTicks = 20, IntervalTicks = 3, MaxStacks = 2, MoveSpeedAddPerStack = 2f, AttackRangeAddPerStack = 1f });
            catalog.Buffs[1].OnApply.Add(new CombatEffectDefinition
            { Kind = CombatEffectKind.ModifyAttribute, Attribute = CombatAttributeKind.Attack, Value = 2f });
            catalog.Buffs[1].OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Heal, Value = 1f });
            catalog.Buffs.Add(2, new CombatBuffDefinition
            { BuffId = 2, DurationTicks = 30, IntervalTicks = 5, MoveSpeedAddPerStack = -1f });
            catalog.Buffs.Add(3, new CombatBuffDefinition
            { BuffId = 3, Lifetime = CombatBuffLifetime.Persistent, IntervalTicks = 10 });
            catalog.Projectiles.Add(1, new CombatProjectileDefinition
            { ProjectileId = 1, Speed = 1f, FlightTicks = 120, LifetimeTicks = 120, MaxDistance = 20f });
            return catalog;
        }
    }
}
