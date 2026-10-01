using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 基于矩形障碍角点的确定性追击路径。
    /// </summary>
    public sealed partial class CombatWorld
    {
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
            const float clearance = 0.08f;
            foreach (var wall in walls)
            {
                var gap = radius + clearance;
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMin - gap, start.y, wall.yMin - gap), radius);
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMin - gap, start.y, wall.yMax + gap), radius);
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMax + gap, start.y, wall.yMin - gap), radius);
                AddNavigationCorner(nodes, roomId, new Vector3(wall.xMax + gap, start.y, wall.yMax + gap), radius);
            }
            var costs = new float[nodes.Count];
            var previous = new int[nodes.Count];
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
                    if (visited[next] || !WallClear(roomId, nodes[current], nodes[next], radius)) continue;
                    var candidate = costs[current] + Vector3.Distance(nodes[current], nodes[next]);
                    if (candidate + 0.00001f >= costs[next]) continue;
                    costs[next] = candidate;
                    previous[next] = current;
                }
            }
            if (previous[1] < 0) return Vector2.zero;
            var waypoint = 1;
            while (previous[waypoint] > 0) waypoint = previous[waypoint];
            return DirectionTo(start, nodes[waypoint]);
        }

        /// <summary>
        /// 在地图允许占位时加入障碍角点。
        /// </summary>
        /// <param name="nodes">路径节点列表。</param>
        /// <param name="roomId">地图编号。</param>
        /// <param name="point">候选角点。</param>
        /// <param name="radius">敌人半径。</param>
        private void AddNavigationCorner(List<Vector3> nodes, int roomId, Vector3 point, float radius)
        {
            if (CanOccupy(roomId, point, radius)) nodes.Add(point);
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
