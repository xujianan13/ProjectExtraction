using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 基于矩形障碍角点的确定性导航与追击路径。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 仅保留最近一次主动导航查询使用的静态角点图。
        /// </summary>
        private NavigationGraph navigationGraph;

        /// <summary>
        /// 保存静态几何快照、合法角点及角点之间的可见边。
        /// </summary>
        private sealed class NavigationGraph
        {
            public int RoomId;
            public float Radius;
            public float Clearance;
            public bool HasBounds;
            public Rect Bounds;
            public List<Rect> SourceWalls;
            public List<Rect> Walls;
            public List<Vector3> Corners;
            public bool[,] Visible;
        }

        /// <summary>
        /// 查询当前地图中避开静态障碍的完整 XZ 路线，路径高度统一为角色当前高度。
        /// </summary>
        /// <param name="actor">当前活跃地图中的存活角色。</param>
        /// <param name="destination">有限的目标坐标；高度不参与寻路。</param>
        /// <param name="clearance">障碍角点额外留出的非负间距。</param>
        /// <param name="path">接收起点、转折点与终点的列表；失败时清空，不检查动态角色占位。</param>
        /// <returns>起终点合法且存在可通行路线则为真。</returns>
        public bool TryFindNavigationPath(CombatEntityRef actor, Vector3 destination, float clearance, List<Vector3> path)
        {
            path.Clear();
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) || state.DeadPending ||
                state.Health <= 0f || state.Spec.RoomId != activeRoomId || !IsFinite(state.Position) ||
                !IsFinite(destination) || !IsFinite(clearance) || clearance < 0f || !IsFinite(state.Spec.Radius + clearance)) return false;
            var roomId = state.Spec.RoomId;
            var radius = state.Spec.Radius;
            var start = state.Position;
            destination.y = start.y;
            if (!CanOccupy(roomId, start, radius) || !CanOccupy(roomId, destination, radius)) return false;
            if (NavigationWallClear(roomId, start, destination, radius))
            {
                path.Add(start);
                path.Add(destination);
                return true;
            }
            var graph = GetNavigationGraph(roomId, radius, clearance);
            var nodes = new List<Vector3>(graph.Corners.Count + 2) { start, destination };
            foreach (var corner in graph.Corners) nodes.Add(new Vector3(corner.x, start.y, corner.z));
            if (!TrySolveNavigation(roomId, nodes, radius, graph, out var previous)) return false;
            for (var waypoint = 1; waypoint >= 0; waypoint = previous[waypoint]) path.Add(nodes[waypoint]);
            path.Reverse();
            return true;
        }

        /// <summary>
        /// 为敌人求下一段可通行的追击方向。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="start">敌人当前坐标。</param>
        /// <param name="goal">目标当前坐标。</param>
        /// <param name="radius">敌人占位半径。</param>
        /// <returns>下一段 XZ 平面归一化方向，无路径时返回零。</returns>
        private Vector2 FindPursuitDirection(int roomId, Vector3 start, Vector3 goal, float radius)
        {
            if (WallClear(roomId, start, goal, radius)) return DirectionTo(start, goal);
            if (!obstacles.TryGetValue(roomId, out var walls)) return Vector2.zero;
            var nodes = new List<Vector3> { start, goal };
            AddNavigationCorners(nodes, roomId, walls, radius, 0.08f, start.y);
            if (!TrySolveNavigation(roomId, nodes, radius, null, out var previous)) return Vector2.zero;
            var waypoint = 1;
            while (previous[waypoint] > 0) waypoint = previous[waypoint];
            return DirectionTo(start, nodes[waypoint]);
        }

        /// <summary>
        /// 在起终点与静态角点组成的可见图上运行确定性最短路求解。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="nodes">起点、终点及按固定顺序排列的角点。</param>
        /// <param name="radius">角色占位半径。</param>
        /// <param name="graph">主动导航使用的静态可见图；敌人追击传入空值以保持原扫掠规则。</param>
        /// <param name="previous">每个可达节点的前驱索引。</param>
        /// <returns>终点可达则为真。</returns>
        private bool TrySolveNavigation(int roomId, List<Vector3> nodes, float radius, NavigationGraph graph, out int[] previous)
        {
            var costs = new float[nodes.Count];
            previous = new int[nodes.Count];
            var visited = new bool[nodes.Count];
            for (var i = 0; i < nodes.Count; i++) { costs[i] = float.PositiveInfinity; previous[i] = -1; }
            costs[0] = 0f;
            for (var iteration = 0; iteration < nodes.Count; iteration++)
            {
                var current = -1;
                for (var i = 0; i < nodes.Count; i++)
                    if (!visited[i] && (current < 0 || costs[i] < costs[current])) current = i;
                if (current < 0 || float.IsPositiveInfinity(costs[current]) || current == 1) break;
                visited[current] = true;
                for (var next = 1; next < nodes.Count; next++)
                {
                    if (visited[next]) continue;
                    var visible = graph == null ? WallClear(roomId, nodes[current], nodes[next], radius) :
                        current >= 2 && next >= 2 ? graph.Visible[current - 2, next - 2] :
                        NavigationWallClear(roomId, nodes[current], nodes[next], radius);
                    if (!visible) continue;
                    var candidate = costs[current] + Vector3.Distance(nodes[current], nodes[next]);
                    if (candidate + 0.00001f >= costs[next]) continue;
                    costs[next] = candidate;
                    previous[next] = current;
                }
            }
            return previous[1] >= 0;
        }

        /// <summary>
        /// 复用最近一次静态图，几何、地图、半径或间距改变时重新生成。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="radius">角色占位半径。</param>
        /// <param name="clearance">角点额外间距。</param>
        /// <returns>与当前几何一致的静态导航图。</returns>
        private NavigationGraph GetNavigationGraph(int roomId, float radius, float clearance)
        {
            obstacles.TryGetValue(roomId, out var walls);
            var hasBounds = roomBounds.TryGetValue(roomId, out var bounds);
            var graph = navigationGraph;
            if (graph != null && graph.RoomId == roomId && graph.Radius == radius && graph.Clearance == clearance &&
                graph.HasBounds == hasBounds && graph.Bounds.Equals(bounds) && ReferenceEquals(graph.SourceWalls, walls) &&
                NavigationWallsMatch(graph.Walls, walls)) return graph;
            graph = new NavigationGraph
            {
                RoomId = roomId, Radius = radius, Clearance = clearance, HasBounds = hasBounds, Bounds = bounds,
                SourceWalls = walls, Walls = walls == null ? new List<Rect>() : new List<Rect>(walls), Corners = new List<Vector3>()
            };
            AddNavigationCorners(graph.Corners, roomId, graph.Walls, radius, clearance, 0f);
            graph.Visible = new bool[graph.Corners.Count, graph.Corners.Count];
            for (var i = 0; i < graph.Corners.Count; i++)
                for (var j = i + 1; j < graph.Corners.Count; j++)
                    graph.Visible[i, j] = graph.Visible[j, i] = NavigationWallClear(roomId, graph.Corners[i], graph.Corners[j], radius);
            navigationGraph = graph;
            return graph;
        }

        /// <summary>
        /// 比较障碍快照，识别原列表追加、替换或同数量重建后的几何变化。
        /// </summary>
        /// <param name="snapshot">缓存中的障碍快照。</param>
        /// <param name="walls">当前障碍列表；无障碍时可为空。</param>
        /// <returns>所有障碍几何及顺序一致则为真。</returns>
        private static bool NavigationWallsMatch(List<Rect> snapshot, List<Rect> walls)
        {
            if (walls == null) return snapshot.Count == 0;
            if (snapshot.Count != walls.Count) return false;
            for (var i = 0; i < walls.Count; i++)
                if (!snapshot[i].Equals(walls[i])) return false;
            return true;
        }

        /// <summary>
        /// 按固定顺序加入膨胀障碍矩形的合法角点。
        /// </summary>
        /// <param name="nodes">接收角点的列表。</param>
        /// <param name="roomId">地图编号。</param>
        /// <param name="walls">障碍矩形列表。</param>
        /// <param name="radius">角色占位半径。</param>
        /// <param name="clearance">角点额外间距。</param>
        /// <param name="height">角点高度。</param>
        private void AddNavigationCorners(List<Vector3> nodes, int roomId, List<Rect> walls, float radius, float clearance, float height)
        {
            var gap = radius + clearance;
            foreach (var wall in walls)
            {
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMin - gap, height, wall.yMin - gap), radius);
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMin - gap, height, wall.yMax + gap), radius);
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMax + gap, height, wall.yMin - gap), radius);
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMax + gap, height, wall.yMax + gap), radius);
            }
        }

        /// <summary>
        /// 在地图允许占位时加入障碍角点。
        /// </summary>
        /// <param name="nodes">路径节点列表。</param>
        /// <param name="roomId">地图编号。</param>
        /// <param name="point">候选角点。</param>
        /// <param name="radius">角色占位半径。</param>
        private void AddNavigationCorner(List<Vector3> nodes, int roomId, Vector3 point, float radius)
        {
            if (IsFinite(point) && CanOccupy(roomId, point, radius)) nodes.Add(point);
        }

        /// <summary>
        /// 检查主动导航线段是否进入膨胀墙体内部，允许合法贴边与离开接触面。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <param name="radius">角色占位半径。</param>
        /// <returns>线段没有进入墙体内部则为真。</returns>
        private bool NavigationWallClear(int roomId, Vector3 start, Vector3 end, float radius)
        {
            if (!obstacles.TryGetValue(roomId, out var walls)) return true;
            foreach (var wall in walls)
            {
                var near = 0f;
                var far = 1f;
                if (NavigationAxisCrosses(start.x, end.x - start.x, wall.xMin - radius, wall.xMax + radius, ref near, ref far) &&
                    NavigationAxisCrosses(start.z, end.z - start.z, wall.yMin - radius, wall.yMax + radius, ref near, ref far) &&
                    near < far && far > 0f && near < 1f) return false;
            }
            return true;
        }

        /// <summary>
        /// 将线段进入单轴开区间的比例与已知区间取交集。
        /// </summary>
        /// <param name="start">单轴起点。</param>
        /// <param name="delta">单轴位移。</param>
        /// <param name="minimum">开区间最小值。</param>
        /// <param name="maximum">开区间最大值。</param>
        /// <param name="near">已知进入比例。</param>
        /// <param name="far">已知离开比例。</param>
        /// <returns>线段存在进入区间内部的比例则为真。</returns>
        private static bool NavigationAxisCrosses(float start, float delta, float minimum, float maximum, ref float near, ref float far)
        {
            if (delta == 0f) return start > minimum && start < maximum;
            var a = (minimum - start) / delta;
            var b = (maximum - start) / delta;
            near = Mathf.Max(near, Mathf.Min(a, b));
            far = Mathf.Min(far, Mathf.Max(a, b));
            return near < far;
        }

        /// <summary>
        /// 检查带角色半径的线段是否避开所有墙体。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <param name="radius">角色半径。</param>
        /// <returns>路径无墙体阻挡则为真。</returns>
        private bool WallClear(int roomId, Vector3 start, Vector3 end, float radius)
        {
            if (!obstacles.TryGetValue(roomId, out var walls)) return true;
            foreach (var wall in walls)
                if (SweepAabb(start, end, wall, radius, out _, out _)) return false;
            return true;
        }

        /// <summary>
        /// 求两个世界坐标间的 XZ 单位方向。
        /// </summary>
        /// <param name="start">起点。</param>
        /// <param name="end">终点。</param>
        /// <returns>归一化方向或零。</returns>
        private static Vector2 DirectionTo(Vector3 start, Vector3 end)
        {
            var delta = new Vector2(end.x - start.x, end.z - start.z);
            return delta.sqrMagnitude > 0.000001f ? delta.normalized : Vector2.zero;
        }
    }
}
