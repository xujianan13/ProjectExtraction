using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 首轮技能列表调度、前向武器判定和辅助攻击的真实战斗保障。
    /// </summary>
    public sealed class CombatSkillLoadoutTests
    {
        private CombatWorld world;

        /// <summary>
        /// 释放本次测试创建的隔离世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            world?.Dispose();
            world = null;
        }

        /// <summary>
        /// 零时长动作每帧最多启动一个，游标轮转且不占动作技能同帧执行。
        /// </summary>
        [Test]
        public void Polling_ZeroDurationActionsRotateWhileAuxiliaryRunsInParallel()
        {
            var catalog = new CombatCatalog();
            catalog.Skills.Add(1, CueSkill(1, true));
            catalog.Skills.Add(2, CueSkill(2, true));
            catalog.Skills.Add(3, CueSkill(3, false));
            world = CreateWorld(catalog, out var player);
            Assert.That(world.TrySetSkillLoadout(player, 1, new[] { 1, 2, 3 }), Is.True);
            for (int tick = 0; tick < 6; tick++)
            {
                world.Step();
                int[] cues = DrainCues(world);
                Assert.That(cues.Where(id => id != 3), Is.EqualTo(new[] { tick % 2 == 0 ? 1 : 2 }));
                Assert.That(cues.Count(id => id == 3), Is.EqualTo(1));
            }
        }

        /// <summary>
        /// 首个零帧状态立即影响本轮后续准入，缺少敌人不会阻塞自身技能。
        /// </summary>
        [Test]
        public void Polling_ZeroTickBuffChangesLaterAdmissionAndMissingEnemyDoesNotBlockSelf()
        {
            var catalog = new CombatCatalog();
            var enemySkill = CueSkill(1, true);
            enemySkill.TargetKind = CombatSkillTargetKind.Enemy;
            enemySkill.StartRange = 3f;
            var apply = CueSkill(2, false);
            apply.Nodes[0].Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.ApplyBuff, LinkedId = 7 });
            var guarded = CueSkill(3, false);
            guarded.RequiredMissingBuffId = 7;
            catalog.Skills.Add(1, enemySkill);
            catalog.Skills.Add(2, apply);
            catalog.Skills.Add(3, guarded);
            catalog.Buffs.Add(7, new CombatBuffDefinition { BuffId = 7, DurationTicks = 20, IntervalTicks = 20 });
            world = CreateWorld(catalog, out var player);
            Assert.That(world.TrySetSkillLoadout(player, 1, new[] { 1, 2, 3 }), Is.True);
            world.Step();
            Assert.That(DrainCues(world), Is.EqualTo(new[] { 2 }));
        }

        /// <summary>
        /// 无敌人时低血量复苏仍施放，已有恢复状态期间不覆盖实例或重置周期。
        /// </summary>
        [Test]
        public void ConditionalSelfBuff_HealsWithoutEnemyAndKeepsExistingInstance()
        {
            var catalog = new CombatCatalog();
            var recovery = CueSkill(3, false);
            recovery.CastMode = CombatSkillCastMode.Conditional;
            recovery.MaxHealthRatio = 0.5f;
            recovery.RequiredMissingBuffId = 7;
            recovery.Nodes[0].Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.ApplyBuff, LinkedId = 7 });
            var buff = new CombatBuffDefinition { BuffId = 7, DurationTicks = 8, IntervalTicks = 2 };
            buff.OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Heal, DefinitionId = 70, Value = 5f });
            catalog.Skills.Add(3, recovery);
            catalog.Buffs.Add(7, buff);
            AddInstantDamage(catalog, 9, 60f);
            world = CreateWorld(catalog, out var player);
            Assert.That(world.TrySetSkillLoadout(player, 0, new[] { 3 }), Is.True);
            world.Step();
            Assert.That(DrainCues(world), Is.Empty);
            var enemy = CreateEnemy(world, 2, Vector3.forward * 2f);
            Assert.That(world.TrySpawnProjectile(enemy, 9, player, Vector3.zero, Vector3.back), Is.True);
            Assert.That(world.TrySetPosition(enemy, Vector3.forward * 19f), Is.True);
            world.Step();
            Assert.That(DrainCues(world), Is.EqualTo(new[] { 3 }));
            var buffs = new List<CombatBuffSnapshot>();
            world.GetBuffsSnapshot(buffs);
            Assert.That(buffs.Count, Is.EqualTo(1));
            long instance = buffs[0].InstanceId;
            for (int tick = 0; tick < 4; tick++) world.Step();
            world.GetBuffsSnapshot(buffs);
            Assert.That(buffs.Count, Is.EqualTo(1));
            Assert.That(buffs[0].InstanceId, Is.EqualTo(instance));
            Assert.That(world.TryGetActor(player, out var healed), Is.True);
            Assert.That(healed.Health, Is.EqualTo(50f));
            Assert.That(DrainCues(world), Is.Empty);
        }

        /// <summary>
        /// 前向长矩形只命中前方直线目标，不能覆盖背后、侧面或穿墙。
        /// </summary>
        /// <param name="wall">是否在前方目标之前放置墙体。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void ForwardBox_HitsMultipleForwardTargetsAndRespectsWidthOriginAndWall(bool wall)
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 1, StartRange = 6f };
            var node = new CombatTimelineNode { Tick = 0 };
            node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 10, Value = 10f,
                Shape = CombatHitShape.ForwardBox, Radius = 5f, Width = 1f, RequireLineOfSight = true,
                CanCritical = false, CanDodge = false });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            world = CreateWorld(catalog, out var player);
            var near = CreateEnemy(world, 2, Vector3.forward);
            var far = CreateEnemy(world, 3, Vector3.forward * 4f);
            var behind = CreateEnemy(world, 4, Vector3.back);
            var side = CreateEnemy(world, 5, new Vector3(1.5f, 0f, 3f));
            if (wall) world.AddObstacle(1, new Rect(-0.6f, 2f, 1.2f, 0.2f));
            Assert.That(world.TryStartSkill(player, 1, far), Is.True);
            world.Step();
            AssertHealth(near, 90f);
            AssertHealth(far, wall ? 100f : 90f);
            AssertHealth(behind, 100f);
            AssertHealth(side, 100f);
        }

        /// <summary>
        /// 跟踪目标死亡后重新选敌，已发射飞弹仍使用生成时的进攻属性。
        /// </summary>
        [Test]
        public void HomingProjectile_ReacquiresAfterTargetDiesAndKeepsOffenseSnapshot()
        {
            var catalog = new CombatCatalog();
            var projectile = new CombatProjectileDefinition { ProjectileId = 8, Motion = ProjectileMotionKind.Straight,
                Target = ProjectileTargetKind.EntityLock, LostTarget = ProjectileLostTarget.Reacquire, ReacquireRange = 8f,
                Speed = 60f, MaxSpeed = 60f, TurnDegreesPerSecond = 36000f, Radius = 0.1f, LifetimeTicks = 30 };
            projectile.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 80,
                AttackCoefficient = 1f, CanCritical = false, CanDodge = false });
            catalog.Projectiles.Add(8, projectile);
            world = CreateWorld(catalog, out var player);
            var first = CreateEnemy(world, 2, Vector3.forward * 6f);
            var next = CreateEnemy(world, 3, Vector3.forward * 3f);
            Assert.That(world.TrySpawnProjectile(player, 8, first, Vector3.forward * 6f, Vector3.forward), Is.True);
            Assert.That(world.TryAddAttributeBonus(player, CombatAttributeKind.Attack, 90f), Is.True);
            Assert.That(world.TrySetPosition(first, Vector3.right * 9f), Is.True);
            AddInstantDamage(catalog, 9, 200f);
            Assert.That(world.TrySpawnProjectile(player, 9, first, Vector3.right * 9f, Vector3.right), Is.True);
            for (int tick = 0; tick < 8; tick++) world.Step();
            AssertHealth(next, 90f);
            Assert.That(world.IsFaulted, Is.False);
        }

        /// <summary>
        /// 普攻入口替换保留普通技能冷却，调用方修改原列表不改变已安装技能。
        /// </summary>
        [Test]
        public void ReplaceBasicAttack_PreservesNormalCooldownAndCopiesLoadout()
        {
            var catalog = new CombatCatalog();
            catalog.Skills.Add(1, CueSkill(1, true));
            catalog.Skills.Add(2, CueSkill(2, true));
            var normal = CueSkill(3, false);
            normal.CooldownTicks = 10;
            catalog.Skills.Add(3, normal);
            world = CreateWorld(catalog, out var player);
            int[] ids = { 1, 3 };
            Assert.That(world.TrySetSkillLoadout(player, 1, ids), Is.True);
            ids[1] = 2;
            world.Step();
            Assert.That(DrainCues(world), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(world.TrySetSkillLoadout(player, 2, new[] { 2, 3 }), Is.True);
            for (int tick = 0; tick < 9; tick++)
            {
                world.Step();
                Assert.That(DrainCues(world), Is.EqualTo(new[] { 2 }));
            }
            world.Step();
            Assert.That(DrainCues(world), Is.EquivalentTo(new[] { 2, 3 }));
        }

        /// <summary>
        /// 常驻控制器只生成一组轨道，快照恢复保留命中间隔且不重放首次效果。
        /// </summary>
        [Test]
        public void PersistentOrbit_RoundTripKeepsUniqueControllerAndHitInterval()
        {
            CombatCatalog catalog = OrbitCatalog();
            world = CreateWorld(catalog, out var player);
            var enemy = CreateEnemy(world, 2, Vector3.forward);
            Assert.That(world.TrySetSkillLoadout(player, 0, new[] { 4 }), Is.True);
            world.Step();
            world.Step();
            AssertHealth(enemy, 90f);
            world.Step();
            DrainCues(world);
            Assert.That(world.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Buffs.Count, Is.EqualTo(1));
            Assert.That(saved.Projectiles.Count, Is.EqualTo(2));
            var decoded = JsonUtility.FromJson<CombatWorldSnapshot>(JsonUtility.ToJson(saved));
            Assert.That(CombatWorld.TryRestore(catalog, decoded, out var restored, out error), Is.True, error);
            Assert.That(restored.TryFindActor(2, out var restoredEnemy), Is.True);
            Assert.That(restored.DrainEvents(new List<CombatEvent>()), Is.Zero);
            for (int tick = 0; tick < 8; tick++)
            {
                world.Step();
                restored.Step();
                Assert.That(world.TryGetActor(enemy, out var expected), Is.True);
                Assert.That(restored.TryGetActor(restoredEnemy, out var actual), Is.True);
                Assert.That(actual.Health, Is.EqualTo(expected.Health));
            }
            AssertHealth(enemy, 60f);
            var projectiles = new List<CombatProjectileSnapshot>();
            var buffs = new List<CombatBuffSnapshot>();
            world.GetProjectilesSnapshot(projectiles);
            world.GetBuffsSnapshot(buffs);
            Assert.That(projectiles.Count, Is.EqualTo(2));
            Assert.That(buffs.Count, Is.EqualTo(1));
            Assert.That(world.TrySetSkillLoadout(player, 0, new int[0]), Is.True);
            world.GetProjectilesSnapshot(projectiles);
            world.GetBuffsSnapshot(buffs);
            Assert.That(projectiles, Is.Empty);
            Assert.That(buffs, Is.Empty);
            restored.Dispose();
        }

        /// <summary>
        /// 死亡复活及切区维持原辅助技能冷却，不重新消耗冷却或补发首次伤害。
        /// </summary>
        [Test]
        public void PersistentOrbit_RevivalAndTransferPreserveCooldownAndRemainingHitInterval()
        {
            CombatCatalog catalog = OrbitCatalog();
            AddInstantDamage(catalog, 9, 200f);
            world = CreateWorld(catalog, out var player);
            var enemy = CreateEnemy(world, 2, Vector3.forward);
            Assert.That(world.TrySetSkillLoadout(player, 0, new[] { 4 }), Is.True);
            world.Step();
            world.Step();
            AssertHealth(enemy, 90f);
            Assert.That(world.TrySpawnProjectile(enemy, 9, player, Vector3.zero, Vector3.back), Is.True);
            world.Step();
            var projectiles = new List<CombatProjectileSnapshot>();
            world.GetProjectilesSnapshot(projectiles);
            Assert.That(projectiles, Is.Empty);
            var skills = new List<CombatSkillSnapshot>();
            world.GetSkillsSnapshot(player, skills);
            int remaining = skills.Single().CooldownTicks;
            DrainCues(world);
            Assert.That(world.TryRevivePlayer(player), Is.True);
            world.Step();
            AssertHealth(enemy, 90f);
            world.GetSkillsSnapshot(player, skills);
            Assert.That(skills.Single().IsPersistentActive, Is.True);
            Assert.That(skills.Single().CooldownTicks, Is.EqualTo(remaining - 1));
            world.GetProjectilesSnapshot(projectiles);
            Assert.That(projectiles.Count, Is.EqualTo(2));
            Assert.That(world.TryTransferActor(player, 2, Vector3.zero), Is.True);
            world.Step();
            world.GetSkillsSnapshot(player, skills);
            Assert.That(skills.Single().CooldownTicks, Is.EqualTo(remaining - 2));
            world.GetProjectilesSnapshot(projectiles);
            Assert.That(projectiles.Count, Is.EqualTo(2));
            Assert.That(projectiles.All(item => item.RoomId == 2), Is.True);
            AssertHealth(enemy, 90f);
        }

        /// <summary>
        /// 普攻词条跟随当前三类武器入口，同时保留普通技能自己的伤害规则。
        /// </summary>
        /// <param name="basicSkillId">刀剑、斧锤或长柄的普攻技能编号。</param>
        [TestCase(1)]
        [TestCase(10)]
        [TestCase(11)]
        public void BasicAttackModifier_FollowsWeaponEntryWithoutAffectingNormalSkill(int basicSkillId)
        {
            var catalog = new CombatCatalog();
            foreach (int id in new[] { basicSkillId, 90 })
            {
                var skill = new CombatSkillDefinition { CombatSkillId = id, DurationTicks = 1, StartRange = 6f,
                    CastMode = CombatSkillCastMode.Manual, SkillType = id == basicSkillId ? CombatSkillType.BasicAttack : CombatSkillType.Normal };
                var node = new CombatTimelineNode { Tick = 0 };
                node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = id,
                    AttackCoefficient = 1f, Radius = 6f, CanCritical = false, CanDodge = false });
                skill.Nodes.Add(node);
                catalog.Skills.Add(id, skill);
            }
            world = CreateWorld(catalog, out var player);
            var enemy = CreateEnemy(world, 2, Vector3.forward);
            Assert.That(world.TrySetSkillLoadout(player, basicSkillId, new[] { basicSkillId, 90 }), Is.True);
            Assert.That(world.TryReplaceAugments(player, "weapon", new[] { new CombatAugmentDefinition
                { Id = "basic-damage", AffectsBasicAttack = true, DamageMultiplier = 2f } }), Is.True);
            Assert.That(world.TryStartSkill(player, basicSkillId, enemy), Is.True);
            world.Step();
            AssertHealth(enemy, 80f);
            Assert.That(world.TryStartSkill(player, 90, enemy), Is.True);
            world.Step();
            AssertHealth(enemy, 70f);
        }

        /// <summary>
        /// 创建两把静止轨道剑与唯一常驻控制器，隔离运动和命中节奏。
        /// </summary>
        /// <returns>可以用于快照及生命周期测试的目录。</returns>
        private static CombatCatalog OrbitCatalog()
        {
            var catalog = new CombatCatalog();
            var skill = CueSkill(4, false);
            skill.CastMode = CombatSkillCastMode.Persistent;
            skill.CooldownTicks = 30;
            skill.MaintainBuffId = 7;
            skill.Nodes[0].Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.ApplyBuff, LinkedId = 7 });
            var buff = new CombatBuffDefinition { BuffId = 7, Lifetime = CombatBuffLifetime.Persistent, IntervalTicks = 5 };
            buff.OnApply.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 8,
                Count = 2, SpreadDegrees = 360f });
            var projectile = new CombatProjectileDefinition { ProjectileId = 8, Motion = ProjectileMotionKind.Orbit,
                OrbitRadius = 1f, OrbitDegreesPerSecond = 0f, Radius = 0.15f, SameTargetIntervalTicks = 3,
                MaxHitsPerTarget = 100, MaxDistinctTargets = 100, LifetimeTicks = 0, MaxDistance = 0f };
            projectile.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 80,
                AttackCoefficient = 1f, CanCritical = false, CanDodge = false });
            catalog.Skills.Add(4, skill);
            catalog.Buffs.Add(7, buff);
            catalog.Projectiles.Add(8, projectile);
            return catalog;
        }

        /// <summary>
        /// 创建带零帧事件的自身技能，隔离表现并观察真实接纳顺序。
        /// </summary>
        /// <param name="id">技能和事件编号。</param>
        /// <param name="action">是否占角色动作。</param>
        /// <returns>可直接加入测试目录的技能。</returns>
        private static CombatSkillDefinition CueSkill(int id, bool action)
        {
            var skill = new CombatSkillDefinition { CombatSkillId = id, DurationTicks = 0, OccupiesAction = action,
                TargetKind = CombatSkillTargetKind.Self, EffectClass = action ? CombatSkillEffectClass.Active : CombatSkillEffectClass.Auxiliary,
                SkillType = CombatSkillType.Normal };
            var node = new CombatTimelineNode { Tick = 0 };
            node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = id });
            skill.Nodes.Add(node);
            return skill;
        }

        /// <summary>
        /// 创建只有玩家的隔离战斗世界。
        /// </summary>
        /// <param name="catalog">本次测试目录。</param>
        /// <param name="player">创建后的玩家引用。</param>
        /// <returns>由测试结束时释放的战斗世界。</returns>
        private static CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef player)
        {
            var result = new CombatWorld(catalog, 17);
            result.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            result.SetRoomBounds(2, new Rect(-20f, -20f, 40f, 40f));
            player = result.CreateActor(new CombatActorSpec { ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true,
                Position = Vector3.zero, MaxHealth = 100f, Attack = 10f, Radius = 0.1f, AttackRange = 6f });
            Assert.That(result.SetActiveRoom(1, player), Is.True);
            return result;
        }

        /// <summary>
        /// 创建不会移动或自动施法的敌人。
        /// </summary>
        /// <param name="world">所属战斗世界。</param>
        /// <param name="id">业务角色编号。</param>
        /// <param name="position">初始位置。</param>
        /// <returns>创建后的敌人引用。</returns>
        private static CombatEntityRef CreateEnemy(CombatWorld world, int id, Vector3 position) => world.CreateActor(new CombatActorSpec
        {
            ActorId = id, RoomId = 1, Team = 2, Position = position, MaxHealth = 100f, Radius = 0.1f
        });

        /// <summary>
        /// 添加只对目标本身结算的瞬时测试伤害飞弹。
        /// </summary>
        /// <param name="catalog">允许修改的测试目录。</param>
        /// <param name="id">飞弹编号。</param>
        /// <param name="damage">固定伤害。</param>
        private static void AddInstantDamage(CombatCatalog catalog, int id, float damage)
        {
            var projectile = new CombatProjectileDefinition { ProjectileId = id, Motion = ProjectileMotionKind.Instant,
                SpawnAnchor = ProjectileSpawnAnchor.TargetEntityAtSpawn, Radius = 0.2f };
            projectile.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = id * 10,
                Value = damage, CanCritical = false, CanDodge = false });
            catalog.Projectiles.Add(id, projectile);
        }

        /// <summary>
        /// 读取并消费本帧技能事件，保留顺序。
        /// </summary>
        /// <param name="world">事件来源世界。</param>
        /// <returns>本次产生的技能事件编号。</returns>
        private static int[] DrainCues(CombatWorld world)
        {
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            return events.Where(item => item.Kind == CombatEventKind.Cue).Select(item => item.DefinitionId).ToArray();
        }

        /// <summary>
        /// 检查角色真实生命值。
        /// </summary>
        /// <param name="actor">待检查角色。</param>
        /// <param name="health">期望生命值。</param>
        private void AssertHealth(CombatEntityRef actor, float health)
        {
            Assert.That(world.TryGetActor(actor, out var state), Is.True);
            Assert.That(state.Health, Is.EqualTo(health));
        }
    }
}
