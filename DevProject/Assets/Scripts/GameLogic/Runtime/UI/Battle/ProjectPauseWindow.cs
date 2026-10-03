using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 手动暂停菜单，提供恢复、设置、玩法帮助与主动放弃的确认入口。
    /// </summary>
    public sealed class ProjectPauseWindow : GameWindow
    {
        private ExpeditionRun run;
        private ProjectSession session;
        private ProjectRunCoordinator coordinator;
        private UnityEngine.UI.Button resume;
        private UnityEngine.UI.Button settings;
        private UnityEngine.UI.Button guide;
        private UnityEngine.UI.Button abandon;
        private UnityEngine.UI.Button retry;
        private UnityEngine.UI.Text feedback;
        private bool confirming;
        private bool retrying;

        /// <summary>
        /// 在安全区内建立可滚动的暂停菜单，兼容窄屏与横屏。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "PauseSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "PausePanel", new Color(0.055f, 0.11f, 0.16f, 0.98f));
            panel.anchorMin = new Vector2(0.08f, 0.08f);
            panel.anchorMax = new Vector2(0.92f, 0.92f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 20f);
            var title = ProjectUiFactory.Label(layout, "游戏暂停", 36, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 68f);
            var scroll = ProjectUiFactory.Scroll(layout, "Actions", out RectTransform actions);
            var size = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.flexibleHeight = 1f;
            size.minHeight = 100f;
            resume = ProjectUiFactory.ActionButton(actions, "继续游戏", Resume);
            settings = ProjectUiFactory.ActionButton(actions, "设置", () => OpenUtility(ProjectServices.SettingsWindow));
            guide = ProjectUiFactory.ActionButton(actions, "玩法帮助", () => OpenUtility(ProjectServices.GuideWindow));
            abandon = ProjectUiFactory.ActionButton(actions, "放弃本局", () => AbandonAsync().Forget());
            retry = ProjectUiFactory.ActionButton(actions, "重试保存", () => RetryAsync().Forget());
            foreach (var button in new[] { resume, settings, guide, abandon, retry })
                ProjectUiFactory.Height((RectTransform)button.transform, 76f);
            var rules = ProjectUiFactory.Label(actions, "正常撤离可携出全部物品。\n主动放弃只保留安全箱内物品，入场体力不退还。",
                24, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(rules.rectTransform, 150f);
            feedback = ProjectUiFactory.Label(actions, string.Empty, 23, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(feedback.rectTransform, 100f);
        }

        /// <summary>
        /// 绑定本局与存档变化，关闭时只释放本菜单拥有的暂停。
        /// </summary>
        /// <param name="args">包含本局引用的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            session = ProjectServices.Session;
            coordinator = ProjectServices.Coordinator;
            if (run == null || session == null || coordinator == null) return;
            ExpeditionRun currentRun = run;
            ProjectSession currentSession = session;
            ProjectRunCoordinator currentCoordinator = coordinator;
            currentRun.Changed += Refresh;
            currentSession.Changed += Refresh;
            currentCoordinator.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                currentRun.Changed -= Refresh;
                currentSession.Changed -= Refresh;
                currentCoordinator.Changed -= Refresh;
                currentRun.ClosePanel(ExpeditionPanelKind.Pause);
                if (ReferenceEquals(run, currentRun)) run = null;
            });
        }

        /// <summary>
        /// 显示暂停菜单时刷新门禁。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 按当前单局与存档事务更新操作状态。
        /// </summary>
        private void Refresh()
        {
            if (run == null || session == null || coordinator == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.Pause)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            bool available = coordinator.Phase == ProjectRunPhase.Playing && run.CanAbandon && !confirming && !retrying;
            resume.interactable = available;
            settings.interactable = available;
            guide.interactable = available;
            abandon.interactable = available;
            retry.gameObject.SetActive(session.HasPendingSave);
            retry.interactable = session.HasPendingSave && !retrying && !confirming && !coordinator.IsPaused;
            if (session.HasUncommittedSave) feedback.text = "设置或指引进度尚未保存，完成保存后可继续游戏。";
        }

        /// <summary>
        /// 确认没有未提交存档后恢复本局。
        /// </summary>
        private void Resume()
        {
            if (resume.interactable && run?.CanAbandon == true) CloseSelfAsync().Forget();
        }

        /// <summary>
        /// 在暂停状态上层打开设置或帮助，保持本菜单的暂停原因。
        /// </summary>
        /// <param name="windowId">目标窗口身份。</param>
        private void OpenUtility(int windowId)
        {
            if (run?.CanAbandon != true || confirming || retrying) return;
            GameEntry.Get<IUIModule>()?.OpenWindowAsync(windowId).Forget();
        }

        /// <summary>
        /// 二次确认后提交一次主动放弃，不播放角色死亡动作。
        /// </summary>
        /// <returns>确认与终局提交的异步任务。</returns>
        private async UniTaskVoid AbandonAsync()
        {
            if (run?.CanAbandon != true || confirming || retrying) return;
            IUIModule ui = GameEntry.Get<IUIModule>();
            if (ui == null) return;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            confirming = true;
            Refresh();
            var answer = await ui.ConfirmAsync("放弃本局？", "安全箱外的入场装备与战利品将全部丢失；只带回安全箱物品。入场体力不退还，且不获得撤离经验。",
                "确认放弃", "继续探索", scope.Token).SuppressCancellationThrow();
            confirming = false;
            if (answer.IsCanceled || !scope.IsActive || !ReferenceEquals(current, run)) return;
            if (answer.Result && !coordinator.TryAbandonRun(current)) feedback.text = "当前状态不能放弃，请稍后重试。";
            Refresh();
        }

        /// <summary>
        /// 重试原偏好或引导存档事务，完成前保持暂停。
        /// </summary>
        /// <returns>存档重试任务。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (session?.HasPendingSave != true || retrying || confirming) return;
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
