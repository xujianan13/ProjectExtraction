using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Character
{
    /// <summary>
    /// 局外展示装备和技能共同计算后的角色战斗属性。
    /// </summary>
    public sealed class CharacterStatsWindow : GameWindow
    {
        private ProjectSession session;
        private RectTransform list;
        private UnityEngine.UI.Text feedback;

        /// <summary>
        /// 创建可滚动的角色属性面板。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "CharacterStatsContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 14f);
            ProjectUiFactory.Stretch(layout, 24f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 72f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "角色属性", 32, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "Attributes", out list);
            UnityEngine.UI.LayoutElement size = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.flexibleHeight = 1f;
            size.minHeight = 100f;
            feedback = ProjectUiFactory.Label(layout, string.Empty, 21, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 0.96f));
            ProjectUiFactory.Height(feedback.rectTransform, 50f);
        }

        /// <summary>
        /// 订阅已保存局外状态变化。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            if (session == null)
            {
                feedback.text = "角色数据尚未就绪";
                return;
            }
            session.Changed += Refresh;
            OpenScope.Defer(() => session.Changed -= Refresh);
            Refresh();
        }

        /// <summary>
        /// 显示八项战斗属性及暴击值换算概率。
        /// </summary>
        private void Refresh()
        {
            if (session == null) return;
            ProjectCharacterStats stats = session.CharacterStats;
            ProjectUiFactory.Clear(list);
            Add(ProjectAttributeType.MaxHealth, stats.MaxHealth);
            Add(ProjectAttributeType.Attack, stats.Attack);
            Add(ProjectAttributeType.Defense, stats.Defense);
            Add(ProjectAttributeType.CriticalRating, stats.CriticalRating);
            Add(ProjectAttributeType.CriticalDamage, stats.CriticalDamage);
            Add(ProjectAttributeType.DodgeChance, stats.DodgeChance);
            Add(ProjectAttributeType.DamageIncrease, stats.DamageIncrease);
            Add(ProjectAttributeType.DamageReduction, stats.DamageReduction);
            foreach (ProjectCollectionBonusSource source in session.CollectionBonusSources)
            {
                ProjectUiFactory.Label(list, "收藏成就 · " + source.Name, 25, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.4f));
                foreach (ProjectAttributeValue bonus in source.Attributes)
                    ProjectUiFactory.Label(list, ProjectEquipmentText.FinalAttribute(bonus.Attribute, bonus.Value), 22, TextAnchor.MiddleLeft, Color.white);
            }
            feedback.text = "总属性包含装备、局外技能与已完成收藏成就";
        }

        /// <summary>
        /// 添加一行最终战斗属性。
        /// </summary>
        /// <param name="attribute">属性类别。</param>
        /// <param name="value">最终属性值。</param>
        private void Add(ProjectAttributeType attribute, float value)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(list, ProjectEquipmentText.FinalAttribute(attribute, value), 25,
                TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(label.rectTransform, 68f);
        }
    }
}
