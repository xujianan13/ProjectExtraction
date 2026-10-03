using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 玩家近战索敌的遮挡筛选及敌人绕障追击回归测试。
    /// </summary>
    public sealed class CombatTargetSelectionTests
    {
        private CombatWorld world;

        /// <summary>
        /// 每个用例结束后释放战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            world?.Dispose();
            world = null;
        }

        /// <summary>
        /// 近处目标被墙遮挡时，玩家仍应攻击范围内可见的较远目标。
        /// </summary>
        [Test]
        public void Player_AttacksVisibleEnemyWhenNearerEnemyIsBlocked()
        {
            world = CreateWorld();
            world.AddObstacle(1, new Rect(0.5f, -0.2f, 0.2f, 0.4f));
            world.CreateActor(CreatePlayer(Vector3.zero));
            var blocked = world.CreateActor(CreateEnemy(2, new Vector3(1.4f, 0f, 0f)));
            var visible = world.CreateActor(CreateEnemy(3, new Vector3(0f, 0f, 2f)));

            world.Step();
            world.Step();
            Assert.That(world.TryGetActor(visible, out var visibleState), Is.True);
            Assert.That(visibleState.Health, Is.LessThan(100f));
            Assert.That(world.TryGetActor(blocked, out var blockedState), Is.True);
            Assert.That(blockedState.Health, Is.EqualTo(100f));
        }

        /// <summary>
        /// 范围内所有目标均被墙遮挡时，玩家不应发起攻击。
        /// </summary>
        [Test]
        public void Player_DoesNotAttackWhenAllEnemiesAreBlocked()
        {
            world = CreateWorld();
            world.AddObstacle(1, new Rect(0.5f, -0.2f, 0.2f, 0.4f));
            world.CreateActor(CreatePlayer(Vector3.zero));
            var blocked = world.CreateActor(CreateEnemy(2, new Vector3(1.4f, 0f, 0f)));

            world.Step();
            var telegraphs = new List<CombatTelegraphSnapshot>();
            world.GetTelegraphsSnapshot(telegraphs);
            Assert.That(telegraphs, Is.Empty);
            for (var i = 0; i < 2; i++) world.Step();

            Assert.That(world.TryGetActor(blocked, out var state), Is.True);
            Assert.That(state.Health, Is.EqualTo(100f));
        }

        /// <summary>
        /// 敌人与玩家隔墙时仍按仇恨范围追击并绕开障碍。
        /// </summary>
        [Test]
        public void Enemy_PursuesPlayerBehindWall()
        {
            world = CreateWorld();
            world.SetRoomBounds(1, new Rect(-8f, -6f, 16f, 12f));
            world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f));
            world.CreateActor(CreatePlayer(new Vector3(3f, 0f, 0f), false));
            var enemySpec = CreateEnemy(2, new Vector3(-3f, 0f, 0f));
            enemySpec.BasicSkillId = 1;
            enemySpec.AttackRange = 1.5f;
            enemySpec.AggroRange = 10f;
            enemySpec.MoveSpeed = 4f;
            var enemy = world.CreateActor(enemySpec);

            var maxAbsZ = 0f;
            for (var i = 0; i < 120; i++)
            {
                world.Step();
                Assert.That(world.TryGetActor(enemy, out var state), Is.True);
                maxAbsZ = Mathf.Max(maxAbsZ, Mathf.Abs(state.Position.z));
            }

            Assert.That(maxAbsZ, Is.GreaterThan(1f));
        }

        /// <summary>
        /// 创建无位移的一帧近战技能及战斗世界。
        /// </summary>
        /// <returns>已设置战斗边界的世界。</returns>
        private static CombatWorld CreateWorld()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 2, CooldownTicks = 30, StartRange = 2.5f };
            var hit = new CombatTimelineNode { Tick = 1 };
            hit.Effects.Add(new CombatEffectDefinition
            {
                Kind = CombatEffectKind.Damage, Value = 15f, Shape = CombatHitShape.Sector,
                Radius = 2.5f, Angle = 100f, Height = 2f, RequireLineOfSight = true
            });
            skill.Nodes.Add(hit);
            catalog.Skills.Add(1, skill);
            var result = new CombatWorld(catalog);
            result.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            return result;
        }

        /// <summary>
        /// 创建测试玩家配置。
        /// </summary>
        /// <param name="position">出生位置。</param>
        /// <param name="canAttack">是否开启自动普攻。</param>
        /// <returns>玩家配置。</returns>
        private static CombatActorSpec CreatePlayer(Vector3 position, bool canAttack = true) => new CombatActorSpec
        {
            ActorId = 1, RoomId = 1, Team = 1, IsPlayer = true, Position = position,
            MaxHealth = 100f, Radius = 0.3f, AttackRange = 2.5f, BasicSkillId = canAttack ? 1 : 0
        };

        /// <summary>
        /// 创建静止的测试敌人配置。
        /// </summary>
        /// <param name="id">角色编号。</param>
        /// <param name="position">出生位置。</param>
        /// <returns>敌人配置。</returns>
        private static CombatActorSpec CreateEnemy(int id, Vector3 position) => new CombatActorSpec
        {
            ActorId = id, RoomId = 1, Team = 2, Position = position, MaxHealth = 100f, Radius = 0.3f
        };
    }
}
