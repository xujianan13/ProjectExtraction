using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 在事件与效果均已消费的完整边界复制玩家状态，不导出整个世界或其他角色。
        /// 外部来源 Buff 不保留；玩家自身 Buff 保留剩余周期，当前动作与位置不保留。
        /// </summary>
        /// <param name="player">当前世界登记的玩家引用。</param>
        /// <param name="checkpoint">成功时返回与世界集合隔离的玩家检查点。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>玩家及配置有效且处于完整边界时为真。</returns>
        public bool TryCapturePlayerCheckpoint(CombatEntityRef player, out CombatPlayerCheckpoint checkpoint, out string error)
        {
            checkpoint = null;
            error = "玩家检查点要求已消费全部事件与效果的完整战斗边界。";
            if (!AtPlayerCheckpointBoundary()) return false;
            error = "玩家引用无效或并非当前世界登记的玩家。";
            if (!IsValid(player) || player.Id != playerId || !actors.TryGetValue(player.Id, out var actor) || !actor.Spec.IsPlayer)
                return false;
            var captured = new CombatPlayerCheckpoint
            {
                Spec = CopyPlayerCheckpointSpec(actor.Spec), BaseSpec = CopyPlayerCheckpointSpec(actor.BaseSpec), Health = actor.Health,
                PermanentBonuses = (float[])actor.PermanentBonuses.Clone(), SkillCursor = actor.SkillCursor, DeathIdWatermark = nextDeathId
            };
            captured.EnabledPersistentSkills.AddRange(actor.EnabledPersistentSkills);
            captured.EnabledPersistentSkills.Sort();
            var skillIds = new List<int>(actor.SkillCooldowns.Keys);
            skillIds.Sort();
            foreach (var id in skillIds)
                captured.SkillCooldowns.Add(new CombatSkillCooldownState { SkillId = id, RemainingTicks = actor.SkillCooldowns[id] });
            var groupIds = new HashSet<string>(actor.AugmentGroups.Keys, StringComparer.Ordinal);
            groupIds.UnionWith(actor.AugmentCooldowns.Keys);
            var orderedGroups = new List<string>(groupIds);
            orderedGroups.Sort(StringComparer.Ordinal);
            foreach (var id in orderedGroups)
            {
                var group = new CombatPlayerAugmentGroupCheckpoint { GroupId = id };
                if (actor.AugmentGroups.TryGetValue(id, out var definitions))
                    foreach (var definition in definitions) group.Definitions.Add(CopyAugment(definition));
                if (actor.AugmentCooldowns.TryGetValue(id, out var cooldowns))
                {
                    var augmentIds = new List<string>(cooldowns.Keys);
                    augmentIds.Sort(StringComparer.Ordinal);
                    foreach (var augmentId in augmentIds)
                        group.Cooldowns.Add(new CombatPlayerAugmentCooldownCheckpoint
                        { AugmentId = augmentId, RemainingTicks = Math.Max(0L, cooldowns[augmentId] - TickIndex) });
                }
                captured.AugmentGroups.Add(group);
            }
            foreach (var id in OrderedEntityIds(buffs.Keys))
            {
                var buff = buffs[id];
                if (buff.Owner != player.Id || buff.Removed || buff.SourceActorId != actor.Spec.ActorId) continue;
                if (!catalog.Buffs.TryGetValue(buff.BuffId, out var definition))
                { error = "玩家自身 Buff 的配置缺失。"; return false; }
                captured.Buffs.Add(new CombatPlayerBuffCheckpoint
                {
                    BuffId = buff.BuffId, Stacks = buff.Stacks, RangeDelta = buff.RangeDelta, RangeMultiplier = buff.RangeMultiplier,
                    PassiveDerived = buff.PassiveDerived, Offense = ExportOffense(buff.Offense),
                    RemainingTicks = definition.Lifetime == CombatBuffLifetime.Persistent ? long.MaxValue : buff.ExpireTick - TickIndex,
                    NextTickIn = Math.Max(0L, buff.NextTick - TickIndex)
                });
            }
            if (!ValidPlayerCheckpoint(captured, out error)) return false;
            checkpoint = captured;
            return true;
        }

        /// <summary>
        /// 在没有登记玩家的世界恢复检查点并激活指定地图，不重放死亡或 Buff 首次效果。
        /// 目的地图几何由调用方建立；角色身份与占位沿用创建角色的校验，来源缺失的外部状态不会补造。
        /// </summary>
        /// <param name="checkpoint">独立玩家检查点。</param>
        /// <param name="roomId">新玩家所属并激活的地图编号。</param>
        /// <param name="position">新地图内由宿主指定的合法出生位置。</param>
        /// <param name="player">成功时返回当前世界中新建的玩家引用。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>完整校验通过并恢复玩家及自身 Buff 时为真。</returns>
        public bool TryRestorePlayerCheckpoint(CombatPlayerCheckpoint checkpoint, int roomId, Vector3 position,
            out CombatEntityRef player, out string error)
        {
            player = default;
            error = "玩家恢复要求完整战斗边界且目的世界尚未登记玩家。";
            if (!AtPlayerCheckpointBoundary() || playerId != 0 || TickIndex < 0 || TickIndex == long.MaxValue) return false;
            if (!ValidPlayerCheckpoint(checkpoint, out error)) return false;
            error = "目的地图、出生位置、角色生命身份或实体容量无效。";
            var spec = CopySpec(checkpoint.Spec);
            spec.RoomId = roomId;
            spec.Position = position;
            if (roomId <= 0 || !IsFinite(position) || spec.ActorId <= highestActorId ||
                !CanOccupy(roomId, position, spec.Radius, 0) || nextPersistentKey > long.MaxValue - 1L - checkpoint.Buffs.Count) return false;
            error = "玩家 Buff 或词条冷却的剩余帧数超过目的世界可表示时钟。";
            foreach (var group in checkpoint.AugmentGroups)
                foreach (var cooldown in group.Cooldowns)
                    if (cooldown.RemainingTicks > long.MaxValue - TickIndex) return false;
            foreach (var buff in checkpoint.Buffs)
                if (buff.NextTickIn > long.MaxValue - TickIndex ||
                    catalog.Buffs[buff.BuffId].Lifetime == CombatBuffLifetime.Timed && buff.RemainingTicks > long.MaxValue - TickIndex)
                    return false;
            player = CreateActor(spec);
            if (player.Id == 0) { error = "玩家配置未通过角色创建校验。"; return false; }
            var actor = actors[player.Id];
            actor.BaseSpec = CopySpec(checkpoint.BaseSpec);
            actor.Health = checkpoint.Health;
            actor.SkillCursor = checkpoint.SkillCursor;
            checkpoint.PermanentBonuses.CopyTo(actor.PermanentBonuses, 0);
            actor.DeadPending = checkpoint.Health == 0f;
            actor.DeathPublished = actor.DeadPending;
            actor.DeathClock = actor.DeadPending ? TickIndex : -1;
            nextDeathId = Math.Max(nextDeathId, checkpoint.DeathIdWatermark);
            foreach (var id in checkpoint.EnabledPersistentSkills) actor.EnabledPersistentSkills.Add(id);
            foreach (var cooldown in checkpoint.SkillCooldowns) actor.SkillCooldowns.Add(cooldown.SkillId, cooldown.RemainingTicks);
            foreach (var group in checkpoint.AugmentGroups)
            {
                if (group.Definitions.Count > 0)
                {
                    actor.AugmentGroups.Add(group.GroupId, group.Definitions.ConvertAll(CopyAugment));
                    actor.AugmentGroupOrder.Add(group.GroupId);
                }
                if (group.Cooldowns.Count == 0) continue;
                var cooldowns = new Dictionary<string, long>(StringComparer.Ordinal);
                foreach (var cooldown in group.Cooldowns) cooldowns.Add(cooldown.AugmentId, TickIndex + cooldown.RemainingTicks);
                actor.AugmentCooldowns.Add(group.GroupId, cooldowns);
            }
            actor.AugmentGroupOrder.Sort(StringComparer.Ordinal);
            foreach (var saved in checkpoint.Buffs)
            {
                var entity = Allocate();
                buffs.Add(entity.Id, new BuffComponent
                {
                    Owner = player.Id, SourceActorId = actor.Spec.ActorId, BuffId = saved.BuffId, Stacks = saved.Stacks,
                    RangeDelta = saved.RangeDelta, RangeMultiplier = saved.RangeMultiplier, PassiveDerived = saved.PassiveDerived,
                    Offense = ImportOffense(saved.Offense), NextTick = TickIndex + saved.NextTickIn,
                    ExpireTick = catalog.Buffs[saved.BuffId].Lifetime == CombatBuffLifetime.Persistent ? long.MaxValue : TickIndex + saved.RemainingTicks
                });
            }
            RecalculateBonuses(player.Id);
            activeRoomId = roomId;
            if (!roomTicks.ContainsKey(roomId)) roomTicks.Add(roomId, 0);
            error = null;
            return true;
        }

        /// <summary>
        /// 检查玩家检查点操作是否处于已消费的完整边界。
        /// </summary>
        /// <returns>当前不在模拟或效果执行中且没有待消费事件时为真。</returns>
        private bool AtPlayerCheckpointBoundary() =>
            !stepping && !faulted && effectQueue.Count == 0 && events.Count == 0 && activeRootId == 0 && activeDepth == 0;

        /// <summary>
        /// 复制角色配置并移除房间及位置，保留独立技能和弱点数组。
        /// </summary>
        /// <param name="spec">来源角色配置。</param>
        /// <returns>不携带原地图与位置的独立配置。</returns>
        private static CombatActorSpec CopyPlayerCheckpointSpec(CombatActorSpec spec)
        {
            var result = CopySpec(spec);
            result.RoomId = 0;
            result.Position = Vector3.zero;
            return result;
        }

        /// <summary>
        /// 在修改世界之前检查独立玩家数据、配置引用及剩余时钟。
        /// </summary>
        /// <param name="checkpoint">待校验的外部检查点。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>全部玩家数据与自身状态可独立重建时为真。</returns>
        private bool ValidPlayerCheckpoint(CombatPlayerCheckpoint checkpoint, out string error)
        {
            error = "玩家检查点版本、基础属性或技能状态无效。";
            if (checkpoint == null || checkpoint.Version != 1 || !checkpoint.Spec.IsPlayer || checkpoint.Spec.ActorId <= 0 ||
                checkpoint.Spec.Team == 0 || !ValidBaseAttributes(checkpoint.Spec) || !ValidBaseAttributes(checkpoint.BaseSpec) ||
                !IsFinite(checkpoint.Spec.Radius) || checkpoint.Spec.Radius <= 0f || !IsFinite(checkpoint.Spec.AggroRange) ||
                checkpoint.Spec.AggroRange < 0f || checkpoint.Spec.CriticalDamage < 1f || checkpoint.Spec.DodgeChance > ProbabilityCeiling ||
                checkpoint.Spec.DamageReduction > ProbabilityCeiling || !ValidSkillLoadout(checkpoint.Spec.SkillIds) ||
                !ValidSkillLoadout(checkpoint.BaseSpec.SkillIds) || !ValidDamageVulnerabilities(checkpoint.Spec.DamageVulnerabilities) ||
                !ValidDamageVulnerabilities(checkpoint.BaseSpec.DamageVulnerabilities) || checkpoint.Spec.BasicSkillId < 0 ||
                checkpoint.Spec.BasicSkillId > 0 && (!catalog.Skills.TryGetValue(checkpoint.Spec.BasicSkillId, out var basic) || !ValidSavedSkill(basic)) ||
                !IsFinite(checkpoint.Health) || checkpoint.Health < 0f || checkpoint.Health > checkpoint.Spec.MaxHealth ||
                checkpoint.DeathIdWatermark < 0 || checkpoint.DeathIdWatermark == int.MaxValue || checkpoint.PermanentBonuses == null ||
                checkpoint.PermanentBonuses.Length != 8 || checkpoint.SkillCursor < 0 || checkpoint.EnabledPersistentSkills == null ||
                checkpoint.SkillCooldowns == null || checkpoint.AugmentGroups == null || checkpoint.Buffs == null) return false;
            foreach (var bonus in checkpoint.PermanentBonuses)
                if (!IsFinite(bonus) || bonus < 0f || bonus > AttributeCeiling) return false;
            if (checkpoint.PermanentBonuses[5] > ProbabilityCeiling || checkpoint.PermanentBonuses[7] > ProbabilityCeiling) return false;
            var skillIds = GetSkillIds(checkpoint.Spec);
            if (skillIds.Length > 0 && checkpoint.SkillCursor >= skillIds.Length) return false;
            var persistentIds = new HashSet<int>();
            foreach (var id in checkpoint.EnabledPersistentSkills)
                if (!persistentIds.Add(id) || Array.IndexOf(skillIds, id) < 0 || catalog.Skills[id].CastMode != CombatSkillCastMode.Persistent)
                    return false;
            var cooldownIds = new HashSet<int>();
            foreach (var cooldown in checkpoint.SkillCooldowns)
                if (cooldown == null || cooldown.RemainingTicks < 0 || !cooldownIds.Add(cooldown.SkillId) ||
                    !catalog.Skills.TryGetValue(cooldown.SkillId, out var definition) || !ValidSavedSkill(definition)) return false;
            error = "玩家词条来源、定义或剩余冷却无效。";
            var groups = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in checkpoint.AugmentGroups)
            {
                if (group == null || string.IsNullOrWhiteSpace(group.GroupId) || !groups.Add(group.GroupId) ||
                    group.Definitions == null || group.Definitions.Count > 64 || group.Cooldowns == null) return false;
                var definitionIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var definition in group.Definitions)
                    if (!ValidAugment(definition) || !definitionIds.Add(definition.Id)) return false;
                var savedIds = new HashSet<string>(StringComparer.Ordinal);
                foreach (var cooldown in group.Cooldowns)
                    if (cooldown == null || string.IsNullOrWhiteSpace(cooldown.AugmentId) || cooldown.AugmentId.Length > 128 ||
                        !savedIds.Add(cooldown.AugmentId) || cooldown.RemainingTicks < 0) return false;
            }
            error = "玩家自身 Buff 的配置、层数、剩余周期或死亡状态无效。";
            var buffIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var buff in checkpoint.Buffs)
            {
                if (buff == null || checkpoint.Health == 0f || !catalog.Buffs.TryGetValue(buff.BuffId, out var definition) ||
                    !ValidBuff(definition) || !ValidSavedEffects(definition.OnApply) || !ValidSavedEffects(definition.OnTick) ||
                    !ValidSavedEffects(definition.OnExpire) || buff.Stacks <= 0 || buff.Stacks > definition.MaxStacks ||
                    buff.NextTickIn < 0 || !ValidSavedRange(buff.RangeDelta, buff.RangeMultiplier) || !ValidSavedOffense(buff.Offense) ||
                    !buffIds.Add(buff.BuffId + ":" + buff.PassiveDerived) ||
                    (definition.Lifetime == CombatBuffLifetime.Persistent ? buff.RemainingTicks != long.MaxValue :
                        buff.RemainingTicks <= 0 || buff.RemainingTicks > definition.DurationTicks)) return false;
            }
            error = null;
            return true;
        }
    }
}
