using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Run
{
    /// <summary>
    /// 跨场景显示加载、结算与恢复操作的全局窗口。
    /// </summary>
    public sealed class RunTransitionWindow : GameWindow
    {
        private ProjectRunCoordinator coordinator;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text message;
        private UnityEngine.UI.Button retryButton;
        private UnityEngine.UI.Button cancelButton;
        private UnityEngine.UI.Button returnButton;
        private bool submitting;

        /// <summary>
        /// 创建完整背景及安全区内的阶段信息。
        /// </summary>
        protected override void BindComponents()
        {
            ProjectUiFactory.Stretch(RootTransform, 0f);
            RectTransform backdrop = ProjectUiFactory.Panel(RootTransform, "TransitionBackdrop", new Color(0.025f, 0.055f, 0.09f, 1f));
            ProjectUiFactory.Stretch(backdrop, 0f);
            RectTransform safeArea = ProjectUiFactory.Fill(backdrop, "SafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Vertical(safeArea, "TransitionContent", 18f);
            panel.anchorMin = new Vector2(0.08f, 0.12f);
            panel.anchorMax = new Vector2(0.92f, 0.88f);
            panel.offsetMin = Vector2.zero;
            panel.offsetMax = Vector2.zero;
            title = ProjectUiFactory.Label(panel, string.Empty, 40, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 96f);
            message = ProjectUiFactory.Label(panel, string.Empty, 25, TextAnchor.MiddleCenter, new Color(0.8f, 0.9f, 0.96f));
            ProjectUiFactory.Height(message.rectTransform, 150f);
            retryButton = ProjectUiFactory.ActionButton(panel, "重试当前操作", () => RetryAsync().Forget());
            ProjectUiFactory.Height((RectTransform)retryButton.transform, 72f);
            cancelButton = ProjectUiFactory.ActionButton(panel, "取消出战", CancelLoading);
            ProjectUiFactory.Height((RectTransform)cancelButton.transform, 72f);
            returnButton = ProjectUiFactory.ActionButton(panel, "返回主界面", () => ReturnHomeAsync().Forget());
            ProjectUiFactory.Height((RectTransform)returnButton.transform, 72f);
        }

        /// <summary>
        /// 订阅当前项目流程变化。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            coordinator = ProjectServices.Coordinator;
            if (coordinator == null)
            {
                title.text = "流程尚未就绪";
                message.text = "无法读取项目流程状态。";
                retryButton.gameObject.SetActive(false);
                cancelButton.gameObject.SetActive(false);
                returnButton.gameObject.SetActive(false);
                return;
            }
            coordinator.Changed += Refresh;
            OpenScope.Defer(() => coordinator.Changed -= Refresh);
            Refresh();
        }

        /// <summary>
        /// 按协调器阶段刷新提示和恢复按钮。
        /// </summary>
        private void Refresh()
        {
            if (coordinator == null) return;
            title.text = coordinator.Phase switch
            {
                ProjectRunPhase.Restoring => "正在恢复",
                ProjectRunPhase.AdmissionSaving => "正在保存入场",
                ProjectRunPhase.LoadingExpedition => "正在加载关卡",
                ProjectRunPhase.Activating => "正在准备战斗",
                ProjectRunPhase.FreezingOutcome => "正在记录结果",
                ProjectRunPhase.Settling => "正在结算",
                ProjectRunPhase.ReturningHome => "正在返回主界面",
                ProjectRunPhase.RecoveryRequired => "需要恢复",
                ProjectRunPhase.ResultsReady => "结算已完成",
                _ => "请稍候"
            };
            message.text = coordinator.Message;
            retryButton.gameObject.SetActive(coordinator.Phase == ProjectRunPhase.RecoveryRequired);
            retryButton.interactable = coordinator.CanRetry && !submitting;
            cancelButton.gameObject.SetActive(coordinator.CanCancel);
            cancelButton.interactable = coordinator.CanCancel && !submitting;
            returnButton.gameObject.SetActive(coordinator.CanReturn);
            returnButton.interactable = coordinator.CanReturn && !submitting;
        }

        /// <summary>
        /// 请求协调器重试原阶段。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (coordinator == null || submitting || !coordinator.CanRetry) return;
            submitting = true;
            Refresh();
            await coordinator.RetryAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            submitting = false;
            Refresh();
        }

        /// <summary>
        /// 当结果页不可用时仍可从全局窗口返回主界面。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid ReturnHomeAsync()
        {
            if (coordinator == null || submitting || !coordinator.CanReturn) return;
            submitting = true;
            Refresh();
            await coordinator.ReturnHomeAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            submitting = false;
            Refresh();
        }

        /// <summary>
        /// 登记加载取消意图，等待协调器处理未确认的写入。
        /// </summary>
        private void CancelLoading()
        {
            if (coordinator?.CanCancel != true) return;
            coordinator.CancelLoading();
            Refresh();
        }
    }
}