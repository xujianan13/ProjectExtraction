using System;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 战斗世界的显式释放入口。
    /// </summary>
    public sealed partial class CombatWorld
    {
        public string FaultReason { get; private set; }
        public Action<string> ErrorLogger { get; set; }

        /// <summary>
        /// 标记配置或执行故障并通知注入的日志接口。
        /// </summary>
        /// <param name="reason">可诊断的故障原因。</param>
        private void MarkFault(string reason)
        {
            if (faulted) return;
            faulted = true;
            FaultReason = reason;
            ErrorLogger?.Invoke(reason);
        }

        /// <summary>
        /// 释放当前单局的组件、事件与地图状态，不触发战斗效果。
        /// </summary>
        public void Dispose()
        {
            actors.Clear();
            casts.Clear();
            displacements.Clear();
            projectiles.Clear();
            buffs.Clear();
            roomBounds.Clear();
            obstacles.Clear();
            navigationGraph = null;
            roomTicks.Clear();
            effectQueue.Clear();
            events.Clear();
            clearedRooms.Clear();
            freeIds.Clear();
            for (var id = 1; id < slots.Count; id++)
            {
                slots[id].Alive = false;
                slots[id].Generation++;
                freeIds.Push(id);
            }
            activeRoomId = 0;
            playerId = 0;
            accumulator = 0;
            faulted = false;
            FaultReason = null;
        }
    }
}
