using System;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 局内战斗属性强化与装备基础属性的安全入口。
    /// </summary>
    public sealed partial class CombatWorld
    {
        private const float AttributeCeiling = 1000000f;
        private const float ProbabilityCeiling = 0.75f;

        /// <summary>
        /// 给存活角色增加一项永久局内属性强化。
        /// </summary>
        /// <param name="actor">目标角色引用。</param>
        /// <param name="attribute">要增加的属性。</param>
        /// <param name="amount">有限且为正的增量。</param>
        /// <returns>实际增加了属性时为真。</returns>
        public bool TryAddAttributeBonus(CombatEntityRef actor, CombatAttributeKind attribute, float amount) =>
            ApplyAttributeBonus(actor, attribute, amount);

        /// <summary>
        /// 预览更换装备和局外技能基础属性后的最终战斗属性。
        /// </summary>
        /// <param name="actor">目标角色引用。</param>
        /// <param name="baseSpec">新基础属性，仅使用八项战斗属性、移动速度和攻击范围。</param>
        /// <param name="preview">叠加已接受局内永久强化后的角色配置，不包含独立 Buff 修正。</param>
        /// <returns>角色有效且基础属性合法时为真；施法中也可预览。</returns>
        public bool TryPreviewBaseAttributes(CombatEntityRef actor, CombatActorSpec baseSpec, out CombatActorSpec preview)
        {
            preview = default;
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) ||
                state.DeadPending || state.Health <= 0f || !ValidBaseAttributes(baseSpec)) return false;
            preview = CopySpec(ComposeAttributes(state, baseSpec));
            return true;
        }

        /// <summary>
        /// 替换装备和局外技能计算后的基础属性，并保留局内状态。
        /// </summary>
        /// <param name="actor">目标角色引用。</param>
        /// <param name="baseSpec">新基础属性，仅使用八项战斗属性、移动速度和攻击范围。</param>
        /// <returns>角色存活且基础属性合法并成功替换时为真。</returns>
        public bool TryReplaceBaseAttributes(CombatEntityRef actor, CombatActorSpec baseSpec)
        {
            if (!TryPreviewBaseAttributes(actor, baseSpec, out var preview)) return false;
            var state = actors[actor.Id];
            state.BaseSpec = CopySpec(baseSpec);
            state.Spec = preview;
            state.Health = Mathf.Min(state.Health, preview.MaxHealth);
            return true;
        }

        /// <summary>
        /// 同步玩家永久成长后的基础属性，死亡玩家保持零生命与待确认死亡状态。
        /// </summary>
        /// <param name="player">当前世界登记的玩家引用。</param>
        /// <param name="baseSpec">由装备与永久成长计算的基础属性。</param>
        /// <returns>玩家和基础属性有效且替换成功时为真。</returns>
        public bool TryRefreshPlayerBaseAttributes(CombatEntityRef player, CombatActorSpec baseSpec)
        {
            if (faulted || stepping || !IsValid(player) || player.Id != playerId ||
                !actors.TryGetValue(player.Id, out var state) || !state.Spec.IsPlayer || !ValidBaseAttributes(baseSpec)) return false;
            state.Spec = CopySpec(ComposeAttributes(state, baseSpec));
            var refreshed = state.BaseSpec;
            refreshed.MaxHealth = baseSpec.MaxHealth;
            refreshed.Attack = baseSpec.Attack;
            refreshed.Defense = baseSpec.Defense;
            refreshed.CriticalRating = baseSpec.CriticalRating;
            refreshed.CriticalDamage = baseSpec.CriticalDamage;
            refreshed.DodgeChance = baseSpec.DodgeChance;
            refreshed.DamageIncrease = baseSpec.DamageIncrease;
            refreshed.DamageReduction = baseSpec.DamageReduction;
            refreshed.MoveSpeed = baseSpec.MoveSpeed;
            refreshed.AttackRange = baseSpec.AttackRange;
            state.BaseSpec = CopySpec(refreshed);
            state.Health = Mathf.Min(state.Health, state.Spec.MaxHealth);
            return true;
        }

        /// <summary>
        /// 检查基础属性是否适合战斗角色。
        /// </summary>
        /// <param name="spec">基础属性。</param>
        /// <returns>全部数值有限且位于合法非负区间时为真。</returns>
        private static bool ValidBaseAttributes(CombatActorSpec spec) =>
            IsFinite(spec.MaxHealth) && spec.MaxHealth > 0f && IsFinite(spec.Attack) && spec.Attack >= 0f &&
            IsFinite(spec.Defense) && spec.Defense >= 0f && IsFinite(spec.CriticalRating) && spec.CriticalRating >= 0f &&
            IsFinite(spec.CriticalDamage) && spec.CriticalDamage >= 0f &&
            IsFinite(spec.DodgeChance) && spec.DodgeChance >= 0f &&
            IsFinite(spec.DamageIncrease) && spec.DamageIncrease >= 0f &&
            IsFinite(spec.DamageReduction) && spec.DamageReduction >= 0f &&
            IsFinite(spec.MoveSpeed) && spec.MoveSpeed >= 0f && spec.MoveSpeed <= AttributeCeiling &&
            IsFinite(spec.AttackRange) && spec.AttackRange >= 0f && spec.AttackRange <= AttributeCeiling;

        /// <summary>
        /// 将基础值和累计永久强化合成为角色属性。
        /// </summary>
        /// <param name="state">现有角色状态。</param>
        /// <param name="baseSpec">新基础属性。</param>
        /// <returns>保持角色身份及其他配置的完整角色配置。</returns>
        private static CombatActorSpec ComposeAttributes(ActorComponent state, CombatActorSpec baseSpec)
        {
            var result = state.Spec;
            var bonus = state.PermanentBonuses;
            result.MaxHealth = Cap(baseSpec.MaxHealth, bonus[0], AttributeCeiling);
            result.Attack = Cap(baseSpec.Attack, bonus[1], AttributeCeiling);
            result.Defense = Cap(baseSpec.Defense, bonus[2], AttributeCeiling);
            result.CriticalRating = Cap(baseSpec.CriticalRating, bonus[3], AttributeCeiling);
            result.CriticalDamage = Cap(baseSpec.CriticalDamage == 0f ? 1.5f : Mathf.Max(1f, baseSpec.CriticalDamage), bonus[4], AttributeCeiling);
            result.DodgeChance = Cap(baseSpec.DodgeChance, bonus[5], ProbabilityCeiling);
            result.DamageIncrease = Cap(baseSpec.DamageIncrease, bonus[6], AttributeCeiling);
            result.DamageReduction = Cap(baseSpec.DamageReduction, bonus[7], ProbabilityCeiling);
            result.MoveSpeed = baseSpec.MoveSpeed;
            result.AttackRange = baseSpec.AttackRange;
            return result;
        }

        /// <summary>
        /// 按属性上限合并基础值和累计强化值。
        /// </summary>
        /// <param name="value">基础值。</param>
        /// <param name="bonus">累计强化值。</param>
        /// <param name="ceiling">最终属性上限。</param>
        /// <returns>有限且不超过上限的属性。</returns>
        private static float Cap(float value, float bonus, float ceiling) =>
            (float)Math.Min(ceiling, (double)value + bonus);

        /// <summary>
        /// 检查属性枚举是否在可强化范围内。
        /// </summary>
        /// <param name="attribute">待检查属性。</param>
        /// <returns>属性有对应战斗数值时为真。</returns>
        private static bool ValidAttribute(CombatAttributeKind attribute) =>
            attribute >= CombatAttributeKind.MaxHealth && attribute <= CombatAttributeKind.DamageReduction;

        /// <summary>
        /// 更新累计永久强化，保留最大生命强化原有的补血规则。
        /// </summary>
        /// <param name="actor">目标角色引用。</param>
        /// <param name="attribute">要增加的属性。</param>
        /// <param name="amount">属性增量。</param>
        /// <returns>角色属性实际增加时为真。</returns>
        private bool ApplyAttributeBonus(CombatEntityRef actor, CombatAttributeKind attribute, float amount)
        {
            if (faulted || !IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) || state.DeadPending ||
                state.Health <= 0f || !ValidAttribute(attribute) || !IsFinite(amount) || amount <= 0f) return false;
            var index = (int)attribute;
            var ceiling = attribute == CombatAttributeKind.DodgeChance || attribute == CombatAttributeKind.DamageReduction
                ? ProbabilityCeiling : AttributeCeiling;
            var current = AttributeValue(state.Spec, attribute);
            if (current >= ceiling) return false;
            var oldBonus = state.PermanentBonuses[index];
            state.PermanentBonuses[index] = (float)Math.Min(ceiling, (double)oldBonus + amount);
            var updated = ComposeAttributes(state, state.BaseSpec);
            var actual = AttributeValue(updated, attribute) - current;
            if (actual <= 0f)
            {
                state.PermanentBonuses[index] = oldBonus;
                return false;
            }
            state.Spec = updated;
            if (attribute == CombatAttributeKind.MaxHealth)
                state.Health = (float)Math.Min(updated.MaxHealth, (double)state.Health + actual);
            return true;
        }

        /// <summary>
        /// 获取一项战斗属性的有效值。
        /// </summary>
        /// <param name="spec">角色配置。</param>
        /// <param name="attribute">属性类型。</param>
        /// <returns>指定属性值。</returns>
        private static float AttributeValue(CombatActorSpec spec, CombatAttributeKind attribute)
        {
            switch (attribute)
            {
                case CombatAttributeKind.MaxHealth: return spec.MaxHealth;
                case CombatAttributeKind.Attack: return spec.Attack;
                case CombatAttributeKind.Defense: return spec.Defense;
                case CombatAttributeKind.CriticalRating: return spec.CriticalRating;
                case CombatAttributeKind.CriticalDamage: return spec.CriticalDamage;
                case CombatAttributeKind.DodgeChance: return spec.DodgeChance;
                case CombatAttributeKind.DamageIncrease: return spec.DamageIncrease;
                case CombatAttributeKind.DamageReduction: return spec.DamageReduction;
                default: return 0f;
            }
        }
    }
}
