using System;
using System.Collections.Generic;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 项目玩法配置访问与跨表校验。
    /// </summary>
    public static class ProjectRulesConfig
    {
        /// <summary>
        /// 取得唯一玩家规则行。
        /// </summary>
        public static CfgProfileRow Profile => GameTables.CfgProfile?.GetById(1);

        /// <summary>
        /// 取得唯一 Rogue 策略行。
        /// </summary>
        public static CfgRoguePolicyRow RoguePolicy => GameTables.CfgRoguePolicy?.GetById(1);

        /// <summary>
        /// 按身份取得成长技能。
        /// </summary>
        /// <param name="id">技能身份。</param>
        /// <returns>技能配置行。</returns>
        public static CfgSkillsRow Skill(string id) => GameTables.CfgSkills?.GetById(id);

        /// <summary>
        /// 按身份取得物品模板。
        /// </summary>
        /// <param name="id">模板身份。</param>
        /// <returns>物品模板行。</returns>
        public static CfgItemDefinitionsRow Item(string id) => GameTables.CfgItemDefinitions?.GetById(id);

        /// <summary>
        /// 按枚举值取得品质。
        /// </summary>
        /// <param name="quality">品质枚举值。</param>
        /// <returns>品质配置行。</returns>
        public static CfgQualitiesRow Quality(ProjectItemQuality quality) => GameTables.CfgQualities?.GetById((int)quality);

        /// <summary>
        /// 按身份取得词条。
        /// </summary>
        /// <param name="id">词条身份。</param>
        /// <returns>词条配置行。</returns>
        public static CfgAffixesRow Affix(string id) => GameTables.CfgAffixes?.GetById(id);

        /// <summary>
        /// 校验玩法规则与跨表引用。
        /// </summary>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>配置是否可以启用。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgProfile == null || GameTables.CfgSkills == null || GameTables.CfgItemDefinitions == null ||
                GameTables.CfgQualities == null || GameTables.CfgAffixes == null || GameTables.CfgLootPool == null ||
                GameTables.CfgLootQualities == null || GameTables.CfgRoguePolicy == null || GameTables.CfgRogueRewards == null)
            {
                error = "局外玩法配置表未全部加载。";
                return false;
            }
            CfgProfileRow p = Profile;
            if (p == null || GameTables.CfgProfile.GetAll().Count != 1 || p.StaminaCap <= 0 || p.EntryCost <= 0 ||
                p.RecoverySeconds <= 0 || p.SupplyAmount <= 0 || p.DailyResetHour < 0 || p.DailyResetHour > 23 ||
                p.GameUtcOffsetHours < -12 || p.GameUtcOffsetHours > 14 || p.SafeSlots < 0 ||
                p.NpcItemPrice < 0 || p.ExtractionExperience < 0 || p.SkillPointsPerLevel < 0 ||
                p.ExperienceBase <= 0 || p.ExperiencePerLevel < 0 ||
                p.RunHistoryLimit < 1 || p.RunHistoryLimit > 100 || p.MaxLevel < 1 || p.MaxLevel > 1000 ||
                p.StartingLevel < 1 || p.StartingLevel > p.MaxLevel || p.StartingExperience < 0 ||
                p.StartingGold < 0 || p.StartingDiamonds < 0 || p.StartingSkillPoints < 0 ||
                !FiniteFloat(p.BaseMaxHealth) || !FiniteFloat(p.BaseCriticalDamage) ||
                !FiniteFloat(p.BaseAttackRange) || !FiniteFloat(p.BaseMoveSpeed) ||
                !FiniteFloat(p.UnarmedAttack) || !FiniteFloat(p.MaxAttributeValue) ||
                !FiniteFloat(p.MaxAttackRange) || !FiniteFloat(p.MaxDodgeChance) ||
                !FiniteFloat(p.MaxDamageReduction) || !FiniteFloat(p.AffixLevelScale) ||
                p.BaseMaxHealth <= 0 || p.BaseCriticalDamage < 1 || p.BaseAttackRange <= 0 || p.BaseMoveSpeed <= 0 ||
                p.UnarmedAttack < 0 || p.MaxAttributeValue <= 0 ||
                p.BaseMaxHealth > p.MaxAttributeValue || p.BaseCriticalDamage > p.MaxAttributeValue ||
                p.UnarmedAttack > p.MaxAttributeValue || p.BaseAttackRange > p.MaxAttackRange ||
                p.MaxAttributeValue > ProjectEquipmentRules.MaxAttributeValue ||
                p.MaxAttackRange <= 0 || p.MaxAttackRange > ProjectEquipmentRules.MaxAttackRange ||
                p.MaxDodgeChance < 0 || p.MaxDodgeChance > 1 || p.MaxDamageReduction < 0 || p.MaxDamageReduction > 1 ||
                string.IsNullOrWhiteSpace(p.DefaultLootPoolID) || p.AffixLevelScale < 0 ||
                p.SupplyHourFirst < 0 || p.SupplyHourFirst > 23 || p.SupplyHourSecond <= p.SupplyHourFirst ||
                p.SupplyHourSecond > 23 || p.SupplyHourThird <= p.SupplyHourSecond || p.SupplyHourThird > 23 ||
                !Enum.IsDefined(typeof(ProjectItemQuality), p.StarterQuality) ||
                (long)p.ExperienceBase + (long)(p.MaxLevel - 1) * p.ExperiencePerLevel > int.MaxValue ||
                (long)p.StartingSkillPoints + (long)p.MaxLevel * p.SkillPointsPerLevel > int.MaxValue)
            {
                error = "玩家规则字段无效。";
                return false;
            }
            CfgItemDefinitionsRow starter = Item(p.StarterDefinitionId);
            if (starter == null || !starter.StarterEquipped || starter.Type != (int)ProjectItemType.Weapon)
            {
                error = "初始武器模板无效。";
                return false;
            }
            foreach (string id in new[] { "power", "vitality", "agility" })
            {
                CfgSkillsRow skill = Skill(id);
                if (skill == null || skill.MaxRank < 0 || skill.MaxRank > 1000 || skill.CostBase < 0 ||
                    skill.CostPerRank < 0 || !FiniteFloat(skill.EffectPerRank) ||
                    !FiniteFloat(skill.DisplayMultiplier) || skill.EffectPerRank < 0 || skill.DisplayMultiplier <= 0 ||
                    !FiniteFloat(1d + skill.MaxRank * skill.EffectPerRank) ||
                    !FiniteFloat(skill.MaxRank * skill.EffectPerRank * skill.DisplayMultiplier) ||
                    id == "agility" && !FiniteFloat(p.BaseMoveSpeed * (1d + skill.MaxRank * skill.EffectPerRank)) ||
                    (long)skill.CostBase + (long)skill.MaxRank * skill.CostPerRank > int.MaxValue)
                {
                    error = "成长技能配置无效：" + id;
                    return false;
                }
            }
            foreach (CfgItemDefinitionsRow item in GameTables.CfgItemDefinitions.GetAll())
                if (string.IsNullOrWhiteSpace(item.ID) || string.IsNullOrWhiteSpace(item.Name) ||
                    item.Type != (int)ProjectItemType.Weapon && item.Type != (int)ProjectItemType.Armor &&
                    item.Type != (int)ProjectItemType.Item && item.Type != (int)ProjectItemType.Collection ||
                    (item.Type == (int)ProjectItemType.Item || item.Type == (int)ProjectItemType.Collection) && item.StarterEquipped ||
                    item.StarterEquipped && item.ID != p.StarterDefinitionId ||
                    !FiniteFloat(item.AttackBase) || !FiniteFloat(item.AttackPerLevel) ||
                    !FiniteFloat(item.HealthBase) || !FiniteFloat(item.HealthPerLevel) ||
                    !FiniteFloat(item.DefenseBase) || !FiniteFloat(item.DefensePerLevel) ||
                    !FiniteFloat(item.RangeBase) || !FiniteFloat(item.RangeRandom) ||
                    item.SellBase < 0 || item.SellPerLevel < 0 ||
                    item.SellRandomExclusive < 0 || item.SellPerQuality < 0 || item.AttackBase < 0 ||
                    item.AttackPerLevel < 0 || item.AttackRandomExclusive < 0 || item.HealthBase < 0 ||
                    item.HealthPerLevel < 0 || item.HealthRandomExclusive < 0 || item.DefenseBase < 0 ||
                    item.DefensePerLevel < 0 || item.DefenseRandomMin < 0 ||
                    item.DefenseRandomExclusive < item.DefenseRandomMin || item.RangeBase < 0 || item.RangeRandom < 0 ||
                    (long)item.SellBase + (long)p.MaxLevel * item.SellPerLevel +
                    item.SellRandomExclusive + (long)5 * item.SellPerQuality > int.MaxValue ||
                    item.AttackBase + p.MaxLevel * item.AttackPerLevel + item.AttackRandomExclusive > p.MaxAttributeValue ||
                    item.HealthBase + p.MaxLevel * item.HealthPerLevel + item.HealthRandomExclusive > p.MaxAttributeValue ||
                    item.DefenseBase + p.MaxLevel * item.DefensePerLevel + item.DefenseRandomExclusive > p.MaxAttributeValue ||
                    item.RangeBase + item.RangeRandom > p.MaxAttackRange)
                {
                    error = "物品模板无效：" + item.ID;
                    return false;
                }
            if (GameTables.CfgQualities.GetAll().Count != Enum.GetValues(typeof(ProjectItemQuality)).Length)
            {
                error = "物品品质不完整。";
                return false;
            }
            long qualityWeight = 0;
            foreach (CfgQualitiesRow q in GameTables.CfgQualities.GetAll())
            {
                if (!Enum.IsDefined(typeof(ProjectItemQuality), q.ID) || q.Weight < 0 || q.AffixCount < 0 ||
                    !FiniteFloat(q.ScaleMin) || !FiniteFloat(q.ScaleMax) ||
                    !FiniteFloat(q.ColorR) || !FiniteFloat(q.ColorG) || !FiniteFloat(q.ColorB) ||
                    q.ScaleMin < 0 || q.ScaleMax < q.ScaleMin || q.ColorR < 0 || q.ColorR > 1 ||
                    q.ColorG < 0 || q.ColorG > 1 || q.ColorB < 0 || q.ColorB > 1 ||
                    string.IsNullOrWhiteSpace(q.Name))
                {
                    error = "品质配置无效。";
                    return false;
                }
                qualityWeight += q.Weight;
            }
            if (qualityWeight <= 0 || qualityWeight > int.MaxValue || GameTables.CfgAffixes.GetAll().Count == 0)
            {
                error = "品质或词条池为空。";
                return false;
            }
            var attributes = new HashSet<int>();
            foreach (CfgAffixesRow affix in GameTables.CfgAffixes.GetAll())
                if (string.IsNullOrWhiteSpace(affix.ID) || !Enum.IsDefined(typeof(ProjectAttributeType), affix.Attribute) ||
                    !FiniteFloat(affix.Scale) || affix.Scale <= 0 || !attributes.Add(affix.Attribute))
                {
                    error = "词条配置无效。";
                    return false;
                }
            foreach (CfgQualitiesRow q in GameTables.CfgQualities.GetAll())
                if (q.AffixCount > attributes.Count)
                {
                    error = "品质词条数量超过词条池。";
                    return false;
                }
            double maxAffixGrowth = 1d + (p.MaxLevel - 1d) * p.AffixLevelScale;
            foreach (CfgQualitiesRow quality in GameTables.CfgQualities.GetAll())
            {
                if (quality.AffixCount == 0) continue;
                foreach (CfgAffixesRow affix in GameTables.CfgAffixes.GetAll())
                {
                    double maximum = Math.Round(affix.Scale * quality.ScaleMax * maxAffixGrowth, 3);
                    if (!FiniteFloat(maximum) || maximum > p.MaxAttributeValue)
                    {
                        error = "词条生成值超出存档范围：" + affix.ID + "，品质 " + quality.ID + "。";
                        return false;
                    }
                }
            }
            long defaultLootWeight = 0;
            var poolWeights = new Dictionary<string, long>();
            foreach (CfgLootPoolRow loot in GameTables.CfgLootPool.GetAll())
            {
                CfgItemDefinitionsRow definition = Item(loot.DefinitionId);
                if (string.IsNullOrWhiteSpace(loot.PoolID) || definition == null || loot.Weight <= 0 ||
                    loot.MinLevel < 1 || loot.MaxLevel < loot.MinLevel)
                {
                    error = "掉落池引用或权重无效。";
                    return false;
                }
                poolWeights.TryGetValue(loot.PoolID, out long poolWeight);
                poolWeights[loot.PoolID] = poolWeight + loot.Weight;
                if (poolWeights[loot.PoolID] > int.MaxValue)
                {
                    error = "掉落池总权重超过可表示范围：" + loot.PoolID;
                    return false;
                }
                if (loot.PoolID == p.DefaultLootPoolID) defaultLootWeight += loot.Weight;
            }
            if (defaultLootWeight <= 0)
            {
                error = "默认掉落池为空。";
                return false;
            }
            var poolsWithQualityDraw = new HashSet<string>(StringComparer.Ordinal);
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (Item(row.DefinitionId).Type != (int)ProjectItemType.Collection) poolsWithQualityDraw.Add(row.PoolID);
            var qualityByPool = new Dictionary<string, HashSet<int>>(StringComparer.Ordinal);
            var qualityWeightsByPool = new Dictionary<string, long>(StringComparer.Ordinal);
            foreach (CfgLootQualitiesRow row in GameTables.CfgLootQualities.GetAll())
            {
                if (row == null || string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.PoolID) ||
                    !poolWeights.ContainsKey(row.PoolID) || !Enum.IsDefined(typeof(ProjectItemQuality), row.Quality) ||
                    row.Weight < 0 || Quality((ProjectItemQuality)row.Quality) == null)
                {
                    error = "掉落池品质引用或权重无效。";
                    return false;
                }
                if (!qualityByPool.TryGetValue(row.PoolID, out HashSet<int> qualities))
                {
                    qualities = new HashSet<int>();
                    qualityByPool.Add(row.PoolID, qualities);
                }
                if (!qualities.Add(row.Quality))
                {
                    error = "掉落池品质重复：" + row.PoolID + "/" + row.Quality;
                    return false;
                }
                qualityWeightsByPool.TryGetValue(row.PoolID, out long weight);
                qualityWeightsByPool[row.PoolID] = weight + row.Weight;
            }
            foreach (string poolId in poolsWithQualityDraw)
                if (!qualityByPool.TryGetValue(poolId, out HashSet<int> qualities) ||
                    qualities.Count != Enum.GetValues(typeof(ProjectItemQuality)).Length ||
                    qualityWeightsByPool[poolId] <= 0 || qualityWeightsByPool[poolId] > int.MaxValue)
                {
                    error = "掉落池品质缺失或总权重无效：" + poolId;
                    return false;
                }
            for (int level = 1; level <= p.MaxLevel; level++)
                if (!ProjectEquipmentRules.HasLoot(p.DefaultLootPoolID, level))
                {
                    error = "默认掉落池缺少等级 " + level + " 的物品。";
                    return false;
                }
            CfgRoguePolicyRow policy = RoguePolicy;
            if (policy == null || GameTables.CfgRoguePolicy.GetAll().Count != 1 || policy.MaxRefreshes < 0 ||
                policy.SelectedUpgradeWeightMultiplier < 1 || policy.SelectedUpgradeWeightMultiplier > 16 ||
                policy.UnlockedComboWeightMultiplier < 1 || policy.UnlockedComboWeightMultiplier > 16)
            {
                error = "Rogue 策略无效。";
                return false;
            }
            int rogueCount = 0;
            long rogueWeight = 0;
            foreach (CfgRogueRewardsRow reward in GameTables.CfgRogueRewards.GetAll())
            {
                if (string.IsNullOrWhiteSpace(reward.ID) || string.IsNullOrWhiteSpace(reward.Title) || reward.Weight <= 0 ||
                    !Enum.IsDefined(typeof(GameLogic.Gameplay.Roguelike.RogueRewardKind), reward.Kind) ||
                    !FiniteFloat(reward.Amount) ||
                    reward.Kind == 0 && (!Enum.IsDefined(typeof(ProjectAttributeType), reward.Attribute) ||
                        reward.Amount <= 0 || reward.Amount > p.MaxAttributeValue))
                {
                    error = "Rogue 奖励无效。";
                    return false;
                }
                rogueWeight += reward.Weight;
                rogueCount++;
            }
            if (rogueWeight * Math.Max(policy.SelectedUpgradeWeightMultiplier, policy.UnlockedComboWeightMultiplier) > int.MaxValue)
                error = "Rogue 奖励有效总权重超过可表示范围。";
            if (rogueCount == 0) error = "Rogue 技能奖励池为空。";
            return error == null && ProjectAugmentConfig.Validate(out error) && ProjectV2RewardConfig.Validate(out error);
        }

        /// <summary>
        /// 判断配置数值能安全转成 Unity 使用的有限单精度值。
        /// </summary>
        /// <param name="value">配置数值。</param>
        /// <returns>是否处于有限单精度范围。</returns>
        private static bool FiniteFloat(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value) && value >= -float.MaxValue && value <= float.MaxValue;
    }
}
