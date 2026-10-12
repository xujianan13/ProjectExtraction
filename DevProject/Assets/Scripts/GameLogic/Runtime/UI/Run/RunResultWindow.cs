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
            RectTransform layout = ProjectUiFactory.Vertical(safeArea, "ResultLayout", 12f);
            ProjectUiFactory.Stretch(layout, 24f);
            title = ProjectUiFactory.Label(layout, string.Empty, 42, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 72f);
            UnityEngine.UI.ScrollRect summaryScroll = ProjectUiFactory.Scroll(layout, "ResultSummary", out RectTransform summaryContent);
            ProjectUiFactory.Height((RectTransform)summaryScroll.transform, 160f);
            summary = ProjectUiFactory.Label(summaryContent, string.Empty, 23, TextAnchor.UpperLeft, new Color(0.8f, 0.9f, 0.96f));
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "ResultItems", out items);
            UnityEngine.UI.LayoutElement itemsSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            itemsSize.flexibleHeight = 1f;
            itemsSize.minHeight = 100f;
            returnButton = ProjectUiFactory.ActionButton(layout, "返回主界面", () => ReturnHomeAsync().Forget());
            ProjectUiFactory.Height((RectTransform)returnButton.transform, 90f);
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
            title.text = outcome.Extracted ? outcome.Statistics?.IsFinalCompletion == true ? "最终通关" :
                outcome.Statistics?.HasRegionData == true ? "提前带回成功" : "撤离成功（通关类型未记录）" :
                outcome.Abandoned ? "已放弃本局" : "角色死亡";
            summary.text = $"关卡 {outcome.LevelId}  ·  有效游玩 {Mathf.CeilToInt(outcome.ElapsedSeconds)} 秒\n" +
                $"等级 {result.LevelBefore} → {result.LevelAfter}  ·  经验 {result.ExperienceBefore} → {result.ExperienceAfter}\n" +
                $"打怪经验 {outcome.AwardedKillExperience}  ·  撤离经验 {outcome.ExtractionExperience}\n" +
                $"技能点 {result.SkillPointsBefore} → {result.SkillPointsAfter}  ·  金币 {result.GoldBefore} → {result.GoldAfter}";
            AddRunDetails(result);
            AddSection("带回物品", result.RetainedItems);
            AddSection("丢失物品", result.LostItems);
            returnButton.interactable = coordinator != null && coordinator.CanReturn && !returning;
        }

        /// <summary>
        /// 展示随终局冻结的表现及资产变化，旧记录不推测缺失数据。
        /// </summary>
        /// <param name="result">已保存的结算结果。</param>
        private void AddRunDetails(ProjectRunResult result)
        {
            ProjectRunStatistics stats = result.Outcome.Statistics;
            string performance = stats?.HasData == true ? stats.Describe() : "这份历史结算未记录单局表现。";
            AddDetailBlock("本局表现", performance);
            ProjectRunEconomySummary economy = ProjectRunEconomy.Calculate(result);
            string entry = economy.HasEntrySnapshot ? economy.EntrySellValue.ToString() : "历史结算未记录";
            string change = economy.HasEntrySnapshot ? economy.NetAssetValue.ToString("+0;-0;0") : "无法计算";
            AddDetailBlock("物品与支出", $"入场装备估值 {entry}\n携回物品估值 {economy.RetainedSellValue}  ·  丢失物品估值 {economy.LostSellValue}\n" +
                $"本局购买支出 {result.Outcome.GoldSpent} 金币\n资产变化 {change}\n" +
                "估值按物品出售价格计算，未自动兑换金币。\n资产变化 = 携回估值 − 入场估值 − 本局支出。");
        }

        /// <summary>
        /// 向滚动区添加随可用宽度自动换行的结算说明。
        /// </summary>
        /// <param name="heading">区块标题。</param>
        /// <param name="description">说明正文。</param>
        private void AddDetailBlock(string heading, string description)
        {
            UnityEngine.UI.Text caption = ProjectUiFactory.Label(items, heading, 29, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(caption.rectTransform, 55f);
            ProjectUiFactory.Label(items, description, 23, TextAnchor.UpperLeft, new Color(0.8f, 0.9f, 0.96f));
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
                string description = ProjectEquipmentText.ColoredTitle(item) + "\n" + ProjectEquipmentText.Details(item);
                UnityEngine.UI.Text label = ProjectUiFactory.Label(items, description, 22, TextAnchor.UpperLeft, Color.white);
                label.supportRichText = true;
                ProjectUiFactory.Height(label.rectTransform, ProjectEquipmentText.PreferredHeight(description, 120f));
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

    }
}
