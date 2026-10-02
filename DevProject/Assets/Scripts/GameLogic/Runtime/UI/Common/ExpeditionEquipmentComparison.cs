using System.Text;
using GameLogic.Project;

namespace GameLogic.UI.Common
{
    /// <summary>
    /// 将装备操作前后的角色属性格式化为对照文本。
    /// </summary>
    public static class ExpeditionEquipmentComparison
    {
        /// <summary>
        /// 逐项展示当前属性和装备操作后的预览值。
        /// </summary>
        /// <param name="current">当前角色属性。</param>
        /// <param name="preview">预计操作后的角色属性。</param>
        /// <returns>适合滚动详情区域的对照文本。</returns>
        public static string Format(ProjectCharacterStats current, ProjectCharacterStats preview)
        {
            if (current == null || preview == null) return "暂无装备属性预览";
            var text = new StringBuilder("角色属性对比\n");
            Add(text, "生命上限", current.MaxHealth, preview.MaxHealth);
            Add(text, "攻击", current.Attack, preview.Attack);
            Add(text, "防御", current.Defense, preview.Defense);
            Add(text, "暴击值", current.CriticalRating, preview.CriticalRating);
            Add(text, "暴击伤害", current.CriticalDamage * 100f, preview.CriticalDamage * 100f, "%");
            Add(text, "闪避率", current.DodgeChance * 100f, preview.DodgeChance * 100f, "%");
            Add(text, "伤害加成", current.DamageIncrease * 100f, preview.DamageIncrease * 100f, "%");
            Add(text, "伤害减免", current.DamageReduction * 100f, preview.DamageReduction * 100f, "%");
            Add(text, "攻击范围", current.AttackRange, preview.AttackRange);
            Add(text, "移动速度", current.MoveSpeed, preview.MoveSpeed);
            return text.ToString().TrimEnd();
        }

        /// <summary>
        /// 写入一行属性变化。
        /// </summary>
        /// <param name="text">输出文本。</param>
        /// <param name="name">属性名称。</param>
        /// <param name="current">当前值。</param>
        /// <param name="preview">预览值。</param>
        /// <param name="unit">可选单位。</param>
        private static void Add(StringBuilder text, string name, float current, float preview, string unit = "") =>
            text.Append(name).Append(' ').Append(current.ToString("0.#")).Append(unit).Append(" → ")
                .Append(preview.ToString("0.#")).Append(unit).AppendLine();
    }
}
