using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 可通过 JsonUtility 烘焙与恢复的地图几何描述。
    /// </summary>
    [Serializable]
    public sealed class RegionMapDefinition
    {
        public int MapId;
        public string Version;
        public Rect Bounds;
        public float NorthYawDegrees;
        public List<RegionMapObstacle> Obstacles = new();
        public List<RegionMapAnchor> Anchors = new();
        public List<RegionNoSpawnZone> NoSpawnZones = new();

        /// <summary>
        /// 创建完整独立的地图描述副本。
        /// </summary>
        /// <returns>不共享列表及其成员的副本。</returns>
        public RegionMapDefinition Copy() => new RegionMapDefinition
        {
            MapId = MapId, Version = Version, Bounds = Bounds, NorthYawDegrees = NorthYawDegrees, Obstacles = Obstacles.ConvertAll(value => value.Copy()),
            Anchors = Anchors.ConvertAll(value => value.Copy()), NoSpawnZones = NoSpawnZones.ConvertAll(value => value.Copy())
        };

        /// <summary>
        /// 检查地图身份、几何和锚点描述。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>描述结构完整时为真。</returns>
        public bool TryValidate(out string error)
        {
            error = null;
            if (MapId <= 0 || string.IsNullOrWhiteSpace(Version) || !RegionValueRules.Finite(Bounds.x) ||
                !RegionValueRules.Finite(Bounds.y) || !RegionValueRules.Finite(Bounds.width) || !RegionValueRules.Finite(Bounds.height) ||
                Bounds.width <= 0f || Bounds.height <= 0f || !RegionValueRules.Finite(Bounds.xMax) || !RegionValueRules.Finite(Bounds.yMax) ||
                !RegionValueRules.Finite(NorthYawDegrees) ||
                Obstacles == null || Anchors == null || NoSpawnZones == null)
            { error = "区域地图身份或边界无效。"; return false; }
            foreach (RegionMapObstacle obstacle in Obstacles)
                if (obstacle == null || !RegionValueRules.Finite(obstacle.Min.x) || !RegionValueRules.Finite(obstacle.Min.y) ||
                    !RegionValueRules.Finite(obstacle.Max.x) || !RegionValueRules.Finite(obstacle.Max.y) ||
                    obstacle.Min.x >= obstacle.Max.x || obstacle.Min.y >= obstacle.Max.y || obstacle.Min.x < Bounds.xMin ||
                    obstacle.Min.y < Bounds.yMin || obstacle.Max.x > Bounds.xMax || obstacle.Max.y > Bounds.yMax)
                { error = "区域地图障碍无效。"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionMapAnchor anchor in Anchors)
                if (anchor == null || string.IsNullOrWhiteSpace(anchor.Id) || !ids.Add(anchor.Id) ||
                    !Enum.IsDefined(typeof(RegionAnchorKind), anchor.Kind) || !RegionValueRules.Finite(anchor.Position) ||
                    anchor.Capacity <= 0 || !Contains(anchor.Position))
                { error = "区域地图锚点无效或重复。"; return false; }
            foreach (RegionNoSpawnZone zone in NoSpawnZones)
                if (zone == null || !RegionValueRules.Finite(zone.Center) || !RegionValueRules.NonNegative(zone.Radius))
                { error = "禁止出生范围无效。"; return false; }
            return true;
        }

        /// <summary>
        /// 查询稳定锚点身份。
        /// </summary>
        /// <param name="id">锚点身份。</param>
        /// <param name="anchor">找到时的锚点。</param>
        /// <returns>锚点存在时为真。</returns>
        public bool TryGetAnchor(string id, out RegionMapAnchor anchor)
        {
            anchor = Anchors.Find(value => value.Id == id);
            return anchor != null;
        }

        /// <summary>
        /// 检查坐标是否位于地图闭合边界内。
        /// </summary>
        /// <param name="position">地图坐标。</param>
        /// <returns>位于边界内时为真。</returns>
        private bool Contains(Vector3 position) => position.x >= Bounds.xMin && position.x <= Bounds.xMax &&
            position.z >= Bounds.yMin && position.z <= Bounds.yMax;
    }

    /// <summary>
    /// 与 ECS 矩形碰撞保持一致的地图查询。
    /// </summary>
    public sealed class RegionMapGeometry
    {
        private readonly RegionMapDefinition map;

        /// <summary>
        /// 包装已校验的几何描述。
        /// </summary>
        /// <param name="map">有效地图描述。</param>
        public RegionMapGeometry(RegionMapDefinition map) => this.map = map;

        /// <summary>
        /// 检查圆形角色的静态占位，与战斗层扩张矩形的规则一致。
        /// </summary>
        /// <param name="position">地图坐标。</param>
        /// <param name="radius">角色半径。</param>
        /// <returns>边界或障碍阻挡时为真。</returns>
        public bool IsBlocked(Vector3 position, float radius)
        {
            if (!RegionValueRules.Finite(position) || !RegionValueRules.NonNegative(radius) ||
                position.x - radius < map.Bounds.xMin || position.x + radius > map.Bounds.xMax ||
                position.z - radius < map.Bounds.yMin || position.z + radius > map.Bounds.yMax) return true;
            foreach (RegionMapObstacle obstacle in map.Obstacles)
                if (position.x + radius > obstacle.Min.x && position.x - radius < obstacle.Max.x &&
                    position.z + radius > obstacle.Min.y && position.z - radius < obstacle.Max.y) return true;
            return false;
        }

        /// <summary>
        /// 检查普通或精英出生是否触及禁止出生范围。
        /// </summary>
        /// <param name="position">出生坐标。</param>
        /// <param name="radius">敌人半径。</param>
        /// <returns>触及禁止范围时为真。</returns>
        public bool IsSpawnForbidden(Vector3 position, float radius)
        {
            foreach (RegionNoSpawnZone zone in map.NoSpawnZones)
            {
                float dx = position.x - zone.Center.x, dz = position.z - zone.Center.z;
                float sum = radius + zone.Radius;
                if (dx * dx + dz * dz < sum * sum) return true;
            }
            return false;
        }

        /// <summary>
        /// 将坐标限制到矩形外边界，障碍占位仍须另行校验。
        /// </summary>
        /// <param name="position">候选坐标。</param>
        /// <param name="radius">角色半径。</param>
        /// <returns>限制到外边界的坐标。</returns>
        public Vector3 ClampPosition(Vector3 position, float radius)
        {
            position.x = Mathf.Clamp(position.x, map.Bounds.xMin + radius, map.Bounds.xMax - radius);
            position.z = Mathf.Clamp(position.z, map.Bounds.yMin + radius, map.Bounds.yMax - radius);
            return position;
        }

        /// <summary>
        /// 用受限栅格证明所有指定目标互相可达。
        /// </summary>
        /// <param name="targets">必须可达的入口、目标及出口。</param>
        /// <param name="radius">验证角色半径。</param>
        /// <param name="spacing">验证栅格间距。</param>
        /// <param name="maxCells">调用方允许的最大栅格数量。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部目标位于同一可达分量时为真。</returns>
        public bool TryValidateReachability(IReadOnlyList<Vector3> targets, float radius, float spacing, int maxCells, out string error)
        {
            error = null;
            float width = map.Bounds.width - radius * 2f, height = map.Bounds.height - radius * 2f;
            if (targets == null || targets.Count == 0 || !RegionValueRules.NonNegative(radius) || !RegionValueRules.Finite(spacing) ||
                spacing <= 0f || maxCells <= 0 || width <= 0f || height <= 0f)
            { error = "区域可达性验证参数无效。"; return false; }
            double requestedColumns = Math.Ceiling(width / spacing) + 1, requestedRows = Math.Ceiling(height / spacing) + 1;
            if (requestedColumns * requestedRows > maxCells || requestedColumns > int.MaxValue || requestedRows > int.MaxValue)
            { error = "区域可达性验证超过栅格预算。"; return false; }
            int columns = (int)requestedColumns, rows = (int)requestedRows;
            float stepX = width / (columns - 1), stepZ = height / (rows - 1);
            Vector3 origin = new Vector3(map.Bounds.xMin + radius, 0f, map.Bounds.yMin + radius);
            var blocked = new bool[columns * rows];
            for (int index = 0; index < blocked.Length; index++) blocked[index] = IsBlocked(GridPoint(index, columns, origin, stepX, stepZ), radius);
            var goals = new HashSet<int>();
            foreach (Vector3 target in targets)
            {
                if (IsBlocked(target, radius)) { error = "区域目标与静态几何重叠。"; return false; }
                int x = Mathf.Clamp(Mathf.RoundToInt((target.x - origin.x) / stepX), 0, columns - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((target.z - origin.z) / stepZ), 0, rows - 1);
                int cell = z * columns + x;
                if (blocked[cell] || !SegmentClear(target, GridPoint(cell, columns, origin, stepX, stepZ), radius))
                { error = "区域目标无法连接到验证栅格。"; return false; }
                goals.Add(cell);
            }
            var reached = new bool[blocked.Length];
            var queue = new Queue<int>();
            foreach (int first in goals) { reached[first] = true; queue.Enqueue(first); break; }
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                goals.Remove(current);
                int x = current % columns, z = current / columns;
                if (x > 0) Visit(current, current - 1, columns, origin, stepX, stepZ, radius, blocked, reached, queue);
                if (x + 1 < columns) Visit(current, current + 1, columns, origin, stepX, stepZ, radius, blocked, reached, queue);
                if (z > 0) Visit(current, current - columns, columns, origin, stepX, stepZ, radius, blocked, reached, queue);
                if (z + 1 < rows) Visit(current, current + columns, columns, origin, stepX, stepZ, radius, blocked, reached, queue);
            }
            if (goals.Count > 0) { error = "区域入口、目标或出口不连通。"; return false; }
            return true;
        }

        /// <summary>
        /// 计算验证栅格的世界坐标。
        /// </summary>
        /// <param name="cell">栅格序号。</param>
        /// <param name="columns">每行列数。</param>
        /// <param name="origin">栅格原点。</param>
        /// <param name="stepX">水平步长。</param>
        /// <param name="stepZ">纵向步长。</param>
        /// <returns>栅格世界坐标。</returns>
        private static Vector3 GridPoint(int cell, int columns, Vector3 origin, float stepX, float stepZ) =>
            origin + new Vector3(cell % columns * stepX, 0f, cell / columns * stepZ);

        /// <summary>
        /// 把通畅且尚未访问的相邻栅格入队。
        /// </summary>
        /// <param name="from">当前栅格。</param>
        /// <param name="destination">目标栅格。</param>
        /// <param name="columns">每行列数。</param>
        /// <param name="origin">栅格原点。</param>
        /// <param name="stepX">水平步长。</param>
        /// <param name="stepZ">纵向步长。</param>
        /// <param name="radius">角色半径。</param>
        /// <param name="blocked">静态阻挡记录。</param>
        /// <param name="reached">已经访问的记录。</param>
        /// <param name="queue">待访问队列。</param>
        private void Visit(int from, int destination, int columns, Vector3 origin, float stepX, float stepZ, float radius,
            bool[] blocked, bool[] reached, Queue<int> queue)
        {
            if (reached[destination] || blocked[destination] || !SegmentClear(GridPoint(from, columns, origin, stepX, stepZ),
                GridPoint(destination, columns, origin, stepX, stepZ), radius)) return;
            reached[destination] = true;
            queue.Enqueue(destination);
        }

        /// <summary>
        /// 检查线段是否穿过扩张后的矩形障碍。
        /// </summary>
        /// <param name="from">起点。</param>
        /// <param name="to">终点。</param>
        /// <param name="radius">角色半径。</param>
        /// <returns>线段通畅时为真。</returns>
        private bool SegmentClear(Vector3 from, Vector3 to, float radius)
        {
            foreach (RegionMapObstacle obstacle in map.Obstacles)
            {
                float start = 0f, end = 1f;
                if (Clip(-(to.x - from.x), from.x - obstacle.Min.x + radius, ref start, ref end) &&
                    Clip(to.x - from.x, obstacle.Max.x + radius - from.x, ref start, ref end) &&
                    Clip(-(to.z - from.z), from.z - obstacle.Min.y + radius, ref start, ref end) &&
                    Clip(to.z - from.z, obstacle.Max.y + radius - from.z, ref start, ref end)) return false;
            }
            return true;
        }

        /// <summary>
        /// 裁剪矩形一侧对应的线段区间。
        /// </summary>
        /// <param name="p">轴向增量。</param>
        /// <param name="q">起点到边界的距离。</param>
        /// <param name="start">可行区间起点。</param>
        /// <param name="end">可行区间终点。</param>
        /// <returns>仍有交集时为真。</returns>
        private static bool Clip(float p, float q, ref float start, ref float end)
        {
            if (p == 0f) return q >= 0f;
            float ratio = q / p;
            if (p < 0f) { if (ratio > end) return false; start = Mathf.Max(start, ratio); }
            else { if (ratio < start) return false; end = Mathf.Min(end, ratio); }
            return true;
        }
    }
}
