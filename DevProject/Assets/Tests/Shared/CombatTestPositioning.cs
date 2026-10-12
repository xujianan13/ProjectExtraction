using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using UnityEngine;

namespace ProjectExtraction.Tests.Shared
{
    /// <summary>
    /// 在真实战斗碰撞约束内寻找测试角色的近战站位，不更改地图或角色半径。
    /// </summary>
    public static class CombatTestPositioning
    {
        /// <summary>
        /// 根据双方实际半径及攻击距离采样合法站位，拒绝障碍遮挡与动态重叠。
        /// </summary>
        /// <param name="world">处于完整提交边界的真实战斗世界。</param>
        /// <param name="actor">需要移动的存活角色。</param>
        /// <param name="target">需要接近的同地图存活角色。</param>
        /// <param name="desiredRange">最大中心距离；非正数使用移动角色的实际基础攻击范围。</param>
        /// <returns>找到视线通畅且由权威碰撞入口接受的位置时为真。</returns>
        public static bool TryPlaceAdjacent(CombatWorld world, CombatEntityRef actor, CombatEntityRef target, float desiredRange = 0f)
        {
            if (!world.TryGetActor(actor, out CombatActorSnapshot sourceView) || sourceView.Dead ||
                !world.TryGetActor(target, out CombatActorSnapshot targetView) || targetView.Dead ||
                sourceView.RoomId != targetView.RoomId || !world.TryExportState(out CombatWorldSnapshot state, out _)) return false;
            CombatActorState source = state.Actors.Find(value => value.Spec.ActorId == sourceView.ActorId);
            CombatActorState destination = state.Actors.Find(value => value.Spec.ActorId == targetView.ActorId);
            float minimum = (source.Spec.Radius + destination.Spec.Radius) * 1.02f;
            float maximum = desiredRange > 0f ? desiredRange : source.Spec.AttackRange;
            if (float.IsNaN(maximum) || float.IsInfinity(maximum) || maximum < minimum) return false;
            CombatRoomState room = state.Rooms.Find(value => value.RoomId == source.Spec.RoomId);
            Vector3 bearing = source.Position - destination.Position;
            bearing.y = 0f;
            bearing = bearing.sqrMagnitude > 0f ? bearing.normalized : Vector3.back;
            for (int ring = 0; ring < 4; ring++)
            {
                float distance = Mathf.Lerp(minimum, maximum, ring / 3f);
                for (int sample = 0; sample < 32; sample++)
                {
                    Vector3 candidate = destination.Position + Quaternion.Euler(0f, sample * 360f / 32f, 0f) * bearing * distance;
                    candidate.y = source.Position.y;
                    if (ClearLine(candidate, destination.Position, room.Obstacles) && world.TrySetPosition(actor, candidate)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 使用矩形障碍的精确射线交点排除近战中心连线穿墙的位置。
        /// </summary>
        /// <param name="from">采样站位。</param>
        /// <param name="to">目标角色中心。</param>
        /// <param name="obstacles">当前地图的权威矩形障碍。</param>
        /// <returns>整段中心连线无障碍交点时为真。</returns>
        private static bool ClearLine(Vector3 from, Vector3 to, IReadOnlyList<Rect> obstacles)
        {
            Vector3 delta = to - from;
            delta.y = 0f;
            Vector3 origin = new Vector3(from.x, 0f, from.z);
            var ray = new Ray(origin, delta.normalized);
            foreach (Rect obstacle in obstacles)
            {
                var bounds = new Bounds(new Vector3(obstacle.center.x, 0f, obstacle.center.y),
                    new Vector3(obstacle.width, 1f, obstacle.height));
                if (bounds.IntersectRay(ray, out float distance) && distance <= delta.magnitude) return false;
            }
            return true;
        }
    }
}
