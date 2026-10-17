using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 完整战斗持久化及死亡生命引用隔离的定向测试。
    /// </summary>
    public sealed class CombatPersistenceTests
    {
        /// <summary>
        /// 软弱点登记实际伤害编号，允许多个配置行共用同一伤害定义。
        /// </summary>
        [Test]
        public void DamageVulnerabilityCatalog_UsesDefinitionIdInsteadOfTableRowId()
        {
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(2f);
            var expected = new HashSet<int>();
            bool distinctId = false;
            foreach (CfgCombatEffectsRow row in GameTables.CfgCombatEffects.GetAll())
            {
                if (row.Kind != (int)CombatEffectKind.Damage) continue;
                expected.Add(row.DefinitionID);
                if (row.ID != row.DefinitionID) distinctId = true;
            }
            Assert.That(distinctId, Is.True, "真实表必须覆盖配置行编号与伤害编号不同的情况。");
            Assert.That(catalog.DamageEffectIds, Is.EquivalentTo(expected));
            Assert.That(catalog.DamageEffectIds.Contains(5) && catalog.DamageEffectIds.Contains(1002) && catalog.DamageEffectIds.Contains(1004), Is.True);
        }

        /// <summary>
        /// 不同伤害配置行可共用同一实际定义，登记集合按实际定义去重。
        /// </summary>
        [Test]
        public void DamageVulnerabilityCatalog_AllowsSharedDamageDefinition()
        {
            CfgCombatEffectsRow row = GameTables.CfgCombatEffects.GetById(5);
            int previous = row.DefinitionID;
            int originalCount = CombatContentConfig.CreateCatalog(2f).DamageEffectIds.Count;
            try
            {
                row.DefinitionID = GameTables.CfgCombatEffects.GetById(10).DefinitionID;
                CombatCatalog shared = CombatContentConfig.CreateCatalog(2f);
                Assert.That(shared.DamageEffectIds.Contains(row.DefinitionID), Is.True);
                Assert.That(shared.DamageEffectIds.Count, Is.EqualTo(originalCount - 1));
            }
            finally { row.DefinitionID = previous; }
        }

        /// <summary>
        /// 中途施法、Buff、位移和飞弹从相同游标继续，后续随机及实际事实保持一致。
        /// </summary>
        [Test]
        public void RoundTrip_ContinuesCastBuffProjectileAndRandom()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(original.TryStartSkill(player, 1, enemy), Is.True);
            AdvanceAndDrain(original, 8);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Casts.Count, Is.EqualTo(1));
            Assert.That(saved.Buffs.Count, Is.EqualTo(1));
            Assert.That(saved.Projectiles.Count, Is.EqualTo(1));
            Assert.That(saved.Displacements.Count, Is.EqualTo(1));
            var decoded = JsonUtility.FromJson<CombatWorldSnapshot>(JsonUtility.ToJson(saved));
            decoded.Actors.Reverse();
            Assert.That(CombatWorld.TryRestore(catalog, decoded, out var restored, out error), Is.True, error);
            Assert.That(restored.TryGetActor(player, out _), Is.False);
            AssertEquivalent(original, restored);
            var leftEvents = new List<CombatEvent>();
            var rightEvents = new List<CombatEvent>();
            for (int tick = 0; tick < 110; tick++)
            {
                original.Step();
                restored.Step();
                leftEvents.Clear();
                rightEvents.Clear();
                original.DrainEvents(leftEvents);
                restored.DrainEvents(rightEvents);
                Assert.That(rightEvents.ConvertAll(EventIdentity), Is.EqualTo(leftEvents.ConvertAll(EventIdentity)));
                AssertEquivalent(original, restored);
            }
            Assert.That(original.TryExportState(out var later, out error), Is.True, error);
            Assert.That(later.RandomState, Is.Not.EqualTo(saved.RandomState));
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 未消费事件与未执行效果禁止保存，恢复不会重放已经应用的 Buff 初次效果。
        /// </summary>
        [Test]
        public void Export_RequiresConsumedBoundaryAndDoesNotReplayOnApply()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out _, out var enemy);
            Assert.That(original.ApplyBuff(enemy, 1, 1), Is.True);
            Assert.That(original.TryExportState(out _, out _), Is.False);
            original.DrainEvents(new List<CombatEvent>());
            Assert.That(original.TryExportState(out _, out _), Is.False);
            AdvanceAndDrain(original, 1);
            Assert.That(original.TryGetActor(enemy, out var before), Is.True);
            Assert.That(before.Defense, Is.EqualTo(2f));
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            Assert.That(restored.TryFindActor(2, out var restoredEnemy), Is.True);
            Assert.That(restored.TryGetActor(restoredEnemy, out var after), Is.True);
            Assert.That(after.Defense, Is.EqualTo(before.Defense));
            Assert.That(restored.DrainEvents(new List<CombatEvent>()), Is.Zero);
            AssertEquivalent(original, restored);
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 非活动区域保留原有效时钟与 Buff 周期，离线和恢复不推进其时间。
        /// </summary>
        [Test]
        public void Regions_RestoreIndependentClocksAndGeometry()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(original.ApplyBuff(enemy, 1, 1), Is.True);
            AdvanceAndDrain(original, 3);
            Assert.That(original.TryTransferActor(player, 2, new Vector3(-5f, 0f, 0f)), Is.True);
            AdvanceAndDrain(original, 20);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Rooms.Find(room => room.RoomId == 1).Tick, Is.EqualTo(3));
            Assert.That(saved.Rooms.Find(room => room.RoomId == 2).Tick, Is.EqualTo(20));
            Assert.That(saved.Rooms.Find(room => room.RoomId == 2).Obstacles.Count, Is.EqualTo(1));
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            AssertEquivalent(original, restored);
            Assert.That(restored.TryFindActor(1, out var restoredPlayer), Is.True);
            Assert.That(original.TryTransferActor(player, 1, Vector3.zero), Is.True);
            Assert.That(restored.TryTransferActor(restoredPlayer, 1, Vector3.zero), Is.True);
            AdvanceAndDrain(original, 10);
            AdvanceAndDrain(restored, 10);
            AssertEquivalent(original, restored);
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 恢复清除旧摇杆和不足一帧的累计时间，避免自动补跑未提交输入。
        /// </summary>
        [Test]
        public void Restore_ClearsInputAndSubTickAccumulator()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out var player, out _);
            Assert.That(original.SetMove(player, Vector2.right), Is.True);
            original.Advance(CombatWorld.TickSeconds * 0.5f);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            Assert.That(restored.Advance(CombatWorld.TickSeconds * 0.5f), Is.Zero);
            restored.Step();
            Assert.That(restored.TryFindActor(1, out var restoredPlayer), Is.True);
            Assert.That(restored.TryGetActor(restoredPlayer, out var actor), Is.True);
            Assert.That(actor.Position, Is.EqualTo(Vector3.zero));
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 敌人的待执行追击输入属于 AI 状态，恢复后第一帧不能额外停顿。
        /// </summary>
        [Test]
        public void Restore_PreservesPendingAiPursuit()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out _, out _);
            var pursuer = original.CreateActor(new CombatActorSpec { ActorId = 4, RoomId = 1, Team = 2,
                Position = Vector3.back * 8f, MaxHealth = 10000f, Radius = 0.3f,
                MoveSpeed = 3f, AggroRange = 20f, BasicSkillId = 1, AttackRange = 1f });
            Assert.That(pursuer.Id, Is.Not.Zero);
            AdvanceAndDrain(original, 3);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Actors.Find(actor => actor.Spec.ActorId == 4).AiMove.sqrMagnitude, Is.GreaterThan(0f));
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            for (int tick = 0; tick < 12; tick++)
            {
                AdvanceAndDrain(original, 1);
                AdvanceAndDrain(restored, 1);
                AssertEquivalent(original, restored);
            }
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 回收旧生命后在途飞弹仍使用旧来源属性，新生命和锁定目标不会混用。
        /// </summary>
        /// <param name="removeOnDeath">飞弹是否按配置随来源死亡终止。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void RetiredLife_PreservesProjectileSourceAndRejectsActorIdReuse(bool removeOnDeath)
        {
            var catalog = CreateCatalog();
            catalog.Projectiles[1].RemoveOnSourceDeath = removeOnDeath;
            var original = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(original.TrySpawnProjectile(enemy, 1, default, Vector3.zero, Vector3.back), Is.True);
            Assert.That(original.TrySpawnProjectile(player, 2, enemy, Vector3.forward * 8f, Vector3.forward), Is.True);
            Assert.That(original.TryStartSkill(player, 2, default), Is.True);
            original.Step();
            Assert.That(original.TryRetireDeadActor(enemy), Is.False);
            original.DrainEvents(new List<CombatEvent>());
            Assert.That(original.TryRetireDeadActor(enemy), Is.True);
            Assert.That(original.TryFindActor(2, out _), Is.False);
            Assert.That(original.CreateActor(new CombatActorSpec { ActorId = 2, RoomId = 1, Team = 2,
                Position = Vector3.forward * 8f, MaxHealth = 10000f, Radius = 0.3f }).Id, Is.Zero);
            var newLife = original.CreateActor(new CombatActorSpec { ActorId = 4, RoomId = 1, Team = 2,
                Position = Vector3.forward * 8f, MaxHealth = 10000f, Attack = 900f, Radius = 0.3f });
            Assert.That(newLife.Id, Is.Not.Zero);
            Assert.That(original.TryGetActor(enemy, out _), Is.False);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Projectiles.Exists(value => value.SourceKey < 0), Is.True);
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            for (int tick = 0; tick < 175; tick++)
            {
                AdvanceAndDrain(original, 1);
                AdvanceAndDrain(restored, 1);
                AssertEquivalent(original, restored);
            }
            Assert.That(original.TryGetActor(newLife, out var fresh), Is.True);
            Assert.That(fresh.Health, Is.EqualTo(10000f));
            Assert.That(original.TryGetActor(player, out var target), Is.True);
            Assert.That(target.Health, removeOnDeath ? Is.EqualTo(10000f) : Is.LessThan(10000f));
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 已命中过的同次生命在恢复后仍去重，不因轨迹再次重叠而重复造成伤害。
        /// </summary>
        [Test]
        public void ProjectileHitHistory_RestoresOncePerLife()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(original.TrySpawnProjectile(enemy, 1, default, Vector3.zero, Vector3.back), Is.True);
            AdvanceAndDrain(original, 160);
            Assert.That(original.TryGetActor(player, out var before), Is.True);
            Assert.That(before.Health, Is.LessThan(10000f));
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Projectiles[0].Hits.Count, Is.EqualTo(1));
            saved.Projectiles[0].Position = Vector3.zero;
            saved.Projectiles[0].Previous = Vector3.zero;
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            AdvanceAndDrain(restored, 10);
            Assert.That(restored.TryFindActor(1, out var restoredPlayer), Is.True);
            Assert.That(restored.TryGetActor(restoredPlayer, out var after), Is.True);
            Assert.That(after.Health, Is.EqualTo(before.Health));
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 外部快照的错误版本、坏引用和非法数值必须明确失败并不返回部分世界。
        /// </summary>
        /// <param name="fault">需要注入的错误类别。</param>
        [TestCase("version")]
        [TestCase("random")]
        [TestCase("duplicate")]
        [TestCase("target")]
        [TestCase("health")]
        [TestCase("cursor")]
        [TestCase("buff")]
        [TestCase("weakness")]
        public void Restore_RejectsInvalidState(string fault)
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(original.TryStartSkill(player, 1, enemy), Is.True);
            AdvanceAndDrain(original, 8);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            if (fault == "version") saved.Version++;
            else if (fault == "random") saved.RandomState = 0;
            else if (fault == "duplicate") saved.Casts[0].Key = saved.Actors[0].Key;
            else if (fault == "target") saved.Casts[0].TargetKey = saved.NextEntityKey + 1;
            else if (fault == "health") saved.Actors[0].Health = float.NaN;
            else if (fault == "cursor") saved.Casts[0].NextNode = int.MaxValue;
            else if (fault == "buff") catalog.Buffs.Remove(1);
            else saved.Actors[0].Spec.DamageVulnerabilities = new[]
                { new CombatDamageVulnerability { EffectId = 999999, Multiplier = 2f } };
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.False);
            Assert.That(restored, Is.Null);
            Assert.That(error, Is.Not.Empty);
            original.Dispose();
        }

        /// <summary>
        /// 导出及恢复不共享技能数组、词条定义或可写运行集合。
        /// </summary>
        [Test]
        public void Snapshots_DoNotShareWritableState()
        {
            var catalog = CreateCatalog();
            var original = CreateWorld(catalog, out var player, out _);
            var augment = new CombatAugmentDefinition { Id = "power", SkillId = 1, DamageMultiplier = 2f };
            Assert.That(original.TryReplaceAugments(player, "equipment", new[] { augment }), Is.True);
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            saved.Actors[0].PermanentBonuses[0] = 999f;
            saved.Actors[0].AugmentGroups[0].Definitions[0].DamageMultiplier = 5f;
            saved.Rooms[0].Obstacles.Add(new Rect(-1f, -1f, 2f, 2f));
            AssertEquivalent(original, restored);
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 软弱点配置在创建与恢复后均保持原倍率，并只放大匹配的伤害效果。
        /// </summary>
        [Test]
        public void Vulnerability_ColdRestorePreservesDamageAndIndependentConfiguration()
        {
            var catalog = CreateCatalog();
            var strike = new CombatSkillDefinition { CombatSkillId = 3, DurationTicks = 1, CooldownTicks = 1, StartRange = 20f };
            AddNode(strike, 0, new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 901,
                Value = 10f, Radius = 20f, CanCritical = false, CanDodge = false });
            catalog.Skills.Add(3, strike);
            catalog.DamageEffectIds.Add(902);
            var vulnerabilities = new[] { new CombatDamageVulnerability { EffectId = 901, Multiplier = 2f },
                new CombatDamageVulnerability { EffectId = 902, Multiplier = 3f } };
            var original = CreateWorld(catalog, out var player, out var enemy, vulnerabilities);
            Assert.That(enemy.Id, Is.Not.Zero);
            vulnerabilities[0].Multiplier = 999f;
            Assert.That(original.TryStartSkill(player, 3, enemy), Is.True);
            AdvanceAndDrain(original, 1);
            Assert.That(original.TryGetActor(enemy, out var first), Is.True);
            Assert.That(first.Health, Is.EqualTo(9980f));
            Assert.That(original.TryExportState(out var saved, out string error), Is.True, error);
            Assert.That(CombatWorld.TryRestore(catalog, saved, out var restored, out error), Is.True, error);
            saved.Actors[1].Spec.DamageVulnerabilities[0].Multiplier = 99f;
            saved.Actors[1].BaseSpec.DamageVulnerabilities[0].Multiplier = 77f;
            AdvanceAndDrain(original, 1);
            AdvanceAndDrain(restored, 1);
            Assert.That(restored.TryFindActor(1, out var restoredPlayer), Is.True);
            Assert.That(restored.TryFindActor(2, out var restoredEnemy), Is.True);
            Assert.That(original.TryStartSkill(player, 3, enemy), Is.True);
            Assert.That(restored.TryStartSkill(restoredPlayer, 3, restoredEnemy), Is.True);
            AdvanceAndDrain(original, 1);
            AdvanceAndDrain(restored, 1);
            Assert.That(restored.TryGetActor(restoredEnemy, out var second), Is.True);
            Assert.That(second.Health, Is.EqualTo(9960f));
            AssertEquivalent(original, restored);
            original.Dispose();
            restored.Dispose();
        }

        /// <summary>
        /// 创建包含可保存时间轴、周期 Buff 和移动飞弹的独立测试配置。
        /// </summary>
        /// <returns>测试专用配置目录。</returns>
        private static CombatCatalog CreateCatalog()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 25, CooldownTicks = 28, StartRange = 20f };
            AddNode(skill, 0, new CombatEffectDefinition { Kind = CombatEffectKind.ApplyBuff, LinkedId = 1 });
            AddNode(skill, 3, new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 1 });
            AddNode(skill, 5, new CombatEffectDefinition { Kind = CombatEffectKind.Displace, Value = 1f, DurationTicks = 8 });
            AddNode(skill, 10, new CombatEffectDefinition { Kind = CombatEffectKind.Damage, AttackCoefficient = 1f, Radius = 20f });
            catalog.Skills.Add(1, skill);
            var kill = new CombatSkillDefinition { CombatSkillId = 2, DurationTicks = 1, CooldownTicks = 1, StartRange = 20f };
            AddNode(kill, 0, new CombatEffectDefinition { Kind = CombatEffectKind.Damage, Value = 20000f,
                Radius = 20f, CanCritical = false, CanDodge = false });
            catalog.Skills.Add(2, kill);
            var buff = new CombatBuffDefinition { BuffId = 1, DurationTicks = 80, IntervalTicks = 7,
                MaxStacks = 3, MoveSpeedAddPerStack = -0.1f };
            buff.OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 101, Value = 1f,
                CanCritical = false, CanDodge = false });
            buff.OnApply.Add(new CombatEffectDefinition { Kind = CombatEffectKind.ModifyAttribute,
                Attribute = CombatAttributeKind.Defense, Value = 2f });
            buff.OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, AttackCoefficient = 0.1f });
            catalog.Buffs.Add(1, buff);
            var projectile = new CombatProjectileDefinition { ProjectileId = 1, Motion = ProjectileMotionKind.Straight,
                Speed = 3f, LifetimeTicks = 220, MaxDistance = 20f, Radius = 0.15f, MaxDistinctTargets = 3 };
            projectile.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, AttackCoefficient = 1f });
            catalog.Projectiles.Add(1, projectile);
            var locked = new CombatProjectileDefinition { ProjectileId = 2, Motion = ProjectileMotionKind.Straight,
                Target = ProjectileTargetKind.EntityLock, OnlyLockedTarget = true, LostTarget = ProjectileLostTarget.ContinueDirection,
                Speed = 3f, LifetimeTicks = 220, MaxDistance = 20f, Radius = 0.15f };
            locked.OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, Value = 100f, CanDodge = false });
            catalog.Projectiles.Add(2, locked);
            return catalog;
        }

        /// <summary>
        /// 向测试技能增加一个执行节点。
        /// </summary>
        /// <param name="skill">测试技能。</param>
        /// <param name="tick">节点相对帧。</param>
        /// <param name="effect">节点效果。</param>
        private static void AddNode(CombatSkillDefinition skill, int tick, CombatEffectDefinition effect)
        {
            var node = new CombatTimelineNode { Tick = tick };
            node.Effects.Add(effect);
            skill.Nodes.Add(node);
        }

        /// <summary>
        /// 创建两区域和三个按全局顺序登记的角色。
        /// </summary>
        /// <param name="catalog">测试配置。</param>
        /// <param name="player">创建的玩家引用。</param>
        /// <param name="enemy">第一区域敌人引用。</param>
        /// <param name="vulnerabilities">可选敌人软弱点配置。</param>
        /// <returns>尚未模拟的测试世界。</returns>
        private static CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef player, out CombatEntityRef enemy,
            CombatDamageVulnerability[] vulnerabilities = null)
        {
            var world = new CombatWorld(catalog, 817);
            world.SetRoomBounds(1, new Rect(-25f, -25f, 50f, 50f));
            world.SetRoomBounds(2, new Rect(-25f, -25f, 50f, 50f));
            world.AddObstacle(2, new Rect(10f, 10f, 2f, 2f));
            player = world.CreateActor(new CombatActorSpec { ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true,
                Position = Vector3.zero, MaxHealth = 10000f, Attack = 11f, CriticalRating = 100f, Radius = 0.3f, MoveSpeed = 3f });
            enemy = world.CreateActor(new CombatActorSpec { ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.forward * 8f,
                MaxHealth = 10000f, Attack = 4f, Radius = 0.3f, DodgeChance = 0.2f, DamageVulnerabilities = vulnerabilities });
            world.CreateActor(new CombatActorSpec { ActorId = 3, RoomId = 2, Team = 2,
                Position = Vector3.forward * 8f, MaxHealth = 10000f, Radius = 0.3f });
            return world;
        }

        /// <summary>
        /// 推进并消费所有事件，形成完整的保存边界。
        /// </summary>
        /// <param name="world">测试世界。</param>
        /// <param name="ticks">需要推进的完整帧数。</param>
        private static void AdvanceAndDrain(CombatWorld world, int ticks)
        {
            for (int tick = 0; tick < ticks; tick++) world.Step();
            world.DrainEvents(new List<CombatEvent>());
        }

        /// <summary>
        /// 比较不包含运行引用的全部持久状态。
        /// </summary>
        /// <param name="left">原世界。</param>
        /// <param name="right">恢复世界。</param>
        private static void AssertEquivalent(CombatWorld left, CombatWorld right)
        {
            Assert.That(left.TryExportState(out var expected, out string leftError), Is.True, leftError);
            Assert.That(right.TryExportState(out var actual, out string rightError), Is.True, rightError);
            Assert.That(JsonUtility.ToJson(actual), Is.EqualTo(JsonUtility.ToJson(expected)));
        }

        /// <summary>
        /// 取得不依赖世界引用的战斗事实比较键。
        /// </summary>
        /// <param name="fact">实际战斗事实。</param>
        /// <returns>可供两个世界直接比较的事实标识。</returns>
        private static string EventIdentity(CombatEvent fact) =>
            fact.Kind + ":" + fact.Tick + ":" + fact.RoomId + ":" + fact.ActorId + ":" + fact.TargetActorId +
            ":" + fact.DefinitionId + ":" + fact.Value + ":" + fact.Position + ":" + fact.IsCritical;
    }
}
