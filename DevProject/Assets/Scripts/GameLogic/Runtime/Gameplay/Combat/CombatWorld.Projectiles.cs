using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 飞弹实体的生成、积分、连续碰撞与终止系统。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 按配置生成飞弹，瞬时模式在本次效果队列内立即查询。
        /// </summary>
        /// <param name="sourceRef">发射者引用。</param>
        /// <param name="projectileId">飞弹定义编号。</param>
        /// <param name="targetRef">目标角色引用，可为空。</param>
        /// <param name="targetPoint">目标点快照。</param>
        /// <param name="forward">发射方向。</param>
        /// <returns>配置、来源和目标均有效则为真。</returns>
        public bool TrySpawnProjectile(CombatEntityRef sourceRef, int projectileId, CombatEntityRef targetRef,
            Vector3 targetPoint, Vector3 forward)
        {
            ActorComponent source = null;
            var hasSource = IsValid(sourceRef) && actors.TryGetValue(sourceRef.Id, out source);
            CombatOffense offense = hasSource ? new CombatOffense(source.Spec) : CaptureOffense(0);
            var rangeDelta = hasSource && source.Spec.IsPlayer ? source.Spec.AttackRange - catalog.InitialAttackRange : 0f;
            return TrySpawnProjectileInternal(sourceRef, projectileId, targetRef, targetPoint, forward, null, false, offense, rangeDelta);
        }

        /// <summary>
        /// 以可选的事件中心生成飞弹，保持原始发射者归因。
        /// </summary>
        /// <param name="sourceRef">原始发射者引用。</param>
        /// <param name="projectileId">飞弹定义编号。</param>
        /// <param name="targetRef">目标角色引用。</param>
        /// <param name="targetPoint">目标点快照。</param>
        /// <param name="forward">发射方向。</param>
        /// <param name="originOverride">来源锚点的事件中心覆盖位置。</param>
        /// <param name="allowDeadSource">是否允许已发射飞弹在来源死亡后继续生成子效果。</param>
        /// <param name="offense">飞弹出生时冻结的进攻属性。</param>
        /// <param name="rangeDelta">施放时攻击范围相对目录初值的差值。</param>
        /// <returns>生成成功则为真。</returns>
        private bool TrySpawnProjectileInternal(CombatEntityRef sourceRef, int projectileId, CombatEntityRef targetRef,
            Vector3 targetPoint, Vector3 forward, Vector3? originOverride, bool allowDeadSource, CombatOffense offense, float rangeDelta)
        {
            if (!IsValid(sourceRef) || !actors.TryGetValue(sourceRef.Id, out var source) ||
                (!allowDeadSource && source.DeadPending) ||
                source.Spec.RoomId != activeRoomId || !catalog.Projectiles.TryGetValue(projectileId, out var definition) ||
                !ValidProjectile(definition) || !IsFinite(targetPoint) || !IsFinite(forward)) return false;
            ActorComponent target = null;
            var hasTarget = IsValid(targetRef) && actors.TryGetValue(targetRef.Id, out target) &&
                !target.DeadPending && target.Spec.RoomId == source.Spec.RoomId;
            if ((definition.Target == ProjectileTargetKind.EntityLock ||
                 definition.Target == ProjectileTargetKind.EntitySnapshot ||
                 definition.SpawnAnchor == ProjectileSpawnAnchor.TargetEntityAtSpawn) && !hasTarget) return false;
            if (definition.Target == ProjectileTargetKind.EntityLock &&
                definition.Motion != ProjectileMotionKind.Straight) return false;
            if (definition.Motion == ProjectileMotionKind.FixedEndpointArc && definition.Acceleration != 0f) return false;
            var direction = new Vector3(forward.x, forward.y, forward.z).normalized;
            if (direction.sqrMagnitude < 0.0001f) direction = source.Forward;
            var origin = originOverride ?? source.Position;
            if (definition.SpawnAnchor == ProjectileSpawnAnchor.TargetPointSnapshot) origin = targetPoint;
            else if (definition.SpawnAnchor == ProjectileSpawnAnchor.TargetEntityAtSpawn) origin = target.Position;
            origin += Quaternion.LookRotation(direction) * definition.SpawnOffset;
            if (!IsFinite(origin) || !CanOccupy(source.Spec.RoomId, origin, definition.Radius)) return false;
            if (definition.SpawnAnchor == ProjectileSpawnAnchor.Source && !originOverride.HasValue &&
                HasWallBetween(source.Spec.RoomId, source.Position, origin)) return false;
            if (definition.Target == ProjectileTargetKind.EntitySnapshot ||
                definition.Target == ProjectileTargetKind.EntityLock) targetPoint = target.Position;
            if (definition.Target == ProjectileTargetKind.PointSnapshot ||
                definition.Target == ProjectileTargetKind.EntitySnapshot)
            {
                var toTarget = targetPoint - origin;
                if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;
            }
            if (definition.Target == ProjectileTargetKind.EntityLock)
            {
                var toTarget = target.Position - origin;
                if (toTarget.sqrMagnitude > 0.0001f) direction = toTarget.normalized;
            }
            var entity = Allocate();
            var component = new ProjectileComponent
            {
                ProjectileId = projectileId, RangeDelta = rangeDelta, RoomId = source.Spec.RoomId, InstigatorActorId = source.Spec.ActorId,
                Offense = offense, Source = sourceRef, Target = hasTarget ? targetRef : default, LastTargetPoint = targetPoint,
                Position = origin, Previous = origin, Origin = origin, EndPoint = targetPoint,
                Direction = direction, Velocity = direction * definition.Speed, CreatedTick = TickIndex,
                FlightTicks = definition.Motion == ProjectileMotionKind.Instant ? 0 :
                    definition.FlightTicks > 0 ? definition.FlightTicks :
                    Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(origin, targetPoint) /
                        (definition.Speed * TickSeconds)))
            };
            if (definition.Motion == ProjectileMotionKind.GravityArc && definition.FlightTicks > 0)
            {
                var time = component.FlightTicks * TickSeconds;
                component.Velocity = (targetPoint - origin - 0.5f * definition.Gravity * time * time) / time;
            }
            if (definition.Motion == ProjectileMotionKind.FixedEndpointArc && definition.ArcHeight != 0f &&
                component.FlightTicks < 2) component.FlightTicks = 2;
            projectiles.Add(entity.Id, component);
            if (definition.Motion == ProjectileMotionKind.Instant) ResolveInstant(entity.Id, component, definition);
            return true;
        }

        /// <summary>
        /// 把所有飞弹位置复制给表现层。
        /// </summary>
        /// <param name="destination">接收只读飞弹快照的列表。</param>
        /// <returns>写入的飞弹数量。</returns>
        public int GetProjectilesSnapshot(List<CombatProjectileSnapshot> destination)
        {
            if (destination == null) return 0;
            destination.Clear();
            var projectileIds = new List<int>(projectiles.Keys);
            projectileIds.Sort();
            foreach (var id in projectileIds)
            {
                var projectile = projectiles[id];
                if (projectile.Terminal) continue;
                destination.Add(new CombatProjectileSnapshot(Ref(id), projectile.ProjectileId, projectile.RoomId,
                    projectile.Position, projectile.Direction));
            }
            return destination.Count;
        }

        /// <summary>
        /// 在创建点进行一次瞬时命中判定。
        /// </summary>
        /// <param name="id">飞弹实体槽位。</param>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="definition">飞弹配置。</param>
        private void ResolveInstant(int id, ProjectileComponent projectile, CombatProjectileDefinition definition)
        {
            var targets = new List<int>(actors.Keys);
            targets.Sort();
            foreach (var targetId in targets)
            {
                var target = actors[targetId];
                if (!CanProjectileHit(projectile, definition, targetId, target) ||
                    Vector3.Distance(projectile.Position, target.Position) > definition.Radius + target.Spec.Radius) continue;
                RegisterProjectileHit(id, projectile, definition, targetId);
                if (projectile.DistinctHits >= definition.MaxDistinctTargets) break;
            }
            if (projectile.DistinctHits == 0) QueueProjectileEffects(id, projectile, definition.OnArrive, default);
            EndProjectile(id, projectile, definition, false);
        }

        /// <summary>
        /// 推进活跃地图内已存在的飞弹。
        /// </summary>
        private void MoveProjectiles()
        {
            var projectileIds = new List<int>(projectiles.Keys);
            projectileIds.Sort();
            foreach (var id in projectileIds)
            {
                var projectile = projectiles[id];
                if (projectile.Terminal || projectile.RoomId != activeRoomId ||
                    projectile.CreatedTick >= TickIndex ||
                    !catalog.Projectiles.TryGetValue(projectile.ProjectileId, out var definition)) continue;
                if (definition.RemoveOnSourceDeath && (!IsValid(projectile.Source) ||
                    (actors.TryGetValue(projectile.Source.Id, out var source) && source.DeadPending)))
                { EndProjectile(id, projectile, definition, true); continue; }
                if (definition.Target == ProjectileTargetKind.EntityLock)
                {
                    ActorComponent target = null;
                    var targetValid = IsValid(projectile.Target) &&
                        actors.TryGetValue(projectile.Target.Id, out target) &&
                        !target.DeadPending && target.Spec.RoomId == projectile.RoomId;
                    if (!targetValid)
                    {
                        if (definition.LostTarget == ProjectileLostTarget.Expire)
                        { EndProjectile(id, projectile, definition, true); continue; }
                        if (definition.LostTarget == ProjectileLostTarget.LastPoint)
                        {
                            var delta = projectile.LastTargetPoint - projectile.Position;
                            if (delta.sqrMagnitude > 0.0001f) projectile.Direction = delta.normalized;
                        }
                    }
                    else
                    {
                        projectile.LastTargetPoint = target.Position;
                        var toward = target.Position - projectile.Position;
                        if (toward.sqrMagnitude > 0.0001f)
                            projectile.Direction = Vector3.RotateTowards(projectile.Direction,
                                toward.normalized, definition.TurnDegreesPerSecond * Mathf.Deg2Rad * TickSeconds, 0f);
                    }
                }
                var old = projectile.Position;
                var next = CalculateNextPosition(projectile, definition);
                var remaining = Mathf.Max(0f, definition.MaxDistance - projectile.Distance);
                var distance = Vector3.Distance(old, next);
                if (distance > remaining && distance > 0f) next = Vector3.Lerp(old, next, remaining / distance);
                SweepProjectile(id, projectile, definition, old, next);
                projectile.ElapsedTicks++;
                if (projectile.Terminal) continue;
                if (definition.Motion == ProjectileMotionKind.FixedEndpointArc &&
                    projectile.ElapsedTicks >= projectile.FlightTicks)
                {
                    QueueProjectileEffects(id, projectile, definition.OnArrive, default);
                    EndProjectile(id, projectile, definition, false);
                }
                else if (projectile.ElapsedTicks >= definition.LifetimeTicks ||
                         projectile.Distance >= definition.MaxDistance - 0.0001f)
                    EndProjectile(id, projectile, definition, true);
                else if (definition.Motion == ProjectileMotionKind.Straight &&
                         definition.Target != ProjectileTargetKind.Direction &&
                         definition.Target != ProjectileTargetKind.EntityLock &&
                         Vector3.Distance(projectile.Position, projectile.EndPoint) <= definition.Radius)
                {
                    QueueProjectileEffects(id, projectile, definition.OnArrive, default);
                    EndProjectile(id, projectile, definition, false);
                }
            }
        }

        /// <summary>
        /// 计算飞弹在下一逻辑帧的候选位置。
        /// </summary>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="definition">飞弹配置。</param>
        /// <returns>尚未经过碰撞裁剪的候选位置。</returns>
        private static Vector3 CalculateNextPosition(ProjectileComponent projectile, CombatProjectileDefinition definition)
        {
            if (definition.Motion == ProjectileMotionKind.FixedEndpointArc)
            {
                var u = Mathf.Clamp01((projectile.ElapsedTicks + 1f) / projectile.FlightTicks);
                return Vector3.Lerp(projectile.Origin, projectile.EndPoint, u) +
                    Vector3.up * (4f * definition.ArcHeight * u * (1f - u));
            }
            if (definition.Motion == ProjectileMotionKind.GravityArc)
            {
                var next = projectile.Position + projectile.Velocity * TickSeconds +
                    0.5f * definition.Gravity * TickSeconds * TickSeconds;
                projectile.Velocity += definition.Gravity * TickSeconds;
                return next;
            }
            var speed = Mathf.Clamp(projectile.Velocity.magnitude + definition.Acceleration * TickSeconds,
                0f, definition.MaxSpeed > 0f ? definition.MaxSpeed : definition.Speed);
            projectile.Velocity = projectile.Direction.normalized * speed;
            return projectile.Position + projectile.Velocity * TickSeconds;
        }

        /// <summary>
        /// 计算飞弹穿出地图矩形边界时的首次接触。
        /// </summary>
        /// <param name="start">当前线段起点。</param>
        /// <param name="end">当前线段终点。</param>
        /// <param name="bounds">地图 XZ 边界。</param>
        /// <param name="radius">飞弹碰撞半径。</param>
        /// <param name="t">离开边界的线段比例。</param>
        /// <param name="normal">指向地图内侧的边界法线。</param>
        /// <returns>本段飞行将离开地图边界则为真。</returns>
        private static bool SweepRoomBoundary(Vector3 start, Vector3 end, Rect bounds, float radius,
            out float t, out Vector3 normal)
        {
            t = 1f;
            normal = Vector3.zero;
            var minX = bounds.xMin + radius;
            var maxX = bounds.xMax - radius;
            var minZ = bounds.yMin + radius;
            var maxZ = bounds.yMax - radius;
            var dx = end.x - start.x;
            var dz = end.z - start.z;
            if (end.x < minX && dx < 0f)
            {
                var candidate = (minX - start.x) / dx;
                if (candidate >= 0f && candidate <= t) { t = candidate; normal = Vector3.right; }
            }
            if (end.x > maxX && dx > 0f)
            {
                var candidate = (maxX - start.x) / dx;
                if (candidate >= 0f && candidate <= t) { t = candidate; normal = Vector3.left; }
            }
            if (end.z < minZ && dz < 0f)
            {
                var candidate = (minZ - start.z) / dz;
                if (candidate >= 0f && candidate <= t) { t = candidate; normal = Vector3.forward; }
            }
            if (end.z > maxZ && dz > 0f)
            {
                var candidate = (maxZ - start.z) / dz;
                if (candidate >= 0f && candidate <= t) { t = candidate; normal = Vector3.back; }
            }
            return normal != Vector3.zero;
        }

        /// <summary>
        /// 沿候选线段连续扫掠墙体与角色。
        /// </summary>
        /// <param name="id">飞弹槽位。</param>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="definition">飞弹配置。</param>
        /// <param name="start">本次扫掠起点。</param>
        /// <param name="end">本次扫掠终点。</param>
        private void SweepProjectile(int id, ProjectileComponent projectile, CombatProjectileDefinition definition,
            Vector3 start, Vector3 end)
        {
            var remainingStart = start;
            var remainingEnd = end;
            for (var contact = 0; contact < 16 && !projectile.Terminal; contact++)
            {
                var bestT = 1.00001f;
                var wallNormal = Vector3.zero;
                var hitActorId = 0;
                if (roomBounds.TryGetValue(projectile.RoomId, out var bounds) &&
                    SweepRoomBoundary(remainingStart, remainingEnd, bounds, definition.Radius, out var boundaryT,
                        out var boundaryNormal))
                { bestT = boundaryT; wallNormal = boundaryNormal; }
                if (obstacles.TryGetValue(projectile.RoomId, out var walls))
                    foreach (var wall in walls)
                        if (SweepAabb(remainingStart, remainingEnd, wall, definition.Radius, out var t, out var normal) &&
                            t < bestT)
                        { bestT = t; wallNormal = normal; hitActorId = 0; }
                var actorIds = new List<int>(actors.Keys);
                actorIds.Sort();
                foreach (var actorId in actorIds)
                {
                    var actor = actors[actorId];
                    if (!CanProjectileHit(projectile, definition, actorId, actor) ||
                        !SweepCircle(remainingStart, remainingEnd, actor.Position,
                            definition.Radius + actor.Spec.Radius, out var t) || t >= bestT - 0.000001f) continue;
                    bestT = t;
                    hitActorId = actorId;
                    wallNormal = Vector3.zero;
                }
                if (bestT > 1f)
                {
                    projectile.Distance += Vector3.Distance(remainingStart, remainingEnd);
                    projectile.Position = remainingEnd;
                    return;
                }
                var contactPoint = Vector3.Lerp(remainingStart, remainingEnd, Mathf.Clamp01(bestT));
                projectile.Distance += Vector3.Distance(remainingStart, contactPoint);
                projectile.Position = contactPoint;
                if (hitActorId != 0)
                {
                    RegisterProjectileHit(id, projectile, definition, hitActorId);
                    if (projectile.DistinctHits >= definition.MaxDistinctTargets)
                    { EndProjectile(id, projectile, definition, false); return; }
                    var travel = remainingEnd - remainingStart;
                    remainingStart = contactPoint + travel.normalized * 0.001f;
                    if ((remainingEnd - remainingStart).sqrMagnitude < 0.000001f) return;
                    continue;
                }
                if (projectile.Bounces >= definition.MaxBounces ||
                    definition.Motion == ProjectileMotionKind.FixedEndpointArc)
                { EndProjectile(id, projectile, definition, false); return; }
                projectile.Bounces++;
                var leftover = remainingEnd - contactPoint;
                var reflected = Vector3.Reflect(leftover, wallNormal);
                projectile.Direction = Vector3.Reflect(projectile.Direction, wallNormal).normalized;
                projectile.Velocity = Vector3.Reflect(projectile.Velocity, wallNormal);
                remainingStart = contactPoint + projectile.Direction * 0.001f;
                remainingEnd = remainingStart + reflected;
            }
        }

        /// <summary>
        /// 判断飞弹是否允许命中指定角色。
        /// </summary>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="definition">飞弹配置。</param>
        /// <param name="targetId">目标实体槽位。</param>
        /// <param name="target">目标角色。</param>
        /// <returns>阵营、地图、死亡状态及去重均允许则为真。</returns>
        private bool CanProjectileHit(ProjectileComponent projectile, CombatProjectileDefinition definition, int targetId,
            ActorComponent target)
        {
            if (target.DeadPending || target.Spec.RoomId != projectile.RoomId || target.Spec.ActorId == projectile.InstigatorActorId ||
                (definition.OnlyLockedTarget && targetId != projectile.Target.Id)) return false;
            if (IsValid(projectile.Source) && actors.TryGetValue(projectile.Source.Id, out var source) &&
                source.Spec.Team == target.Spec.Team) return false;
            var targetRef = Ref(targetId);
            if (!projectile.Hits.TryGetValue(targetRef, out var hit)) return true;
            if (hit.Count >= definition.MaxHitsPerTarget) return false;
            return definition.SameTargetIntervalTicks > 0 && TickIndex - hit.Tick >= definition.SameTargetIntervalTicks;
        }

        /// <summary>
        /// 计算扫掠线段首次进入角色圆形的比例。
        /// </summary>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <param name="center">角色中心。</param>
        /// <param name="radius">双方半径之和。</param>
        /// <param name="t">首次接触的线段比例。</param>
        /// <returns>扫掠命中角色则为真。</returns>
        private static bool SweepCircle(Vector3 start, Vector3 end, Vector3 center, float radius, out float t)
        {
            t = 0f;
            var delta = end - start;
            var from = start - center;
            if (Mathf.Abs(start.y - center.y) > 2f && Mathf.Abs(end.y - center.y) > 2f) return false;
            delta.y = 0f;
            from.y = 0f;
            var a = Vector3.Dot(delta, delta);
            if (a < 0.0000001f) return from.sqrMagnitude <= radius * radius;
            var b = 2f * Vector3.Dot(from, delta);
            var c = Vector3.Dot(from, from) - radius * radius;
            if (c <= 0f) return true;
            var discriminant = b * b - 4f * a * c;
            if (discriminant < 0f) return false;
            t = (-b - Mathf.Sqrt(discriminant)) / (2f * a);
            return t >= 0f && t <= 1f;
        }

        /// <summary>
        /// 记录合法飞弹命中并把命中效果加入 FIFO 队列。
        /// </summary>
        /// <param name="id">飞弹实体槽位。</param>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="definition">飞弹配置。</param>
        /// <param name="targetId">命中角色槽位。</param>
        private void RegisterProjectileHit(int id, ProjectileComponent projectile, CombatProjectileDefinition definition, int targetId)
        {
            var targetRef = Ref(targetId);
            if (!projectile.Hits.TryGetValue(targetRef, out var hit)) projectile.DistinctHits++;
            hit.Tick = TickIndex;
            hit.Count++;
            projectile.Hits[targetRef] = hit;
            events.Add(new CombatEvent(CombatEventKind.ProjectileHit, TickIndex, projectile.RoomId,
                projectile.InstigatorActorId, actors[targetId].Spec.ActorId, projectile.ProjectileId, 0f, projectile.Position));
            QueueProjectileEffects(id, projectile, definition.OnHit, targetRef);
        }

        /// <summary>
        /// 把飞弹事件中的配置效果追加到统一效果队列。
        /// </summary>
        /// <param name="id">飞弹实体槽位。</param>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="effects">配置效果列表。</param>
        /// <param name="target">可选命中目标。</param>
        private void QueueProjectileEffects(int id, ProjectileComponent projectile, List<CombatEffectDefinition> effects,
            CombatEntityRef target)
        {
            foreach (var effect in effects)
                Enqueue(effect, id, target, projectile.Position, projectile.Direction, projectile.InstigatorActorId, projectile.Offense, projectile.RangeDelta);
        }

        /// <summary>
        /// 锁定飞弹终止原因并发出一次终止事件。
        /// </summary>
        /// <param name="id">飞弹实体槽位。</param>
        /// <param name="projectile">飞弹运行组件。</param>
        /// <param name="definition">飞弹配置。</param>
        /// <param name="expired">是否按到期效果结束。</param>
        private void EndProjectile(int id, ProjectileComponent projectile, CombatProjectileDefinition definition, bool expired)
        {
            if (projectile.Terminal) return;
            projectile.Terminal = true;
            if (expired) QueueProjectileEffects(id, projectile, definition.OnExpire, default);
            events.Add(new CombatEvent(CombatEventKind.ProjectileEnded, TickIndex, projectile.RoomId,
                projectile.InstigatorActorId, 0, projectile.ProjectileId, expired ? 1f : 0f, projectile.Position));
        }
    }
}
