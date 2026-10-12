using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 提交玩家下一路点，在每个逻辑帧内按最终移速限步并执行原有连续碰撞。
        /// 路点属于临时输入，不写入快照；普通移动输入和换区会清除该输入。
        /// </summary>
        /// <param name="actor">当前地图中的存活玩家。</param>
        /// <param name="waypoint">已经通过路径查询的下一路点。</param>
        /// <returns>路点输入已接纳时为真。</returns>
        public bool SetNavigationWaypoint(CombatEntityRef actor, Vector3 waypoint)
        {
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) || !state.Spec.IsPlayer ||
                state.DeadPending || state.Spec.RoomId != activeRoomId || !IsFinite(waypoint)) return false;
            waypoint.y = state.Position.y;
            state.NavigationWaypoint = waypoint;
            state.HasNavigationWaypoint = true;
            state.Move = DirectionTo(state.Position, waypoint);
            return true;
        }

        /// <summary>
        /// 读取导航所需的实际移动能力，战斗位移期间由导航层等待后重新规划。
        /// </summary>
        /// <param name="actor">当前地图中的存活角色。</param>
        /// <param name="speed">叠加移动速度修正后的非负速度。</param>
        /// <param name="displaced">是否正在执行击退等战斗位移。</param>
        /// <returns>角色处于有效移动生命周期时为真。</returns>
        public bool TryGetMovementStatus(CombatEntityRef actor, out float speed, out bool displaced)
        {
            speed = 0f;
            displaced = false;
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) ||
                state.DeadPending || state.Spec.RoomId != activeRoomId) return false;
            speed = Mathf.Max(0f, state.Spec.MoveSpeed + state.MoveBonus);
            displaced = FindDisplacement(actor.Id, out _);
            return true;
        }
    }
}
