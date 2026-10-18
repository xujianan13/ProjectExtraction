using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 中间房完成后的出口选择，只提交前往下一房请求。
    /// </summary>
    public sealed class RegionExitWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text summary;
        private UnityEngine.UI.Button continueRoom;
        private UnityEngine.UI.Button stay;

        /// <summary>
        /// 建立安全区内的房间推进操作。
        /// </summary>
        protected override void Init()
        {
            RectTransform safe = ProjectUiFactory.Fill(RootTransform, "ExitSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safe, "ExitPanel", new Color(0.05f, 0.1f, 0.14f, 0.98f));
            panel.anchorMin = new Vector2(0.06f, 0.1f);
            panel.anchorMax = new Vector2(0.94f, 0.9f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "ExitLayout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            title = ProjectUiFactory.Label(layout, "房间完成", 34, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 64f);
            var scroll = ProjectUiFactory.Scroll(layout, "ExitActions", out RectTransform actions);
            var scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 100f;
            summary = ProjectUiFactory.Label(actions, "", 25, TextAnchor.MiddleLeft, Color.white);
            summary.name = "RoomProgressSummary";
            ProjectUiFactory.Height(summary.rectTransform, 150f);
            continueRoom = ProjectUiFactory.ActionButton(actions, "进入下一房", ContinueRoom);
            stay = ProjectUiFactory.ActionButton(actions, "留在本房", () => CloseSelfAsync().Forget());
            foreach (var button in new[] { continueRoom, stay }) ProjectUiFactory.Height((RectTransform)button.transform, 80f);
        }

        /// <summary>
        /// 绑定房间出口面板，关闭时释放面板暂停。
        /// </summary>
        /// <param name="args">包含单局的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            run.Changed += Refresh;
            ExpeditionRun current = run;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.ExtractionConfirmation);
                if (ReferenceEquals(run, current)) run = null;
            });
            Refresh();
        }

        /// <summary>
        /// 显示中间房推进费用，并在保存时锁定操作。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.IsFinalRoom || run.ActivePanel != ExpeditionPanelKind.ExtractionConfirmation)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            title.text = "房间 " + (run.CurrentRoomIndex + 1) + " / " + run.RoomCount + " 已完成";
            summary.text = run.IsSavingRun ? "正在保存本局进度，请稍候。" :
                "进入下一房消耗 " + PrototypeRules.RoomEntryCost + " 体力，关卡背包随本局保留。\n出战装备固定，通关后带回关卡背包。";
            ProjectUiFactory.Height(summary.rectTransform, ProjectEquipmentText.PreferredHeight(summary.text, 150f) + 36f);
            continueRoom.GetComponentInChildren<UnityEngine.UI.Text>().text = "进入下一房 · 体力 " + PrototypeRules.RoomEntryCost;
            bool available = !run.IsSavingRun && run.IsCurrentRoomComplete && run.CurrentRoom.ExitEnabled;
            continueRoom.interactable = stay.interactable = available;
        }

        /// <summary>
        /// 提交单向进入下一房间的请求。
        /// </summary>
        private void ContinueRoom()
        {
            if (!continueRoom.interactable) return;
            if (run.TryContinueRoom()) CloseSelfAsync().Forget();
            else
            {
                summary.text = run.Message;
                ProjectUiFactory.Height(summary.rectTransform, ProjectEquipmentText.PreferredHeight(summary.text, 150f) + 36f);
            }
        }
    }
}
