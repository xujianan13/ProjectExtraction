using System.Collections.Generic;
using System.Text;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.UI.Common
{
    /// <summary>
    /// 将装备品质、基础属性与额外词条转换为玩家可读文本。
    /// </summary>
    public static class ProjectEquipmentText
    {
        /// <summary>
        /// 返回物品类别的中文名称。
        /// </summary>
        /// <param name="type">物品类别。</param>
        /// <returns>类别名称。</returns>
        public static string TypeName(ProjectItemType type) => type == ProjectItemType.Weapon ? "武器" :
            ProjectEquipmentRules.IsArmor(type) ? "护甲" : "物品";

        /// <summary>
        /// 返回包含品质的物品标题。
        /// </summary>
        /// <param name="item">物品实例。</param>
        /// <returns>供列表和详情显示的标题。</returns>
        public static string Title(ProjectItem item) => item == null ? string.Empty :
            $"[{ProjectEquipmentRules.QualityName(item.Quality)}] {item.Name} · {TypeName(item.Type)}";

        /// <summary>
        /// 返回品质对应的文字颜色。
        /// </summary>
        /// <param name="item">物品实例。</param>
        /// <returns>可用于标题的颜色。</returns>
        public static Color QualityColor(ProjectItem item) => item == null ? Color.white : ProjectEquipmentRules.QualityColor(item.Quality);

        /// <summary>
        /// 返回只为品质标题上色的富文本。
        /// </summary>
        /// <param name="item">物品实例。</param>
        /// <returns>带品质颜色的标题。</returns>
        public static string ColoredTitle(ProjectItem item)
        {
            if (item == null) return string.Empty;
            string color = ColorUtility.ToHtmlStringRGB(QualityColor(item));
            return $"<color=#{color}>{Title(item)}</color>";
        }

        /// <summary>
        /// 展示选中装备同部位的当前穿戴详情，便于直接比较基础属性和高级词条。
        /// </summary>
        /// <param name="selected">正在查看的物品快照。</param>
        /// <param name="items">当前仓库或局内背包的权威物品快照。</param>
        /// <returns>当前装备详情、已穿戴提示或空槽说明；普通物品返回空字符串。</returns>
        public static string EquippedDetails(ProjectItem selected, IReadOnlyList<ProjectItem> items)
        {
            if (selected.Type == ProjectItemType.Item) return string.Empty;
            string heading = "当前穿戴（" + TypeName(selected.Type) + "）\n";
            foreach (ProjectItem equipped in items)
            {
                bool sameSlot = equipped.Type == selected.Type ||
                    ProjectEquipmentRules.IsArmor(equipped.Type) && ProjectEquipmentRules.IsArmor(selected.Type);
                if (!equipped.Equipped || !sameSlot) continue;
                if (equipped.Id == selected.Id)
                    return heading + ColoredTitle(equipped) + "\n选中装备已穿戴，属性预览展示卸下后的变化。";
                return heading + ColoredTitle(equipped) + "\n" + Details(equipped);
            }
            return heading + "该部位未穿戴装备。";
        }

        /// <summary>
        /// 生成战斗 HUD 使用的简短战利品摘要。
        /// </summary>
        /// <param name="item">已经复制的待拾取物品。</param>
        /// <returns>品质、等级、价格和主要属性摘要。</returns>
        public static string LootSummary(ProjectItem item)
        {
            if (item == null) return string.Empty;
            var builder = new StringBuilder(Title(item));
            builder.Append("\n等级 ").Append(item.RequiredLevel).Append(" · 售价 ").Append(item.SellGold).Append(" 金币");
            int shown = 0;
            foreach (ProjectAttributeValue attribute in ProjectEquipmentRules.GetItemAttributes(item))
            {
                if (attribute == null) continue;
                builder.Append(shown == 0 ? "\n" : " · ").Append(ItemAttribute(attribute.Attribute, attribute.Value));
                if (++shown == 2) break;
            }
            builder.Append("\n词条 ").Append(item.Affixes?.Count ?? 0).Append(" · 高级 ").Append(item.AdvancedAffixes?.Count ?? 0);
            return builder.ToString();
        }

        /// <summary>
        /// 格式化角色最终属性。
        /// </summary>
        /// <param name="attribute">属性类别。</param>
        /// <param name="value">最终属性值。</param>
        /// <returns>中文属性说明。</returns>
        public static string FinalAttribute(ProjectAttributeType attribute, float value)
        {
            if (attribute == ProjectAttributeType.CriticalRating)
            {
                float chance = value <= 0f ? 0f : value / (value + 100f);
                return $"暴击值 {value:0.#} · 暴击率 {chance * 100f:0.#}%";
            }
            if (attribute == ProjectAttributeType.CriticalDamage) return $"暴击伤害 {value * 100f:0.#}%";
            return AttributeName(attribute) + " " + FormatValue(attribute, value, false);
        }

        /// <summary>
        /// 格式化装备上的一条属性增量。
        /// </summary>
        /// <param name="attribute">属性类别。</param>
        /// <param name="value">属性增量。</param>
        /// <returns>中文属性说明。</returns>
        public static string ItemAttribute(ProjectAttributeType attribute, float value)
        {
            return AttributeName(attribute) + " " + FormatValue(attribute, value, true);
        }

        /// <summary>
        /// 生成不包含内部实例标识的物品详情。
        /// </summary>
        /// <param name="item">物品实例。</param>
        /// <returns>基础属性与额外词条分区的文本。</returns>
        public static string Details(ProjectItem item)
        {
            if (item == null) return "选择物品查看详情";
            var builder = new StringBuilder();
            builder.Append("需求等级 ").Append(item.RequiredLevel);
            if (item.Type == ProjectItemType.Weapon) builder.Append(" · 攻击范围 ").Append(item.Range.ToString("0.#"));
            builder.AppendLine().AppendLine("基础属性");
            int baseCount = 0;
            if (item.EquipmentVersion > 0 && item.BaseAttributes != null)
            {
                foreach (ProjectAttributeValue attribute in item.BaseAttributes)
                {
                    if (attribute == null) continue;
                    builder.AppendLine(ItemAttribute(attribute.Attribute, attribute.Value));
                    baseCount++;
                }
            }
            else
            {
                foreach (ProjectAttributeValue attribute in ProjectEquipmentRules.GetItemAttributes(item))
                {
                    if (attribute == null) continue;
                    builder.AppendLine(ItemAttribute(attribute.Attribute, attribute.Value));
                    baseCount++;
                }
            }
            if (baseCount == 0) builder.AppendLine("无");
            builder.AppendLine("额外词条");
            if (item.Affixes == null || item.Affixes.Count == 0) builder.Append("无");
            else foreach (ProjectEquipmentAffix affix in item.Affixes)
                if (affix != null) builder.AppendLine(ItemAttribute(affix.Attribute, affix.Value));
            builder.AppendLine().AppendLine("高级词条");
            if (item.AdvancedAffixes == null || item.AdvancedAffixes.Count == 0) builder.Append("无");
            else foreach (ProjectAdvancedAffix affix in item.AdvancedAffixes)
                if (affix != null) builder.AppendLine(AdvancedAffixText(affix));
            return builder.ToString().TrimEnd();
        }

        /// <summary>
        /// 使用已冻结的数值展示高级词条效果，不重新抽取词条。
        /// </summary>
        /// <param name="affix">物品携带的高级词条。</param>
        /// <returns>标题和实际百分比效果说明。</returns>
        private static string AdvancedAffixText(ProjectAdvancedAffix affix)
        {
            CfgCombatAugmentsRow augment = GameTables.CfgCombatAugments?.GetById(affix.AugmentId);
            if (augment == null || augment.Version != affix.Version) return "高级词条配置不可用";
            string percentage = (affix.Value * 100f).ToString("0.#") + "%";
            string description = augment.Description ?? string.Empty;
            return augment.Title + " · " + (description.Contains("{value}") ?
                description.Replace("{value}", percentage) : description + " " + percentage);
        }

        /// <summary>
        /// 按文本行数为滚动列表设置建议高度。
        /// </summary>
        /// <param name="value">待显示文本。</param>
        /// <param name="minimum">最小高度。</param>
        /// <returns>建议高度。</returns>
        public static float PreferredHeight(string value, float minimum = 72f)
        {
            if (string.IsNullOrEmpty(value)) return minimum;
            int lines = 1;
            foreach (char character in value) if (character == '\n') lines++;
            return Mathf.Max(minimum, lines * 34f + 18f);
        }

        /// <summary>
        /// 返回属性的中文名称。
        /// </summary>
        /// <param name="attribute">属性类别。</param>
        /// <returns>属性名称。</returns>
        private static string AttributeName(ProjectAttributeType attribute) => attribute switch
        {
            ProjectAttributeType.MaxHealth => "生命",
            ProjectAttributeType.Attack => "攻击",
            ProjectAttributeType.Defense => "防御",
            ProjectAttributeType.CriticalRating => "暴击值",
            ProjectAttributeType.CriticalDamage => "暴击伤害",
            ProjectAttributeType.DodgeChance => "闪避率",
            ProjectAttributeType.DamageIncrease => "伤害加成",
            ProjectAttributeType.DamageReduction => "伤害减免",
            _ => "属性"
        };

        /// <summary>
        /// 根据最终值或装备增量格式化数值。
        /// </summary>
        /// <param name="attribute">属性类别。</param>
        /// <param name="value">待显示数值。</param>
        /// <param name="bonus">是否显示为装备增量。</param>
        /// <returns>格式化数值。</returns>
        private static string FormatValue(ProjectAttributeType attribute, float value, bool bonus)
        {
            string prefix = bonus && value >= 0f ? "+" : string.Empty;
            if (attribute == ProjectAttributeType.CriticalDamage)
                return prefix + (value * 100f).ToString("0.#") + (bonus ? " 个百分点" : "%");
            if (attribute == ProjectAttributeType.DodgeChance || attribute == ProjectAttributeType.DamageIncrease ||
                attribute == ProjectAttributeType.DamageReduction)
                return prefix + (value * 100f).ToString("0.#") + "%";
            return prefix + value.ToString("0.#");
        }
    }
}
