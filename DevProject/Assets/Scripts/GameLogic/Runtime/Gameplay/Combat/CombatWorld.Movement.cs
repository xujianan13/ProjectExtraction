using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 角色占位与连续碰撞。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 检查墙体与地图边界的占位。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="position">待检查位置。</param>
        /// <param name="radius">占位半径。</param>
        /// <returns>位置允许占用则为真。</returns>
        private bool CanOccupy(int roomId, Vector3 position, float radius)
        {
            if (roomBounds.TryGetValue(roomId, out var bounds) &&
                (position.x - radius < bounds.xMin || position.x + radius > bounds.xMax ||
                 position.z - radius < bounds.yMin || position.z + radius > bounds.yMax)) return false;
            if (!obstacles.TryGetValue(roomId, out var walls)) return true;
            foreach (var wall in walls)
                if (position.x + radius > wall.xMin && position.x - radius < wall.xMax &&
                    position.z + radius > wall.yMin && position.z - radius < wall.yMax) return false;
            return true;
        }

        /// <summary>
        /// 检查墙体及所有存活角色的 XZ 圆形占位。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="position">待检查位置。</param>
        /// <param name="radius">占位半径。</param>
        /// <param name="ignoreActorId">需要忽略的自身槽位。</param>
        /// <returns>未被占用则为真。</returns>
        private bool CanOccupy(int roomId, Vector3 position, float radius, int ignoreActorId)
        {
            if (!CanOccupy(roomId, position, radius)) return false;
            foreach (var pair in actors)
            {
                var other = pair.Value;
                if (pair.Key == ignoreActorId || other.DeadPending || other.Spec.RoomId != roomId) continue;
                var dx = position.x - other.Position.x;
                var dz = position.z - other.Position.z;
                var sum = radius + other.Spec.Radius;
                if (dx * dx + dz * dz < sum * sum) return false;
            }
            return true;
        }

        /// <summary>
        /// 沿期望位移连续扫掠，并在接触后沿切面滑动。
        /// </summary>
        /// <param name="actorId">移动角色槽位。</param>
        /// <param name="actor">移动角色状态。</param>
        /// <param name="delta">本帧期望位移。</param>
        private void MoveActor(int actorId, ActorComponent actor, Vector3 delta)
        {
            delta.y = 0f;
            if (delta.sqrMagnitude < 0.0000001f) return;
            var origin = actor.Position;
            var position = origin;
            var remaining = delta;
            for (var iteration = 0; iteration < 3 && remaining.sqrMagnitude > 0.0000001f; iteration++)
            {
                if (!SweepMovement(actorId, actor, position, position + remaining, out var hitT, out var normal))
                {
                    position += remaining;
                    break;
                }
                position += remaining * Mathf.Max(0f, hitT - 0.0001f / remaining.magnitude);
                remaining *= 1f - hitT;
                var intoSurface = Vector3.Dot(remaining, normal);
                if (intoSurface < 0f) remaining -= normal * intoSurface;
                else break;
            }
            actor.Position = position;
            actor.Velocity += (position - origin) / TickSeconds;
            actor.Forward = delta.normalized;
        }

        /// <summary>
        /// 查找线段上最早的墙体、边界或角色接触。
        /// </summary>
        /// <param name="actorId">移动角色槽位。</param>
        /// <param name="actor">移动角色状态。</param>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <param name="hitT">接触比例。</param>
        /// <param name="normal">接触法线。</param>
        /// <returns>发生阻挡则为真。</returns>
        private bool SweepMovement(int actorId, ActorComponent actor, Vector3 start, Vector3 end, out float hitT, out Vector3 normal)
        {
            hitT = 1f;
            normal = Vector3.zero;
            var motion = end - start;
            var radius = actor.Spec.Radius;
            if (roomBounds.TryGetValue(actor.Spec.RoomId, out var bounds))
            {
                CheckBoundary(start.x, motion.x, bounds.xMin + radius, bounds.xMax - radius, Vector3.right, ref hitT, ref normal);
                CheckBoundary(start.z, motion.z, bounds.yMin + radius, bounds.yMax - radius, Vector3.forward, ref hitT, ref normal);
            }
            if (obstacles.TryGetValue(actor.Spec.RoomId, out var walls))
                foreach (var wall in walls)
                    if (SweepAabb(start, end, wall, radius, out var t, out var wallNormal) &&
                        wallNormal.sqrMagnitude > 0f && Vector3.Dot(motion, wallNormal) < 0f && t < hitT)
                    { hitT = t; normal = wallNormal; }
            var actorIds = new List<int>(actors.Keys);
            SortEntityIds(actorIds);
            foreach (var id in actorIds)
            {
                var other = actors[id];
                if (id == actorId || other.DeadPending || other.Spec.RoomId != actor.Spec.RoomId) continue;
                if (SweepCircle(start, end, other.Position, radius + other.Spec.Radius, out var t, out var actorNormal) &&
                    Vector3.Dot(motion, actorNormal) < 0f && t < hitT)
                { hitT = t; normal = actorNormal; }
            }
            return normal.sqrMagnitude > 0f;
        }

        /// <summary>
        /// 更新单一坐标轴的边界接触。
        /// </summary>
        /// <param name="start">坐标起点。</param>
        /// <param name="delta">坐标位移。</param>
        /// <param name="minimum">合法最小坐标。</param>
        /// <param name="maximum">合法最大坐标。</param>
        /// <param name="positiveNormal">最小边界指向内部的法线。</param>
        /// <param name="hitT">当前最早接触比例。</param>
        /// <param name="normal">当前接触法线。</param>
        private static void CheckBoundary(float start, float delta, float minimum, float maximum,
            Vector3 positiveNormal, ref float hitT, ref Vector3 normal)
        {
            if (delta < 0f && start + delta < minimum)
            {
                var t = Mathf.Max(0f, (minimum - start) / delta);
                if (t < hitT) { hitT = t; normal = positiveNormal; }
            }
            else if (delta > 0f && start + delta > maximum)
            {
                var t = Mathf.Max(0f, (maximum - start) / delta);
                if (t < hitT) { hitT = t; normal = -positiveNormal; }
            }
        }

        /// <summary>
        /// 解移动圆和静止圆在 XZ 平面的第一次接触。
        /// </summary>
        /// <param name="start">移动圆心起点。</param>
        /// <param name="end">移动圆心终点。</param>
        /// <param name="center">静止圆心。</param>
        /// <param name="radius">两圆半径之和。</param>
        /// <param name="hitT">接触比例。</param>
        /// <param name="normal">接触法线。</param>
        /// <returns>线段接触圆则为真。</returns>
        private static bool SweepCircle(Vector3 start, Vector3 end, Vector3 center, float radius, out float hitT, out Vector3 normal)
        {
            hitT = 0f;
            normal = Vector3.zero;
            var delta = new Vector2(end.x - start.x, end.z - start.z);
            var offset = new Vector2(start.x - center.x, start.z - center.z);
            var a = Vector2.Dot(delta, delta);
            if (a < 0.00000001f) return false;
            var b = Vector2.Dot(offset, delta);
            var c = Vector2.Dot(offset, offset) - radius * radius;
            if (c <= 0f)
            {
                if (b >= 0f) return false;
                normal = new Vector3(offset.x, 0f, offset.y).normalized;
                return normal.sqrMagnitude > 0f;
            }
            var discriminant = b * b - a * c;
            if (discriminant < 0f) return false;
            var t = (-b - Mathf.Sqrt(discriminant)) / a;
            if (t < 0f || t > 1f) return false;
            hitT = t;
            var contact = offset + delta * t;
            normal = new Vector3(contact.x, 0f, contact.y).normalized;
            return true;
        }
    }
}
