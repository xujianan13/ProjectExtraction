using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Home
{
    /// <summary>
    /// 局外主界面，读取项目会话并导航至仓库、技能和商店窗口。
    /// </summary>
    public sealed class HomeWindow : GameWindow
    {
        private RectTransform legacyPanel;
        private RectTransform levelList;
        private UnityEngine.UI.Text progression;
        private UnityEngine.UI.Text resources;
        private RectTransform experienceFill;
        private UnityEngine.UI.Text levelDetails;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button startButton;
        private UnityEngine.UI.Button retryButton;
        private UnityEngine.UI.Button supplyButton;
        private UnityEngine.UI.Button warehouseButton;
        private UnityEngine.UI.Button skillsButton;
        private UnityEngine.UI.Button shopButton;
        private UnityEngine.UI.Button characterButton;
        private ProjectSession session;
        private ProjectRunCoordinator coordinator;
        private bool launching;
        private bool retrying;

        /// <summary>
        /// 查找现有 Home Prefab 宿主。
        /// </summary>
        protected override void BindComponents()
        {
            Transform panel = RootTransform.Find("Panel");
            if (panel != null)
            {
                legacyPanel = panel as RectTransform;
                Transform title = panel.Find("Title");
                if (title != null) ProjectUiFactory.UseFont(title.GetComponent<UnityEngine.UI.Text>()?.font);
            }
        }

        /// <summary>
        /// 构建安全区内的主界面。
        /// </summary>
        protected override void Init()
        {
            if (legacyPanel != null) legacyPanel.gameObject.SetActive(false);
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "HomeContent", new Color(0.04f, 0.08f, 0.12f, 1f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Vertical(layout, "Header", 8f);
            ProjectUiFactory.Height(header, 208f);
            progression = ProjectUiFactory.Label(header, string.Empty, 31, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(progression.rectTransform, 50f);
            RectTransform experienceBar = ProjectUiFactory.Panel(header, "ExperienceBar", new Color(0.14f, 0.25f, 0.3f, 1f));
            ProjectUiFactory.Height(experienceBar, 14f);
            experienceFill = ProjectUiFactory.Panel(experienceBar, "Fill", new Color(0.42f, 0.78f, 0.84f, 1f));
            experienceFill.anchorMin = Vector2.zero;
            experienceFill.anchorMax = Vector2.one;
            experienceFill.offsetMin = Vector2.zero;
            experienceFill.offsetMax = Vector2.zero;
            resources = ProjectUiFactory.Label(header, string.Empty, 23, TextAnchor.MiddleLeft, new Color(0.76f, 0.9f, 0.95f));
            ProjectUiFactory.Height(resources.rectTransform, 56f);
            supplyButton = ProjectUiFactory.ActionButton(header, "领取补给", () => ClaimSupplyAsync().Forget());
            ProjectUiFactory.Height(supplyButton.GetComponent<RectTransform>(), 60f);

            RectTransform body = ProjectUiFactory.Vertical(layout, "Main", 12f);
            body.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleHeight = 1f;
            RectTransform headingRow = ProjectUiFactory.Horizontal(body, "LevelHeading", 12f);
            ProjectUiFactory.Height(headingRow, 56f);
            UnityEngine.UI.Text heading = ProjectUiFactory.Label(headingRow, "选择关卡", 32, TextAnchor.MiddleLeft, Color.white);
            heading.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            characterButton = ProjectUiFactory.ActionButton(headingRow, "角色属性", () => GameEntry.Get<IUIModule>().OpenWindowAsync(2400).Forget());
            characterButton.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 150f;
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(body, "Levels", out levelList);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 80f;
            UnityEngine.UI.ScrollRect detailsScroll = ProjectUiFactory.Scroll(body, "LevelDetails", out RectTransform detailsContent);
            ProjectUiFactory.Height((RectTransform)detailsScroll.transform, 94f);
            levelDetails = ProjectUiFactory.Label(detailsContent, string.Empty, 23, TextAnchor.UpperLeft, Color.white);
            startButton = ProjectUiFactory.ActionButton(body, "出战", () => StartRunAsync().Forget());
            ProjectUiFactory.Height(startButton.GetComponent<RectTransform>(), 90f);
            retryButton = ProjectUiFactory.ActionButton(body, "重试保存", () => RetrySaveAsync().Forget());
            ProjectUiFactory.Height(retryButton.GetComponent<RectTransform>(), 80f);
            retryButton.gameObject.SetActive(false);
            feedback = ProjectUiFactory.Label(body, string.Empty, 21, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(feedback.rectTransform, 50f);

            RectTransform footer = ProjectUiFactory.Horizontal(layout, "Footer", 16f);
            ProjectUiFactory.Height(footer, 90f);
            warehouseButton = ProjectUiFactory.ActionButton(footer, "仓库", () => GameEntry.Get<IUIModule>().OpenWindowAsync(2101).Forget());
            skillsButton = ProjectUiFactory.ActionButton(footer, "技能", () => GameEntry.Get<IUIModule>().OpenWindowAsync(2102).Forget());
            shopButton = ProjectUiFactory.ActionButton(footer, "商店", () => GameEntry.Get<IUIModule>().OpenWindowAsync(ProjectServices.HomeShopWindow).Forget());
        }

        /// <summary>
        /// 绑定本轮会话变化通知。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            coordinator = ProjectServices.Coordinator;
            if (session == null || coordinator == null)
            {
                feedback.text = "项目数据尚未就绪";
                startButton.interactable = false;
                supplyButton.interactable = false;
                warehouseButton.interactable = false;
                skillsButton.interactable = false;
                shopButton.interactable = false;
                characterButton.interactable = false;
                return;
            }
            session.Changed += Refresh;
            OpenScope.Defer(() => session.Changed -= Refresh);
            coordinator.Changed += Refresh;
            OpenScope.Defer(() => coordinator.Changed -= Refresh);
            session.RefreshClock();
        }

        /// <summary>
        /// 刷新真实项目数据。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 刷新顶部属性、关卡与按钮。
        /// </summary>
        private void Refresh()
        {
            if (session == null || coordinator == null) return;
            progression.text = $"等级 {session.Level}    经验 {session.Experience}/{session.ExperienceToNextLevel}";
            resources.text = $"体力 {session.Stamina}/{session.StaminaCap}    金币 {session.Gold}    钻石 {session.Diamonds}";
            Vector2 maximum = experienceFill.anchorMax;
            maximum.x = Mathf.Clamp01((float)session.Experience / Mathf.Max(1, session.ExperienceToNextLevel));
            experienceFill.anchorMax = maximum;
            ProjectUiFactory.Clear(levelList);
            int selectedLevelId = coordinator.SelectedLevelId;
            bool selectedExists = false;
            foreach (ProjectLevelOption option in session.Levels)
                if (option.Id == selectedLevelId && session.Level >= option.RequiredLevel) selectedExists = true;
            if (!selectedExists)
            {
                selectedLevelId = -1;
                foreach (ProjectLevelOption option in session.Levels)
                    if (session.Level >= option.RequiredLevel) { selectedLevelId = option.Id; break; }
            }
            if (selectedLevelId > 0 && coordinator.SelectedLevelId != selectedLevelId) coordinator.SelectedLevelId = selectedLevelId;
            foreach (ProjectLevelOption option in session.Levels)
            {
                int id = option.Id;
                string lockText = session.Level >= option.RequiredLevel ? string.Empty : $" · 需等级 {option.RequiredLevel}";
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(levelList, (option.Id == selectedLevelId ? "● " : "○ ") + option.Name + lockText,
                    () => SelectLevel(id));
                ProjectUiFactory.Height(button.GetComponent<RectTransform>(), 62f);
            }
            ProjectLevelOption selected = null;
            foreach (ProjectLevelOption option in session.Levels)
                if (option.Id == selectedLevelId) selected = option;
            levelDetails.text = selected == null ? "暂无可选关卡" : selected.Description + (session.Level < selected.RequiredLevel ? "\n等级不足" : string.Empty);
            bool canStart = selected != null && session.Level >= selected.RequiredLevel && session.Stamina >= PrototypeRules.EntryCost &&
                coordinator.CanStart && !session.IsBusy && !launching;
            startButton.interactable = canStart;
            bool canEdit = coordinator.Phase == ProjectRunPhase.HomeReady && !session.IsBusy && !launching;
            supplyButton.interactable = canEdit;
            warehouseButton.interactable = canEdit;
            skillsButton.interactable = canEdit;
            shopButton.interactable = canEdit;
            characterButton.interactable = canEdit;
            retryButton.gameObject.SetActive(session.HasPendingSave);
            retryButton.interactable = session.HasPendingSave && coordinator.Phase == ProjectRunPhase.HomeReady && !retrying && !launching;
            if (!string.IsNullOrEmpty(session.LastRunSummary) && string.IsNullOrEmpty(feedback.text))
                feedback.text = session.LastRunSummary;
        }

        /// <summary>
        /// 选择当前准备进入的关卡。
        /// </summary>
        /// <param name="id">关卡标识。</param>
        private void SelectLevel(int id)
        {
            if (coordinator == null || coordinator.Phase != ProjectRunPhase.HomeReady) return;
            coordinator.SelectedLevelId = id;
        }

        /// <summary>
        /// 等待单局启动结果再显示反馈。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid StartRunAsync()
        {
            if (session == null || coordinator == null || session.IsBusy || launching || !startButton.interactable) return;
            launching = true;
            Refresh();
            bool started = await coordinator.StartRunAsync(coordinator.SelectedLevelId);
            if (Root == null || State != GameWindowState.Opened) return;
            launching = false;
            feedback.text = started ? "已开始出战" : coordinator.Message;
            Refresh();
        }

        /// <summary>
        /// 提交补给领取事务并显示已保存结果。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid ClaimSupplyAsync()
        {
            if (session == null || session.IsBusy || launching || !supplyButton.interactable) return;
            launching = true;
            Refresh();
            ProjectOperationResult result = await session.ClaimSupplyAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            launching = false;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 重试原存档事务，不重复发放奖励。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid RetrySaveAsync()
        {
            if (session == null || !session.HasPendingSave || retrying) return;
            retrying = true;
            Refresh();
            ProjectOperationResult result = await session.RetryPendingSaveAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            retrying = false;
            feedback.text = result.Message;
            Refresh();
        }

    }
}
