using Random = System.Random;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 根据固定种子生成连通、双向、四向的首版地图图。
    /// </summary>
    public static class LevelGenerator
    {
        private readonly struct GraphEdge
        {
            public readonly int From;
            public readonly int To;
            public readonly RoomDirection Direction;

            /// <summary>
            /// 创建从较小网格标识向东或向北的候选连接。
            /// </summary>
            /// <param name="from">起点标识。</param>
            /// <param name="to">终点标识。</param>
            /// <param name="direction">从起点看向终点的方向。</param>
            public GraphEdge(int from, int to, RoomDirection direction)
            {
                From = from;
                To = to;
                Direction = direction;
            }
        }

        /// <summary>
        /// 使用首版配置和固定种子生成一局地图。
        /// </summary>
        /// <param name="seed">生成种子；同种子产生同一结果。</param>
        /// <param name="run">成功时的单局地图状态。</param>
        /// <returns>生成结果满足连通、出口与内容约束时为真。</returns>
        public static bool TryGenerate(int seed, out LevelRunState run)
        {
            return TryGenerate(seed, PrototypeLevelConfig.DefaultLevel, out run);
        }

        /// <summary>
        /// 使用指定关卡配置和固定种子生成一局地图。
        /// </summary>
        /// <param name="seed">生成种子。</param>
        /// <param name="level">关卡定义。</param>
        /// <param name="run">成功时的单局地图状态。</param>
        /// <returns>生成结果满足连通、出口与内容约束时为真。</returns>
        public static bool TryGenerate(int seed, LevelDefinition level, out LevelRunState run)
        {
            run = null;
            int count = level == null ? 0 : level.Row.GridWidth * level.Row.GridHeight;
            if (count < 6) return false;
            var random = new Random(seed);
            var allEdges = new List<GraphEdge>();
            for (int y = 0; y < level.Row.GridHeight; y++)
            {
                for (int x = 0; x < level.Row.GridWidth; x++)
                {
                    int id = y * level.Row.GridWidth + x + 1;
                    if (x + 1 < level.Row.GridWidth) allEdges.Add(new GraphEdge(id, id + 1, RoomDirection.East));
                    if (y + 1 < level.Row.GridHeight)
                        allEdges.Add(new GraphEdge(id, id + level.Row.GridWidth, RoomDirection.North));
                }
            }

            int startId = random.Next(1, count + 1);
            var connected = new HashSet<int> { startId };
            var chosen = new List<GraphEdge>(count + level.Row.ExtraConnectionMax);
            while (connected.Count < count)
            {
                var frontier = new List<GraphEdge>();
                foreach (GraphEdge edge in allEdges)
                {
                    if (connected.Contains(edge.From) != connected.Contains(edge.To)) frontier.Add(edge);
                }

                if (frontier.Count == 0) return false;
                GraphEdge selected = frontier[random.Next(frontier.Count)];
                chosen.Add(selected);
                connected.Add(selected.From);
                connected.Add(selected.To);
            }

            var extra = new List<GraphEdge>();
            foreach (GraphEdge edge in allEdges)
            {
                bool used = false;
                foreach (GraphEdge selected in chosen)
                {
                    if (selected.From == edge.From && selected.To == edge.To) { used = true; break; }
                }
                if (!used) extra.Add(edge);
            }

            int requestedExtra = random.Next(level.Row.ExtraConnectionMin, level.Row.ExtraConnectionMax + 1);
            if (extra.Count < requestedExtra) return false;
            for (int i = 0; i < requestedExtra; i++)
            {
                int index = random.Next(extra.Count);
                chosen.Add(extra[index]);
                extra.RemoveAt(index);
            }

            int bossId = FindFarthestRoom(startId, count, chosen, random);
            if (bossId <= 0) return false;
            var candidates = new List<int>();
            for (int id = 1; id <= count; id++)
            {
                if (id != startId && id != bossId) candidates.Add(id);
            }
            int npcId = TakeRandom(candidates, random);
            var lootIds = new HashSet<int>();
            for (int i = 0; i < level.Row.LootRoomCount; i++) lootIds.Add(TakeRandom(candidates, random));

            var rooms = new List<LevelRoomState>(count);
            for (int id = 1; id <= count; id++)
            {
                LevelRoomType type = id == bossId ? LevelRoomType.Boss
                    : id == npcId ? LevelRoomType.Npc
                    : lootIds.Contains(id) ? LevelRoomType.Loot : LevelRoomType.Mob;
                var coordinate = new Vector2Int((id - 1) % level.Row.GridWidth, (id - 1) / level.Row.GridWidth);
                rooms.Add(new LevelRoomState(id, coordinate, type, id == startId));
            }

            foreach (GraphEdge edge in chosen)
            {
                if (!rooms[edge.From - 1].TryAddExit(edge.Direction, edge.To)) return false;
                if (!rooms[edge.To - 1].TryAddExit(Opposite(edge.Direction), edge.From)) return false;
            }

            var extractionCandidates = new List<LevelRoomState>();
            foreach (LevelRoomState room in rooms)
            {
                if (room.Type == LevelRoomType.Mob && !room.IsStart) extractionCandidates.Add(room);
            }
            if (extractionCandidates.Count == 0) return false;
            extractionCandidates[random.Next(extractionCandidates.Count)].SetExtractionCandidate(true);
            foreach (LevelRoomState room in rooms)
            {
                if (room.IsStart || room.HasExtractionCandidate || random.NextDouble() >= level.Row.ExtraExtractionChance) continue;
                room.SetExtractionCandidate(room.Type == LevelRoomType.Mob || room.Type == LevelRoomType.Boss);
            }
            foreach (LevelRoomState room in rooms)
            {
                if (!LevelContentConfig.TryCreateRoomPlan(level, room, random, out LevelRoomPlan plan)) return false;
                room.SetPlan(plan);
            }
            run = new LevelRunState(seed, rooms, startId);
            return true;
        }

        /// <summary>
        /// 获取出口的对向入口方向。
        /// </summary>
        /// <param name="direction">原出口方向。</param>
        /// <returns>对向方向。</returns>
        public static RoomDirection Opposite(RoomDirection direction)
        {
            switch (direction)
            {
                case RoomDirection.North: return RoomDirection.South;
                case RoomDirection.East: return RoomDirection.West;
                case RoomDirection.South: return RoomDirection.North;
                default: return RoomDirection.East;
            }
        }

        /// <summary>
        /// 从候选列表中确定性抽取并移除一个标识。
        /// </summary>
        /// <param name="candidates">可修改的候选列表。</param>
        /// <param name="random">本局随机源。</param>
        /// <returns>选中的地图标识。</returns>
        private static int TakeRandom(List<int> candidates, Random random)
        {
            int index = random.Next(candidates.Count);
            int id = candidates[index];
            candidates.RemoveAt(index);
            return id;
        }

        /// <summary>
        /// 判断移除候选 Boss 地图后其他地图是否仍从起点可达。
        /// </summary>
        /// <param name="excludedId">暂时移除的候选地图。</param>
        /// <param name="startId">起点地图标识。</param>
        /// <param name="count">原地图总数。</param>
        /// <param name="edges">已选连接。</param>
        /// <returns>候选不是其他地图必经节点时为真。</returns>
        private static bool KeepsOtherRoomsConnected(int excludedId, int startId, int count, List<GraphEdge> edges)
        {
            var reached = new HashSet<int> { startId };
            var queue = new Queue<int>();
            queue.Enqueue(startId);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (GraphEdge edge in edges)
                {
                    int neighbor = edge.From == current ? edge.To : edge.To == current ? edge.From : 0;
                    if (neighbor == 0 || neighbor == excludedId || !reached.Add(neighbor)) continue;
                    queue.Enqueue(neighbor);
                }
            }
            return reached.Count == count - 1;
        }

        /// <summary>
        /// 按最终连接图挑选距起点最远的 Boss 地图。
        /// </summary>
        /// <param name="startId">起点标识。</param>
        /// <param name="count">地图总数。</param>
        /// <param name="edges">已选连接。</param>
        /// <param name="random">同距离时的随机源。</param>
        /// <returns>非起点 Boss 地图标识。</returns>
        private static int FindFarthestRoom(int startId, int count, List<GraphEdge> edges, Random random)
        {
            var distance = new int[count + 1];
            for (int id = 1; id <= count; id++) distance[id] = -1;
            var queue = new Queue<int>();
            distance[startId] = 0;
            queue.Enqueue(startId);
            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                foreach (GraphEdge edge in edges)
                {
                    int neighbor = edge.From == current ? edge.To : edge.To == current ? edge.From : 0;
                    if (neighbor == 0 || distance[neighbor] >= 0) continue;
                    distance[neighbor] = distance[current] + 1;
                    queue.Enqueue(neighbor);
                }
            }

            int maximum = -1;
            var farthest = new List<int>();
            for (int id = 1; id <= count; id++)
            {
                if (id == startId || !KeepsOtherRoomsConnected(id, startId, count, edges)) continue;
                if (distance[id] > maximum) { maximum = distance[id]; farthest.Clear(); }
                if (distance[id] == maximum) farthest.Add(id);
            }
            return farthest[random.Next(farthest.Count)];
        }
    }
}
