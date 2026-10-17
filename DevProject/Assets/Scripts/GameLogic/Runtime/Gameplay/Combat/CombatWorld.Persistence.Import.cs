using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 完整持久数据的无副作用组件重建。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 先重建持久身份，再装填关联组件及已应用状态。
        /// </summary>
        /// <param name="snapshot">已经完成全量校验的快照。</param>
        private void ImportState(CombatWorldSnapshot snapshot)
        {
            var keys = new List<long>();
            foreach (var value in snapshot.Actors) keys.Add(value.Key);
            foreach (var value in snapshot.Casts) keys.Add(value.Key);
            foreach (var value in snapshot.Displacements) keys.Add(value.Key);
            foreach (var value in snapshot.Projectiles) keys.Add(value.Key);
            foreach (var value in snapshot.Buffs) keys.Add(value.Key);
            keys.Sort();
            var references = new Dictionary<long, int>();
            foreach (long key in keys)
            {
                var reference = Allocate();
                slots[reference.Id].PersistentKey = key;
                references.Add(key, reference.Id);
            }
            TickIndex = snapshot.Tick;
            nextPersistentKey = snapshot.NextEntityKey;
            nextRootId = snapshot.NextRootId;
            nextTriggerId = snapshot.NextTriggerId;
            nextDeathId = snapshot.NextDeathId;
            highestActorId = snapshot.HighestActorId;
            activeRoomId = snapshot.ActiveRoomId;
            playerId = snapshot.PlayerKey == 0 ? 0 : references[snapshot.PlayerKey];
            foreach (var room in snapshot.Rooms)
            {
                roomTicks.Add(room.RoomId, room.Tick);
                if (room.HasBounds) roomBounds.Add(room.RoomId, room.Bounds);
                if (room.Obstacles.Count > 0) obstacles.Add(room.RoomId, new List<Rect>(room.Obstacles));
                if (room.Cleared) clearedRooms.Add(room.RoomId);
            }
            var orderedActors = new List<CombatActorState>(snapshot.Actors);
            orderedActors.Sort((left, right) => left.Key.CompareTo(right.Key));
            foreach (var value in orderedActors)
            {
                var actor = new ActorComponent { Spec = CopySpec(value.Spec), BaseSpec = CopySpec(value.BaseSpec),
                    Position = value.Position, Forward = value.Forward, Health = value.Health, MoveBonus = value.MoveBonus,
                    RangeBonus = value.RangeBonus, DeadPending = value.DeadPending, DeathPublished = value.DeathPublished,
                    LastInstigatorActorId = value.LastInstigatorActorId, Velocity = value.Velocity, CueDirection = value.CueDirection,
                    CueId = value.CueId, CueDurationTicks = value.CueDurationTicks, CueClock = value.CueClock,
                    HitClock = value.HitClock, DeathClock = value.DeathClock, Move = value.Spec.IsPlayer ? Vector2.zero : value.AiMove };
                actor.SkillCursor = value.SkillCursor;
                actor.ActionAcceptedTick = value.ActionAcceptedTick;
                foreach (var id in value.EnabledPersistentSkills) actor.EnabledPersistentSkills.Add(id);
                value.PermanentBonuses.CopyTo(actor.PermanentBonuses, 0);
                foreach (var cooldown in value.SkillCooldowns) actor.SkillCooldowns.Add(cooldown.SkillId, cooldown.RemainingTicks);
                foreach (var group in value.AugmentGroups)
                {
                    if (group.Definitions.Count > 0)
                    {
                        actor.AugmentGroups.Add(group.GroupId, group.Definitions.ConvertAll(CopyAugment));
                        actor.AugmentGroupOrder.Add(group.GroupId);
                    }
                    if (group.Cooldowns.Count > 0)
                    {
                        var cooldowns = new Dictionary<string, long>(System.StringComparer.Ordinal);
                        foreach (var cooldown in group.Cooldowns) cooldowns.Add(cooldown.AugmentId, cooldown.Tick);
                        actor.AugmentCooldowns.Add(group.GroupId, cooldowns);
                    }
                }
                actor.AugmentGroupOrder.Sort(System.StringComparer.Ordinal);
                actors.Add(references[value.Key], actor);
            }
            foreach (var value in snapshot.Casts)
                casts.Add(references[value.Key], new CastComponent { Owner = references[value.OwnerKey], SkillId = value.SkillId,
                    StartTick = value.StartTick, Target = ImportReference(value.TargetKey, references), TargetPoint = value.TargetPoint,
                    Forward = value.Forward, NextNode = value.NextNode, RangeDelta = value.RangeDelta,
                    TimeMultiplier = value.TimeMultiplier, RangeMultiplier = value.RangeMultiplier, UsesWorldClock = value.UsesWorldClock, Offense = ImportOffense(value.Offense) });
            foreach (var value in snapshot.Displacements)
                displacements.Add(references[value.Key], new DisplacementComponent { Owner = references[value.OwnerKey],
                    Direction = value.Direction, DistancePerTick = value.DistancePerTick, RemainingTicks = value.RemainingTicks,
                    CreatedTick = value.CreatedTick });
            foreach (var value in snapshot.Projectiles)
            {
                var projectile = new ProjectileComponent { ProjectileId = value.ProjectileId, RangeDelta = value.RangeDelta,
                    RangeMultiplier = value.RangeMultiplier, PassiveDerived = value.PassiveDerived, RoomId = value.RoomId,
                    InstigatorActorId = value.InstigatorActorId, SourceTeam = value.SourceTeam, Offense = ImportOffense(value.Offense),
                    Source = ImportReference(value.SourceKey, references), Target = ImportReference(value.TargetKey, references),
                    LastTargetPoint = value.LastTargetPoint, Position = value.Position, Previous = value.Previous, Origin = value.Origin,
                    EndPoint = value.EndPoint, Velocity = value.Velocity, Direction = value.Direction, FlightTicks = value.FlightTicks,
                    ElapsedTicks = value.ElapsedTicks, CreatedTick = value.CreatedTick, Bounces = value.Bounces,
                    Distance = value.Distance, DistinctHits = value.DistinctHits, Terminal = value.Terminal, OrbitAngle = value.OrbitAngle, ControllerSkillId = value.ControllerSkillId };
                foreach (var hit in value.Hits) projectile.Hits.Add(ImportReference(hit.TargetKey, references),
                    new HitRecord { Tick = hit.Tick, Count = hit.Count });
                projectiles.Add(references[value.Key], projectile);
            }
            foreach (var value in snapshot.Buffs)
                buffs.Add(references[value.Key], new BuffComponent { Owner = references[value.OwnerKey], BuffId = value.BuffId,
                    SourceActorId = value.SourceActorId, Offense = ImportOffense(value.Offense), Stacks = value.Stacks,
                    ExpireTick = value.ExpireTick, NextTick = value.NextTick, Removed = value.Removed,
                    RangeDelta = value.RangeDelta, RangeMultiplier = value.RangeMultiplier, PassiveDerived = value.PassiveDerived });
        }

        /// <summary>
        /// 在新世界内重映射有效关联，失效关联保持永远不能匹配新生命。
        /// </summary>
        /// <param name="key">有效持久键、零或负失效键。</param>
        /// <param name="references">持久身份到运行槽位的映射。</param>
        /// <returns>新世界引用，失效非空引用使用负槽位。</returns>
        private CombatEntityRef ImportReference(long key, Dictionary<long, int> references) => key == 0 ? default :
            key < 0 ? new CombatEntityRef(worldId, (int)key, 1) : Ref(references[key]);
    }
}
