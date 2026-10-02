using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 本局高级词条的接纳、技能修正和确认伤害触发。
    /// </summary>
    public sealed partial class CombatWorld
    {
        public const float MinSkillMultiplier = 0.1f;
        public const float MaxSkillMultiplier = 10f;

        /// <summary>
        /// 原子替换一组角色词条；内部冷却按角色、组和词条编号保留。
        /// </summary>
        /// <param name="actor">持有词条的角色。</param>
        /// <param name="groupId">独立组编号，例如 equipment 或 rogue。</param>
        /// <param name="definitions">本组新定义；空列表用于卸下整组。</param>
        /// <returns>角色与全部定义有效并完成替换时为真。</returns>
        public bool TryReplaceAugments(CombatEntityRef actor, string groupId, IReadOnlyList<CombatAugmentDefinition> definitions)
        {
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var owner) || owner.DeadPending ||
                string.IsNullOrWhiteSpace(groupId) || definitions == null || definitions.Count > 64) return false;
            var copied = new List<CombatAugmentDefinition>(definitions.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var definition in definitions)
            {
                if (!ValidAugment(definition) || !ids.Add(definition.Id)) return false;
                var clone = new CombatAugmentDefinition
                {
                    Id = definition.Id, Trigger = definition.Trigger, Chance = definition.Chance,
                    CooldownTicks = definition.CooldownTicks, TargetSelf = definition.TargetSelf,
                    SkillId = definition.SkillId, CooldownMultiplier = definition.CooldownMultiplier,
                    RangeMultiplier = definition.RangeMultiplier, DamageMultiplier = definition.DamageMultiplier
                };
                foreach (var effect in definition.Effects) clone.Effects.Add(CopyEffectWithRadius(effect, effect.Radius));
                copied.Add(clone);
            }
            if (copied.Count == 0)
            {
                owner.AugmentGroups.Remove(groupId);
                owner.AugmentGroupOrder.Remove(groupId);
            }
            else
            {
                if (!owner.AugmentGroups.ContainsKey(groupId))
                {
                    owner.AugmentGroupOrder.Add(groupId);
                    owner.AugmentGroupOrder.Sort(StringComparer.Ordinal);
                }
                owner.AugmentGroups[groupId] = copied;
            }
            return true;
        }

        /// <summary>
        /// 校验词条参数和效果引用。
        /// </summary>
        /// <param name="definition">待接纳定义。</param>
        /// <returns>可安全用于战斗时为真。</returns>
        private bool ValidAugment(CombatAugmentDefinition definition)
        {
            if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || definition.Id.Length > 128 ||
                definition.Trigger < CombatTriggerKind.None || definition.Trigger > CombatTriggerKind.Damaged ||
                !IsFinite(definition.Chance) || definition.Chance < 0f || definition.Chance > 1f ||
                definition.CooldownTicks < 0 || definition.SkillId < 0 ||
                definition.SkillId > 0 && !catalog.Skills.ContainsKey(definition.SkillId) ||
                definition.Effects == null || definition.Effects.Count > 16 || !ValidMultiplier(definition.CooldownMultiplier) ||
                !ValidMultiplier(definition.RangeMultiplier) || !ValidMultiplier(definition.DamageMultiplier)) return false;
            if (definition.Trigger == CombatTriggerKind.None && definition.Effects.Count > 0 ||
                definition.Trigger != CombatTriggerKind.None && definition.Effects.Count == 0) return false;
            foreach (var effect in definition.Effects)
                if (!ValidEffect(effect) ||
                    effect.Kind == CombatEffectKind.SpawnProjectile &&
                        (!catalog.Projectiles.TryGetValue(effect.LinkedId, out var projectile) || !ValidProjectile(projectile)) ||
                    effect.Kind == CombatEffectKind.ApplyBuff &&
                        (!catalog.Buffs.TryGetValue(effect.LinkedId, out var buff) || !ValidBuff(buff))) return false;
            return true;
        }

        /// <summary>
        /// 校验单个技能倍率处于受控范围。
        /// </summary>
        /// <param name="value">候选倍率。</param>
        /// <returns>倍率有限且在允许区间时为真。</returns>
        private static bool ValidMultiplier(float value) =>
            IsFinite(value) && value >= MinSkillMultiplier && value <= MaxSkillMultiplier;

        /// <summary>
        /// 合成本角色当前技能的三项乘法修正。
        /// </summary>
        /// <param name="owner">技能拥有者。</param>
        /// <param name="skillId">即将施放的技能编号。</param>
        /// <param name="cooldown">最终冷却倍率。</param>
        /// <param name="range">最终范围倍率。</param>
        /// <param name="damage">最终伤害倍率。</param>
        private static void GetSkillModifiers(ActorComponent owner, int skillId, out float cooldown, out float range, out float damage)
        {
            double cooldownProduct = 1d, rangeProduct = 1d, damageProduct = 1d;
            foreach (var groupId in owner.AugmentGroupOrder)
                foreach (var definition in owner.AugmentGroups[groupId])
                {
                    if (definition.SkillId != skillId &&
                        (definition.SkillId != 0 || owner.Spec.BasicSkillId != skillId)) continue;
                    cooldownProduct *= definition.CooldownMultiplier;
                    rangeProduct *= definition.RangeMultiplier;
                    damageProduct *= definition.DamageMultiplier;
                }
            cooldown = (float)Math.Max(MinSkillMultiplier, Math.Min(MaxSkillMultiplier, cooldownProduct));
            range = (float)Math.Max(MinSkillMultiplier, Math.Min(MaxSkillMultiplier, rangeProduct));
            damage = (float)Math.Max(MinSkillMultiplier, Math.Min(MaxSkillMultiplier, damageProduct));
        }

        /// <summary>
        /// 从一次实际扣血结果尝试执行已装备的条件词条。
        /// </summary>
        /// <param name="ownerId">词条拥有者槽位。</param>
        /// <param name="otherId">对方角色槽位。</param>
        /// <param name="trigger">已确认的触发类型。</param>
        private void TriggerAugments(int ownerId, int otherId, CombatTriggerKind trigger)
        {
            if (!actors.TryGetValue(ownerId, out var owner) || owner.DeadPending ||
                !actors.TryGetValue(otherId, out var other) || owner.Spec.RoomId != other.Spec.RoomId ||
                trigger == CombatTriggerKind.Damaged && other.DeadPending) return;
            foreach (var groupId in owner.AugmentGroupOrder)
            {
                if (!owner.AugmentGroups.TryGetValue(groupId, out var definitions)) continue;
                foreach (var definition in definitions)
                {
                    if (definition.Trigger != trigger || definition.Chance <= 0f) continue;
                    if (!owner.AugmentCooldowns.TryGetValue(groupId, out var cooldowns))
                    {
                        cooldowns = new Dictionary<string, long>(StringComparer.Ordinal);
                        owner.AugmentCooldowns.Add(groupId, cooldowns);
                    }
                    var now = GetBuffClock(owner);
                    if (cooldowns.TryGetValue(definition.Id, out var nextTick) && now < nextTick) continue;
                    if (definition.Chance < 1f && random.NextDouble() >= definition.Chance) continue;
                    cooldowns[definition.Id] = now + definition.CooldownTicks;
                    var direction = other.Position - owner.Position;
                    if (direction.sqrMagnitude < 0.0001f) direction = owner.Forward;
                    var target = definition.TargetSelf ? Ref(ownerId) : Ref(otherId);
                    var point = definition.TargetSelf ? owner.Position : other.Position;
                    var offense = new CombatOffense(owner.Spec);
                    foreach (var effect in definition.Effects)
                        Enqueue(effect, ownerId, target, point, direction.normalized, owner.Spec.ActorId, offense,
                            0f, 1f, true);
                }
            }
        }

        /// <summary>
        /// 对有效存活目标执行治疗并发布实际回血事件。
        /// </summary>
        /// <param name="command">治疗效果及来源上下文。</param>
        private void ExecuteHeal(EffectCommand command)
        {
            var targetRef = command.Target;
            if (targetRef.Id == 0)
            {
                if (actors.ContainsKey(command.Source)) targetRef = Ref(command.Source);
                else if (projectiles.TryGetValue(command.Source, out var projectile)) targetRef = projectile.Source;
            }
            if (!IsValid(targetRef) || !actors.TryGetValue(targetRef.Id, out var target) ||
                target.DeadPending || target.Health <= 0f) return;
            var amount = Math.Max(0d, (double)command.Definition.Value +
                (double)command.Offense.Attack * command.Definition.AttackCoefficient);
            var previous = target.Health;
            target.Health = (float)Math.Min((double)target.Spec.MaxHealth, (double)target.Health + amount);
            var actual = target.Health - previous;
            if (actual <= 0f) return;
            events.Add(new CombatEvent(CombatEventKind.Heal, TickIndex, target.Spec.RoomId,
                command.InstigatorActorId, target.Spec.ActorId, command.Definition.DefinitionId, actual, target.Position));
        }

        /// <summary>
        /// 按稳定业务角色编号查找当前实体槽位。
        /// </summary>
        /// <param name="actorId">来源角色业务编号。</param>
        /// <returns>存在时返回槽位，否则返回零。</returns>
        private int FindActorSlot(int actorId)
        {
            if (actorId <= 0) return 0;
            foreach (var pair in actors)
                if (pair.Value.Spec.ActorId == actorId) return pair.Key;
            return 0;
        }
    }
}
