using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 敌人多技能选择、冷却与施法互斥测试。
    /// </summary>
    public sealed class CombatEnemySkillTests
    {
        /// <summary>
        /// 验证每个技能独立冷却且进行中施法保持互斥。
        /// </summary>
        [Test]
        public void ManualSkill_UsesIndependentCooldownAndExclusiveCast()
        {
            var world = CreateWorld(out var enemy, out var player, 5);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.False);
            world.Step();
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.False);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
            world.Step();
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.False);
            for (var i = 0; i < 3; i++) world.Step();
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
        }

        /// <summary>
        /// 验证非活跃房间不会消耗敌人的独立技能冷却。
        /// </summary>
        [Test]
        public void EnemyCooldown_FreezesOutsideActiveRoom()
        {
            var world = CreateWorld(out var enemy, out var player, 5);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.True);
            world.Step();
            Assert.That(world.SetActiveRoom(2, player), Is.True);
            for (var i = 0; i < 10; i++) world.Step();
            Assert.That(world.SetActiveRoom(1, player), Is.True);
            Assert.That(world.TryStartSkill(enemy, 1, player), Is.False);
            Assert.That(world.TryStartSkill(enemy, 2, player), Is.True);
        }

        /// <summary>
        /// 验证远近距离切换、生命阶段选择及冷却期间不回退基础技能。
        /// </summary>
        [Test]
        public void AutoSkill_UsesDistanceHealthAndNoBasicFallback()
        {
            var world = CreateWorld(out var enemy, out var player, 8, new[]
            {
                Rule(1, 1, 5, 0f, 1f, 0f, 2f), Rule(2, 1, 5, 0f, 1f, 2f, 7f),
                Rule(3, 2, 1, 0f, 0.5f, 0f, 7f)
            });
            world.Step();
            Assert.That(Cues(world), Is.EqualTo(new[] { 202 }));
            for (var i = 0; i < 3; i++) world.Step();
            Assert.That(Cues(world), Is.Empty);
            Assert.That(world.TrySetPosition(player, new Vector3(4f, 0f, 0f)), Is.True);
            world.Step();
            Assert.That(Cues(world), Is.EqualTo(new[] { 201 }));
            Assert.That(world.TryStartSkill(player, 9, enemy), Is.True);
            world.Step();
            Cues(world);
            Assert.That(world.TryGetActor(enemy, out var damaged), Is.True);
            Assert.That(damaged.Health, Is.LessThan(50f));
            world.Step();
            Assert.That(Cues(world), Is.EqualTo(new[] { 203 }));
        }

        /// <summary>
        /// 验证世界创建角色时复制技能条件，调用方后续改动不影响本局。
        /// </summary>
        [Test]
        public void ActorSpec_CopiesEnemySkillRules()
        {
            var rules = new[] { Rule(1, 1, 1, 0f, 1f, 0f, 7f) };
            var world = CreateWorld(out _, out _, 4, rules);
            rules[0] = Rule(2, 1, 1, 0f, 1f, 0f, 7f);
            world.Step();
            Assert.That(Cues(world), Is.EqualTo(new[] { 201 }));
        }

        /// <summary>
        /// 验证仅有远程技能时，目标贴近不会让敌人停止移动。
        /// </summary>
        [Test]
        public void AutoSkill_TooCloseToMinDistance_Retreats()
        {
            var world = CreateWorld(out var enemy, out var player, 4,
                new[] { Rule(2, 1, 1, 0f, 1f, 2f, 7f) });
            Assert.That(world.TrySetPosition(player, new Vector3(4f, 0f, 0f)), Is.True);
            world.Step();
            Assert.That(Cues(world), Is.Empty);
            for (var i = 0; i < 5; i++) world.Step();
            Assert.That(world.TryGetActor(enemy, out var moved), Is.True);
            Assert.That(moved.Position.x, Is.GreaterThan(5f));
        }

        /// <summary>
        /// 验证相同种子和状态序列产生相同的同优先级加权选择。
        /// </summary>
        [Test]
        public void WeightedChoice_IsDeterministicForSeed()
        {
            var rules = new[] { Rule(1, 1, 1, 0f, 1f, 0f, 7f), Rule(2, 1, 3, 0f, 1f, 0f, 7f) };
            var left = CreateWorld(out _, out _, 19, rules);
            var right = CreateWorld(out _, out _, 19, rules);
            var leftCues = new List<int>();
            var rightCues = new List<int>();
            for (var i = 0; i < 20; i++)
            {
                left.Step();
                right.Step();
                leftCues.AddRange(Cues(left));
                rightCues.AddRange(Cues(right));
            }
            Assert.That(leftCues, Is.EqualTo(rightCues));
            Assert.That(leftCues.Count, Is.GreaterThan(1));
        }

        /// <summary>
        /// 创建用于测试的技能条件。
        /// </summary>
        /// <param name="id">技能编号。</param>
        /// <param name="priority">选择优先级。</param>
        /// <param name="weight">同级权重。</param>
        /// <param name="minHealth">最低生命比例。</param>
        /// <param name="maxHealth">最高生命比例。</param>
        /// <param name="minDistance">最近距离。</param>
        /// <param name="maxDistance">最远距离。</param>
        /// <returns>技能选择条件。</returns>
        private static CombatEnemySkillDefinition Rule(int id, int priority, int weight, float minHealth, float maxHealth,
            float minDistance, float maxDistance) => new CombatEnemySkillDefinition
        {
            SkillId = id, Priority = priority, Weight = weight, MinHealthRatio = minHealth, MaxHealthRatio = maxHealth,
            MinDistance = minDistance, MaxDistance = maxDistance
        };

        /// <summary>
        /// 创建只有 Cue 和测试伤害的战斗世界。
        /// </summary>
        /// <param name="enemy">创建后的敌人引用。</param>
        /// <param name="player">创建后的玩家引用。</param>
        /// <param name="seed">战斗随机种子。</param>
        /// <param name="rules">敌人自动技能条件。</param>
        /// <returns>已经创建角色的战斗世界。</returns>
        private static CombatWorld CreateWorld(out CombatEntityRef enemy, out CombatEntityRef player, int seed,
            CombatEnemySkillDefinition[] rules = null)
        {
            var catalog = new CombatCatalog();
            for (var id = 1; id <= 3; id++)
            {
                var skill = new CombatSkillDefinition { CombatSkillId = id, DurationTicks = 1, CooldownTicks = 5, StartRange = 8f };
                var node = new CombatTimelineNode { Tick = 0 };
                node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 200 + id });
                skill.Nodes.Add(node);
                catalog.Skills.Add(id, skill);
            }
            var damage = new CombatSkillDefinition { CombatSkillId = 9, DurationTicks = 1, StartRange = 8f };
            var hit = new CombatTimelineNode { Tick = 1 };
            hit.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 209, Value = 60f, Radius = 8f });
            damage.Nodes.Add(hit);
            catalog.Skills.Add(9, damage);
            var world = new CombatWorld(catalog, seed);
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            player = world.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = Vector3.zero,
                MaxHealth = 100f, Radius = 0.3f
            });
            enemy = world.CreateActor(new CombatActorSpec
            {
                ActorId = 2, RoomId = 1, Team = 2, Position = new Vector3(5f, 0f, 0f),
                MaxHealth = 100f, MoveSpeed = 4f, Radius = 0.3f, AggroRange = 8f, BasicSkillId = rules == null ? 0 : 1, EnemySkills = rules
            });
            Assert.That(player.Id, Is.Not.Zero);
            Assert.That(enemy.Id, Is.Not.Zero);
            return world;
        }

        /// <summary>
        /// 读取并清空世界产生的技能 Cue 事件。
        /// </summary>
        /// <param name="world">待读取战斗世界。</param>
        /// <returns>本次技能 Cue 的定义编号。</returns>
        private static List<int> Cues(CombatWorld world)
        {
            var events = new List<CombatEvent>();
            world.DrainEvents(events);
            var result = new List<int>();
            foreach (var item in events)
                if (item.Kind == CombatEventKind.Cue) result.Add(item.DefinitionId);
            return result;
        }
    }
}
