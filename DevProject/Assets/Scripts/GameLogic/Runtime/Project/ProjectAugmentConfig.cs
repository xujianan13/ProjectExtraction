using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 将装备与局内强化的冻结数值转换为战斗纯数据，并校验跨表依赖。
    /// </summary>
    public static class ProjectAugmentConfig
    {
        /// <summary>
        /// 从配置创建独立效果快照，不共享表或其他角色的可写数据。
        /// </summary>
        /// <param name="id">强化配置身份。</param>
        /// <param name="sourceId">当前物品或奖励的稳定来源身份。</param>
        /// <param name="value">冻结的效果系数、绝对概率或技能比例。</param>
        /// <param name="attackRange">本局创建战斗目录时的基础攻击范围。</param>
        /// <param name="definition">转换成功的独立强化。</param>
        /// <returns>配置及冻结数值有效时为真。</returns>
        public static bool TryCreate(string id, string sourceId, float value, float attackRange, out CombatAugmentDefinition definition)
        {
            definition = null;
            CfgCombatAugmentsRow row = GameTables.CfgCombatAugments?.GetById(id);
            if (row == null || string.IsNullOrWhiteSpace(sourceId) || !ValidValue(row, value) ||
                !CombatContentConfig.TryCreateEffects(row.EffectIDs, attackRange, out List<CombatEffectDefinition> effects)) return false;
            var result = new CombatAugmentDefinition
            {
                Id = sourceId, Trigger = (CombatTriggerKind)row.Trigger, Chance = row.ValueMode == 1 ? value : (float)row.Chance,
                CooldownTicks = row.CooldownTicks, TargetSelf = row.TargetSelf, SkillId = row.SkillID, Effects = effects
            };
            if (row.ValueMode == 0)
                foreach (CombatEffectDefinition effect in effects)
                {
                    effect.Value *= value;
                    effect.AttackCoefficient *= value;
                    if (!Finite(effect.Value) || !Finite(effect.AttackCoefficient)) return false;
                }
            if (row.ValueMode == 2)
            {
                if (row.ModifierKind == 1) result.CooldownMultiplier = 1 - value;
                else if (row.ModifierKind == 2) result.RangeMultiplier = 1 + value;
                else if (row.ModifierKind == 3) result.DamageMultiplier = 1 + value;
                else return false;
            }
            definition = result;
            return true;
        }

        /// <summary>
        /// 校验冻结数值的结构边界，生成区间变化不修改已保存的词条。
        /// </summary>
        /// <param name="row">强化定义。</param>
        /// <param name="value">冻结数值。</param>
        /// <returns>数值可以安全进入 ECS 时为真。</returns>
        public static bool ValidValue(CfgCombatAugmentsRow row, double value) => row != null && Finite(value) && value >= 0 &&
            (row.ValueMode != 1 || value <= 1) && (row.ValueMode != 2 ||
                row.ModifierKind == 1 && 1f - (float)value >= CombatWorld.MinSkillMultiplier ||
                row.ModifierKind >= 2 && 1f + (float)value <= CombatWorld.MaxSkillMultiplier);

        /// <summary>
        /// 校验高级词条、强化规则和奖励前置关系。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>全部定义可以启用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgCombatAugments == null || GameTables.CfgAdvancedAffixes == null || GameTables.CfgCombatEffects == null ||
                GameTables.CfgCombatSkills == null || GameTables.CfgRogueRewards == null || GameTables.CfgQualities == null)
                return Fail("高级词条与强化配置未全部加载。", out error);
            foreach (CfgCombatAugmentsRow row in GameTables.CfgCombatAugments.GetAll())
            {
                if (string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.Title) || row.Version < 1 ||
                    row.Trigger < 0 || row.Trigger > 4 || !Finite(row.Chance) || row.Chance < 0 || row.Chance > 1 ||
                    row.CooldownTicks < 0 || row.SkillID < 0 || row.SkillID > 0 && GameTables.CfgCombatSkills.GetById(row.SkillID) == null ||
                    row.ValueMode < 0 || row.ValueMode > 2 || row.ModifierKind < 0 || row.ModifierKind > 3 || row.EffectIDs == null || row.EffectIDs.Length > 16 ||
                    row.ValueMode == 2 && (row.Trigger != 0 || row.ModifierKind == 0 || row.EffectIDs.Length != 0) ||
                    row.ValueMode != 2 && (row.Trigger == 0 || row.ModifierKind != 0 || row.EffectIDs.Length == 0))
                    return Fail("战斗强化结构无效：" + row.ID, out error);
                foreach (int effectId in row.EffectIDs)
                {
                    CfgCombatEffectsRow effect = GameTables.CfgCombatEffects.GetById(effectId);
                    if (effect == null || row.ValueMode == 0 && effect.Kind != (int)CombatEffectKind.Damage && effect.Kind != (int)CombatEffectKind.Heal)
                        return Fail("强化效果引用或倍率模式无效：" + row.ID, out error);
                }
            }
            foreach (CfgAdvancedAffixesRow row in GameTables.CfgAdvancedAffixes.GetAll())
            {
                CfgCombatAugmentsRow augment = GameTables.CfgCombatAugments.GetById(row.AugmentID);
                if (string.IsNullOrWhiteSpace(row.ID) || row.ItemType != (int)ProjectItemType.Weapon && row.ItemType != (int)ProjectItemType.Armor ||
                    row.MinQuality < 0 || row.MinQuality > 5 || row.Weight < 1 || row.ValueMax < row.ValueMin ||
                    !ValidValue(augment, row.ValueMin) || !ValidValue(augment, row.ValueMax) ||
                    !TryCreate(row.AugmentID, row.ID, (float)row.ValueMax, (float)ProjectRulesConfig.Profile.BaseAttackRange, out _))
                    return Fail("高级词条生成规则无效：" + row.ID, out error);
            }
            foreach (CfgQualitiesRow quality in GameTables.CfgQualities.GetAll())
                foreach (ProjectItemType type in new[] { ProjectItemType.Weapon, ProjectItemType.Armor })
                {
                    int count = 0;
                    long weight = 0;
                    foreach (CfgAdvancedAffixesRow row in GameTables.CfgAdvancedAffixes.GetAll())
                        if (row.ItemType == (int)type && row.MinQuality <= quality.ID)
                        {
                            count++;
                            weight += row.Weight;
                        }
                    if (quality.AdvancedAffixCount < 0 || quality.AdvancedAffixCount > 32 || quality.AdvancedAffixCount > count || weight > int.MaxValue)
                        return Fail("品质高级词条池不足或权重越界：" + quality.ID + "/" + type, out error);
                }
            return ValidateRewards(out error);
        }

        /// <summary>
        /// 检查技能奖励等级与可达前置，允许有限构筑自然耗尽。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>奖励组合合法时为真。</returns>
        private static bool ValidateRewards(out string error)
        {
            error = null;
            int rootCount = 0;
            int augmentCount = 0;
            foreach (CfgRogueRewardsRow row in GameTables.CfgRogueRewards.GetAll())
            {
                bool hasPrerequisite = !string.IsNullOrEmpty(row.PrerequisiteID);
                if (!Enum.IsDefined(typeof(GameLogic.Gameplay.Roguelike.RogueRewardKind), row.Kind) ||
                    row.MaxRank < 1 || row.PrerequisiteRank < 0 || hasPrerequisite != (row.PrerequisiteRank > 0))
                    return Fail("Rogue 等级或前置等级无效：" + row.ID, out error);
                if (!hasPrerequisite) rootCount++;
                if (row.Kind == 2)
                {
                    augmentCount++;
                    CfgCombatAugmentsRow augment = GameTables.CfgCombatAugments.GetById(row.AugmentID);
                    if (row.MaxRank < 1 || row.Amount <= 0 || !ValidValue(augment, row.Amount * row.MaxRank) ||
                        !TryCreate(row.AugmentID, row.ID, (float)(row.Amount * row.MaxRank), (float)ProjectRulesConfig.Profile.BaseAttackRange, out _))
                        return Fail("Rogue 战斗强化数值无效：" + row.ID, out error);
                }
                else if (!string.IsNullOrEmpty(row.AugmentID)) return Fail("普通奖励不能引用战斗强化：" + row.ID, out error);
                var visited = new HashSet<string> { row.ID };
                var groups = new HashSet<string>();
                if (!string.IsNullOrEmpty(row.ExclusiveGroup)) groups.Add(row.ExclusiveGroup);
                CfgRogueRewardsRow current = row;
                while (!string.IsNullOrEmpty(current.PrerequisiteID))
                {
                    CfgRogueRewardsRow prior = GameTables.CfgRogueRewards.GetById(current.PrerequisiteID);
                    if (prior == null || !visited.Add(prior.ID) || current.PrerequisiteRank < 1 ||
                        prior.MaxRank > 0 && prior.MaxRank < current.PrerequisiteRank ||
                        !string.IsNullOrEmpty(prior.ExclusiveGroup) && !groups.Add(prior.ExclusiveGroup))
                        return Fail("Rogue 前置循环、缺失或互斥导致不可达：" + row.ID, out error);
                    current = prior;
                }
            }
            if (augmentCount > 64) return Fail("Rogue 强化来源超过 ECS 容量。", out error);
            if (rootCount == 0) return Fail("Rogue 技能池缺少可达的初始强化。", out error);
            return true;
        }

        /// <summary>
        /// 检查数值能够安全转换为有限单精度值。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>处于有限单精度范围时为真。</returns>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;

        /// <summary>
        /// 返回可展示的配置错误，不以异常中断启动。
        /// </summary>
        /// <param name="message">错误说明。</param>
        /// <param name="error">传递给调用方的原因。</param>
        /// <returns>始终为假。</returns>
        private static bool Fail(string message, out string error)
        {
            error = message;
            return false;
        }
    }
}
