using System;
using System.Collections.Generic;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 模板几何和遭遇占位的启动验证。
    /// </summary>
    public static partial class LevelContentConfig
    {
        /// <summary>
        /// 验证遭遇怪群与布局、入口和玩家出生的空间关系。
        /// </summary>
        /// <param name="template">待验证模板。</param>
        /// <param name="encounter">可从模板抽取的遭遇。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>怪群可安全出生时为真。</returns>
        private static bool ValidateEncounter(CfgRoomTemplatesRow template, CfgEncountersRow encounter, out string error)
        {
            error = null;
            LevelDefinition definition = CreateDefinition(GameTables.CfgLevels.GetById(template.LevelID), template.LayoutID);
            var layout = new LevelRoomLayout(definition);
            var positions = new List<Vector2>();
            var radii = new List<float>();
            bool guaranteedDrop = false;
            foreach (SpawnDefinition spawn in GetSpawns(encounter.ID))
            {
                CfgEnemiesRow enemy = GameTables.CfgEnemies.GetById(spawn.EnemyID);
                if (!HasPool(enemy.DropPoolID, template.LevelID) ||
                    enemy.BaseHealth + enemy.HealthPerLevel * template.LevelID > float.MaxValue ||
                    (long)enemy.ExperienceBase + (long)enemy.ExperiencePerLevel * template.LevelID > int.MaxValue)
                { error = "关卡敌人没有该等级可抽取的掉落项：" + template.LevelID + "/" + enemy.ID; return false; }
                if (enemy.DropChance >= 1) guaranteedDrop = true;
                float radius = (float)enemy.Radius;
                for (int index = 0; index < spawn.Count; index++)
                {
                    if (positions.Count >= 999) { error = "单房间敌人超过实体编号容量：" + encounter.ID; return false; }
                    Vector3 point = spawn.PositionAt(index);
                    if (layout.IsBlocked(point, radius)) { error = "刷怪点与边界或障碍重叠：" + spawn.Row.ID; return false; }
                    var position = new Vector2(point.x, point.z);
                    for (int earlier = 0; earlier < positions.Count; earlier++)
                        if (Vector2.Distance(position, positions[earlier]) < radius + radii[earlier])
                        { error = "刷怪点相互重叠：" + spawn.Row.ID; return false; }
                    foreach (RoomDirection direction in Enum.GetValues(typeof(RoomDirection)))
                    {
                        if ((template.AllowedExits & (1 << (int)direction)) == 0) continue;
                        Vector2 entry = EntrancePoint(definition.Layout, direction);
                        if (Vector2.Distance(position, entry) < radius + PlayerRadius)
                        { error = "刷怪点堵住房间入口：" + spawn.Row.ID; return false; }
                    }
                    if (template.IsStart)
                    {
                        CfgPlayerCombatRow player = GameTables.CfgPlayerCombat.GetById(1);
                        if (Vector2.Distance(position, new Vector2((float)player.SpawnX, (float)player.SpawnZ)) < radius + PlayerRadius)
                        { error = "刷怪点与玩家出生重叠：" + spawn.Row.ID; return false; }
                    }
                    positions.Add(position);
                    radii.Add(radius);
                }
            }
            if (template.RoomType == LevelRoomType.Boss.ToString() && !guaranteedDrop)
            { error = "Boss 遭遇缺少必掉敌人：" + encounter.ID; return false; }
            return true;
        }

        /// <summary>
        /// 验证玩家从起点或所有入口能走到出口和交互点。
        /// </summary>
        /// <param name="template">待验证模板。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>布局具有可达路径时为真。</returns>
        private static bool ValidateGeometry(CfgRoomTemplatesRow template, out string error)
        {
            error = null;
            LevelDefinition definition = CreateDefinition(GameTables.CfgLevels.GetById(template.LevelID), template.LayoutID);
            var layout = new LevelRoomLayout(definition);
            CfgRoomLayoutsRow row = definition.Layout;
            float radius = PlayerRadius;
            if (row.ExitHalfWidth <= radius || row.SpawnInset < row.WallThickness * 0.5 + radius ||
                row.ExitCenterInset < row.WallThickness * 0.5 + radius)
            { error = "房间出入口不足玩家通行：" + template.ID; return false; }
            var targets = new List<Vector2>();
            if (template.IsStart)
            {
                CfgPlayerCombatRow player = GameTables.CfgPlayerCombat.GetById(1);
                targets.Add(new Vector2((float)player.SpawnX, (float)player.SpawnZ));
            }
            foreach (RoomDirection direction in Enum.GetValues(typeof(RoomDirection)))
            {
                if ((template.AllowedExits & (1 << (int)direction)) == 0) continue;
                targets.Add(EntrancePoint(row, direction));
                targets.Add(ExitPoint(row, direction));
            }
            if (template.RoomType == LevelRoomType.Loot.ToString())
                targets.Add(new Vector2((float)row.ChestX, (float)row.ChestZ));
            if (template.RoomType == LevelRoomType.Npc.ToString())
                targets.Add(new Vector2((float)row.NpcX, (float)row.NpcZ));
            targets.Add(new Vector2((float)row.ExtractionX, (float)row.ExtractionZ));
            foreach (Vector2 target in targets)
                if (layout.IsBlocked(new Vector3(target.x, 0, target.y), radius))
                { error = "玩家出生、入口、出口或交互点与障碍重叠：" + template.ID; return false; }
            if (!Reachable(layout, targets, radius))
            { error = "房间布局封路，入口或交互点不可达：" + template.ID; return false; }
            return true;
        }

        private static float PlayerRadius => (float)GameTables.CfgPlayerCombat.GetById(1).Radius;

        /// <summary>
        /// 取得某一侧的入口出生位置。
        /// </summary>
        /// <param name="row">布局配置。</param>
        /// <param name="direction">入口方向。</param>
        /// <returns>局部 XZ 位置。</returns>
        private static Vector2 EntrancePoint(CfgRoomLayoutsRow row, RoomDirection direction)
        {
            float distance = (float)(row.HalfExtent - row.SpawnInset);
            return DirectionPoint(direction, distance);
        }

        /// <summary>
        /// 取得某一侧出口交互位置。
        /// </summary>
        /// <param name="row">布局配置。</param>
        /// <param name="direction">出口方向。</param>
        /// <returns>局部 XZ 位置。</returns>
        private static Vector2 ExitPoint(CfgRoomLayoutsRow row, RoomDirection direction)
        {
            float distance = (float)(row.HalfExtent - row.ExitCenterInset);
            return DirectionPoint(direction, distance);
        }

        /// <summary>
        /// 获取方向对应的轴向位置。
        /// </summary>
        /// <param name="direction">房间方向。</param>
        /// <param name="distance">距中心的距离。</param>
        /// <returns>局部 XZ 位置。</returns>
        private static Vector2 DirectionPoint(RoomDirection direction, float distance)
        {
            switch (direction)
            {
                case RoomDirection.North: return new Vector2(0, distance);
                case RoomDirection.East: return new Vector2(distance, 0);
                case RoomDirection.South: return new Vector2(0, -distance);
                default: return new Vector2(-distance, 0);
            }
        }

        /// <summary>
        /// 使用受限栅格验证全部目标连通。
        /// </summary>
        /// <param name="layout">当前布局。</param>
        /// <param name="targets">必须互相可达的位置。</param>
        /// <param name="radius">玩家圆形半径。</param>
        /// <returns>所有目标在同一可达区域时为真。</returns>
        private static bool Reachable(LevelRoomLayout layout, IReadOnlyList<Vector2> targets, float radius)
        {
            float limit = layout.HalfExtent - layout.WallThickness * 0.5f - radius;
            if (limit <= 0 || limit > 32 || targets.Count == 0) return false;
            const float spacing = 0.2f;
            int side = Mathf.CeilToInt(limit * 2f / spacing) + 1;
            if (side > 325) return false;
            var blocked = new bool[side * side];
            for (int z = 0; z < side; z++)
                for (int x = 0; x < side; x++)
                {
                    Vector3 point = new Vector3(-limit + x * spacing, 0, -limit + z * spacing);
                    blocked[z * side + x] = layout.IsBlocked(point, radius);
                }
            var goalCells = new HashSet<int>();
            foreach (Vector2 target in targets)
            {
                int x = Mathf.Clamp(Mathf.RoundToInt((target.x + limit) / spacing), 0, side - 1);
                int z = Mathf.Clamp(Mathf.RoundToInt((target.y + limit) / spacing), 0, side - 1);
                int cell = z * side + x;
                Vector2 cellPoint = GridPoint(cell, side, limit, spacing);
                if (blocked[cell] || !SegmentClear(layout, target, cellPoint, radius)) return false;
                goalCells.Add(cell);
            }
            var reached = new bool[blocked.Length];
            var queue = new Queue<int>();
            foreach (int first in goalCells)
            {
                queue.Enqueue(first);
                reached[first] = true;
                break;
            }
            while (queue.Count > 0)
            {
                int cell = queue.Dequeue();
                goalCells.Remove(cell);
                int x = cell % side;
                int z = cell / side;
                if (x > 0) Visit(cell, cell - 1, side, limit, spacing, radius, layout, blocked, reached, queue);
                if (x + 1 < side) Visit(cell, cell + 1, side, limit, spacing, radius, layout, blocked, reached, queue);
                if (z > 0) Visit(cell, cell - side, side, limit, spacing, radius, layout, blocked, reached, queue);
                if (z + 1 < side) Visit(cell, cell + side, side, limit, spacing, radius, layout, blocked, reached, queue);
            }
            return goalCells.Count == 0;
        }

        /// <summary>
        /// 取得栅格中心的局部位置。
        /// </summary>
        /// <param name="cell">栅格编号。</param>
        /// <param name="side">每边栅格数。</param>
        /// <param name="limit">玩家中心可活动的半边界。</param>
        /// <param name="spacing">栅格间距。</param>
        /// <returns>局部 XZ 位置。</returns>
        private static Vector2 GridPoint(int cell, int side, float limit, float spacing)
        {
            return new Vector2(-limit + cell % side * spacing, -limit + cell / side * spacing);
        }

        /// <summary>
        /// 验证整段路径不穿过按玩家半径扩张的障碍。
        /// </summary>
        /// <param name="layout">当前布局。</param>
        /// <param name="from">线段起点。</param>
        /// <param name="to">线段终点。</param>
        /// <param name="radius">玩家半径。</param>
        /// <returns>线段与静态障碍无交集时为真。</returns>
        private static bool SegmentClear(LevelRoomLayout layout, Vector2 from, Vector2 to, float radius)
        {
            foreach (LevelObstacle obstacle in layout.Obstacles)
            {
                float minX = obstacle.Min.x - radius;
                float maxX = obstacle.Max.x + radius;
                float minZ = obstacle.Min.y - radius;
                float maxZ = obstacle.Max.y + radius;
                float start = 0f;
                float end = 1f;
                if (!Clip(- (to.x - from.x), from.x - minX, ref start, ref end) ||
                    !Clip(to.x - from.x, maxX - from.x, ref start, ref end) ||
                    !Clip(- (to.y - from.y), from.y - minZ, ref start, ref end) ||
                    !Clip(to.y - from.y, maxZ - from.y, ref start, ref end)) continue;
                return false;
            }
            return true;
        }

        /// <summary>
        /// 将线段参数区间裁剪到矩形的一侧。
        /// </summary>
        /// <param name="p">轴向线段增量。</param>
        /// <param name="q">起点到边界的有向距离。</param>
        /// <param name="start">可见区间下界。</param>
        /// <param name="end">可见区间上界。</param>
        /// <returns>线段仍有交集时为真。</returns>
        private static bool Clip(float p, float q, ref float start, ref float end)
        {
            if (Mathf.Abs(p) < 0.000001f) return q >= 0f;
            float t = q / p;
            if (p < 0f)
            {
                if (t > end) return false;
                if (t > start) start = t;
            }
            else
            {
                if (t < start) return false;
                if (t < end) end = t;
            }
            return true;
        }

        /// <summary>
        /// 将整段可通行且未访问的栅格边入队。
        /// </summary>
        /// <param name="from">当前栅格编号。</param>
        /// <param name="cell">候选栅格编号。</param>
        /// <param name="side">每边栅格数。</param>
        /// <param name="limit">玩家中心可活动的半边界。</param>
        /// <param name="spacing">栅格间距。</param>
        /// <param name="radius">玩家半径。</param>
        /// <param name="layout">当前布局。</param>
        /// <param name="blocked">静态阻挡表。</param>
        /// <param name="reached">已访问表。</param>
        /// <param name="queue">待遍历队列。</param>
        private static void Visit(int from, int cell, int side, float limit, float spacing, float radius, LevelRoomLayout layout,
            bool[] blocked, bool[] reached, Queue<int> queue)
        {
            if (blocked[cell] || reached[cell] ||
                !SegmentClear(layout, GridPoint(from, side, limit, spacing), GridPoint(cell, side, limit, spacing), radius)) return;
            reached[cell] = true;
            queue.Enqueue(cell);
        }
    }
}
