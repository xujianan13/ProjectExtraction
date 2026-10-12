using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 从配置表读取并校验新手指南，动态数值统一取玩家规则。
    /// </summary>
    public static class ProjectGuideConfig
    {
        private static readonly Regex Placeholder = new Regex(@"\{([^{}]+)\}", RegexOptions.Compiled);

        /// <summary>
        /// 按排序值读取指南页。
        /// </summary>
        /// <returns>排序后的指南页。</returns>
        public static IReadOnlyList<CfgGuideRow> All()
        {
            if (GameTables.CfgGuide == null) return Array.Empty<CfgGuideRow>();
            var rows = new List<CfgGuideRow>(GameTables.CfgGuide.GetAll());
            rows.Sort((left, right) =>
            {
                int order = left.SortOrder.CompareTo(right.SortOrder);
                return order != 0 ? order : string.CompareOrdinal(left.ID, right.ID);
            });
            return rows;
        }

        /// <summary>
        /// 校验指南页字段、排序值和动态参数。
        /// </summary>
        /// <param name="error">首个错误，成功时为空。</param>
        /// <returns>配置可以显示时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgGuide == null || ProjectRulesConfig.Profile == null)
            { error = "新手指南或玩家规则配置未加载。"; return false; }
            IReadOnlyList<CfgGuideRow> rows = All();
            if (rows.Count == 0)
            { error = "新手指南没有可显示内容。"; return false; }
            var orders = new HashSet<int>();
            foreach (CfgGuideRow row in rows)
            {
                if (string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.Title) ||
                    string.IsNullOrWhiteSpace(row.Body) || row.SortOrder < 0 || !orders.Add(row.SortOrder))
                { error = "新手指南字段或排序值无效：" + row.ID; return false; }
                if (!ValidatePlaceholders(row.Title, out error) || !ValidatePlaceholders(row.Body, out error))
                { error = "新手指南 " + row.ID + "：" + error; return false; }
            }
            return true;
        }

        /// <summary>
        /// 校验文本中只包含已支持的玩家规则占位符。
        /// </summary>
        /// <param name="template">待校验文本。</param>
        /// <param name="error">首个错误，成功时为空。</param>
        /// <returns>文本可安全替换时为真。</returns>
        public static bool ValidatePlaceholders(string template, out string error)
        {
            error = null;
            if (template == null)
            { error = "文本为空。"; return false; }
            string issue = null;
            string remaining = Placeholder.Replace(template, match =>
            {
                if (!Known(match.Groups[1].Value) && issue == null) issue = "未知动态参数 " + match.Value;
                return string.Empty;
            });
            if (issue != null) { error = issue; return false; }
            if (remaining.IndexOf('{') >= 0 || remaining.IndexOf('}') >= 0)
            { error = "动态参数括号不匹配。"; return false; }
            return true;
        }

        /// <summary>
        /// 将指南模板中的白名单占位符替换为当前玩家配置。
        /// </summary>
        /// <param name="template">指南模板。</param>
        /// <param name="text">可显示的指南文本，失败时为空。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>成功替换时为真。</returns>
        public static bool TryFormat(string template, out string text, out string error)
        {
            text = null;
            CfgProfileRow profile = ProjectRulesConfig.Profile;
            if (profile == null)
            { error = "玩家规则配置未加载。"; return false; }
            if (!ValidatePlaceholders(template, out error)) return false;
            text = Placeholder.Replace(template, match => Value(profile, match.Groups[1].Value).ToString(CultureInfo.InvariantCulture));
            return true;
        }
        /// <summary>
        /// 检查占位符是否来自允许展示的规则字段。
        /// </summary>
        /// <param name="name">字段名。</param>
        /// <returns>可替换时为真。</returns>
        private static bool Known(string name) => name == "EntryCost" || name == "StaminaCap" ||
            name == "RecoverySeconds" || name == "DailyResetHour" || name == "SupplyHourFirst" ||
            name == "SupplyHourSecond" || name == "SupplyHourThird" || name == "SupplyAmount";

        /// <summary>
        /// 读取白名单字段的当前值。
        /// </summary>
        /// <param name="profile">玩家规则。</param>
        /// <param name="name">字段名。</param>
        /// <returns>字段值。</returns>
        private static int Value(CfgProfileRow profile, string name)
        {
            switch (name)
            {
                case "EntryCost": return profile.EntryCost;
                case "StaminaCap": return profile.StaminaCap;
                case "RecoverySeconds": return profile.RecoverySeconds;
                case "DailyResetHour": return profile.DailyResetHour;
                case "SupplyHourFirst": return profile.SupplyHourFirst;
                case "SupplyHourSecond": return profile.SupplyHourSecond;
                case "SupplyHourThird": return profile.SupplyHourThird;
                case "SupplyAmount": return profile.SupplyAmount;
                default: return 0;
            }
        }
    }
}
