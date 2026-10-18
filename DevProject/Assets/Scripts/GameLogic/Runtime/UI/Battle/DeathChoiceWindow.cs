using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 角色倒下后的选择弹窗，提供广告复活、暂离、携物撤离与死亡结算。
    /// </summary>
    public sealed class DeathChoiceWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Button revive;
        private UnityEngine.UI.Button settle;
        private UnityEngine.UI.Button suspend;
        private UnityEngine.UI.Button adExtraction;
        private UnityEngine.UI.Text feedback;
        private bool requesting;
        private bool closing;

        /// <summary>
        /// 在安全区内创建可滚动的死亡规则和处理入口。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "DeathSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "DeathPanel", new Color(0.075f, 0.09f, 0.13f, 0.98f));
            panel.anchorMin = new Vector2(0.06f, 0.1f);
            panel.anchorMax = new Vector2(0.94f, 0.9f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "DeathLayout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(layout, "你已倒下", 44, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 80f);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "DeathActions", out RectTransform actions);
            UnityEngine.UI.LayoutElement size = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            size.flexibleHeight = 1f;
            size.minHeight = 100f;
            UnityEngine.UI.Text rules = ProjectUiFactory.Label(actions,
                "广告复活后继续本局，已有物品保留。\n暂离主城保留死亡选择，继续本局后仍需处理。\n\n" +
                "结算仅失去本关所得，携入装备自动退回。广告撤离可再选择最多三件本关物品，携入装备不占名额。",
                32, TextAnchor.MiddleLeft, new Color(0.9f, 0.94f, 1f));
            ProjectUiFactory.Height(rules.rectTransform, 360f);
            revive = ProjectUiFactory.ActionButton(actions, "看广告复活", () => ReviveAsync().Forget());
            suspend = ProjectUiFactory.ActionButton(actions, "暂离主城", () => SuspendAsync().Forget());
            adExtraction = ProjectUiFactory.ActionButton(actions, "看广告携物撤离", () => AdExtractionAsync().Forget());
            settle = ProjectUiFactory.ActionButton(actions, "结算返回", ConfirmDeath);
            foreach (UnityEngine.UI.Button button in new[] { revive, suspend, adExtraction, settle })
            {
                button.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 36;
                ProjectUiFactory.Height((RectTransform)button.transform, 96f);
            }
            feedback = ProjectUiFactory.Label(actions, string.Empty, 30, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(feedback.rectTransform, 120f);
        }

        /// <summary>
        /// 绑定本轮窗口与单局状态，返回动作只消费输入，关闭后保留死亡待选状态。
        /// </summary>
        /// <param name="args">包含当前单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            requesting = closing = false;
            if (run == null) return;
            ExpeditionRun current = run;
            current.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.DeathChoice);
                if (ReferenceEquals(run, current)) run = null;
            });
            IInputModule input = GameEntry.Get<IInputModule>();
            if (input != null)
            {
                System.IDisposable back = input.RegisterBackHandler(() => true, 250);
                OpenScope.Defer(() => back.Dispose());
            }
            Refresh();
        }

        /// <summary>
        /// 重复聚焦时按当前广告和保存状态刷新按钮。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 在保存或广告期间锁定按钮，复活或确认结算后关闭当前打开轮次。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || !run.IsAwaitingDeathChoice || run.ActivePanel != ExpeditionPanelKind.DeathChoice)
            {
                if (State == GameWindowState.Opened && !closing) CloseCurrentAsync(run, OpenScope).Forget();
                return;
            }
            bool available = run.CanChooseDeath && !requesting && !closing;
            revive.interactable = settle.interactable = available;
            suspend.interactable = run.CanSuspend && !requesting && !closing;
            adExtraction.interactable = !run.IsSavingRun && !requesting && !closing &&
                (run.HasAdExtractionAuthorization || run.CanRequestAdExtraction);
            adExtraction.GetComponentInChildren<UnityEngine.UI.Text>().text = run.HasAdExtractionAuthorization ? "继续选择撤离物品" : "看广告携物撤离";
            feedback.text = run.IsSavingRun ? "正在保存本局进度，请稍候。" :
                run.IsReviveAdPending || requesting ? "正在处理请求，请稍候。" : run.Message;
        }

        /// <summary>
        /// 将广告复活请求绑定到当前打开作用域，旧请求不操作重开的窗口。
        /// </summary>
        /// <returns>本轮广告复活请求任务。</returns>
        private async UniTaskVoid ReviveAsync()
        {
            if (run == null || requesting || closing || !run.CanChooseDeath) return;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            requesting = true;
            Refresh();
            await current.RequestAdReviveAsync(scope.Token).SuppressCancellationThrow();
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            requesting = false;
            Refresh();
        }

        /// <summary>
        /// 提交死亡结算，由单局变化通知关闭窗口。
        /// </summary>
        private void ConfirmDeath()
        {
            if (run == null || requesting || closing || !run.CanChooseDeath) return;
            run.TryConfirmDeath();
            Refresh();
        }

        /// <summary>
        /// 保存死亡待处理状态并暂离主城。
        /// </summary>
        /// <returns>暂离请求任务。</returns>
        private async UniTaskVoid SuspendAsync()
        {
            if (run == null || !suspend.interactable) return;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            requesting = true;
            Refresh();
            await ProjectServices.Coordinator.SuspendRunAsync(current);
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            requesting = false;
            Refresh();
        }

        /// <summary>
        /// 完成广告或复用已有资格后选择本次带出的物品。
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
            requesting = true;
            Refresh();
            await current.RequestAdExtractionAsync();
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            requesting = false;
            Refresh();
        }

        /// <summary>
        /// 以当前打开令牌关闭窗口，避免延迟关闭任务影响同类窗口的新打开轮次。
        /// </summary>
        /// <param name="current">本轮窗口绑定的单局。</param>
        /// <param name="scope">本轮窗口打开作用域。</param>
        /// <returns>当前窗口关闭任务。</returns>
        private async UniTaskVoid CloseCurrentAsync(ExpeditionRun current, LifetimeScope scope)
        {
            if (!scope.IsActive || !ReferenceEquals(OpenScope, scope) || !ReferenceEquals(run, current)) return;
            closing = true;
            await GameEntry.Get<IUIModule>().CloseWindowAsync(Id, ct: scope.Token).SuppressCancellationThrow();
            if (scope.IsActive && ReferenceEquals(OpenScope, scope) && ReferenceEquals(run, current)) closing = false;
        }
    }
}
