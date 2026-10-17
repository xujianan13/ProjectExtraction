using System;
using System.Collections.Generic;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 完整战斗边界的导出、恢复与稳定身份管理。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 复制已消费事件的完整战斗状态，拒绝半帧或故障世界。
        /// </summary>
        /// <param name="snapshot">成功时返回独立的持久数据。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>处于完整可恢复边界时为真。</returns>
        public bool TryExportState(out CombatWorldSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = "战斗状态尚未完成事件消费或效果执行。";
            if (stepping || faulted || effectQueue.Count != 0 || events.Count != 0 || activeRootId != 0 || activeDepth != 0) return false;
            var result = new CombatWorldSnapshot
            {
                RandomAlgorithmVersion = PersistentRandom.AlgorithmVersion, RandomState = random.State, Tick = TickIndex,
                NextEntityKey = nextPersistentKey, NextRootId = nextRootId, NextTriggerId = nextTriggerId,
                NextDeathId = nextDeathId, HighestActorId = highestActorId, ActiveRoomId = activeRoomId,
                PlayerKey = playerId == 0 ? 0 : slots[playerId].PersistentKey
            };
            var roomIds = new HashSet<int>(roomBounds.Keys);
            roomIds.UnionWith(obstacles.Keys);
            roomIds.UnionWith(roomTicks.Keys);
            roomIds.UnionWith(clearedRooms);
            if (activeRoomId > 0) roomIds.Add(activeRoomId);
            foreach (var actor in actors.Values) roomIds.Add(actor.Spec.RoomId);
            var orderedRooms = new List<int>(roomIds);
            orderedRooms.Sort();
            foreach (int roomId in orderedRooms)
            {
                bool hasBounds = roomBounds.TryGetValue(roomId, out Rect bounds);
                var room = new CombatRoomState { RoomId = roomId, Tick = GetRoomTick(roomId), HasBounds = hasBounds,
                    Bounds = bounds, Cleared = clearedRooms.Contains(roomId) };
                if (obstacles.TryGetValue(roomId, out var walls)) room.Obstacles.AddRange(walls);
                result.Rooms.Add(room);
            }
            var invalidReferences = new Dictionary<CombatEntityRef, long>();
            foreach (var cast in casts.Values) ReserveInvalidReference(cast.Target, invalidReferences);
            foreach (var projectile in projectiles.Values)
            {
                ReserveInvalidReference(projectile.Source, invalidReferences);
                ReserveInvalidReference(projectile.Target, invalidReferences);
                foreach (var hit in projectile.Hits) ReserveInvalidReference(hit.Key, invalidReferences);
            }
            foreach (int id in OrderedEntityIds(actors.Keys)) result.Actors.Add(ExportActor(id, actors[id]));
            foreach (int id in OrderedEntityIds(casts.Keys))
            {
                var cast = casts[id];
                result.Casts.Add(new CombatCastState { Key = slots[id].PersistentKey, OwnerKey = slots[cast.Owner].PersistentKey,
                    SkillId = cast.SkillId, StartTick = cast.StartTick, TargetKey = ExportReference(cast.Target, invalidReferences),
                    TargetPoint = cast.TargetPoint, Forward = cast.Forward, NextNode = cast.NextNode, RangeDelta = cast.RangeDelta,
                    TimeMultiplier = cast.TimeMultiplier, RangeMultiplier = cast.RangeMultiplier, UsesWorldClock = cast.UsesWorldClock, Offense = ExportOffense(cast.Offense) });
            }
            foreach (int id in OrderedEntityIds(displacements.Keys))
            {
                var move = displacements[id];
                result.Displacements.Add(new CombatDisplacementState { Key = slots[id].PersistentKey,
                    OwnerKey = slots[move.Owner].PersistentKey, Direction = move.Direction, DistancePerTick = move.DistancePerTick,
                    RemainingTicks = move.RemainingTicks, CreatedTick = move.CreatedTick });
            }
            foreach (int id in OrderedEntityIds(projectiles.Keys))
            {
                var value = projectiles[id];
                var projectile = new CombatProjectileState { Key = slots[id].PersistentKey, ProjectileId = value.ProjectileId,
                    RangeDelta = value.RangeDelta, RangeMultiplier = value.RangeMultiplier, PassiveDerived = value.PassiveDerived,
                    RoomId = value.RoomId, InstigatorActorId = value.InstigatorActorId, SourceTeam = value.SourceTeam,
                    Offense = ExportOffense(value.Offense), SourceKey = ExportReference(value.Source, invalidReferences),
                    TargetKey = ExportReference(value.Target, invalidReferences), LastTargetPoint = value.LastTargetPoint,
                    Position = value.Position, Previous = value.Previous, Origin = value.Origin, EndPoint = value.EndPoint,
                    Velocity = value.Velocity, Direction = value.Direction, FlightTicks = value.FlightTicks,
                    ElapsedTicks = value.ElapsedTicks, CreatedTick = value.CreatedTick, Bounces = value.Bounces,
                    Distance = value.Distance, DistinctHits = value.DistinctHits, Terminal = value.Terminal, OrbitAngle = value.OrbitAngle, ControllerSkillId = value.ControllerSkillId };
                foreach (var hit in value.Hits)
                    projectile.Hits.Add(new CombatProjectileHitState { TargetKey = ExportReference(hit.Key, invalidReferences),
                        Tick = hit.Value.Tick, Count = hit.Value.Count });
                projectile.Hits.Sort((left, right) => left.TargetKey.CompareTo(right.TargetKey));
                result.Projectiles.Add(projectile);
            }
            foreach (int id in OrderedEntityIds(buffs.Keys))
            {
                var buff = buffs[id];
                result.Buffs.Add(new CombatBuffState { Key = slots[id].PersistentKey, OwnerKey = slots[buff.Owner].PersistentKey,
                    RangeDelta = buff.RangeDelta, RangeMultiplier = buff.RangeMultiplier, PassiveDerived = buff.PassiveDerived,
                    BuffId = buff.BuffId, SourceActorId = buff.SourceActorId, Offense = ExportOffense(buff.Offense), Stacks = buff.Stacks,
                    ExpireTick = buff.ExpireTick, NextTick = buff.NextTick, Removed = buff.Removed });
            }
            snapshot = result;
            error = null;
            return true;
        }

        /// <summary>
        /// 从完整持久数据重建新世界，不发布旧事件或重新应用效果。
        /// </summary>
        /// <param name="catalog">与保存版本一致且已经加载的配置目录。</param>
        /// <param name="snapshot">待恢复的完整战斗数据。</param>
        /// <param name="world">成功时返回清空旧输入的新世界。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>完整数据与配置通过校验并恢复时为真。</returns>
        public static bool TryRestore(CombatCatalog catalog, CombatWorldSnapshot snapshot, out CombatWorld world, out string error)
        {
            world = null;
            error = "战斗快照版本、随机状态或集合无效。";
            if (catalog == null || snapshot == null || snapshot.Version != 2 ||
                snapshot.RandomAlgorithmVersion != PersistentRandom.AlgorithmVersion ||
                !PersistentRandom.TryRestore(snapshot.RandomState, out var restoredRandom) || snapshot.Rooms == null ||
                snapshot.Actors == null || snapshot.Casts == null || snapshot.Displacements == null ||
                snapshot.Projectiles == null || snapshot.Buffs == null) return false;
            var candidate = new CombatWorld(catalog) { random = restoredRandom };
            if (!candidate.ValidatePersistentState(snapshot, out error)) { candidate.Dispose(); return false; }
            candidate.ImportState(snapshot);
            world = candidate;
            error = null;
            return true;
        }

        /// <summary>
        /// 用不会复用的业务角色编号取得当前生命引用。
        /// </summary>
        /// <param name="actorId">本局全局递增的角色生命编号。</param>
        /// <param name="actor">找到的当前世界引用。</param>
        /// <returns>角色尚未被回收时为真。</returns>
        public bool TryFindActor(int actorId, out CombatEntityRef actor)
        {
            int id = FindActorSlot(actorId);
            actor = id == 0 ? default : Ref(id);
            return id != 0;
        }

        /// <summary>
        /// 在死亡事实消费后回收尸体，旧飞弹保留冻结来源且不会指向新生命。
        /// </summary>
        /// <param name="actor">需要回收的旧生命引用。</param>
        /// <returns>死亡已发布、已消费且尸体被回收时为真。</returns>
        public bool TryRetireDeadActor(CombatEntityRef actor)
        {
            if (stepping || effectQueue.Count != 0 || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) ||
                state.Spec.IsPlayer || !state.DeadPending || !state.DeathPublished) return false;
            foreach (var fact in events)
                if (fact.Kind == CombatEventKind.Death && fact.TargetActorId == state.Spec.ActorId) return false;
            CancelOwnedCast(actor.Id, false);
            RemoveOwnedDisplacement(actor.Id);
            foreach (int id in OrderedEntityIds(buffs.Keys))
                if (buffs[id].Owner == actor.Id) RemoveBuff(id, false);
            InvalidateRetiredReferences(actor, state.Spec.ActorId);
            actors.Remove(actor.Id);
            Release(actor.Id);
            return true;
        }

        /// <summary>
        /// 将旧生命关联转换为稳定的失效身份，保持多次保存及槽位复用后的去重语义。
        /// </summary>
        /// <param name="actor">刚回收的旧生命引用。</param>
        /// <param name="actorId">不会复用的旧生命业务身份。</param>
        private void InvalidateRetiredReferences(CombatEntityRef actor, int actorId)
        {
            var invalid = new CombatEntityRef(worldId, -actorId, 1);
            foreach (var cast in casts.Values) if (cast.Target.Equals(actor)) cast.Target = invalid;
            foreach (var projectile in projectiles.Values)
            {
                if (projectile.Source.Equals(actor)) projectile.Source = invalid;
                if (projectile.Target.Equals(actor)) projectile.Target = invalid;
                if (projectile.Hits.TryGetValue(actor, out var hit))
                {
                    projectile.Hits.Remove(actor);
                    projectile.Hits.Add(invalid, hit);
                }
            }
        }

        /// <summary>
        /// 按持久实体键排序运行槽位，恢复后仍按原因果次序推进。
        /// </summary>
        /// <param name="entityIds">待排序的运行槽位。</param>
        private void SortEntityIds(List<int> entityIds) => entityIds.Sort((left, right) =>
            slots[left].PersistentKey.CompareTo(slots[right].PersistentKey));

        /// <summary>
        /// 创建按持久实体键排序的独立运行槽位列表。
        /// </summary>
        /// <param name="entityIds">来源运行槽位集合。</param>
        /// <returns>排序后的独立列表。</returns>
        private List<int> OrderedEntityIds(IEnumerable<int> entityIds)
        {
            var ordered = new List<int>(entityIds);
            SortEntityIds(ordered);
            return ordered;
        }

        /// <summary>
        /// 将引用转换为持久键，使用负键保留已经失效的非空关联。
        /// </summary>
        /// <param name="reference">当前运行引用。</param>
        /// <param name="invalidReferences">本次导出的失效引用去重表。</param>
        /// <returns>持久键、零或唯一的负失效键。</returns>
        private long ExportReference(CombatEntityRef reference, Dictionary<CombatEntityRef, long> invalidReferences)
        {
            if (reference.Id == 0) return 0;
            if (IsValid(reference)) return slots[reference.Id].PersistentKey;
            if (reference.World == worldId && reference.Id < 0) return reference.Id;
            if (!invalidReferences.TryGetValue(reference, out long key))
            {
                key = -1;
                while (invalidReferences.ContainsValue(key)) key--;
                invalidReferences.Add(reference, key);
            }
            return key;
        }

        /// <summary>
        /// 预留恢复后已有的负引用键，防止新失效引用与旧引用合并。
        /// </summary>
        /// <param name="reference">待检查的运行引用。</param>
        /// <param name="invalidReferences">本次导出的失效引用去重表。</param>
        private void ReserveInvalidReference(CombatEntityRef reference, Dictionary<CombatEntityRef, long> invalidReferences)
        {
            if (reference.World == worldId && reference.Id < 0 && !invalidReferences.ContainsKey(reference))
                invalidReferences.Add(reference, reference.Id);
        }

        /// <summary>
        /// 复制角色配置中的可写技能数组。
        /// </summary>
        /// <param name="spec">角色配置。</param>
        /// <returns>不共享技能数组的配置。</returns>
        private static CombatActorSpec CopySpec(CombatActorSpec spec)
        {
            if (spec.SkillIds != null) spec.SkillIds = (int[])spec.SkillIds.Clone();
            if (spec.EnemySkills != null) spec.EnemySkills = (CombatEnemySkillDefinition[])spec.EnemySkills.Clone();
            if (spec.DamageVulnerabilities != null)
                spec.DamageVulnerabilities = (CombatDamageVulnerability[])spec.DamageVulnerabilities.Clone();
            return spec;
        }

        /// <summary>
        /// 复制可写词条及效果配置。
        /// </summary>
        /// <param name="definition">已经接纳的词条。</param>
        /// <returns>独立词条配置。</returns>
        private static CombatAugmentDefinition CopyAugment(CombatAugmentDefinition definition)
        {
            var copy = new CombatAugmentDefinition { Id = definition.Id, Trigger = definition.Trigger,
                Chance = definition.Chance, CooldownTicks = definition.CooldownTicks, TargetSelf = definition.TargetSelf,
                AffectsBasicAttack = definition.AffectsBasicAttack, SkillId = definition.SkillId, CooldownMultiplier = definition.CooldownMultiplier,
                RangeMultiplier = definition.RangeMultiplier, DamageMultiplier = definition.DamageMultiplier };
            foreach (var effect in definition.Effects) copy.Effects.Add(CopyEffectWithRadius(effect, effect.Radius));
            return copy;
        }

        /// <summary>
        /// 复制角色组件及全部持久集合。
        /// </summary>
        /// <param name="id">运行槽位。</param>
        /// <param name="actor">角色组件。</param>
        /// <returns>独立角色持久数据。</returns>
        private CombatActorState ExportActor(int id, ActorComponent actor)
        {
            var result = new CombatActorState { Key = slots[id].PersistentKey, Spec = CopySpec(actor.Spec), BaseSpec = CopySpec(actor.BaseSpec),
                PermanentBonuses = (float[])actor.PermanentBonuses.Clone(), Position = actor.Position, Forward = actor.Forward,
                AiMove = actor.Spec.IsPlayer ? Vector2.zero : actor.Move,
                Health = actor.Health, MoveBonus = actor.MoveBonus, RangeBonus = actor.RangeBonus, DeadPending = actor.DeadPending,
                DeathPublished = actor.DeathPublished, LastInstigatorActorId = actor.LastInstigatorActorId, Velocity = actor.Velocity,
                CueDirection = actor.CueDirection, CueId = actor.CueId, CueDurationTicks = actor.CueDurationTicks,
                CueClock = actor.CueClock, HitClock = actor.HitClock, DeathClock = actor.DeathClock };
            result.SkillCursor = actor.SkillCursor;
            result.ActionAcceptedTick = actor.ActionAcceptedTick;
            result.EnabledPersistentSkills.AddRange(actor.EnabledPersistentSkills);
            result.EnabledPersistentSkills.Sort();
            var skillIds = new List<int>(actor.SkillCooldowns.Keys);
            skillIds.Sort();
            foreach (int skillId in skillIds) result.SkillCooldowns.Add(new CombatSkillCooldownState
                { SkillId = skillId, RemainingTicks = actor.SkillCooldowns[skillId] });
            var groupIds = new HashSet<string>(actor.AugmentGroups.Keys, StringComparer.Ordinal);
            groupIds.UnionWith(actor.AugmentCooldowns.Keys);
            var orderedGroups = new List<string>(groupIds);
            orderedGroups.Sort(StringComparer.Ordinal);
            foreach (string groupId in orderedGroups)
            {
                var group = new CombatAugmentGroupState { GroupId = groupId };
                if (actor.AugmentGroups.TryGetValue(groupId, out var definitions))
                    foreach (var definition in definitions) group.Definitions.Add(CopyAugment(definition));
                if (actor.AugmentCooldowns.TryGetValue(groupId, out var cooldowns))
                {
                    var augmentIds = new List<string>(cooldowns.Keys);
                    augmentIds.Sort(StringComparer.Ordinal);
                    foreach (string augmentId in augmentIds) group.Cooldowns.Add(new CombatAugmentCooldownState
                        { AugmentId = augmentId, Tick = cooldowns[augmentId] });
                }
                result.AugmentGroups.Add(group);
            }
            return result;
        }

        /// <summary>
        /// 复制效果的进攻属性快照。
        /// </summary>
        /// <param name="offense">已冻结的进攻属性。</param>
        /// <returns>独立持久数据。</returns>
        private static CombatOffenseState ExportOffense(CombatOffense offense) => new()
        {
            Attack = offense.Attack, CriticalRating = offense.CriticalRating, CriticalDamage = offense.CriticalDamage,
            DamageIncrease = offense.DamageIncrease, DamageMultiplier = offense.DamageMultiplier
        };

        /// <summary>
        /// 还原效果属性，不读取当前来源角色属性。
        /// </summary>
        /// <param name="offense">已校验的持久进攻数据。</param>
        /// <returns>恢复的冻结属性。</returns>
        private static CombatOffense ImportOffense(CombatOffenseState offense) => new(new CombatActorSpec
        {
            Attack = offense.Attack, CriticalRating = offense.CriticalRating, CriticalDamage = offense.CriticalDamage,
            DamageIncrease = offense.DamageIncrease
        }, offense.DamageMultiplier);
    }
}
