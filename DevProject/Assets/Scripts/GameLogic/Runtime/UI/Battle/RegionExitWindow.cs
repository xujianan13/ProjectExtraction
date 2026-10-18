using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 房间出口确认，中间房继续前进，最终房确认带回全部物品。
    /// </summary>
    public sealed class RegionExitWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text summary;
        private UnityEngine.UI.Button continueRoom;
        private UnityEngine.UI.Button stay;

        /// <summary>
        /// 建立安全区内的房间推进或最终带回操作。
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
            continueRoom = ProjectUiFactory.ActionButton(actions, "进入下一房", ConfirmExit);
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
        /// 显示中间房费用或最终带回说明，并在保存时锁定操作。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.ExtractionConfirmation)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            title.text = run.IsFinalRoom ? "最终挑战已完成" : "房间 " + (run.CurrentRoomIndex + 1) + " / " + run.RoomCount + " 已完成";
            summary.text = run.IsSavingRun ? "正在保存本局进度，请稍候。" :
                run.IsFinalRoom ? "Boss 宝箱奖励已全部领取。确认后带回全部关卡背包与固定出战装备，结束本局。" :
                "进入下一房消耗 " + PrototypeRules.RoomEntryCost + " 体力，关卡背包随本局保留。\n出战装备固定，通关后带回关卡背包。";
            ProjectUiFactory.Height(summary.rectTransform, ProjectEquipmentText.PreferredHeight(summary.text, 150f) + 36f);
            continueRoom.GetComponentInChildren<UnityEngine.UI.Text>().text = run.IsFinalRoom ? "带回结束" :
                "进入下一房 · 体力 " + PrototypeRules.RoomEntryCost;
            bool available = !run.IsSavingRun && run.IsCurrentRoomComplete && run.CurrentRoom.ExitEnabled;
            continueRoom.interactable = stay.interactable = available;
        }

        /// <summary>
        /// 根据房间类型提交继续前进或最终带回请求。
        /// </summary>
        private void ConfirmExit()
        {
            if (!continueRoom.interactable) return;
            if (run.IsFinalRoom ? run.TryConfirmExtraction() : run.TryContinueRoom()) CloseSelfAsync().Forget();
            else
            {
                summary.text = run.Message;
                ProjectUiFactory.Height(summary.rectTransform, ProjectEquipmentText.PreferredHeight(summary.text, 150f) + 36f);
            }
        }
    }
}
