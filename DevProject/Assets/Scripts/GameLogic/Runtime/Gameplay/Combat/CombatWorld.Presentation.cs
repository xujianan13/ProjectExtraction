using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 尚未发生的命中或飞弹发射的只读预警，不参与碰撞与伤害。
    /// </summary>
    public struct CombatTelegraphSnapshot
    {
        public CombatEntityRef Cast;
        public int ActorId;
        public int RoomId;
        public Vector3 Origin;
        public Vector3 Direction;
        public CombatHitShape Shape;
        public float Radius;
        public float Angle;
        public float Width;
        public float Progress;
        public bool IsProjectile;
        public bool IsPlayer;
    }

    /// <summary>
    /// 将当前施法的真实时间轴与命中配置投影为地面预警。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 导出当前房间正在蓄力的下一次伤害节点；已命中、取消及死亡的动作不再预警。
        /// </summary>
        /// <param name="output">接收预警的复用列表，调用时先清空。</param>
        public void GetTelegraphsSnapshot(List<CombatTelegraphSnapshot> output)
        {
            if (output == null) return;
            output.Clear();
            foreach (var pair in casts)
            {
                var cast = pair.Value;
                if (!actors.TryGetValue(cast.Owner, out var owner) || owner.DeadPending || owner.Spec.RoomId != activeRoomId ||
                    !catalog.Skills.TryGetValue(cast.SkillId, out var skill)) continue;
                long elapsed = (cast.UsesWorldClock ? TickIndex : GetRoomTick(owner.Spec.RoomId)) - cast.StartTick;
                for (int index = cast.NextNode; index < skill.Nodes.Count; index++)
                {
                    CombatTimelineNode node = skill.Nodes[index];
                    if (node.Tick <= elapsed) continue;
                    bool found = false;
                    foreach (CombatEffectDefinition effect in node.Effects)
                    {
                        bool missile = effect.Kind == CombatEffectKind.SpawnProjectile;
                        if (effect.Kind != CombatEffectKind.Damage && !missile) continue;
                        float radius = effect.AttackRangeSource ? Mathf.Max(0f, effect.Radius + cast.RangeDelta) : effect.Radius;
                        float width = effect.Width;
                        Vector3 origin = owner.Position;
                        Vector3 direction = cast.Forward;
                        CombatHitShape shape = effect.Shape;
                        if (missile)
                        {
                            if (!catalog.Projectiles.TryGetValue(effect.LinkedId, out var projectile)) continue;
                            Vector3 offset = Quaternion.LookRotation(cast.Forward) * projectile.SpawnOffset;
                            origin += offset;
                            if (projectile.SpawnAnchor != ProjectileSpawnAnchor.Source)
                            {
                                origin = cast.TargetPoint + offset;
                                if (projectile.SpawnAnchor == ProjectileSpawnAnchor.TargetEntityAtSpawn &&
                                    IsValid(cast.Target) && actors.TryGetValue(cast.Target.Id, out var target)) origin = target.Position + offset;
                                shape = CombatHitShape.Circle;
                                radius = Mathf.Max(0.2f, projectile.Radius);
                            }
                            else
                            {
                                shape = CombatHitShape.Capsule;
                                radius = Mathf.Min(Mathf.Max(1f, projectile.MaxDistance), Mathf.Max(1f, skill.StartRange + (skill.AttackRangeSource ? cast.RangeDelta : 0f)));
                                width = Mathf.Max(0.15f, projectile.Radius * 2f);
                                if (projectile.Target != ProjectileTargetKind.Direction) direction = cast.TargetPoint - origin;
                                if ((projectile.Target == ProjectileTargetKind.EntityLock || projectile.Target == ProjectileTargetKind.EntitySnapshot) &&
                                    IsValid(cast.Target) &&
                                    actors.TryGetValue(cast.Target.Id, out var locked)) direction = locked.Position - origin;
                            }
                        }
                        output.Add(new CombatTelegraphSnapshot
                        {
                            Cast = Ref(pair.Key), ActorId = owner.Spec.ActorId, RoomId = owner.Spec.RoomId,
                            Origin = origin, Direction = direction, Shape = shape, Radius = radius, Width = width,
                            Angle = effect.Angle, Progress = Mathf.Clamp01(elapsed / (float)Mathf.Max(1, node.Tick)),
                            IsProjectile = missile, IsPlayer = owner.Spec.IsPlayer
                        });
                        found = true;
                        break;
                    }
                    if (found) break;
                }
            }
        }
    }
}
