using System;
using System.Collections.Generic;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 生成时冻结的高级词条，定义和效果版本共同确定运行时语义。
    /// </summary>
    [Serializable]
    public sealed class ProjectAdvancedAffix
    {
        public string DefinitionId;
        public string AugmentId;
        public int Version;
        public float Value;

        /// <summary>
        /// 创建不共享可写字段的高级词条副本。
        /// </summary>
        /// <returns>独立的高级词条。</returns>
        public ProjectAdvancedAffix Copy() => (ProjectAdvancedAffix)MemberwiseClone();
    }

    /// <summary>
    /// 高级词条的冻结抽取与存档完整性校验。
    /// </summary>
    internal static class ProjectAdvancedAffixRules
    {
        /// <summary>
        /// 根据装备类型和品质无放回地按权重生成词条。
        /// </summary>
        /// <param name="random">装备使用的随机源。</param>
        /// <param name="type">武器或护甲类型。</param>
        /// <param name="quality">装备品质。</param>
        /// <param name="count">品质要求的词条数。</param>
        /// <param name="result">成功时的冻结词条。</param>
        /// <returns>存在足够且有效的配置时为真。</returns>
        internal static bool TryRoll(Random random, ProjectItemType type, ProjectItemQuality quality, int count,
            out List<ProjectAdvancedAffix> result)
        {
            result = new List<ProjectAdvancedAffix>();
            if (random == null || count < 0 || count > 32 || GameTables.CfgAdvancedAffixes == null || GameTables.CfgCombatAugments == null) return false;
            if (count == 0) return true;
            var eligible = new List<CfgAdvancedAffixesRow>();
            foreach (CfgAdvancedAffixesRow row in GameTables.CfgAdvancedAffixes.GetAll())
            {
                CfgCombatAugmentsRow augment = GameTables.CfgCombatAugments.GetById(row.AugmentID);
                if (row.ItemType == (int)type && row.MinQuality <= (int)quality && row.Weight > 0 &&
                    augment != null && ValidRange(row, augment)) eligible.Add(row);
            }
            if (eligible.Count < count) return false;
            for (int index = 0; index < count; index++)
            {
                long total = 0;
                foreach (CfgAdvancedAffixesRow row in eligible) total += row.Weight;
                if (total < 1 || total > int.MaxValue) return false;
                int roll = random.Next((int)total);
                int chosen = 0;
                for (; chosen < eligible.Count - 1; chosen++)
                {
                    if (roll < eligible[chosen].Weight) break;
                    roll -= eligible[chosen].Weight;
                }
                CfgAdvancedAffixesRow selected = eligible[chosen];
                eligible.RemoveAt(chosen);
                CfgCombatAugmentsRow augment = GameTables.CfgCombatAugments.GetById(selected.AugmentID);
                result.Add(new ProjectAdvancedAffix
                {
                    DefinitionId = selected.ID, AugmentId = selected.AugmentID, Version = augment.Version,
                    Value = (float)(selected.ValueMin + random.NextDouble() * (selected.ValueMax - selected.ValueMin))
                });
            }
            return true;
        }

        /// <summary>
        /// 校验新版装备的高级词条数量、来源、版本和冻结数值。
        /// </summary>
        /// <param name="item">待检查的装备。</param>
        /// <returns>通过时为空，否则为明确错误。</returns>
        internal static string Validate(ProjectItem item)
        {
            if (item.AdvancedAffixes == null || item.AdvancedAffixes.Count > 32 ||
                item.Type == ProjectItemType.Item && item.AdvancedAffixes.Count != 0) return "高级词条容量或物品类型无效。";
            if (GameTables.CfgAdvancedAffixes == null || GameTables.CfgCombatAugments == null) return "高级词条配置未加载。";
            var ids = new HashSet<string>();
            foreach (ProjectAdvancedAffix affix in item.AdvancedAffixes)
            {
                if (affix == null || string.IsNullOrWhiteSpace(affix.DefinitionId) || !ids.Add(affix.DefinitionId))
                    return "高级词条身份为空或重复。";
                CfgAdvancedAffixesRow definition = GameTables.CfgAdvancedAffixes.GetById(affix.DefinitionId);
                CfgCombatAugmentsRow augment = definition == null ? null : GameTables.CfgCombatAugments.GetById(definition.AugmentID);
                if (definition == null || augment == null || definition.ItemType != (int)item.Type ||
                    affix.AugmentId != definition.AugmentID || affix.Version != augment.Version ||
                    !ProjectAugmentConfig.ValidValue(augment, affix.Value))
                    return "高级词条定义、效果版本或数值无效。";
            }
            return null;
        }

        /// <summary>
        /// 检查高级词条的配置数值可以冻结为有限浮点数。
        /// </summary>
        /// <param name="row">高级词条定义。</param>
        /// <param name="augment">效果配置和数值模式。</param>
        /// <returns>数值区间有限且符合效果模式时为真。</returns>
        private static bool ValidRange(CfgAdvancedAffixesRow row, CfgCombatAugmentsRow augment) =>
            row.ValueMax >= row.ValueMin && row.ValueMax <= float.MaxValue &&
            ProjectAugmentConfig.ValidValue(augment, row.ValueMin) && ProjectAugmentConfig.ValidValue(augment, row.ValueMax);
    }
}
