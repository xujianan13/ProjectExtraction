using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 一次关卡运行的图、当前地图与探索状态。
    /// </summary>
    public sealed class LevelRunState
    {
        private readonly List<LevelRoomState> rooms;
        private readonly Dictionary<int, LevelRoomState> byId;

        public int Seed { get; }
        public int StartRoomId { get; }
        public int CurrentRoomId { get; private set; }
        public IReadOnlyList<LevelRoomState> Rooms => rooms;

        /// <summary>
        /// 创建已生成的单局关卡状态。
        /// </summary>
        /// <param name="seed">本局生成种子。</param>
        /// <param name="generatedRooms">包含稳定标识与连接的地图。</param>
        /// <param name="startRoomId">起始地图标识。</param>
        internal LevelRunState(int seed, List<LevelRoomState> generatedRooms, int startRoomId)
        {
            Seed = seed;
            rooms = generatedRooms;
            byId = new Dictionary<int, LevelRoomState>(rooms.Count);
            foreach (LevelRoomState room in rooms) byId.Add(room.RoomId, room);
            StartRoomId = startRoomId;
            CurrentRoomId = startRoomId;
            byId[startRoomId].MarkVisited();
        }

        /// <summary>
        /// 查询指定地图的本局状态。
        /// </summary>
        /// <param name="roomId">地图标识。</param>
        /// <param name="room">存在时的地图状态。</param>
        /// <returns>存在时为真。</returns>
        public bool TryGetRoom(int roomId, out LevelRoomState room) => byId.TryGetValue(roomId, out room);

        /// <summary>
        /// 查询当前地图指定出口的目标，不改变当前地图。
        /// </summary>
        /// <param name="direction">出口方向。</param>
        /// <param name="destinationRoomId">存在时的目标地图标识。</param>
        /// <returns>该方向有连接时为真。</returns>
        public bool TryGetDestination(RoomDirection direction, out int destinationRoomId)
        {
            return byId[CurrentRoomId].TryGetExit(direction, out destinationRoomId);
        }

        /// <summary>
        /// 在目标资源准备成功后提交一次相邻地图切换。
        /// </summary>
        /// <param name="destinationRoomId">目标地图标识。</param>
        /// <returns>目标与当前地图相连并已切换时为真。</returns>
        public bool TryCommitTransition(int destinationRoomId)
        {
            LevelRoomState current = byId[CurrentRoomId];
            bool connected = false;
            foreach (int neighborId in current.Exits.Values)
            {
                if (neighborId != destinationRoomId) continue;
                connected = true;
                break;
            }

            if (!connected || !byId.TryGetValue(destinationRoomId, out LevelRoomState destination)) return false;
            CurrentRoomId = destinationRoomId;
            destination.MarkVisited();
            return true;
        }

        /// <summary>
        /// 构建不含任何撤离数据的玩家小地图投影。
        /// </summary>
        /// <returns>已访问地图、相邻未知轮廓与已知连接的只读投影。</returns>
        public LevelMinimapProjection GetMinimapProjection()
        {
            var visibleIds = new HashSet<int>();
            var connections = new List<LevelMinimapConnection>();
            var connectionKeys = new HashSet<long>();
            foreach (LevelRoomState room in rooms)
            {
                if (!room.IsVisited) continue;
                visibleIds.Add(room.RoomId);
                foreach (int neighborId in room.Exits.Values)
                {
                    visibleIds.Add(neighborId);
                    int low = room.RoomId < neighborId ? room.RoomId : neighborId;
                    int high = room.RoomId < neighborId ? neighborId : room.RoomId;
                    long key = ((long)low << 32) | (uint)high;
                    if (connectionKeys.Add(key)) connections.Add(new LevelMinimapConnection(low, high));
                }
            }

            var projected = new List<LevelMinimapRoom>(visibleIds.Count);
            foreach (int roomId in visibleIds)
            {
                LevelRoomState room = byId[roomId];
                bool visited = room.IsVisited;
                bool? cleared = visited && (room.Type == LevelRoomType.Mob || room.Type == LevelRoomType.Boss)
                    ? room.IsCleared : (bool?)null;
                projected.Add(new LevelMinimapRoom(roomId, room.GridPosition, visited ? room.Type : (LevelRoomType?)null,
                    visited, roomId == CurrentRoomId, cleared));
            }

            projected.Sort((a, b) => a.RoomId.CompareTo(b.RoomId));
            connections.Sort((a, b) =>
            {
                int first = a.FromRoomId.CompareTo(b.FromRoomId);
                return first != 0 ? first : a.ToRoomId.CompareTo(b.ToRoomId);
            });
            return new LevelMinimapProjection(projected, connections);
        }
    }
}
