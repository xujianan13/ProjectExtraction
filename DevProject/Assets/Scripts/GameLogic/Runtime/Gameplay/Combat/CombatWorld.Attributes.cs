using System;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 局内战斗属性强化与效果共用的安全入口。
    /// </summary>
    public sealed partial class CombatWorld
    {
        private const float AttributeCeiling = 1000000f;
        private const float ProbabilityCeiling = 0.75f;

        /// <summary>
        /// 给存活角色增加一项战斗属性。
        /// </summary>
        /// <param name="actor">目标角色引用。</param>
        /// <param name="attribute">要增加的属性。</param>
        /// <param name="amount">有限且为正的增量。</param>
        /// <returns>实际增加了属性时为真。</returns>
        public bool TryAddAttributeBonus(CombatEntityRef actor, CombatAttributeKind attribute, float amount) =>
            ApplyAttributeBonus(actor, attribute, amount);

        /// <summary>
        /// 检查属性枚举是否在可强化范围内。
        /// </summary>
        /// <param name="attribute">待检查属性。</param>
        /// <returns>属性有对应战斗数值时为真。</returns>
        private static bool ValidAttribute(CombatAttributeKind attribute) =>
            attribute >= CombatAttributeKind.MaxHealth && attribute <= CombatAttributeKind.DamageReduction;

        /// <summary>
        /// 更新角色配置并保留当前已损生命。
        /// </summary>
        /// <param name="actor">目标角色引用。</param>
        /// <param name="attribute">要增加的属性。</param>
        /// <param name="amount">属性增量。</param>
        /// <returns>角色属性实际增加时为真。</returns>
        private bool ApplyAttributeBonus(CombatEntityRef actor, CombatAttributeKind attribute, float amount)
        {
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) || state.DeadPending ||
                state.Health <= 0f || !ValidAttribute(attribute) || !IsFinite(amount) || amount <= 0f) return false;

            CombatActorSpec spec = state.Spec;
            float current;
            switch (attribute)
            {
                case CombatAttributeKind.MaxHealth: current = spec.MaxHealth; break;
                case CombatAttributeKind.Attack: current = spec.Attack; break;
                case CombatAttributeKind.Defense: current = spec.Defense; break;
                case CombatAttributeKind.CriticalRating: current = spec.CriticalRating; break;
                case CombatAttributeKind.CriticalDamage: current = spec.CriticalDamage; break;
                case CombatAttributeKind.DodgeChance: current = spec.DodgeChance; break;
                case CombatAttributeKind.DamageIncrease: current = spec.DamageIncrease; break;
                case CombatAttributeKind.DamageReduction: current = spec.DamageReduction; break;
                default: return false;
            }

            float ceiling = attribute == CombatAttributeKind.DodgeChance || attribute == CombatAttributeKind.DamageReduction
                ? ProbabilityCeiling : AttributeCeiling;
            if (!IsFinite(current) || current >= ceiling) return false;
            float updated = (float)Math.Min((double)ceiling, (double)current + amount);
            if (updated <= current) return false;
            switch (attribute)
            {
                case CombatAttributeKind.MaxHealth:
                    spec.MaxHealth = updated;
                    state.Health = (float)Math.Min(updated, (double)state.Health + ((double)updated - current));
                    break;
                case CombatAttributeKind.Attack: spec.Attack = updated; break;
                case CombatAttributeKind.Defense: spec.Defense = updated; break;
                case CombatAttributeKind.CriticalRating: spec.CriticalRating = updated; break;
                case CombatAttributeKind.CriticalDamage: spec.CriticalDamage = updated; break;
                case CombatAttributeKind.DodgeChance: spec.DodgeChance = updated; break;
                case CombatAttributeKind.DamageIncrease: spec.DamageIncrease = updated; break;
                case CombatAttributeKind.DamageReduction: spec.DamageReduction = updated; break;
            }
            state.Spec = spec;
            return true;
        }
    }
}
