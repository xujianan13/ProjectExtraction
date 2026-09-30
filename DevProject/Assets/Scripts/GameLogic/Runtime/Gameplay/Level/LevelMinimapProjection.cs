using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 小地图上一个已知或相邻未知的地图节点。
    /// </summary>
    public readonly struct LevelMinimapRoom
    {
        public readonly int RoomId;
        public readonly Vector2Int GridPosition;
        public readonly LevelRoomType? Type;
        public readonly bool IsVisited;
        public readonly bool IsCurrent;
        public readonly bool? IsCleared;

        /// <summary>
        /// 创建玩家可见的地图节点。
        /// </summary>
        /// <param name="roomId">地图标识。</param>
        /// <param name="gridPosition">小地图网格坐标。</param>
        /// <param name="type">未访问时为空的地图类型。</param>
        /// <param name="isVisited">是否已访问。</param>
        /// <param name="isCurrent">是否当前地图。</param>
        /// <param name="isCleared">已访问战斗地图的清理状态，其余情况为空。</param>
        public LevelMinimapRoom(int roomId, Vector2Int gridPosition, LevelRoomType? type, bool isVisited, bool isCurrent, bool? isCleared)
        {
            RoomId = roomId;
            GridPosition = gridPosition;
            Type = type;
            IsVisited = isVisited;
            IsCurrent = isCurrent;
            IsCleared = isCleared;
        }
    }

    /// <summary>
    /// 小地图中从已访问地图可知的一条双向连接。
    /// </summary>
    public readonly struct LevelMinimapConnection
    {
        public readonly int FromRoomId;
        public readonly int ToRoomId;

        /// <summary>
        /// 创建小地图连接。
        /// </summary>
        /// <param name="fromRoomId">较小的地图标识。</param>
        /// <param name="toRoomId">较大的地图标识。</param>
        public LevelMinimapConnection(int fromRoomId, int toRoomId)
        {
            FromRoomId = fromRoomId;
            ToRoomId = toRoomId;
        }
    }

    /// <summary>
    /// 专供界面使用的小地图投影，类型中没有撤离字段。
    /// </summary>
    public sealed class LevelMinimapProjection
    {
        public IReadOnlyList<LevelMinimapRoom> Rooms { get; }
        public IReadOnlyList<LevelMinimapConnection> Connections { get; }

        /// <summary>
        /// 创建只读小地图投影。
        /// </summary>
        /// <param name="rooms">允许显示的地图节点。</param>
        /// <param name="connections">允许显示的连接。</param>
        public LevelMinimapProjection(IReadOnlyList<LevelMinimapRoom> rooms, IReadOnlyList<LevelMinimapConnection> connections)
        {
            Rooms = rooms;
            Connections = connections;
        }
    }
}
