using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 局内属性强化与延迟伤害快照测试。
    /// </summary>
    public sealed class CombatAttributeUpgradeTests
    {
        /// <summary>
        /// 八项属性均增加，概率和普通属性按上限截断。
        /// </summary>
        [Test]
        public void Bonus_UpdatesEveryAttributeAndClampsAtCap()
        {
            var world = CreateWorld(new CombatCatalog(), out var actor, out _);
            var attributes = new[]
            {
                CombatAttributeKind.MaxHealth, CombatAttributeKind.Attack, CombatAttributeKind.Defense,
                CombatAttributeKind.CriticalRating, CombatAttributeKind.CriticalDamage, CombatAttributeKind.DodgeChance,
                CombatAttributeKind.DamageIncrease, CombatAttributeKind.DamageReduction
            };
            foreach (CombatAttributeKind attribute in attributes)
                Assert.That(world.TryAddAttributeBonus(actor, attribute, 1f), Is.True, attribute.ToString());
            Assert.That(world.TryGetActor(actor, out var updated), Is.True);
            Assert.That(updated.MaxHealth, Is.EqualTo(101f));
            Assert.That(updated.Health, Is.EqualTo(101f));
            Assert.That(updated.Attack, Is.EqualTo(11f));
            Assert.That(updated.Defense, Is.EqualTo(1f));
            Assert.That(updated.CriticalRating, Is.EqualTo(1f));
            Assert.That(updated.CriticalDamage, Is.EqualTo(2.5f));
            Assert.That(updated.DodgeChance, Is.EqualTo(0.75f));
            Assert.That(updated.DamageIncrease, Is.EqualTo(1f));
            Assert.That(updated.DamageReduction, Is.EqualTo(0.75f));
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, float.MaxValue), Is.True);
            Assert.That(world.TryGetActor(actor, out updated), Is.True);
            Assert.That(updated.Attack, Is.EqualTo(1000000f));
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, 1f), Is.False);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.DodgeChance, 0.1f), Is.False);
            world.Dispose();
        }

        /// <summary>
        /// 最大生命强化保留损伤，非法输入和死亡实体不改变属性。
        /// </summary>
        [Test]
        public void Bonus_PreservesDamageAndRejectsInvalidOrDeadActor()
        {
            var catalog = DamageCatalog(20f, 1, CombatEffectKind.Damage);
            var world = CreateWorld(catalog, out var actor, out var target);
            Assert.That(world.TryStartSkill(target, 1, actor), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(actor, out var damaged), Is.True);
            Assert.That(damaged.Health, Is.EqualTo(80f));
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.MaxHealth, 25f), Is.True);
            Assert.That(world.TryGetActor(actor, out var upgraded), Is.True);
            Assert.That(upgraded.MaxHealth, Is.EqualTo(125f));
            Assert.That(upgraded.Health, Is.EqualTo(105f));
            Assert.That(world.TryAddAttributeBonus(default, CombatAttributeKind.Attack, 1f), Is.False);
            Assert.That(world.TryAddAttributeBonus(actor, (CombatAttributeKind)999, 1f), Is.False);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, 0f), Is.False);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, -1f), Is.False);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, float.NaN), Is.False);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, float.PositiveInfinity), Is.False);
            Assert.That(world.TryGetActor(actor, out var unchanged), Is.True);
            Assert.That(unchanged.Attack, Is.EqualTo(10f));
            world.Dispose();

            world = CreateWorld(DamageCatalog(200f, 1, CombatEffectKind.Damage), out actor, out target);
            Assert.That(world.TryStartSkill(target, 1, actor), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(actor, out var dead), Is.True);
            Assert.That(dead.Dead, Is.True);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.MaxHealth, 100f), Is.False);
            Assert.That(world.TryReplaceBaseAttributes(actor, new CombatActorSpec { MaxHealth = 200f }), Is.False);
            world.Dispose();
        }

        /// <summary>
        /// 效果路径可强化指定目标，先前施法保留旧攻击快照，后续施法读取新攻击。
        /// </summary>
        [Test]
        public void EffectAndDamage_UseSharedUpgradeAndCastSnapshot()
        {
            var upgrade = DamageCatalog(5f, 1, CombatEffectKind.ModifyAttribute);
            var world = CreateWorld(upgrade, out var actor, out var target);
            Assert.That(world.TryStartSkill(actor, 1, target), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(target, out var upgraded), Is.True);
            Assert.That(upgraded.Attack, Is.EqualTo(5f));
            world.Dispose();

            var catalog = DamageCatalog(0f, 2, CombatEffectKind.Damage);
            world = CreateWorld(catalog, out actor, out target);
            Assert.That(world.TryStartSkill(actor, 1, target), Is.True);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.Attack, 10f), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(target, out var first), Is.True);
            Assert.That(first.Health, Is.EqualTo(90f));
            world.Step();
            Assert.That(world.TryStartSkill(actor, 1, target), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(target, out var second), Is.True);
            Assert.That(second.Health, Is.EqualTo(70f));
            world.Dispose();
        }

        /// <summary>
        /// 换装预览保留永久强化，实际替换仅压低当前生命且不抹去被上限遮蔽的强化。
        /// </summary>
        [Test]
        public void ReplaceBaseAttributes_PreservesBonusesAndClampsHealthWithoutHealing()
        {
            var world = CreateWorld(DamageCatalog(20f, 1, CombatEffectKind.Damage), out var actor, out var target);
            Assert.That(world.TryStartSkill(target, 1, actor), Is.True);
            world.Step();
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.MaxHealth, 25f), Is.True);
            Assert.That(world.TryAddAttributeBonus(actor, CombatAttributeKind.DodgeChance, 0.1f), Is.True);
            var higher = new CombatActorSpec { MaxHealth = 200f, Attack = 30f, CriticalDamage = 1.5f,
                DodgeChance = 0.7f, MoveSpeed = 4f, AttackRange = 5f };
            Assert.That(world.TryPreviewBaseAttributes(actor, higher, out var preview), Is.True);
            Assert.That(preview.MaxHealth, Is.EqualTo(225f));
            Assert.That(preview.DodgeChance, Is.EqualTo(0.75f));
            Assert.That(world.TryReplaceBaseAttributes(actor, higher), Is.True);
            Assert.That(world.TryGetActor(actor, out var equipped), Is.True);
            Assert.That(equipped.Health, Is.EqualTo(105f));
            Assert.That(equipped.Attack, Is.EqualTo(30f));
            var lower = new CombatActorSpec { MaxHealth = 60f, CriticalDamage = 1.5f };
            Assert.That(world.TryReplaceBaseAttributes(actor, lower), Is.True);
            Assert.That(world.TryGetActor(actor, out equipped), Is.True);
            Assert.That(equipped.MaxHealth, Is.EqualTo(85f));
            Assert.That(equipped.Health, Is.EqualTo(85f));
            Assert.That(equipped.DodgeChance, Is.EqualTo(0.1f).Within(0.0001f));
            Assert.That(world.TryReplaceBaseAttributes(actor, new CombatActorSpec { MaxHealth = float.NaN }), Is.False);
            world.Dispose();
        }

        /// <summary>
        /// 施法中换装保留本次进攻和命中范围快照，下一次施法使用新值且冷却不重置。
        /// </summary>
        [Test]
        public void ReplaceBaseAttributes_DuringCastKeepsSnapshotAndCooldown()
        {
            var catalog = new CombatCatalog { InitialAttackRange = 2f };
            var skill = new CombatSkillDefinition
            {
                CombatSkillId = 1, DurationTicks = 3, CooldownTicks = 5, StartRange = 2f, AttackRangeSource = true
            };
            var node = new CombatTimelineNode { Tick = 2 };
            node.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, AttackCoefficient = 1f, Radius = 2f,
                AttackRangeSource = true, CanCritical = false, CanDodge = false
            });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            var world = new CombatWorld(catalog, 8);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, Attack = 10f, CriticalDamage = 1.5f, Radius = 0.3f,
                AttackRange = 2f, BasicSkillId = 1
            });
            var enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.right * 3f,
                MaxHealth = 100f, Radius = 0.3f
            });
            var longRange = new CombatActorSpec { MaxHealth = 100f, Attack = 20f, CriticalDamage = 1.5f, AttackRange = 4f };
            var shortRange = new CombatActorSpec { MaxHealth = 100f, Attack = 5f, CriticalDamage = 1.5f, AttackRange = 1f };
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.False);
            Assert.That(world.TryReplaceBaseAttributes(player, longRange), Is.True);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.False);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            Assert.That(world.TryPreviewBaseAttributes(player, shortRange, out var preview), Is.True);
            Assert.That(preview.Attack, Is.EqualTo(5f));
            Assert.That(world.TryReplaceBaseAttributes(player, shortRange), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var hit), Is.True);
            Assert.That(hit.Health, Is.EqualTo(80f));
            world.Step();
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.False);
            world.Step();
            world.Step();
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.False);
            Assert.That(world.TryReplaceBaseAttributes(player,
                new CombatActorSpec { MaxHealth = 100f, Attack = 5f, CriticalDamage = 1.5f, AttackRange = 4f }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, enemy), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out hit), Is.True);
            Assert.That(hit.Health, Is.EqualTo(75f));
            world.Dispose();
        }

        /// <summary>
        /// 已发射飞弹的到达伤害沿用施放时的攻击范围，不受后续换装影响。
        /// </summary>
        [Test]
        public void ProjectileArrival_UsesCastRangeSnapshotAfterEquipmentChange()
        {
            var catalog = new CombatCatalog { InitialAttackRange = 2f };
            var skill = new CombatSkillDefinition
            {
                CombatSkillId = 1, DurationTicks = 2, StartRange = 2f, AttackRangeSource = true
            };
            var node = new CombatTimelineNode { Tick = 1 };
            node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, LinkedId = 1 });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            var projectile = new CombatProjectileDefinition
            {
                ProjectileId = 1, Motion = ProjectileMotionKind.FixedEndpointArc,
                Target = ProjectileTargetKind.PointSnapshot, FlightTicks = 4, Speed = 10f,
                MaxDistance = 10f, Radius = 0.1f
            };
            projectile.OnArrive.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 10f, Radius = 2f, AttackRangeSource = true,
                CanCritical = false, CanDodge = false
            });
            catalog.Projectiles.Add(1, projectile);
            var world = new CombatWorld(catalog, 9);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 30f, 20f));
            var player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, Radius = 0.3f, AttackRange = 2f, BasicSkillId = 1
            });
            var enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.forward * 7f,
                MaxHealth = 100f, Radius = 0.3f
            });
            Assert.That(world.TryReplaceBaseAttributes(player,
                new CombatActorSpec { MaxHealth = 100f, AttackRange = 4f }), Is.True);
            Assert.That(world.TryStartSkill(player, 1, default), Is.True);
            world.Step();
            world.Step();
            Assert.That(world.TryReplaceBaseAttributes(player,
                new CombatActorSpec { MaxHealth = 100f, AttackRange = 1f }), Is.True);
            world.Step();
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out var hit), Is.True);
            Assert.That(hit.Health, Is.EqualTo(90f));
            Assert.That(world.TryReplaceBaseAttributes(player,
                new CombatActorSpec { MaxHealth = 100f, AttackRange = 4f }), Is.True);
            Assert.That(world.TrySpawnProjectile(player, 1, default, Vector3.forward * 4f, Vector3.forward), Is.True);
            Assert.That(world.TryReplaceBaseAttributes(player,
                new CombatActorSpec { MaxHealth = 100f, AttackRange = 1f }), Is.True);
            world.Step();
            world.Step();
            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(enemy, out hit), Is.True);
            Assert.That(hit.Health, Is.EqualTo(80f));
            world.Dispose();
        }

        /// <summary>
        /// 创建一项可定时执行的属性或伤害效果。
        /// </summary>
        /// <param name="value">效果固定值。</param>
        /// <param name="tick">时间轴执行帧。</param>
        /// <param name="kind">效果类型。</param>
        /// <returns>含一项技能的配置。</returns>
        private static CombatCatalog DamageCatalog(float value, int tick, CombatEffectKind kind)
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = tick + 1, StartRange = 3f };
            var node = new CombatTimelineNode { Tick = tick };
            node.Effects.Add(new CombatEffectDefinition
            {
                Kind = kind, Attribute = CombatAttributeKind.Attack, Value = value,
                AttackCoefficient = kind == CombatEffectKind.Damage ? 1f : 0f,
                CanCritical = false, CanDodge = false, Radius = 3f
            });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            return catalog;
        }

        /// <summary>
        /// 创建相邻且无自动攻击的两个角色。
        /// </summary>
        /// <param name="catalog">战斗效果配置。</param>
        /// <param name="actor">第一名角色。</param>
        /// <param name="target">敌对角色。</param>
        /// <returns>可推进的战斗世界。</returns>
        private static CombatWorld CreateWorld(CombatCatalog catalog, out CombatEntityRef actor, out CombatEntityRef target)
        {
            var world = new CombatWorld(catalog, 4);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            actor = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, Position = Vector3.zero,
                MaxHealth = 100f, Attack = 10f, Radius = 0.3f
            });
            target = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = Vector3.right,
                MaxHealth = 100f, Radius = 0.3f
            });
            Assert.That(actor.Id, Is.GreaterThan(0));
            Assert.That(target.Id, Is.GreaterThan(0));
            return world;
        }
    }
}
