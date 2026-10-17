using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 在死亡事实已消费的完整帧边界，将当前地图的真实玩家原地恢复至最大生命。
        /// 保留角色身份、永久属性、词条与技能冷却，临时 Buff 和施法不补回，已启用常驻辅助在轮询阶段恢复控制状态。
        /// </summary>
        /// <param name="actor">当前世界登记的玩家实体引用。</param>
        /// <returns>玩家已完成死亡发布且原位置可占用并成功复活时为真。</returns>
        public bool TryRevivePlayer(CombatEntityRef actor)
        {
            if (faulted || stepping || effectQueue.Count != 0 || !IsValid(actor) || actor.Id != playerId ||
                !actors.TryGetValue(actor.Id, out var state) || !state.Spec.IsPlayer || state.Spec.RoomId != activeRoomId ||
                !state.DeadPending || !state.DeathPublished || !CanOccupy(state.Spec.RoomId, state.Position, state.Spec.Radius, actor.Id))
                return false;
            foreach (var fact in events)
                if (fact.Kind == CombatEventKind.Death && fact.TargetActorId == state.Spec.ActorId) return false;

            state.Health = state.Spec.MaxHealth;
            state.DeadPending = false;
            state.DeathPublished = false;
            state.LastInstigatorActorId = 0;
            state.Move = Vector2.zero;
            state.HasNavigationWaypoint = false;
            state.NavigationWaypoint = Vector3.zero;
            state.Velocity = Vector3.zero;
            state.CueDirection = Vector3.zero;
            state.CueId = 0;
            state.CueDurationTicks = 0;
            state.CueClock = -1;
            state.HitClock = -1;
            state.DeathClock = -1;
            return true;
        }
    }
}
