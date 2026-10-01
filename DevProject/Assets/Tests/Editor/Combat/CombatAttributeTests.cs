using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 角色属性、统一伤害与延迟来源快照测试。
    /// </summary>
    public sealed class CombatAttributeTests
    {
        /// <summary>
        /// 旧配置系数为零时保持固定伤害，新增属性按统一顺序结算。
        /// </summary>
        [Test]
        public void Damage_UsesAttackDefenseAndMultipliersWithoutChangingDefaults()
        {
            var fixedWorld = CreateSkillWorld(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 1, Value = 10f, Radius = 3f
            }, SourceSpec(), TargetSpec(), 4, out var fixedSource, out var fixedTarget);
            Assert.That(fixedWorld.TryStartSkill(fixedSource, 1, fixedTarget), Is.True);
            fixedWorld.Step();
            Assert.That(fixedWorld.TryGetActor(fixedTarget, out var fixedResult), Is.True);
            Assert.That(fixedResult.Health, Is.EqualTo(90f));
            Assert.That(fixedWorld.TryGetActor(fixedSource, out var fixedStats), Is.True);
            Assert.That(fixedStats.CriticalDamage, Is.EqualTo(1.5f));
            fixedWorld.Dispose();

            var sourceSpec = SourceSpec();
            sourceSpec.Attack = 20f;
            sourceSpec.DamageIncrease = 0.5f;
            var targetSpec = TargetSpec();
            targetSpec.Defense = 100f;
            targetSpec.DamageReduction = 0.25f;
            var world = CreateSkillWorld(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 2, Value = 10f,
                AttackCoefficient = 1f, Radius = 3f
            }, sourceSpec, targetSpec, 4, out var source, out var target);
            Assert.That(world.TryStartSkill(source, 1, target), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(target, out var result), Is.True);
            Assert.That(result.Health, Is.EqualTo(83.125f).Within(0.0001f));
            world.Dispose();
        }

        /// <summary>
        /// 最大暴击值必然暴击；闪避使用独立种子并阻止受伤事件。
        /// </summary>
        [Test]
        public void CriticalAndDodge_AreDeterministicAndDistinct()
        {
            var sourceSpec = SourceSpec();
            sourceSpec.CriticalRating = float.MaxValue;
            sourceSpec.CriticalDamage = 2f;
            var effect = new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 3, Value = 10f, Radius = 3f
            };
            var criticalWorld = CreateSkillWorld(effect, sourceSpec, TargetSpec(), 9, out var source, out var target);
            Assert.That(criticalWorld.TryStartSkill(source, 1, target), Is.True);
            criticalWorld.Step();
            var events = new List<CombatEvent>();
            criticalWorld.DrainEvents(events);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage && item.IsCritical && item.Value == 20f), Is.True);
            criticalWorld.Dispose();

            int seed = 0;
            while (new System.Random(seed).NextDouble() >= 0.75d) seed++;
            var dodgeSpec = TargetSpec();
            dodgeSpec.DodgeChance = 2f;
            for (int repeat = 0; repeat < 2; repeat++)
            {
                var world = CreateSkillWorld(effect, SourceSpec(), dodgeSpec, seed, out source, out target);
                Assert.That(world.TryGetActor(target, out var before), Is.True);
                Assert.That(before.DodgeChance, Is.EqualTo(0.75f));
                Assert.That(world.TryStartSkill(source, 1, target), Is.True);
                world.Step();
                events.Clear();
                world.DrainEvents(events);
                Assert.That(world.TryGetActor(target, out var after), Is.True);
                Assert.That(after.Health, Is.EqualTo(100f));
                Assert.That(events.Exists(item => item.Kind == CombatEventKind.Dodged), Is.True);
                Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage), Is.False);
                world.Dispose();
            }
        }

        /// <summary>
        /// 配置关闭暴击和闪避时不消费概率并保持固定伤害。
        /// </summary>
        [Test]
        public void DamageFlags_DisableCriticalAndDodge()
        {
            var sourceSpec = SourceSpec();
            sourceSpec.CriticalRating = float.MaxValue;
            sourceSpec.CriticalDamage = 3f;
            var targetSpec = TargetSpec();
            targetSpec.DodgeChance = 0.75f;
            var effect = new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 4, Value = 10f,
                Radius = 3f, CanCritical = false, CanDodge = false
            };
            var world = CreateSkillWorld(effect, sourceSpec, targetSpec, 0, out var source, out var target);
            Assert.That(world.TryStartSkill(source, 1, target), Is.True);
            world.Step();
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Dodged), Is.False);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage && !item.IsCritical && item.Value == 10f), Is.True);
            Assert.That(world.TryGetActor(target, out var result), Is.True);
            Assert.That(result.Health, Is.EqualTo(90f));
            world.Dispose();
        }

        /// <summary>
        /// 发射者在命中前死亡时，飞弹仍使用出生时的攻击值。
        /// </summary>
        [Test]
        public void Projectile_KeepsOffenseAfterSourceDeath()
        {
            var catalog = CombatCatalog.CreatePrototype();
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 9, Motion = ProjectileMotionKind.Straight, Speed = 600f, MaxSpeed = 600f,
                LifetimeTicks = 3, MaxDistance = 20f, Radius = 0.2f
            };
            projectile.OnHit.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 9, Value = 5f,
                AttackCoefficient = 1f, CanCritical = false, CanDodge = false
            });
            catalog.Projectiles.Add(9, projectile);
            AddLethalBuff(catalog);
            var world = new CombatWorld(catalog, 11);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var sourceSpec = SourceSpec();
            sourceSpec.Attack = 20f;
            sourceSpec.Position = new Vector3(-2f, 0f, 0f);
            var targetSpec = TargetSpec();
            targetSpec.Position = new Vector3(2f, 0f, 0f);
            var source = world.CreateActor(sourceSpec);
            var target = world.CreateActor(targetSpec);
            Assert.That(world.TrySpawnProjectile(source, 9, target, targetSpec.Position, Vector3.right), Is.True);
            Assert.That(world.ApplyBuff(source, 77, 2), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(source, out var deadSource), Is.True);
            Assert.That(deadSource.Dead, Is.True);
            Assert.That(world.TryGetActor(target, out var result), Is.True);
            Assert.That(result.Health, Is.EqualTo(75f));
            world.Dispose();
        }

        /// <summary>
        /// 施加者死亡后的周期伤害继续使用施加时的攻击属性。
        /// </summary>
        [Test]
        public void Buff_KeepsOffenseAfterSourceDeath()
        {
            var catalog = CombatCatalog.CreatePrototype();
            var periodic = new CombatBuffDefinition { BuffId = 8, DurationTicks = 4, IntervalTicks = 2 };
            periodic.OnTick.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 8, Value = 5f,
                AttackCoefficient = 1f, CanCritical = false, CanDodge = false
            });
            catalog.Buffs.Add(8, periodic);
            AddLethalBuff(catalog);
            var world = new CombatWorld(catalog, 11);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var sourceSpec = SourceSpec();
            sourceSpec.Attack = 20f;
            var source = world.CreateActor(sourceSpec);
            var target = world.CreateActor(TargetSpec());
            Assert.That(world.ApplyBuff(target, 8, 1), Is.True);
            Assert.That(world.ApplyBuff(source, 77, 2), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(source, out var deadSource), Is.True);
            Assert.That(deadSource.Dead, Is.True);
            world.Step();
            Assert.That(world.TryGetActor(target, out var result), Is.True);
            Assert.That(result.Health, Is.EqualTo(75f));
            world.Dispose();
        }

        /// <summary>
        /// 无效属性被拒绝，省略暴伤和过高减伤按契约归一。
        /// </summary>
        [Test]
        public void ActorAttributes_RejectInvalidAndClampBounds()
        {
            var world = new CombatWorld(CombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var invalid = SourceSpec();
            invalid.Attack = float.NaN;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            invalid = SourceSpec();
            invalid.Defense = -1f;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            invalid = SourceSpec();
            invalid.CriticalRating = float.PositiveInfinity;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            invalid = SourceSpec();
            invalid.CriticalDamage = -1f;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            invalid = SourceSpec();
            invalid.DodgeChance = -0.1f;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            invalid = SourceSpec();
            invalid.DamageIncrease = -1f;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            invalid = SourceSpec();
            invalid.DamageReduction = -1f;
            Assert.That(world.CreateActor(invalid).Id, Is.EqualTo(0));
            var valid = SourceSpec();
            valid.DamageReduction = 2f;
            var actor = world.CreateActor(valid);
            Assert.That(actor.Id, Is.GreaterThan(0));
            Assert.That(world.TryGetActor(actor, out var stats), Is.True);
            Assert.That(stats.CriticalDamage, Is.EqualTo(1.5f));
            Assert.That(stats.DamageReduction, Is.EqualTo(0.75f));
            world.Dispose();
        }

        /// <summary>
        /// 建立仅含一次首帧圆形伤害的战斗世界。
        /// </summary>
        /// <param name="effect">本次技能的伤害效果。</param>
        /// <param name="sourceSpec">施法者配置。</param>
        /// <param name="targetSpec">目标配置。</param>
        /// <param name="seed">战斗随机种子。</param>
        /// <param name="source">已创建的施法者引用。</param>
        /// <param name="target">已创建的目标引用。</param>
        /// <returns>角色和技能均已登记的世界。</returns>
        private static CombatWorld CreateSkillWorld(CombatEffectDefinition effect, CombatActorSpec sourceSpec,
            CombatActorSpec targetSpec, int seed, out CombatEntityRef source, out CombatEntityRef target)
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 2, StartRange = 3f };
            var node = new CombatTimelineNode { Tick = 1 };
            node.Effects.Add(effect);
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            var world = new CombatWorld(catalog, seed);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            source = world.CreateActor(sourceSpec);
            target = world.CreateActor(targetSpec);
            Assert.That(source.Id, Is.GreaterThan(0));
            Assert.That(target.Id, Is.GreaterThan(0));
            return world;
        }

        /// <summary>
        /// 建立用于确认飞弹和 Buff 来源死亡时序的致死 Buff。
        /// </summary>
        /// <param name="catalog">待补充的战斗配置。</param>
        private static void AddLethalBuff(CombatCatalog catalog)
        {
            var lethal = new CombatBuffDefinition { BuffId = 77, DurationTicks = 2, IntervalTicks = 1 };
            lethal.OnApply.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, DefinitionId = 77, Value = 1000f,
                CanCritical = false, CanDodge = false
            });
            catalog.Buffs.Add(77, lethal);
        }

        /// <summary>
        /// 创建不自动施法的来源角色配置。
        /// </summary>
        /// <returns>可在测试地图使用的角色配置。</returns>
        private static CombatActorSpec SourceSpec() => new CombatActorSpec
        {
            ActorId = 1, RoomId = 1, Team = 1, Position = Vector3.zero,
            MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
        };

        /// <summary>
        /// 创建不自动施法的敌对角色配置。
        /// </summary>
        /// <returns>可在测试地图使用的角色配置。</returns>
        private static CombatActorSpec TargetSpec() => new CombatActorSpec
        {
            ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.right,
            MaxHealth = 100f, MoveSpeed = 0f, Radius = 0.3f
        };
    }
}
