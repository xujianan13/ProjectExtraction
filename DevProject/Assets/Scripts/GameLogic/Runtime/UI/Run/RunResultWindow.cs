using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Run
{
    /// <summary>
    /// 跨场景展示已保存的单局结算结果。
    /// </summary>
    public sealed class RunResultWindow : GameWindow
    {
        private ProjectRunCoordinator coordinator;
        private ProjectSession session;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text summary;
        private RectTransform items;
        private UnityEngine.UI.Button returnButton;
        private bool returning;

        /// <summary>
        /// 创建完整背景及安全区内的结算明细。
        /// </summary>
        protected override void BindComponents()
        {
            ProjectUiFactory.Stretch(RootTransform, 0f);
            RectTransform backdrop = ProjectUiFactory.Panel(RootTransform, "ResultBackdrop", new Color(0.025f, 0.055f, 0.09f, 1f));
            ProjectUiFactory.Stretch(backdrop, 0f);
            RectTransform safeArea = ProjectUiFactory.Fill(backdrop, "SafeArea", Color.clear);
            RectTransform header = ProjectUiFactory.Vertical(safeArea, "ResultHeader", 12f);
            Place(header, new Vector2(0.06f, 0.6f), new Vector2(0.94f, 0.96f));
            title = ProjectUiFactory.Label(header, string.Empty, 42, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 72f);
            summary = ProjectUiFactory.Label(header, string.Empty, 23, TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 0.96f));
            ProjectUiFactory.Height(summary.rectTransform, 150f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(safeArea, "ResultItems", out items);
            Place((RectTransform)scroll.transform, new Vector2(0.06f, 0.19f), new Vector2(0.94f, 0.59f));
            returnButton = ProjectUiFactory.ActionButton(safeArea, "返回主界面", () => ReturnHomeAsync().Forget());
            Place((RectTransform)returnButton.transform, new Vector2(0.12f, 0.055f), new Vector2(0.88f, 0.14f));
        }

        /// <summary>
        /// 订阅流程与已保存数据变化。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            coordinator = ProjectServices.Coordinator;
            session = ProjectServices.Session;
            if (coordinator != null)
            {
                coordinator.Changed += Refresh;
                OpenScope.Defer(() => coordinator.Changed -= Refresh);
            }
            if (session != null)
            {
                session.Changed += Refresh;
                OpenScope.Defer(() => session.Changed -= Refresh);
            }
            Refresh();
        }

        /// <summary>
        /// 以已提交记录更新结算内容。
        /// </summary>
        private void Refresh()
        {
            ProjectRunResult result = session?.LastResult;
            ProjectUiFactory.Clear(items);
            if (result?.Outcome == null)
            {
                title.text = "结算结果暂不可用";
                summary.text = coordinator?.Message ?? "正在读取已保存的结算结果。";
                returnButton.interactable = false;
                return;
            }
            ProjectRunOutcome outcome = result.Outcome;
            title.text = outcome.Extracted ? "撤离成功" : "角色死亡";
            summary.text = $"关卡 {outcome.LevelId}  ·  游玩 {Mathf.CeilToInt(outcome.ElapsedSeconds)} 秒\n" +
                $"等级 {result.LevelBefore} → {result.LevelAfter}  ·  经验 {result.ExperienceBefore} → {result.ExperienceAfter}\n" +
                $"打怪经验 {outcome.AwardedKillExperience}  ·  撤离经验 {outcome.ExtractionExperience}\n" +
                $"技能点 {result.SkillPointsBefore} → {result.SkillPointsAfter}  ·  金币 {result.GoldBefore} → {result.GoldAfter}";
            AddSection("带回物品", result.RetainedItems);
            AddSection("丢失物品", result.LostItems);
            returnButton.interactable = coordinator != null && coordinator.CanReturn && !returning;
        }

        /// <summary>
        /// 展示物品实例及生成时确定的属性。
        /// </summary>
        /// <param name="heading">分类标题。</param>
        /// <param name="source">物品实例列表。</param>
        private void AddSection(string heading, System.Collections.Generic.IReadOnlyList<ProjectItem> source)
        {
            UnityEngine.UI.Text caption = ProjectUiFactory.Label(items, heading + " (" + (source?.Count ?? 0) + ")", 29, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(caption.rectTransform, 55f);
            if (source == null || source.Count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(items, "无", 22, TextAnchor.MiddleLeft, new Color(0.72f, 0.82f, 0.88f));
                ProjectUiFactory.Height(empty.rectTransform, 42f);
                return;
            }
            foreach (ProjectItem item in source)
            {
                if (item == null) continue;
                string attributes = item.Type == ProjectItemType.Weapon ? $"伤害 {item.Damage:0.#} · 范围 {item.Range:0.#}" :
                    $"生命加成 {item.BonusHealth}";
                UnityEngine.UI.Text label = ProjectUiFactory.Label(items,
                    $"{item.Name}  ·  等级 {item.RequiredLevel}  ·  {attributes}", 22, TextAnchor.MiddleLeft, Color.white);
                ProjectUiFactory.Height(label.rectTransform, 60f);
            }
        }

        /// <summary>
        /// 请求流程返回并等待 Home 确认就绪。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid ReturnHomeAsync()
        {
            if (coordinator == null || returning || !coordinator.CanReturn) return;
            returning = true;
            Refresh();
            await coordinator.ReturnHomeAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            returning = false;
            Refresh();
        }

        /// <summary>
        /// 按屏幕比例定位结果内容。
        /// </summary>
        /// <param name="rect">待布局节点。</param>
        /// <param name="minimum">左下锚点。</param>
        /// <param name="maximum">右上锚点。</param>
        private static void Place(RectTransform rect, Vector2 minimum, Vector2 maximum)
        {
            rect.anchorMin = minimum;
            rect.anchorMax = maximum;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}