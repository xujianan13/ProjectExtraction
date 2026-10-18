using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 手动暂停菜单，提供恢复、暂离、广告撤离与主动放弃入口。
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
        private UnityEngine.UI.Button suspend;
        private UnityEngine.UI.Button adExtraction;
        private UnityEngine.UI.Text feedback;
        private bool confirming;
        private bool retrying;
        private bool suspending;
        private bool requestingAd;

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
            suspend = ProjectUiFactory.ActionButton(actions, "暂离主城", () => SuspendAsync().Forget());
            adExtraction = ProjectUiFactory.ActionButton(actions, "看广告携物撤离", () => AdExtractionAsync().Forget());
            abandon = ProjectUiFactory.ActionButton(actions, "放弃本局", () => AbandonAsync().Forget());
            retry = ProjectUiFactory.ActionButton(actions, "重试保存", () => RetryAsync().Forget());
            foreach (var button in new[] { resume, settings, guide, suspend, adExtraction, abandon, retry })
                ProjectUiFactory.Height((RectTransform)button.transform, 76f);
            var rules = ProjectUiFactory.Label(actions, "暂离保留本局，主城可继续挑战。\n通关带回全部物品；死亡或放弃仅失去本关所得。\n携入装备自动退回，广告撤离另可选择最多三件本关物品。",
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
            confirming = retrying = suspending = requestingAd = false;
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
            bool idle = !confirming && !retrying && !suspending && !requestingAd;
            bool available = coordinator.Phase == ProjectRunPhase.Playing && run.CanAbandon && idle;
            resume.interactable = available;
            settings.interactable = available;
            guide.interactable = available;
            abandon.interactable = available;
            suspend.interactable = coordinator.Phase == ProjectRunPhase.Playing && run.CanSuspend && idle;
            adExtraction.interactable = coordinator.Phase == ProjectRunPhase.Playing && !run.IsSavingRun && idle &&
                (run.HasAdExtractionAuthorization || run.CanRequestAdExtraction);
            adExtraction.GetComponentInChildren<UnityEngine.UI.Text>().text = run.HasAdExtractionAuthorization ? "继续选择撤离物品" : "看广告携物撤离";
            retry.gameObject.SetActive(session.HasPendingSave);
            retry.interactable = session.HasPendingSave && idle && !coordinator.IsPaused;
            if (suspending) feedback.text = "正在保存并暂离主城。";
            else if (requestingAd) feedback.text = "正在请求撤离广告。";
            else if (session.HasUncommittedSave) feedback.text = "进度尚未保存，请先重试保存。";
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
            if (run?.CanAbandon != true || confirming || retrying || suspending || requestingAd) return;
            GameEntry.Get<IUIModule>()?.OpenWindowAsync(windowId).Forget();
        }

        /// <summary>
        /// 二次确认后提交一次主动放弃，不播放角色死亡动作。
        /// </summary>
        /// <returns>确认与终局提交的异步任务。</returns>
        private async UniTaskVoid AbandonAsync()
        {
            if (run?.CanAbandon != true || confirming || retrying || suspending || requestingAd) return;
            IUIModule ui = GameEntry.Get<IUIModule>();
            if (ui == null) return;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            confirming = true;
            Refresh();
            var answer = await ui.ConfirmAsync("放弃本局？", "本关获得的物品将丢失，携入装备自动退回。已经获得的经验和技能成长保留，入场体力不退还。",
                "确认放弃", "继续探索", scope.Token).SuppressCancellationThrow();
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(current, run)) return;
            confirming = false;
            if (answer.IsCanceled) { Refresh(); return; }
            if (answer.Result && !coordinator.TryAbandonRun(current)) feedback.text = "当前状态不能放弃，请稍后重试。";
            Refresh();
        }

        /// <summary>
        /// 保存当前单局并暂离主城，保留同一局继续入口。
        /// </summary>
        /// <returns>暂离请求任务。</returns>
        private async UniTaskVoid SuspendAsync()
        {
            if (run == null || !suspend.interactable) return;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            suspending = true;
            Refresh();
            bool succeeded = await coordinator.SuspendRunAsync(current);
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            suspending = false;
            if (!succeeded) feedback.text = coordinator.Message;
            Refresh();
        }

        /// <summary>
        /// 使用已经确认的广告资格，或完成广告后进入撤离物品选择。
        /// </summary>
        /// <returns>广告请求或选择窗口打开任务。</returns>
        private async UniTaskVoid AdExtractionAsync()
        {
            if (run == null || !adExtraction.interactable) return;
            if (run.HasAdExtractionAuthorization)
            {
                run.TryOpenAdExtraction();
                return;
            }
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            requestingAd = true;
            Refresh();
            bool succeeded = await current.RequestAdExtractionAsync();
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            requestingAd = false;
            if (!succeeded) feedback.text = current.Message;
            Refresh();
        }

        /// <summary>
        /// 重试原偏好或引导存档事务，完成前保持暂停。
        /// </summary>
        /// <returns>存档重试任务。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (session?.HasPendingSave != true || retrying || confirming || suspending || requestingAd) return;
            LifetimeScope scope = OpenScope;
            retrying = true;
            Refresh();
            ProjectOperationResult result = await session.RetryPendingSaveAsync();
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope)) return;
            retrying = false;
            feedback.text = result.Message;
            Refresh();
        }
    }
}
