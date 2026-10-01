using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Skills
{
    /// <summary>
    /// 局外技能提升窗口，使用已保存技能点执行异步升级事务。
    /// </summary>
    public sealed class SkillsWindow : GameWindow
    {
        private RectTransform list;
        private UnityEngine.UI.Text points;
        private UnityEngine.UI.Text details;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button upgradeButton;
        private ProjectSession session;
        private string selectedSkillId;
        private bool operating;

        /// <summary>
        /// 创建技能列表与升级操作界面。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "SkillsContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 72f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "技能", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            points = ProjectUiFactory.Label(layout, string.Empty, 27, TextAnchor.MiddleLeft, new Color(0.9f, 0.81f, 0.52f));
            ProjectUiFactory.Height(points.rectTransform, 58f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "SkillList", out list);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 100f;
            UnityEngine.UI.ScrollRect detailsScroll = ProjectUiFactory.Scroll(layout, "SkillDetails", out RectTransform detailsContent);
            ProjectUiFactory.Height((RectTransform)detailsScroll.transform, 150f);
            details = ProjectUiFactory.Label(detailsContent, "选择技能查看效果", 22, TextAnchor.UpperLeft, Color.white);
            upgradeButton = ProjectUiFactory.ActionButton(layout, "提升技能", () => UpgradeAsync().Forget());
            ProjectUiFactory.Height(upgradeButton.GetComponent<RectTransform>(), 90f);
            feedback = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(feedback.rectTransform, 50f);
        }

        /// <summary>
        /// 订阅当前项目会话的变化。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            if (session == null)
            {
                feedback.text = "项目数据尚未就绪";
                upgradeButton.interactable = false;
                return;
            }
            session.Changed += Refresh;
            OpenScope.Defer(() => session.Changed -= Refresh);
        }

        /// <summary>
        /// 显示技能权威状态。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 重建技能列表和选中详情。
        /// </summary>
        private void Refresh()
        {
            if (session == null) return;
            points.text = $"可用技能点  {session.SkillPoints}";
            ProjectUiFactory.Clear(list);
            bool selectedExists = false;
            foreach (ProjectSkill skill in session.Skills)
                if (skill.Id == selectedSkillId) selectedExists = true;
            if (!selectedExists) selectedSkillId = session.Skills.Count > 0 ? session.Skills[0].Id : null;
            foreach (ProjectSkill skill in session.Skills)
            {
                string id = skill.Id;
                string caption = $"{skill.Name}  {skill.Rank}/{skill.MaxRank}";
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(list, caption, () => SelectSkill(id));
                ProjectUiFactory.Height(button.GetComponent<RectTransform>(), 70f);
            }
            if (session.Skills.Count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(list, "暂无可提升技能", 23, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Height(empty.rectTransform, 90f);
            }
            ProjectSkill selected = FindSelected();
            details.text = selected == null ? "选择技能查看效果" :
                $"{selected.Name}  当前等级 {selected.Rank}/{selected.MaxRank}\n当前效果：{selected.CurrentEffect}\n" +
                (selected.Rank >= selected.MaxRank ? "已达到最高等级" :
                    $"下一效果：{selected.NextEffect}  ·  需要 {selected.NextCost} 技能点");
            upgradeButton.interactable = selected != null && selected.Rank < selected.MaxRank &&
                session.SkillPoints >= selected.NextCost && !session.IsBusy && !operating;
        }

        /// <summary>
        /// 切换当前技能。
        /// </summary>
        /// <param name="id">技能标识。</param>
        private void SelectSkill(string id)
        {
            selectedSkillId = id;
            Refresh();
        }

        /// <summary>
        /// 读取选中的技能。
        /// </summary>
        /// <returns>技能或空。</returns>
        private ProjectSkill FindSelected()
        {
            foreach (ProjectSkill skill in session.Skills)
                if (skill.Id == selectedSkillId) return skill;
            return null;
        }

        /// <summary>
        /// 提交技能提升事务并显示确认结果。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid UpgradeAsync()
        {
            if (session == null || session.IsBusy || operating || !upgradeButton.interactable) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.UpgradeSkillAsync(selectedSkillId);
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            feedback.text = result.Message;
            Refresh();
        }
    }
}
