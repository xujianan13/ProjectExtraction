using System.Collections.Generic;
using System.Reflection;
using GameLogic.Gameplay.Combat;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests.Editor.Combat
{
    /// <summary>
    /// 完整静态导航路线、合法占位及缓存失效测试。
    /// </summary>
    public sealed class CombatNavigationTests
    {
        private CombatWorld world;
        private CombatEntityRef player;
        private readonly List<Vector3> path = new List<Vector3>();

        /// <summary>
        /// 创建具有明确边界与半径的测试世界。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            world = new CombatWorld(new CombatCatalog());
            Assert.That(world.SetRoomBounds(1, new Rect(-8f, -8f, 16f, 16f)), Is.True);
            player = world.CreateActor(CreateActor(1, new Vector3(-6f, 2f, 0f), 0.5f));
            Assert.That(player.Id, Is.GreaterThan(0));
            path.Clear();
        }

        /// <summary>
        /// 释放每个测试的战斗世界。
        /// </summary>
        [TearDown]
        public void TearDown() => world.Dispose();

        /// <summary>
        /// 多个交错矩形要求连续转折，输出仍包含起终点且始终保持角色高度。
        /// </summary>
        [Test]
        public void Path_AroundMultipleWalls_ContainsCompleteSafeRoute()
        {
            var walls = new[] { new Rect(-3f, -7f, 1f, 8f), new Rect(1f, -1f, 1f, 8f) };
            foreach (var wall in walls) Assert.That(world.AddObstacle(1, wall), Is.True);
            var destination = new Vector3(6f, 100f, 0f);
            Assert.That(world.TryFindNavigationPath(player, destination, 0.08f, path), Is.True);
            Assert.That(path.Count, Is.GreaterThanOrEqualTo(4));
            Assert.That(path[0], Is.EqualTo(new Vector3(-6f, 2f, 0f)));
            Assert.That(path[path.Count - 1], Is.EqualTo(new Vector3(6f, 2f, 0f)));
            AssertSafeRoute(path, walls, 0.5f);
            var first = path.ToArray();
            var graph = GetCachedGraph();
            Assert.That(world.TryFindNavigationPath(player, destination, 0.08f, path), Is.True);
            Assert.That(path, Is.EqualTo(first));
            Assert.That(GetCachedGraph(), Is.SameAs(graph));
        }

        /// <summary>
        /// 合法地图边界可作为目的地，越界与障碍内部目的地必须清空旧路线。
        /// </summary>
        [Test]
        public void Destination_ValidBoundarySucceeds_InvalidPlacementClearsPath()
        {
            Assert.That(world.TryFindNavigationPath(player, new Vector3(7.5f, -10f, 0f), 0f, path), Is.True);
            Assert.That(path.Count, Is.EqualTo(2));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(7.501f, 0f, 0f), 0f, path), Is.False);
            Assert.That(path, Is.Empty);
            Assert.That(world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f)), Is.True);
            path.Add(Vector3.one);
            Assert.That(world.TryFindNavigationPath(player, Vector3.zero, 0.08f, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 已到达目标时仍返回两端点，保证调用方按完整路线契约处理。
        /// </summary>
        [Test]
        public void Destination_AlreadyReachedStillIncludesBothEndpoints()
        {
            var position = new Vector3(-6f, 2f, 0f);
            Assert.That(world.TryFindNavigationPath(player, position, 0f, path), Is.True);
            Assert.That(path, Is.EqualTo(new[] { position, position }));
        }

        /// <summary>
        /// 贴墙的合法起终点可以接近、沿切面移动及离开接触面。
        /// </summary>
        [Test]
        public void ExactWallContact_AllowsLegalArrivalTangentAndDeparture()
        {
            var wall = new Rect(-0.5f, -2f, 1f, 4f);
            world.AddObstacle(1, wall);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(-1f, 0f, 0f), 0f, path), Is.True);
            Assert.That(world.TrySetPosition(player, path[path.Count - 1]), Is.True);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(-1f, 0f, 1f), 0f, path), Is.True);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(-6f, 0f, 0f), 0f, path), Is.True);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0f, path), Is.True);
            AssertSafeRoute(path, new[] { wall }, 0.5f);
        }

        /// <summary>
        /// 非有限目的地任一分量均被拒绝，并清空调用方旧路线。
        /// </summary>
        /// <param name="x">目的地横坐标。</param>
        /// <param name="y">目的地高度。</param>
        /// <param name="z">目的地纵坐标。</param>
        [TestCase(float.NaN, 0f, 0f)]
        [TestCase(float.PositiveInfinity, 0f, 0f)]
        [TestCase(0f, float.NaN, 0f)]
        [TestCase(0f, float.NegativeInfinity, 0f)]
        [TestCase(0f, 0f, float.NaN)]
        [TestCase(0f, 0f, float.PositiveInfinity)]
        public void Destination_NonFiniteCoordinatesFail(float x, float y, float z)
        {
            path.Add(Vector3.one);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(x, y, z), 0.08f, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 间距必须有限且非负，即使目标可以直达也不能绕过校验。
        /// </summary>
        /// <param name="clearance">无效角点间距。</param>
        [TestCase(-0.01f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Clearance_InvalidValueFails(float clearance)
        {
            path.Add(Vector3.one);
            Assert.That(world.TryFindNavigationPath(player, Vector3.zero, clearance, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 已经失效、属于其它世界或非活跃地图的角色不能申请路线。
        /// </summary>
        [Test]
        public void Actor_InvalidForeignInactiveAndDisposedReferencesFail()
        {
            Assert.That(world.TryFindNavigationPath(default, Vector3.zero, 0f, path), Is.False);
            var other = new CombatWorld(new CombatCatalog());
            var foreign = other.CreateActor(CreateActor(1, Vector3.zero, 0.5f));
            Assert.That(world.TryFindNavigationPath(foreign, Vector3.zero, 0f, path), Is.False);
            other.Dispose();
            var inactive = CreateActor(2, Vector3.zero, 0.5f);
            inactive.RoomId = 2;
            var actor = world.CreateActor(inactive);
            Assert.That(actor.Id, Is.GreaterThan(0));
            Assert.That(world.TryFindNavigationPath(actor, Vector3.right, 0f, path), Is.False);
            world.Dispose();
            path.Add(Vector3.one);
            Assert.That(world.TryFindNavigationPath(player, Vector3.zero, 0f, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 即使实体仍可读取，死亡角色也不能继续申请导航路线。
        /// </summary>
        [Test]
        public void Actor_DeadReferenceFails()
        {
            world.Dispose();
            var catalog = new CombatCatalog();
            catalog.Buffs.Add(1, new CombatBuffDefinition
            {
                BuffId = 1, DurationTicks = 2, IntervalTicks = 1, MaxStacks = 1, PeriodicDamagePerStack = 200f
            });
            world = new CombatWorld(catalog);
            world.SetRoomBounds(1, new Rect(-8f, -8f, 16f, 16f));
            player = world.CreateActor(CreateActor(1, new Vector3(-6f, 2f, 0f), 0.5f));
            Assert.That(world.ApplyBuff(player, 1, 2), Is.True);
            world.Step();
            Assert.That(world.TryGetActor(player, out var state), Is.True);
            Assert.That(state.Dead, Is.True);
            path.Add(Vector3.one);
            Assert.That(world.TryFindNavigationPath(player, Vector3.zero, 0f, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 动态角色不进入静态图，起终点及经过其它角色均可查询。
        /// </summary>
        [Test]
        public void DynamicActor_DoesNotBlockStaticRoute()
        {
            var other = world.CreateActor(CreateActor(2, new Vector3(3f, 2f, 0f), 0.5f));
            Assert.That(other.Id, Is.GreaterThan(0));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(3f, 0f, 0f), 0f, path), Is.True);
            Assert.That(path.Count, Is.EqualTo(2));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0f, path), Is.True);
            Assert.That(path.Count, Is.EqualTo(2));
        }

        /// <summary>
        /// 完全封住地图的障碍导致不可达；地图变化后非法起点也会被拒绝。
        /// </summary>
        [Test]
        public void UnreachableBarrierAndInvalidatedStart_ClearPath()
        {
            world.AddObstacle(1, new Rect(-0.5f, -8f, 1f, 16f));
            path.Add(Vector3.one);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.False);
            Assert.That(path, Is.Empty);
            world.AddObstacle(1, new Rect(-7f, -1f, 2f, 2f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(-6f, 0f, 3f), 0f, path), Is.False);
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 宽度不足以容纳较大角色的通道不能复用小角色的可见图。
        /// </summary>
        [Test]
        public void ActorRadius_NarrowPassageRejectsLargerActor()
        {
            var wall = new Rect(-0.5f, -7.2f, 1f, 14.4f);
            world.AddObstacle(1, wall);
            var small = world.CreateActor(CreateActor(2, new Vector3(-6f, 2f, 2f), 0.2f));
            Assert.That(world.TryFindNavigationPath(small, new Vector3(6f, 0f, 2f), 0.08f, path), Is.True);
            AssertSafeRoute(path, new[] { wall }, 0.2f);
            var graph = GetCachedGraph();
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.False);
            Assert.That(GetCachedGraph(), Is.Not.SameAs(graph));
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 同一障碍列表追加封路几何后，缓存必须失效并报告不可达。
        /// </summary>
        [Test]
        public void Geometry_AppendingObstacleInvalidatesCache()
        {
            world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.True);
            var graph = GetCachedGraph();
            world.AddObstacle(1, new Rect(-0.5f, -8f, 1f, 16f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.False);
            Assert.That(GetCachedGraph(), Is.Not.SameAs(graph));
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 通过替换或原位重建同数量障碍列表模拟地图几何更新，避免仅引用或数量命中的旧缓存。
        /// </summary>
        /// <param name="replaceList">是否替换列表对象；否则原位清空并重建。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void Geometry_SameCountRebuildInvalidatesCache(bool replaceList)
        {
            world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.True);
            var graph = GetCachedGraph();
            var field = typeof(CombatWorld).GetField("obstacles", BindingFlags.NonPublic | BindingFlags.Instance);
            var rooms = (Dictionary<int, List<Rect>>)field.GetValue(world);
            var wall = new Rect(-0.5f, -3f, 1f, 6f);
            if (replaceList) rooms[1] = new List<Rect> { wall };
            else { rooms[1].Clear(); rooms[1].Add(wall); }
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.True);
            Assert.That(GetCachedGraph(), Is.Not.SameAs(graph));
            Assert.That(path.Exists(point => Mathf.Abs(point.z) >= 3.58f), Is.True);
            AssertSafeRoute(path, new[] { wall }, 0.5f);
        }

        /// <summary>
        /// 调整角点间距或地图边界后不能继续使用旧角点图。
        /// </summary>
        [Test]
        public void ClearanceAndBounds_ChangesInvalidateCache()
        {
            world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.True);
            var graph = GetCachedGraph();
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.4f, path), Is.True);
            Assert.That(GetCachedGraph(), Is.Not.SameAs(graph));
            Assert.That(path.Exists(point => Mathf.Abs(point.z) >= 2.9f), Is.True);
            graph = GetCachedGraph();
            world.SetRoomBounds(1, new Rect(-8f, -2f, 16f, 4f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.4f, path), Is.False);
            Assert.That(GetCachedGraph(), Is.Not.SameAs(graph));
            Assert.That(path, Is.Empty);
        }

        /// <summary>
        /// 同半径角色换图后，导航图应使用新地图的几何。
        /// </summary>
        [Test]
        public void ActiveRoom_TransferBuildsNewGeometry()
        {
            world.AddObstacle(1, new Rect(-0.5f, -2f, 1f, 4f));
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.True);
            var graph = GetCachedGraph();
            world.SetRoomBounds(2, new Rect(-8f, -8f, 16f, 16f));
            var wall = new Rect(-0.5f, -3f, 1f, 6f);
            world.AddObstacle(2, wall);
            Assert.That(world.TryTransferActor(player, 2, new Vector3(-6f, 2f, 0f)), Is.True);
            Assert.That(world.TryFindNavigationPath(player, new Vector3(6f, 0f, 0f), 0.08f, path), Is.True);
            Assert.That(GetCachedGraph(), Is.Not.SameAs(graph));
            AssertSafeRoute(path, new[] { wall }, 0.5f);
        }

        /// <summary>
        /// 创建仅参与移动与占位的测试角色配置。
        /// </summary>
        /// <param name="id">角色编号。</param>
        /// <param name="position">角色初始坐标。</param>
        /// <param name="radius">占位半径。</param>
        /// <returns>无自动攻击的角色配置。</returns>
        private static CombatActorSpec CreateActor(int id, Vector3 position, float radius) => new CombatActorSpec
        {
            ActorId = id, RoomId = 1, Team = 1, IsPlayer = id == 1, Position = position, MaxHealth = 100f, Radius = radius
        };

        /// <summary>
        /// 读取缓存对象，以验证重复查询复用和几何更新失效。
        /// </summary>
        /// <returns>最近一次查询使用的静态图。</returns>
        private object GetCachedGraph() => typeof(CombatWorld).GetField("navigationGraph", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(world);

        /// <summary>
        /// 沿每一段密集采样，独立验证整个角色足迹不进入障碍且路线高度一致。
        /// </summary>
        /// <param name="route">完整路线。</param>
        /// <param name="walls">独立测试障碍数据。</param>
        /// <param name="radius">测试角色半径。</param>
        private static void AssertSafeRoute(List<Vector3> route, Rect[] walls, float radius)
        {
            for (var segment = 1; segment < route.Count; segment++)
                for (var sample = 0; sample <= 200; sample++)
                {
                    var point = Vector3.Lerp(route[segment - 1], route[segment], sample / 200f);
                    Assert.That(point.y, Is.EqualTo(2f));
                    foreach (var wall in walls)
                    {
                        var overlaps = point.x + radius > wall.xMin + 0.00001f && point.x - radius < wall.xMax - 0.00001f &&
                            point.z + radius > wall.yMin + 0.00001f && point.z - radius < wall.yMax - 0.00001f;
                        Assert.That(overlaps, Is.False, $"Route segment {segment}, sample {sample} enters wall {wall} at {point}.");
                    }
                }
        }
    }
}
