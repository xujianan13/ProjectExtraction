using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 持久战斗数据的边界校验，校验失败不创建部分可运行世界。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 校验身份唯一性、时钟、组件参数与全部关联。
        /// </summary>
        /// <param name="snapshot">外部持久输入。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>数据可无副作用重建时为真。</returns>
        private bool ValidatePersistentState(CombatWorldSnapshot snapshot, out string error)
        {
            error = "战斗计数器或活动区域无效。";
            if (snapshot.Tick < 0 || snapshot.Tick == long.MaxValue || snapshot.NextEntityKey < 0 ||
                snapshot.NextEntityKey == long.MaxValue || snapshot.NextRootId < 0 || snapshot.NextRootId == long.MaxValue ||
                snapshot.NextTriggerId < 0 || snapshot.NextTriggerId == long.MaxValue || snapshot.NextDeathId < 0 ||
                snapshot.NextDeathId == int.MaxValue || snapshot.HighestActorId < 0 || snapshot.ActiveRoomId < 0) return false;
            var rooms = new Dictionary<int, CombatRoomState>();
            error = "地图身份、几何或时钟无效。";
            foreach (var room in snapshot.Rooms)
            {
                if (room == null || room.RoomId <= 0 || rooms.ContainsKey(room.RoomId) || room.Tick < 0 ||
                    room.Tick > snapshot.Tick || room.Obstacles == null || room.HasBounds && !ValidSavedRect(room.Bounds)) return false;
                foreach (var wall in room.Obstacles) if (!ValidSavedRect(wall)) return false;
                rooms.Add(room.RoomId, room);
            }
            if (snapshot.ActiveRoomId != 0 && !rooms.ContainsKey(snapshot.ActiveRoomId)) return false;
            var entityKeys = new HashSet<long>();
            var actorKeys = new Dictionary<long, CombatActorState>();
            var actorIds = new HashSet<int>();
            int players = 0;
            error = "角色身份、属性、冷却或动作状态无效。";
            foreach (var actor in snapshot.Actors)
            {
                if (actor == null || !AcceptSavedKey(actor.Key, snapshot.NextEntityKey, entityKeys) ||
                    !ValidSavedActor(actor, snapshot, rooms) || !actorIds.Add(actor.Spec.ActorId)) return false;
                actorKeys.Add(actor.Key, actor);
                if (actor.Spec.IsPlayer) { players++; if (snapshot.PlayerKey != actor.Key) return false; }
            }
            if (players > 1 || (players == 0) != (snapshot.PlayerKey == 0) ||
                snapshot.Actors.Count > 0 && snapshot.ActiveRoomId == 0) return false;
            var castOwners = new HashSet<long>();
            error = "施法游标、来源或技能引用无效。";
            foreach (var cast in snapshot.Casts)
            {
                if (cast == null || !AcceptSavedKey(cast.Key, snapshot.NextEntityKey, entityKeys) ||
                    !actorKeys.TryGetValue(cast.OwnerKey, out var owner) || owner.DeadPending || !castOwners.Add(cast.OwnerKey) ||
                    !catalog.Skills.TryGetValue(cast.SkillId, out var skill) || !ValidSavedSkill(skill) || cast.NextNode < 0 ||
                    cast.NextNode > skill.Nodes.Count || cast.StartTick < 0 ||
                    cast.StartTick > (owner.Spec.IsPlayer ? snapshot.Tick : rooms[owner.Spec.RoomId].Tick) ||
                    cast.UsesWorldClock != owner.Spec.IsPlayer || !IsFinite(cast.TargetPoint) || !IsFinite(cast.Forward) ||
                    !ValidSavedRange(cast.RangeDelta, cast.RangeMultiplier) || !ValidSavedOffense(cast.Offense)) return false;
            }
            var moveOwners = new HashSet<long>();
            error = "位移来源、方向或剩余时间无效。";
            foreach (var move in snapshot.Displacements)
                if (move == null || !AcceptSavedKey(move.Key, snapshot.NextEntityKey, entityKeys) ||
                    !actorKeys.TryGetValue(move.OwnerKey, out var owner) || owner.DeadPending || !moveOwners.Add(move.OwnerKey) ||
                    !IsFinite(move.Direction) || !IsFinite(move.DistancePerTick) || move.RemainingTicks <= 0 ||
                    move.CreatedTick < 0 || move.CreatedTick > snapshot.Tick) return false;
            error = "飞弹轨迹、配置、来源或命中记录无效。";
            foreach (var projectile in snapshot.Projectiles)
            {
                if (projectile == null || !AcceptSavedKey(projectile.Key, snapshot.NextEntityKey, entityKeys) ||
                    !ValidSavedProjectile(projectile, snapshot, rooms)) return false;
                var hitKeys = new HashSet<long>();
                foreach (var hit in projectile.Hits)
                    if (hit == null || hit.TargetKey == 0 || !ValidSavedReference(hit.TargetKey, actorKeys) ||
                        !hitKeys.Add(hit.TargetKey) || hit.Tick < 0 || hit.Tick > snapshot.Tick || hit.Count <= 0 ||
                        hit.Count > catalog.Projectiles[projectile.ProjectileId].MaxHitsPerTarget) return false;
                if (projectile.DistinctHits != projectile.Hits.Count) return false;
            }
            var buffIdentities = new HashSet<string>(StringComparer.Ordinal);
            error = "Buff 来源、层数或周期状态无效。";
            foreach (var buff in snapshot.Buffs)
            {
                if (buff == null || !AcceptSavedKey(buff.Key, snapshot.NextEntityKey, entityKeys) ||
                    !actorKeys.TryGetValue(buff.OwnerKey, out var owner) || owner.DeadPending || buff.Removed ||
                    !catalog.Buffs.TryGetValue(buff.BuffId, out var definition) || !ValidBuff(definition) ||
                    buff.SourceActorId < 0 || buff.SourceActorId > snapshot.HighestActorId || buff.Stacks <= 0 ||
                    buff.Stacks > definition.MaxStacks || buff.ExpireTick < 0 || buff.NextTick < 0 ||
                    !ValidSavedRange(buff.RangeDelta, buff.RangeMultiplier) || !ValidSavedOffense(buff.Offense) ||
                    !buffIdentities.Add(buff.OwnerKey + ":" + buff.BuffId + ":" + buff.SourceActorId + ":" + buff.PassiveDerived)) return false;
                if (!ValidSavedEffects(definition.OnApply) || !ValidSavedEffects(definition.OnTick) ||
                    !ValidSavedEffects(definition.OnExpire)) return false;
            }
            error = "战斗目标或来源引用缺失。";
            foreach (var cast in snapshot.Casts) if (!ValidSavedReference(cast.TargetKey, actorKeys)) return false;
            foreach (var projectile in snapshot.Projectiles)
                if (projectile.SourceKey == 0 || !ValidSavedReference(projectile.SourceKey, actorKeys) ||
                    !ValidSavedReference(projectile.TargetKey, actorKeys)) return false;
            error = "Buff 已应用属性与角色数据不一致。";
            var orderedBuffs = new List<CombatBuffState>(snapshot.Buffs);
            orderedBuffs.Sort((left, right) => left.Key.CompareTo(right.Key));
            foreach (var actor in snapshot.Actors)
            {
                float move = 0f, range = 0f;
                foreach (var buff in orderedBuffs)
                    if (buff.OwnerKey == actor.Key)
                    {
                        var definition = catalog.Buffs[buff.BuffId];
                        move += definition.MoveSpeedAddPerStack * buff.Stacks;
                        range += definition.AttackRangeAddPerStack * buff.Stacks;
                    }
                if (move != actor.MoveBonus || range != actor.RangeBonus) return false;
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 校验持久实体键并登记跨组件唯一身份。
        /// </summary>
        /// <param name="key">持久实体键。</param>
        /// <param name="highest">已分配的最高持久键。</param>
        /// <param name="keys">已出现的持久键。</param>
        /// <returns>键有效且首次出现时为真。</returns>
        private static bool AcceptSavedKey(long key, long highest, HashSet<long> keys) => key > 0 && key <= highest && keys.Add(key);

        /// <summary>
        /// 校验有效、缺席或已经失效的角色引用。
        /// </summary>
        /// <param name="key">关联键。</param>
        /// <param name="actorsByKey">角色身份集合。</param>
        /// <returns>关联可安全重建时为真。</returns>
        private static bool ValidSavedReference(long key, Dictionary<long, CombatActorState> actorsByKey) =>
            key >= int.MinValue && (key <= 0 || actorsByKey.ContainsKey(key));

        /// <summary>
        /// 校验有限且非空的地图矩形。
        /// </summary>
        /// <param name="rect">外部矩形。</param>
        /// <returns>几何合法时为真。</returns>
        private static bool ValidSavedRect(Rect rect) => IsFinite(rect.x) && IsFinite(rect.y) && IsFinite(rect.width) &&
            IsFinite(rect.height) && IsFinite(rect.xMax) && IsFinite(rect.yMax) && rect.width > 0f && rect.height > 0f;

        /// <summary>
        /// 校验冻结的范围修正。
        /// </summary>
        /// <param name="delta">攻击范围差值。</param>
        /// <param name="multiplier">范围倍率。</param>
        /// <returns>差值有限且倍率合法时为真。</returns>
        private static bool ValidSavedRange(float delta, float multiplier) => IsFinite(delta) && ValidMultiplier(multiplier);

        /// <summary>
        /// 校验效果出生时冻结的来源属性。
        /// </summary>
        /// <param name="value">外部进攻快照。</param>
        /// <returns>全部属性合法时为真。</returns>
        private static bool ValidSavedOffense(CombatOffenseState value) => value != null && IsFinite(value.Attack) && value.Attack >= 0f &&
            IsFinite(value.CriticalRating) && value.CriticalRating >= 0f && IsFinite(value.CriticalDamage) && value.CriticalDamage >= 1f &&
            IsFinite(value.DamageIncrease) && value.DamageIncrease >= 0f && ValidMultiplier(value.DamageMultiplier);

        /// <summary>
        /// 校验效果列表及其配置引用。
        /// </summary>
        /// <param name="effects">配置效果列表。</param>
        /// <returns>全部效果及引用合法时为真。</returns>
        private bool ValidSavedEffects(List<CombatEffectDefinition> effects)
        {
            if (effects == null) return false;
            foreach (var effect in effects)
                if (!ValidEffect(effect) || effect.Kind == CombatEffectKind.SpawnProjectile &&
                    (!catalog.Projectiles.TryGetValue(effect.LinkedId, out var projectile) || !ValidProjectile(projectile)) ||
                    effect.Kind == CombatEffectKind.ApplyBuff &&
                    (!catalog.Buffs.TryGetValue(effect.LinkedId, out var buff) || !ValidBuff(buff))) return false;
            return true;
        }

        /// <summary>
        /// 校验技能时间轴，防止恢复游标进入已改变的非法定义。
        /// </summary>
        /// <param name="skill">引用的技能定义。</param>
        /// <returns>时间轴可安全继续时为真。</returns>
        private bool ValidSavedSkill(CombatSkillDefinition skill)
        {
            if (skill == null || skill.CombatSkillId <= 0 || skill.DurationTicks < 0 || skill.CooldownTicks < 0 ||
                !IsFinite(skill.StartRange) || skill.StartRange < 0f) return false;
            int previous = -1;
            foreach (var node in skill.Nodes)
            {
                if (node == null || node.Tick < 0 || node.Tick < previous || node.Tick > skill.DurationTicks ||
                    !ValidSavedEffects(node.Effects)) return false;
                previous = node.Tick;
            }
            return true;
        }

        /// <summary>
        /// 校验角色完整属性、动作时钟和可写集合。
        /// </summary>
        /// <param name="actor">外部角色状态。</param>
        /// <param name="snapshot">所属完整世界状态。</param>
        /// <param name="rooms">已校验的地图记录。</param>
        /// <returns>角色状态及引用合法时为真。</returns>
        private bool ValidSavedActor(CombatActorState actor, CombatWorldSnapshot snapshot, Dictionary<int, CombatRoomState> rooms)
        {
            var spec = actor.Spec;
            if (spec.ActorId <= 0 || spec.ActorId > snapshot.HighestActorId || spec.Team == 0 ||
                !rooms.TryGetValue(spec.RoomId, out var room) || !ValidBaseAttributes(spec) || !ValidBaseAttributes(actor.BaseSpec) ||
                !IsFinite(spec.Position) || !IsFinite(spec.Radius) || spec.Radius <= 0f || !IsFinite(spec.AggroRange) || spec.AggroRange < 0f ||
                !ValidDamageVulnerabilities(spec.DamageVulnerabilities) || !ValidDamageVulnerabilities(actor.BaseSpec.DamageVulnerabilities) ||
                spec.CriticalDamage < 1f || spec.DodgeChance > ProbabilityCeiling || spec.DamageReduction > ProbabilityCeiling ||
                spec.BasicSkillId < 0 || spec.BasicSkillId > 0 &&
                (!catalog.Skills.TryGetValue(spec.BasicSkillId, out var basicSkill) || !ValidSavedSkill(basicSkill)) ||
                !IsFinite(actor.Position) || !IsFinite(actor.Forward) || !IsFinite(actor.Velocity) || !IsFinite(actor.CueDirection) ||
                !IsFinite(actor.AiMove) || actor.AiMove.sqrMagnitude > 1.00001f || spec.IsPlayer && actor.AiMove != Vector2.zero ||
                !IsFinite(actor.Health) || actor.Health < 0f || actor.Health > spec.MaxHealth ||
                actor.DeadPending != (actor.Health == 0f) || actor.DeathPublished && !actor.DeadPending ||
                !IsFinite(actor.MoveBonus) || !IsFinite(actor.RangeBonus) || actor.LastInstigatorActorId < 0 ||
                actor.LastInstigatorActorId > snapshot.HighestActorId || actor.PermanentBonuses == null ||
                actor.PermanentBonuses.Length != 8 || actor.AugmentGroups == null || actor.SkillCooldowns == null ||
                actor.CueDurationTicks < 0) return false;
            long clock = spec.IsPlayer ? snapshot.Tick : room.Tick;
            if (!ValidSavedClock(actor.CueClock, clock) || !ValidSavedClock(actor.HitClock, clock) ||
                !ValidSavedClock(actor.DeathClock, clock)) return false;
            foreach (float bonus in actor.PermanentBonuses) if (!IsFinite(bonus) || bonus < 0f || bonus > AttributeCeiling) return false;
            if (actor.PermanentBonuses[5] > ProbabilityCeiling || actor.PermanentBonuses[7] > ProbabilityCeiling) return false;
            if (spec.EnemySkills != null)
            {
                var skillIds = new HashSet<int>();
                foreach (var skill in spec.EnemySkills)
                    if (!skillIds.Add(skill.SkillId) || skill.Weight <= 0 || !IsFinite(skill.MinHealthRatio) ||
                        !IsFinite(skill.MaxHealthRatio) || !IsFinite(skill.MinDistance) || !IsFinite(skill.MaxDistance) ||
                        skill.MinHealthRatio < 0f || skill.MaxHealthRatio > 1f || skill.MinHealthRatio > skill.MaxHealthRatio ||
                        skill.MinDistance < 0f || skill.MaxDistance < skill.MinDistance ||
                        !catalog.Skills.TryGetValue(skill.SkillId, out var definition) || !ValidSavedSkill(definition)) return false;
            }
            var cooldownIds = new HashSet<int>();
            foreach (var cooldown in actor.SkillCooldowns)
                if (cooldown == null || cooldown.RemainingTicks < 0 || !cooldownIds.Add(cooldown.SkillId) ||
                    !catalog.Skills.TryGetValue(cooldown.SkillId, out var skill) || !ValidSavedSkill(skill)) return false;
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in actor.AugmentGroups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.GroupId) || !groups.Add(group.GroupId) ||
                    group.Definitions == null || group.Definitions.Count > 64 || group.Cooldowns == null) return false;
                var augmentIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var definition in group.Definitions)
                    if (!ValidAugment(definition) || !augmentIds.Add(definition.Id)) return false;
                var savedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var cooldown in group.Cooldowns)
                    if (cooldown == null || string.IsNullOrWhiteSpace(cooldown.AugmentId) || cooldown.AugmentId.Length > 128 ||
                        !savedIds.Add(cooldown.AugmentId) || cooldown.Tick < 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 校验动作已发生时钟或缺席标记。
        /// </summary>
        /// <param name="value">动作时钟，负一表示缺席。</param>
        /// <param name="clock">角色当前有效时钟。</param>
        /// <returns>时钟处于合法历史区间时为真。</returns>
        private static bool ValidSavedClock(long value, long clock) => value >= -1 && value <= clock;

        /// <summary>
        /// 校验飞弹轨迹、源属性与生命周期。
        /// </summary>
        /// <param name="projectile">外部飞弹状态。</param>
        /// <param name="snapshot">所属完整世界状态。</param>
        /// <param name="rooms">已校验的地图记录。</param>
        /// <returns>飞弹状态可安全继续时为真。</returns>
        private bool ValidSavedProjectile(CombatProjectileState projectile, CombatWorldSnapshot snapshot,
            Dictionary<int, CombatRoomState> rooms)
        {
            if (!rooms.ContainsKey(projectile.RoomId) || projectile.InstigatorActorId <= 0 ||
                projectile.InstigatorActorId > snapshot.HighestActorId || projectile.SourceTeam == 0 ||
                !catalog.Projectiles.TryGetValue(projectile.ProjectileId, out var definition) || !ValidProjectile(definition) ||
                !ValidSavedEffects(definition.OnHit) || !ValidSavedEffects(definition.OnArrive) || !ValidSavedEffects(definition.OnExpire) ||
                !ValidSavedRange(projectile.RangeDelta, projectile.RangeMultiplier) || !ValidSavedOffense(projectile.Offense) ||
                !IsFinite(projectile.LastTargetPoint) || !IsFinite(projectile.Position) || !IsFinite(projectile.Previous) ||
                !IsFinite(projectile.Origin) || !IsFinite(projectile.EndPoint) || !IsFinite(projectile.Velocity) ||
                !IsFinite(projectile.Direction) || projectile.FlightTicks < 0 || projectile.ElapsedTicks < 0 ||
                projectile.CreatedTick < 0 || projectile.CreatedTick > snapshot.Tick || projectile.Bounces < 0 ||
                projectile.Bounces > definition.MaxBounces || !IsFinite(projectile.Distance) || projectile.Distance < 0f ||
                projectile.DistinctHits < 0 || projectile.DistinctHits > definition.MaxDistinctTargets ||
                projectile.Hits == null || projectile.Terminal) return false;
            return definition.Motion == ProjectileMotionKind.Instant || projectile.FlightTicks > 0;
        }
    }
}
