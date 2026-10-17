using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 高级词条的触发、技能修正和治疗边界测试。
    /// </summary>
    public sealed class CombatAugmentTests
    {
        /// <summary>
        /// 命中与受伤分别归因，衍生伤害不再触发被动且同词条有内部冷却。
        /// </summary>
        [Test]
        public void ConfirmedDamage_TriggersOwnersWithoutRecursivePassives()
        {
            var catalog = CreateDamageCatalog(10f, 1);
            var bleedBuff = new CombatBuffDefinition
            {
                BuffId = 1, DurationTicks = 90, IntervalTicks = 30, MaxStacks = 1
            };
            bleedBuff.OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 101, Value = 2f,
                CanCritical = false, CanDodge = false });
            catalog.Buffs.Add(1, bleedBuff);
            var world = CreateWorld(catalog, out var player, out var enemy);
            var hit = Augment("hit", CombatTriggerKind.Hit, CombatEffectKind.Damage, 3f, 5);
            var bleed = Augment("bleed", CombatTriggerKind.Hit, CombatEffectKind.ApplyBuff, 0f);
            bleed.Effects[0].LinkedId = 1;
            var counter = Augment("counter", CombatTriggerKind.Damaged, CombatEffectKind.Damage, 4f);
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { hit, bleed }), Is.True);
            Assert.That(world.TryReplaceAugments(enemy, "equipment", new[] { counter }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var firstEnemy), Is.True);
            Assert.That(firstEnemy.Health, Is.EqualTo(87f));
            Assert.That(world.TryGetActor(player, out var firstPlayer), Is.True);
            Assert.That(firstPlayer.Health, Is.EqualTo(96f));
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Damage).Count, Is.EqualTo(3));
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage &&
                item.ActorId == 2 && item.TargetActorId == 1 && item.Value == 4f), Is.True);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.BuffApplied &&
                item.ActorId == 1 && item.TargetActorId == 2), Is.True);
            world.Step();
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var secondEnemy), Is.True);
            Assert.That(secondEnemy.Health, Is.EqualTo(77f));
            Assert.That(world.TryGetActor(player, out var secondPlayer), Is.True);
            Assert.That(secondPlayer.Health, Is.EqualTo(92f));
            for (var index = 0; index < 28; index++) world.Step();
            Assert.That(world.TryGetActor(enemy, out var periodic), Is.True);
            Assert.That(periodic.Health, Is.EqualTo(75f));
            Assert.That(world.TryGetActor(player, out var unchanged), Is.True);
            Assert.That(unchanged.Health, Is.EqualTo(92f));
            world.Dispose();
        }

        /// <summary>
        /// 被动生成飞弹的衍生伤害不会再次触发命中词条。
        /// </summary>
        [Test]
        public void PassiveProjectile_DoesNotTriggerAnotherPassive()
        {
            var catalog = CreateDamageCatalog(10f, 1);
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.Instant,
                SpawnAnchor = ProjectileSpawnAnchor.TargetEntityAtSpawn, Radius = 1f, MaxDistinctTargets = 1
            };
            projectile.OnHit.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 2f, CanCritical = false, CanDodge = false
            });
            catalog.Projectiles.Add(1, projectile);
            var world = CreateWorld(catalog, out var player, out var enemy);
            var augment = Augment("projectile", CombatTriggerKind.Hit, CombatEffectKind.SpawnProjectile, 0f);
            augment.Effects[0].LinkedId = 1;
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { augment }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.Faulted, Is.False);
            Assert.That(world.TryGetActor(enemy, out var target), Is.True);
            Assert.That(target.Health, Is.EqualTo(88f));
            world.Dispose();
        }

        /// <summary>
        /// 更换词条时深拷贝效果，组间互不覆盖，卸下重装不清除内部冷却。
        /// </summary>
        [Test]
        public void ReplaceAugments_PreservesCooldownAndSeparatesGroups()
        {
            var world = CreateWorld(CreateDamageCatalog(10f, 1), out var player, out var enemy);
            var equipment = Augment("stable", CombatTriggerKind.Hit, CombatEffectKind.Damage, 5f, 6);
            var rogue = Augment("bonus", CombatTriggerKind.Hit, CombatEffectKind.Damage, 2f);
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { equipment }), Is.True);
            Assert.That(world.TryReplaceAugments(player, "rogue", new[] { rogue }), Is.True);
            Assert.That(world.TryReplaceAugments(player, "equipment",
                new[] { Augment("invalid", CombatTriggerKind.Hit, CombatEffectKind.SpawnProjectile, 0f) }), Is.False);
            equipment.Effects[0].Value = 50f;
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var first), Is.True);
            Assert.That(first.Health, Is.EqualTo(83f));
            Assert.That(world.TryReplaceAugments(player, "equipment", Array.Empty<CombatAugmentDefinition>()), Is.True);
            world.Step();
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var second), Is.True);
            Assert.That(second.Health, Is.EqualTo(71f));
            world.Step();
            Assert.That(world.TryReplaceAugments(player, "equipment",
                new[] { Augment("stable", CombatTriggerKind.Hit, CombatEffectKind.Damage, 5f, 6) }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var third), Is.True);
            Assert.That(third.Health, Is.EqualTo(59f));
            world.Dispose();
        }

        /// <summary>
        /// 击杀治疗只恢复存活角色的缺失生命，不会复活死亡目标。
        /// </summary>
        [Test]
        public void KillHeal_ClampsToMaximumAndCannotReviveVictim()
        {
            var catalog = CreateDamageCatalog(200f, 1);
            catalog.Skills[1].Nodes[0].Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 200f, Radius = 3f, CanCritical = false, CanDodge = false
            });
            var enemySkill = new CombatSkillDefinition { CombatSkillId = 2, DurationTicks = 2, StartRange = 3f };
            var enemyNode = new CombatTimelineNode { Tick = 1 };
            enemyNode.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 30f, Radius = 3f, CanCritical = false, CanDodge = false
            });
            enemySkill.Nodes.Add(enemyNode);
            catalog.Skills.Add(2, enemySkill);
            var world = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(player, out var wounded), Is.True);
            Assert.That(wounded.Health, Is.EqualTo(70f));
            var selfHeal = Augment("heal-self", CombatTriggerKind.Kill, CombatEffectKind.Heal, 50f);
            selfHeal.TargetSelf = true;
            var deadHeal = Augment("heal-dead", CombatTriggerKind.Kill, CombatEffectKind.Heal, 50f);
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { selfHeal, deadHeal }), Is.True);
            world.Step();
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(player, out var healed), Is.True);
            Assert.That(healed.Health, Is.EqualTo(100f));
            Assert.That(world.TryGetActor(enemy, out var dead), Is.True);
            Assert.That(dead.Health, Is.Zero);
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Heal).Count, Is.EqualTo(1));
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Death && item.TargetActorId == 2).Count, Is.EqualTo(1));
            Assert.That(events.Find(item => item.Kind == CombatEventKind.Heal).Value, Is.EqualTo(30f));
            world.Dispose();
        }

        /// <summary>
        /// 被动衍生伤害仍保留击杀归属，但不会再触发击杀治疗。
        /// </summary>
        [Test]
        public void PassiveKill_KeepsAttributionWithoutAnotherPassiveReward()
        {
            var catalog = CreateDamageCatalog(10f, 1);
            var enemySkill = new CombatSkillDefinition { CombatSkillId = 2, DurationTicks = 2, StartRange = 3f };
            var node = new CombatTimelineNode { Tick = 1 };
            node.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 20f, Radius = 3f, CanCritical = false, CanDodge = false
            });
            enemySkill.Nodes.Add(node);
            catalog.Skills.Add(2, enemySkill);
            var world = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            world.Step();
            var extraDamage = Augment("extra", CombatTriggerKind.Hit, CombatEffectKind.Damage, 90f);
            var heal = Augment("heal", CombatTriggerKind.Kill, CombatEffectKind.Heal, 20f);
            heal.TargetSelf = true;
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { extraDamage, heal }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(player, out var survivor), Is.True);
            Assert.That(survivor.Health, Is.EqualTo(80f));
            Assert.That(world.TryGetActor(enemy, out var dead), Is.True);
            Assert.That(dead.Health, Is.Zero);
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Heal).Count, Is.Zero);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.Death &&
                item.ActorId == 1 && item.TargetActorId == 2).Count, Is.EqualTo(1));
            world.Dispose();
        }

        /// <summary>
        /// 同来源 Buff 按层合并，不同来源保留独立实例与伤害归因。
        /// </summary>
        [Test]
        public void BleedBuff_MergesPerSourceAndKeepsDistinctSourceEvents()
        {
            var catalog = new CombatCatalog();
            var bleedBuff = new CombatBuffDefinition
            {
                BuffId = 1, DurationTicks = 4, IntervalTicks = 1, MaxStacks = 3
            };
            bleedBuff.OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 101, Value = 2f,
                CanCritical = false, CanDodge = false });
            catalog.Buffs.Add(1, bleedBuff);
            var world = CreateWorld(catalog, out var firstSource, out var target);
            var secondSource = world.CreateActor(new CombatActorSpec
            {
                ActorId = 3, RoomId = 1, Team = 1, Position = Vector3.left * 2f,
                MaxHealth = 100f, Radius = 0.3f
            });
            Assert.That(secondSource.Id, Is.GreaterThan(0));
            Assert.That(world.ApplyBuff(target, 1, 1), Is.True);
            Assert.That(world.ApplyBuff(target, 1, 1), Is.True);
            Assert.That(world.ApplyBuff(target, 1, 3), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(target, out var damaged), Is.True);
            Assert.That(damaged.Health, Is.EqualTo(94f));
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.FindAll(item => item.Kind == CombatEventKind.BuffApplied).Count, Is.EqualTo(2));
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage &&
                item.ActorId == 1 && item.TargetActorId == 2 && item.Value == 4f), Is.True);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage &&
                item.ActorId == 3 && item.TargetActorId == 2 && item.Value == 2f), Is.True);
            world.Dispose();
        }

        /// <summary>
        /// 技能修正只作用指定施法者，旧施法保留范围和伤害，新施法读取已移除的修正。
        /// </summary>
        [Test]
        public void SkillModifiers_SnapshotAtCastAndDoNotChangeSharedEnemySkill()
        {
            var catalog = CreateDamageCatalog(0f, 2);
            catalog.InitialAttackRange = 2f;
            catalog.Skills[1].StartRange = 2f;
            catalog.Skills[1].AttackRangeSource = true;
            catalog.Skills[1].CooldownTicks = 10;
            catalog.Skills[1].Nodes[0].Effects[0].AttackCoefficient = 1f;
            catalog.Skills[1].Nodes[0].Effects[0].Radius = 2f;
            catalog.Skills[1].Nodes[0].Effects[0].AttackRangeSource = true;
            var world = CreateWorld(catalog, out var player, out var enemy, 3f, true);
            var modifier = new CombatAugmentDefinition
            {
                Id = "skill", SkillId = 0, RangeMultiplier = 2f,
                DamageMultiplier = 2f, CooldownMultiplier = 0.1f
            };
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.False);
            Assert.That(world.TryReplaceAugments(player, "equipment", new[] { modifier }), Is.True);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.False);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            Assert.That(world.TryReplaceAugments(player, "equipment", Array.Empty<CombatAugmentDefinition>()), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var first), Is.True);
            Assert.That(first.Health, Is.EqualTo(80f));
            world.Step();
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.False);
            Assert.That(world.TrySetPosition(enemy, Vector3.right), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var second), Is.True);
            Assert.That(second.Health, Is.EqualTo(70f));
            world.Dispose();
        }

        /// <summary>
        /// Buff 派生飞弹从持有者位置发射，但保持原施加者归因和阵营。
        /// </summary>
        [Test]
        public void BuffProjectile_KeepsOriginalInstigatorAndTeam()
        {
            var catalog = new CombatCatalog();
            var buff = new CombatBuffDefinition { BuffId = 1, DurationTicks = 3, IntervalTicks = 1, MaxStacks = 1 };
            buff.OnTick.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 1 });
            catalog.Buffs.Add(1, buff);
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.Instant,
                SpawnAnchor = ProjectileSpawnAnchor.TargetEntityAtSpawn, Radius = 1f, MaxDistinctTargets = 1
            };
            projectile.OnHit.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 5f, CanCritical = false, CanDodge = false
            });
            catalog.Projectiles.Add(1, projectile);
            var world = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(world.ApplyBuff(enemy, 1, 1), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var target), Is.True);
            Assert.That(target.Health, Is.EqualTo(95f));
            Assert.That(world.TryGetActor(player, out var source), Is.True);
            Assert.That(source.Health, Is.EqualTo(100f));
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            Assert.That(events.Exists(item => item.Kind == CombatEventKind.Damage &&
                item.ActorId == 1 && item.TargetActorId == 2 && item.Value == 5f), Is.True);
            world.Dispose();
        }

        /// <summary>
        /// 外部直接发射飞弹与施加 Buff 不读取尚未施放的技能倍率。
        /// </summary>
        [Test]
        public void ExternalEffects_DoNotInheritSkillDamageModifier()
        {
            var catalog = CreateDamageCatalog(0f, 1);
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.Instant,
                SpawnAnchor = ProjectileSpawnAnchor.TargetEntityAtSpawn, Radius = 1f, MaxDistinctTargets = 1
            };
            projectile.OnHit.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, AttackCoefficient = 1f,
                CanCritical = false, CanDodge = false
            });
            catalog.Projectiles.Add(1, projectile);
            var buff = new CombatBuffDefinition { BuffId = 1, DurationTicks = 3, IntervalTicks = 1, MaxStacks = 1 };
            buff.OnTick.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, AttackCoefficient = 1f,
                CanCritical = false, CanDodge = false
            });
            catalog.Buffs.Add(1, buff);
            var world = CreateWorld(catalog, out var player, out var enemy);
            Assert.That(world.TryReplaceAugments(player, "equipment",
                new[] { new CombatAugmentDefinition { Id = "damage", SkillId = 1, DamageMultiplier = 10f } }), Is.True);
            Assert.That(world.TrySpawnProjectile(player, 1, enemy, Vector3.right, Vector3.right), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var projectileHit), Is.True);
            Assert.That(projectileHit.Health, Is.EqualTo(90f));
            Assert.That(world.ApplyBuff(enemy, 1, 1), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var buffHit), Is.True);
            Assert.That(buffHit.Health, Is.EqualTo(80f));
            world.Dispose();
        }

        /// <summary>
        /// 已生成飞弹的到达效果保留卸下前的技能范围倍率。
        /// </summary>
        [Test]
        public void Projectile_KeepsRangeModifierSnapshotAfterRemoval()
        {
            var catalog = CreateDamageCatalog(0f, 1);
            catalog.InitialAttackRange = 2f;
            var skill = catalog.Skills[1];
            skill.StartRange = 2f;
            skill.CooldownTicks = 100;
            skill.Nodes[0].Effects.Clear();
            skill.Nodes[0].Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 1 });
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.FixedEndpointArc,
                Target = ProjectileTargetKind.PointSnapshot, FlightTicks = 4, Speed = 10f,
                MaxDistance = 10f, Radius = 0.1f
            };
            projectile.OnArrive.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 10f, Radius = 2f,
                CanCritical = false, CanDodge = false
            });
            catalog.Projectiles.Add(1, projectile);
            var world = CreateWorld(catalog, out var player, out var enemy, 1f, true);
            Assert.That(world.TrySetPosition(enemy, Vector3.forward * 7f), Is.True);
            Assert.That(world.TryReplaceAugments(player, "equipment",
                new[] { new CombatAugmentDefinition { Id = "range", RangeMultiplier = 2f } }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, default), Is.True);
            world.Step();
            Assert.That(world.TryReplaceAugments(player, "equipment", Array.Empty<CombatAugmentDefinition>()), Is.True);
            world.Step();
            world.Step();
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var hit), Is.True);
            Assert.That(hit.Health, Is.EqualTo(90f));
            world.Dispose();
        }

        /// <summary>
        /// 已施加的持续伤害保留卸下前的技能伤害倍率。
        /// </summary>
        [Test]
        public void Buff_KeepsDamageModifierSnapshotAfterRemoval()
        {
            var catalog = CreateDamageCatalog(0f, 1);
            var skill = catalog.Skills[1];
            skill.CooldownTicks = 100;
            skill.Nodes[0].Effects.Clear();
            skill.Nodes[0].Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.ApplyBuff, LinkedId = 1 });
            var buff = new CombatBuffDefinition { BuffId = 1, DurationTicks = 10, IntervalTicks = 3, MaxStacks = 1 };
            buff.OnTick.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 10f, CanCritical = false, CanDodge = false
            });
            catalog.Buffs.Add(1, buff);
            var world = CreateWorld(catalog, out var player, out var enemy, 1f, true);
            Assert.That(world.TryReplaceAugments(player, "equipment",
                new[] { new CombatAugmentDefinition { Id = "damage", DamageMultiplier = 2f } }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryReplaceAugments(player, "equipment", Array.Empty<CombatAugmentDefinition>()), Is.True);
            world.Step();
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var damaged), Is.True);
            Assert.That(damaged.Health, Is.EqualTo(80f));
            world.Dispose();
        }

        /// <summary>
        /// 暴击词条只在有效暴击伤害后追加一次伤害。
        /// </summary>
        [Test]
        public void CriticalHit_TriggersAdditionalDamageOnce()
        {
            var catalog = CreateDamageCatalog(10f, 1);
            catalog.Skills[1].Nodes[0].Effects[0].CanCritical = true;
            var world = CreateWorld(catalog, out var player, out var enemy, 1f, false, float.MaxValue);
            Assert.That(world.TryReplaceAugments(player, "equipment",
                new[] { Augment("critical", CombatTriggerKind.CriticalHit, CombatEffectKind.Damage, 5f) }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var target), Is.True);
            Assert.That(target.Health, Is.EqualTo(85f));
            world.Dispose();
        }

        /// <summary>
        /// 创建无自动攻击的基础伤害技能。
        /// </summary>
        /// <param name="damage">固定伤害。</param>
        /// <param name="tick">伤害时间轴帧。</param>
        /// <returns>技能目录。</returns>
        private static CombatCatalog CreateDamageCatalog(float damage, int tick)
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = tick + 1, StartRange = 3f };
            var node = new CombatTimelineNode { Tick = tick };
            node.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = damage, Radius = 3f, CanCritical = false, CanDodge = false
            });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            return catalog;
        }

        /// <summary>
        /// 创建一项可触发的词条效果。
        /// </summary>
        /// <param name="id">稳定词条编号。</param>
        /// <param name="trigger">触发条件。</param>
        /// <param name="effectKind">效果类型。</param>
        /// <param name="value">效果固定值。</param>
        /// <param name="cooldownTicks">内部冷却帧数。</param>
        /// <returns>可安装词条。</returns>
        private static CombatAugmentDefinition Augment(string id, CombatTriggerKind trigger,
            CombatEffectKind effectKind, float value, int cooldownTicks = 0)
        {
            var definition = new CombatAugmentDefinition { Id = id, Trigger = trigger, CooldownTicks = cooldownTicks };
            definition.Effects.Add(new CombatEffectDefinition
            {
                Kind = effectKind, Value = value, CanCritical = false, CanDodge = false
            });
            return definition;
        }

        /// <summary>
        /// 创建两名相邻且不会自动施法的角色。
        /// </summary>
        /// <param name="catalog">战斗目录。</param>
        /// <param name="player">首名角色。</param>
        /// <param name="enemy">敌对角色。</param>
        /// <param name="distance">两名角色距离。</param>
        /// <param name="basicSkill">玩家是否启用自动基础技能编号。</param>
        /// <param name="criticalRating">玩家初始暴击值。</param>
        /// <returns>可推进的战斗世界。</returns>
        private static CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef player, out CombatEntityRef enemy,
            float distance = 1f, bool basicSkill = false, float criticalRating = 0f)
        {
            var world = new CombatWorld(catalog, 13);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, Attack = 10f, CriticalRating = criticalRating, CriticalDamage = 1f,
                Radius = 0.3f, AttackRange = 2f, BasicSkillId = basicSkill ? 1 : 0
            });
            enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.right * distance,
                MaxHealth = 100f, Radius = 0.3f
            });
            Assert.That(player.Id, Is.GreaterThan(0));
            Assert.That(enemy.Id, Is.GreaterThan(0));
            return world;
        }
    }
}
