using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 角色连续碰撞、换图及敌人绕障的测试。
    /// </summary>
    public sealed class CombatMovementTests
    {
        /// <summary>
        /// 创建拥有明确半径的测试角色。
        /// </summary>
        /// <param name="id">稳定角色编号。</param>
        /// <param name="room">地图编号。</param>
        /// <param name="team">阵营编号。</param>
        /// <param name="position">初始位置。</param>
        /// <param name="speed">普通移动速度。</param>
        /// <returns>测试角色配置。</returns>
        private static CombatActorSpec Spec(int id, int room, int team, Vector3 position, float speed = 0f) =>
            new CombatActorSpec
            {
                ActorId = id, RoomId = room, Team = team, IsPlayer = id == 1, Position = position,
                MaxHealth = 100f, MoveSpeed = speed, Radius = 0.5f
            };

        /// <summary>
        /// 验证同阵营和敌对阵营均拒绝重叠生成与瞬移，自身原位可设置。
        /// </summary>
        [Test]
        public void Occupancy_RejectsBothTeamsAndAllowsSelf()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(Spec(1, 1, 1, Vector3.zero));
            Assert.That(world.CreateActor(Spec(2, 1, 1, new Vector3(0.9f, 0f, 0f))).Id, Is.Zero);
            Assert.That(world.CreateActor(Spec(3, 1, 2, new Vector3(0.9f, 0f, 0f))).Id, Is.Zero);
            var ally = world.CreateActor(Spec(4, 1, 1, new Vector3(3f, 0f, 0f)));
            Assert.That(world.TrySetPosition(ally, new Vector3(0.9f, 0f, 0f)), Is.False);
            Assert.That(world.TrySetPosition(player, Vector3.zero), Is.True);
        }

        /// <summary>
        /// 验证双向高速移动不能穿过角色，斜向接触仍能沿圆切线滑动。
        /// </summary>
        [Test]
        public void FastMovement_BlocksBothDirectionsAndSlides()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            var player = world.CreateActor(Spec(1, 1, 1, Vector3.zero, 600f));
            var enemy = world.CreateActor(Spec(2, 1, 2, new Vector3(3f, 0f, 0f)));
            world.SetMove(player, Vector2.right);
            world.Step();
            world.TryGetActor(player, out var stopped);
            Assert.That(stopped.Position.x, Is.LessThanOrEqualTo(2.001f));
            Assert.That(stopped.Position.x, Is.GreaterThan(1.9f));
            world.SetMove(player, Vector2.zero);
            Assert.That(world.TrySetPosition(player, new Vector3(6f, 0f, 0f)), Is.True);
            Assert.That(world.TrySetPosition(enemy, Vector3.zero), Is.True);
            world.SetMove(player, Vector2.left);
            world.Step();
            world.TryGetActor(player, out stopped);
            Assert.That(stopped.Position.x, Is.GreaterThanOrEqualTo(1f));
            Assert.That(world.TrySetPosition(player, new Vector3(-3f, 0f, -1f)), Is.True);
            world.SetMove(player, new Vector2(1f, 0.5f));
            world.Step();
            world.TryGetActor(player, out var slid);
            Assert.That(Vector2.Distance(new Vector2(slid.Position.x, slid.Position.z), Vector2.zero), Is.GreaterThanOrEqualTo(0.999f));
            Assert.That(slid.Position.z, Is.GreaterThan(-1f));
        }

        /// <summary>
        /// 验证同一帧相向高速移动的双方不会互相穿透。
        /// </summary>
        [Test]
        public void TwoMovingActors_DoNotCrossInOneTick()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-20f, -20f, 40f, 40f));
            var left = world.CreateActor(Spec(1, 1, 1, new Vector3(-2f, 0f, 0f), 600f));
            var right = world.CreateActor(Spec(2, 1, 2, new Vector3(2f, 0f, 0f), 600f));
            world.SetMove(left, Vector2.right);
            world.SetMove(right, Vector2.left);
            world.Step();
            world.TryGetActor(left, out var leftState);
            world.TryGetActor(right, out var rightState);
            Assert.That(leftState.Position.x, Is.LessThan(rightState.Position.x));
            Assert.That(rightState.Position.x - leftState.Position.x, Is.GreaterThanOrEqualTo(0.999f));
        }
        /// <summary>
        /// 验证高速技能位移不能穿过角色或墙体。
        /// </summary>
        [Test]
        public void SkillDisplacement_SweepsActorAndWall()
        {
            var catalog = new CombatCatalog();
            var skill = new CombatSkillDefinition { CombatSkillId = 77, DurationTicks = 3, StartRange = 0f };
            var node = new CombatTimelineNode { Tick = 1 };
            node.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Displace, Value = 20f, DurationTicks = 1 });
            skill.Nodes.Add(node);
            catalog.Skills.Add(77, skill);
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.AddObstacle(1, new Rect(-2f, 5f, 4f, 0.3f));
            var player = world.CreateActor(Spec(1, 1, 1, Vector3.zero));
            world.CreateActor(Spec(2, 1, 2, new Vector3(0f, 0f, 3f)));
            Assert.That(world.TryStartSkill(player, 77, default), Is.True);
            world.Step();
            world.Step();
            world.TryGetActor(player, out var stopped);
            Assert.That(stopped.Position.z, Is.InRange(1.99f, 2.001f));
            var wallWorld = new CombatWorld(catalog);
            wallWorld.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            wallWorld.AddObstacle(1, new Rect(-2f, 5f, 4f, 0.3f));
            var wallMover = wallWorld.CreateActor(Spec(1, 1, 1, Vector3.zero));
            Assert.That(wallWorld.TryStartSkill(wallMover, 77, default), Is.True);
            wallWorld.Step();
            wallWorld.Step();
            wallWorld.TryGetActor(wallMover, out var wallStopped);
            Assert.That(wallStopped.Position.z, Is.InRange(4.49f, 4.501f));
        }

        /// <summary>
        /// 验证墙角扫掠保持合法占位并允许沿墙滑动。
        /// </summary>
        [Test]
        public void WallCorner_DiagonalMotionStaysOutsideAndSlides()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.AddObstacle(1, new Rect(0f, 0f, 2f, 2f));
            var player = world.CreateActor(Spec(1, 1, 1, new Vector3(-2f, 0f, 0f), 240f));
            world.SetMove(player, new Vector2(1f, 1f));
            world.Step();
            world.TryGetActor(player, out var result);
            Assert.That(world.TrySetPosition(player, result.Position), Is.True);
            Assert.That(result.Position.x, Is.LessThanOrEqualTo(-0.5f));
            Assert.That(result.Position.z, Is.GreaterThan(2.5f));
        }

        /// <summary>
        /// 验证精确贴墙的角色向内移动被阻挡，沿墙方向仍可行走。
        /// </summary>
        [Test]
        public void ExactWallContact_BlocksInwardAndAllowsTangent()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.AddObstacle(1, new Rect(0f, 0f, 2f, 2f));
            var player = world.CreateActor(Spec(1, 1, 1, new Vector3(-0.5f, 0f, 1f), 120f));
            Assert.That(player.Id, Is.GreaterThan(0));
            world.SetMove(player, Vector2.right);
            world.Step();
            world.TryGetActor(player, out var blocked);
            Assert.That(blocked.Position.x, Is.LessThanOrEqualTo(-0.5f));
            world.SetMove(player, Vector2.up);
            world.Step();
            world.TryGetActor(player, out var slid);
            Assert.That(slid.Position.z, Is.GreaterThan(1f));
            Assert.That(slid.Position.x, Is.LessThanOrEqualTo(-0.5f));
        }
        /// <summary>
        /// 验证原子换图失败不改变地图和位置，成功换图保持互不重叠。
        /// </summary>
        [Test]
        public void Transfer_RejectsOccupiedEntranceAtomically()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            world.SetRoomBounds(2, new Rect(-10f, -10f, 20f, 20f));
            var player = world.CreateActor(Spec(1, 1, 1, Vector3.zero));
            world.CreateActor(Spec(2, 2, 2, Vector3.zero));
            Assert.That(world.TryTransferActor(player, 2, Vector3.zero), Is.False);
            Assert.That(world.ActiveRoomId, Is.EqualTo(1));
            world.TryGetActor(player, out var oldState);
            Assert.That(oldState.RoomId, Is.EqualTo(1));
            Assert.That(oldState.Position, Is.EqualTo(Vector3.zero));
            Assert.That(world.TryTransferActor(player, 2, new Vector3(2f, 0f, 0f)), Is.True);
            Assert.That(world.ActiveRoomId, Is.EqualTo(2));
        }

        /// <summary>
        /// 验证死亡角色不再阻挡位置设置与生成。
        /// </summary>
        [Test]
        public void DeadActor_DoesNotBlockPlacement()
        {
            var catalog = ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype();
            catalog.Skills[1].Nodes[2].Effects[0].Value = 200f;
            var world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-10f, -10f, 20f, 20f));
            var playerSpec = Spec(1, 1, 1, Vector3.zero);
            playerSpec.AttackRange = 2.2f;
            playerSpec.BasicSkillId = 1;
            world.CreateActor(playerSpec);
            var enemy = world.CreateActor(Spec(2, 1, 2, new Vector3(1.5f, 0f, 0f)));
            for (var i = 0; i < 20; i++) world.Step();
            world.TryGetActor(enemy, out var dead);
            Assert.That(dead.Dead, Is.True);
            Assert.That(world.CreateActor(Spec(3, 1, 2, dead.Position)).Id, Is.GreaterThan(0));
        }

        /// <summary>
        /// 验证敌人绕开中央障碍接近目标，且不会穿过墙体。
        /// </summary>
        [Test]
        public void Enemy_PathsAroundWall()
        {
            var world = new CombatWorld(ProjectExtraction.Tests.Shared.TestCombatCatalog.CreatePrototype());
            world.SetRoomBounds(1, new Rect(-8f, -6f, 16f, 12f));
            world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f));
            world.CreateActor(Spec(1, 1, 1, new Vector3(3f, 0f, 0f)));
            var enemySpec = Spec(2, 1, 2, new Vector3(-3f, 0f, 0f), 4f);
            enemySpec.BasicSkillId = 1;
            enemySpec.AttackRange = 1.5f;
            enemySpec.AggroRange = 10f;
            var enemy = world.CreateActor(enemySpec);
            var maxAbsZ = 0f;
            for (var i = 0; i < 240; i++)
            {
                world.Step();
                world.TryGetActor(enemy, out var state);
                maxAbsZ = Mathf.Max(maxAbsZ, Mathf.Abs(state.Position.z));
                Assert.That(state.Position.x < -1f || state.Position.x > 1f || Mathf.Abs(state.Position.z) >= 2f, Is.True);
            }
            world.TryGetActor(enemy, out var result);
            Assert.That(maxAbsZ, Is.GreaterThan(2f));
            Assert.That(result.Position.x, Is.GreaterThan(0.5f));
        }
    }
}
