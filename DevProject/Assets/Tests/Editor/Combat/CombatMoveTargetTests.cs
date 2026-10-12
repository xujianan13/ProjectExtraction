using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 验证导航路点仍受固定帧移速、连续碰撞和战斗位移约束。
    /// </summary>
    public sealed class CombatMoveTargetTests
    {
        private CombatWorld world;

        /// <summary>
        /// 释放测试创建的战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown() => world?.Dispose();

        /// <summary>
        /// 减速到期导致帧内速度骤增时仍精确停在路点，之后普通输入立即接管。
        /// </summary>
        [Test]
        public void Waypoint_ClampsAfterBuffExpiryAndManualInputTakesOver()
        {
            var catalog = new CombatCatalog();
            catalog.Buffs.Add(1, new CombatBuffDefinition
            {
                BuffId = 1, DurationTicks = 1, IntervalTicks = 10, MaxStacks = 1, MoveSpeedAddPerStack = -599f
            });
            world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            CombatEntityRef player = world.CreateActor(Spec(1, Vector3.zero, 600f));
            Assert.That(world.ApplyBuff(player, 1, 1), Is.True);
            Assert.That(world.SetNavigationWaypoint(player, new Vector3(0.02f, 0f, 0f)), Is.True);
            world.Advance(CombatWorld.TickSeconds * 8f);
            Assert.That(world.TryGetActor(player, out CombatActorSnapshot arrived), Is.True);
            Assert.That(arrived.Position.x, Is.EqualTo(0.02f).Within(0.00001f));
            Assert.That(arrived.Position.z, Is.Zero);
            Assert.That(world.SetMove(player, Vector2.up), Is.True);
            world.Step();
            world.TryGetActor(player, out CombatActorSnapshot manual);
            Assert.That(manual.Position.x, Is.EqualTo(arrived.Position.x).Within(0.00001f));
            Assert.That(manual.Position.z, Is.GreaterThan(9f));
        }

        /// <summary>
        /// 导航路点输入不能穿过静态墙体或存活角色的碰撞占位。
        /// </summary>
        /// <param name="wall">是否使用墙体，假值使用角色阻挡。</param>
        [TestCase(true)]
        [TestCase(false)]
        public void Waypoint_RespectsContinuousCollision(bool wall)
        {
            world = new CombatWorld(new CombatCatalog());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            CombatEntityRef player = world.CreateActor(Spec(1, Vector3.zero, 600f));
            if (wall) world.AddObstacle(1, new Rect(2.5f, -2f, 1f, 4f));
            else Assert.That(world.CreateActor(Spec(2, new Vector3(3f, 0f, 0f), 0f)).Id, Is.GreaterThan(0));
            Assert.That(world.SetNavigationWaypoint(player, new Vector3(6f, 0f, 0f)), Is.True);
            world.Advance(CombatWorld.TickSeconds * 5f);
            world.TryGetActor(player, out CombatActorSnapshot blocked);
            Assert.That(blocked.Position.x, Is.InRange(1.99f, 2.001f));
            Assert.That(blocked.Position.z, Is.Zero);
        }

        /// <summary>
        /// 战斗位移暂停普通路点移动，换区后清除临时路点避免继续走向旧地图坐标。
        /// </summary>
        [Test]
        public void Waypoint_YieldsToDisplacementAndDoesNotSurviveTransfer()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 1, DurationTicks = 4, StartRange = 0f };
            var node = new CombatTimelineNode { Tick = 1 };
            node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Displace, Value = 3f, DurationTicks = 2 });
            skill.Nodes.Add(node);
            catalog.Skills.Add(1, skill);
            world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.SetRoomBounds(2, new Rect(-10f, -10f, 20f, 20f));
            CombatEntityRef player = world.CreateActor(Spec(1, Vector3.zero, 6f));
            Assert.That(world.TryStartSkill(player, 1, default), Is.True);
            Assert.That(world.SetNavigationWaypoint(player, new Vector3(5f, 0f, 0f)), Is.True);
            world.Step();
            world.TryGetActor(player, out CombatActorSnapshot beforeDisplacement);
            Assert.That(world.TryGetMovementStatus(player, out float speed, out bool displaced), Is.True);
            Assert.That(speed, Is.EqualTo(6f));
            Assert.That(displaced, Is.True);
            world.Step();
            world.TryGetActor(player, out CombatActorSnapshot duringDisplacement);
            Assert.That(duringDisplacement.Position.x, Is.EqualTo(beforeDisplacement.Position.x).Within(0.0001f));
            Assert.That(duringDisplacement.Position.z, Is.GreaterThan(beforeDisplacement.Position.z));
            Vector3 entrance = new Vector3(-2f, 0f, -2f);
            Assert.That(world.TryTransferActor(player, 2, entrance), Is.True);
            world.Advance(CombatWorld.TickSeconds * 10f);
            world.TryGetActor(player, out CombatActorSnapshot transferred);
            Assert.That(transferred.Position, Is.EqualTo(entrance));
            Assert.That(world.TryGetMovementStatus(player, out _, out displaced), Is.True);
            Assert.That(displaced, Is.False);
        }

        /// <summary>
        /// 创建没有自动攻击的测试角色。
        /// </summary>
        /// <param name="id">业务角色编号，一号为玩家。</param>
        /// <param name="position">起始位置。</param>
        /// <param name="speed">基础移动速度。</param>
        /// <returns>合法角色配置。</returns>
        private static CombatActorSpec Spec(int id, Vector3 position, float speed) => new CombatActorSpec
        {
            ActorId = id, RoomId = 1, Team = id == 1 ? 1 : 2, IsPlayer = id == 1,
            Position = position, MaxHealth = 100f, MoveSpeed = speed, Radius = 0.5f
        };
    }
}
