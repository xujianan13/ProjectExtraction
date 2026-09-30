using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 单局内一张地图的稳定状态，不复制敌人生命值等战斗真值。
    /// </summary>
    public sealed class LevelRoomState
    {
        private readonly Dictionary<RoomDirection, int> exits = new Dictionary<RoomDirection, int>();
        private readonly Dictionary<string, int> npcStock = new Dictionary<string, int>();

        public int RoomId { get; }
        public Vector2Int GridPosition { get; }
        public LevelRoomType Type { get; }
        public bool IsStart { get; }
        public bool IsVisited { get; private set; }
        public bool IsCleared { get; private set; }
        public bool EnemiesSpawned { get; private set; }
        public bool ChestOpened { get; private set; }
        public bool HasExtractionCandidate { get; private set; }
        public bool ExtractionRequiresClear { get; private set; }
        public bool IsExtractionVisible => HasExtractionCandidate && (!ExtractionRequiresClear || IsCleared);
        public IReadOnlyDictionary<RoomDirection, int> Exits => exits;
        public IReadOnlyDictionary<string, int> NpcStock => npcStock;

        /// <summary>
        /// 创建单局地图状态。
        /// </summary>
        /// <param name="roomId">单局内稳定的地图标识。</param>
        /// <param name="gridPosition">地图图上的网格位置。</param>
        /// <param name="type">地图内容类型。</param>
        /// <param name="isStart">是否为起始地图。</param>
        public LevelRoomState(int roomId, Vector2Int gridPosition, LevelRoomType type, bool isStart)
        {
            RoomId = roomId;
            GridPosition = gridPosition;
            Type = type;
            IsStart = isStart;
        }

        /// <summary>
        /// 读取指定方向连接的地图。
        /// </summary>
        /// <param name="direction">出口方向。</param>
        /// <param name="destinationRoomId">找到时的目标地图标识。</param>
        /// <returns>出口存在时为真。</returns>
        public bool TryGetExit(RoomDirection direction, out int destinationRoomId) => exits.TryGetValue(direction, out destinationRoomId);

        /// <summary>
        /// 标记地图已经被进入。
        /// </summary>
        public void MarkVisited() => IsVisited = true;

        /// <summary>
        /// 标记敌人已按本局计划生成，回访时不可重生。
        /// </summary>
        public void MarkEnemiesSpawned() => EnemiesSpawned = true;

        /// <summary>
        /// 标记地图清理完成；敌人生命与死亡仍由战斗模块持有。
        /// </summary>
        public void MarkCleared() => IsCleared = true;

        /// <summary>
        /// 标记战利品箱已打开。
        /// </summary>
        public void MarkChestOpened() => ChestOpened = true;

        /// <summary>
        /// 读取 NPC 商品当前库存。
        /// </summary>
        /// <param name="offerId">报价标识。</param>
        /// <param name="quantity">成功时的剩余数量。</param>
        /// <returns>报价库存存在时为真。</returns>
        public bool TryGetNpcStock(string offerId, out int quantity)
        {
            quantity = 0;
            return Type == LevelRoomType.Npc && !string.IsNullOrEmpty(offerId) && npcStock.TryGetValue(offerId, out quantity);
        }

        /// <summary>
        /// 设置 NPC 商品剩余库存，不负责交易扣款。
        /// </summary>
        /// <param name="offerId">报价标识。</param>
        /// <param name="quantity">非负的剩余数量。</param>
        /// <returns>输入合法并已更新时为真。</returns>
        public bool TrySetNpcStock(string offerId, int quantity)
        {
            if (Type != LevelRoomType.Npc || string.IsNullOrEmpty(offerId) || quantity < 0) return false;
            npcStock[offerId] = quantity;
            return true;
        }

        /// <summary>
        /// 增加一条已生成的出口连接。
        /// </summary>
        /// <param name="direction">出口方向。</param>
        /// <param name="destinationRoomId">目标地图标识。</param>
        /// <returns>该方向原本没有连接时为真。</returns>
        internal bool TryAddExit(RoomDirection direction, int destinationRoomId)
        {
            if (destinationRoomId <= 0 || exits.ContainsKey(direction)) return false;
            exits.Add(direction, destinationRoomId);
            return true;
        }

        /// <summary>
        /// 设置生成阶段预抽的撤离候选。
        /// </summary>
        /// <param name="requiresClear">是否清图后才显现。</param>
        internal void SetExtractionCandidate(bool requiresClear)
        {
            HasExtractionCandidate = true;
            ExtractionRequiresClear = requiresClear;
        }
    }
}
