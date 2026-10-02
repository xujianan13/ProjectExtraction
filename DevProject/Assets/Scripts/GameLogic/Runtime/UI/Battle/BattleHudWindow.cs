using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 局内生命、交互、探索小地图与物品面板入口。
    /// </summary>
    public sealed class BattleHudWindow : GameWindow
    {
        private ExpeditionRun run;
        private ProjectRunCoordinator coordinator;
        private UnityEngine.UI.Text interactionLabel;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.Text hint;
        private RectTransform map;
        private UnityEngine.UI.Button resumeChoice;
        private UnityEngine.UI.Button interactButton;
        private ExpeditionJoystick joystick;
        private int mapRoom = -1;
        private int mapCleared = -1;
        private bool openingPanel;
        private ExpeditionPanelKind openedPanel;

        /// <summary>
        /// 建立代码驱动的 HUD 层级，沿用框架的 Canvas 与 EventSystem。
        /// </summary>
        protected override void BindComponents()
        {
            RectTransform content = ProjectUiFactory.Fill(RootTransform, "BattleHud", Color.clear);
            var header = ProjectUiFactory.Vertical(content, "Status", 5);
            status = ProjectUiFactory.Label(header, "", 34, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(status.rectTransform, 104f);
            hint = ProjectUiFactory.Label(header, "", 28, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(hint.rectTransform, 104f);
            map = ProjectUiFactory.Panel(content, "Minimap", new Color(0.04f, 0.07f, 0.1f, 0.7f));
            RectTransform stick = CreateDisc(content, "MoveStick", new Color(0.1f, 0.25f, 0.35f, 0.72f));
            joystick = stick.gameObject.AddComponent<ExpeditionJoystick>();
            RectTransform handle = CreateDisc(stick, "Handle", new Color(0.3f, 0.72f, 0.82f, 0.9f));
            handle.anchorMin = Vector2.one * 0.5f;
            handle.anchorMax = Vector2.one * 0.5f;
            handle.sizeDelta = Vector2.one * 100f;
            handle.GetComponent<JoystickDiscGraphic>().raycastTarget = false;
            joystick.SetHandle(handle);
            RectTransform actions = ProjectUiFactory.Vertical(content, "ControlActions", 12f);
            actions.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            interactButton = ProjectUiFactory.ActionButton(actions, "交互 / E", RequestInteraction);
            interactionLabel = interactButton.GetComponentInChildren<UnityEngine.UI.Text>();
            interactionLabel.fontSize = 32;
            ProjectUiFactory.Height((RectTransform)interactButton.transform, 100f);
            var bagButton = ProjectUiFactory.ActionButton(actions, "背包 / 安全箱", OpenInventory);
            bagButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 32;
            ProjectUiFactory.Height((RectTransform)bagButton.transform, 100f);
            resumeChoice = ProjectUiFactory.ActionButton(actions, "继续选择强化", OpenPendingChoice);
            resumeChoice.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 30;
            ProjectUiFactory.Height((RectTransform)resumeChoice.transform, 100f);
            resumeChoice.gameObject.SetActive(false);
            content.gameObject.AddComponent<BattleHudLayout>().Configure(header, map, stick, actions);
        }

        /// <summary>
        /// 绑定当前单局和保存状态。
        /// </summary>
        /// <param name="args">包含单局实例的参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            joystick.Bind(run.SetTouchMove, () => run.CanAcceptPlayerInput);
            run.Changed += Refresh;
            coordinator = ProjectServices.Coordinator;
            if (coordinator != null) coordinator.Changed += Refresh;
            ExpeditionRun current = run;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                if (coordinator != null) coordinator.Changed -= Refresh;
                joystick.ResetInput();
                joystick.Bind(null);
                current.SetTouchMove(Vector2.zero);
                current.ClosePanel(current.ActivePanel);
                if (ReferenceEquals(run, current)) run = null;
            });
            Refresh();
        }

        /// <summary>
        /// 刷新只读显示，地图投影不包含撤离字段。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            run.World.TryGetActor(run.Player, out CombatActorSnapshot player);
            status.text = "生命 " + Mathf.CeilToInt(player.Health) + " / " + Mathf.CeilToInt(player.MaxHealth) +
                "\n本局经验 " + run.KillExperience + " · 金币 " + run.AvailableGold;
            hint.text = run.Message;
            bool choosing = run.IsChoosingRogue && run.PendingRogueChoice != null;
            resumeChoice.gameObject.SetActive(choosing);
            bool acceptsInput = run.CanAcceptPlayerInput;
            joystick.enabled = acceptsInput;
            if (!acceptsInput) joystick.ResetInput();
            interactButton.interactable = acceptsInput;
            if (choosing)
            {
                run.SetTouchMove(Vector2.zero);
            }
            interactionLabel.text = run.InteractionLabel;
            int cleared = 0;
            foreach (LevelRoomState room in run.Maps.Rooms) if (room.IsCleared) cleared++;
            if (mapRoom != run.Maps.CurrentRoomId || cleared != mapCleared)
            {
                mapRoom = run.Maps.CurrentRoomId;
                mapCleared = cleared;
                RefreshMap(run.Maps.GetMinimapProjection());
            }
            if (run.ActivePanel == ExpeditionPanelKind.None) openedPanel = ExpeditionPanelKind.None;
            else if (!openingPanel && openedPanel != run.ActivePanel) OpenActivePanelAsync(run.ActivePanel).Forget();
        }

        /// <summary>
        /// 显示地图方格及已知连线，不绘制撤离点或路线。
        /// </summary>
        /// <param name="projection">已经过滤的信息投影。</param>
        private void RefreshMap(LevelMinimapProjection projection)
        {
            ProjectUiFactory.Clear(map);
            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            var positions = new Dictionary<int, Vector2>();
            foreach (LevelMinimapRoom room in projection.Rooms)
            {
                minX = Mathf.Min(minX, room.GridPosition.x); maxX = Mathf.Max(maxX, room.GridPosition.x);
                minY = Mathf.Min(minY, room.GridPosition.y); maxY = Mathf.Max(maxY, room.GridPosition.y);
            }
            foreach (LevelMinimapRoom room in projection.Rooms)
                positions[room.RoomId] = new Vector2((room.GridPosition.x - minX + 1f) / (maxX - minX + 2f),
                    (room.GridPosition.y - minY + 1f) / (maxY - minY + 2f));
            foreach (LevelMinimapConnection edge in projection.Connections)
            {
                Vector2 from = positions[edge.FromRoomId], to = positions[edge.ToRoomId];
                RectTransform line = ProjectUiFactory.Panel(map, "Link", new Color(0.5f, 0.6f, 0.7f));
                Place(line, Vector2.Min(from, to) - new Vector2(0.004f, 0.004f), Vector2.Max(from, to) + new Vector2(0.004f, 0.004f));
                line.GetComponent<UnityEngine.UI.Image>().raycastTarget = false;
            }
            foreach (LevelMinimapRoom room in projection.Rooms)
            {
                RectTransform cell = ProjectUiFactory.Panel(map, "Room_" + room.RoomId,
                    room.IsCurrent ? new Color(0.1f, 0.7f, 0.8f) : new Color(0.2f, 0.3f, 0.4f));
                Vector2 center = positions[room.RoomId];
                Place(cell, center - new Vector2(0.06f, 0.09f), center + new Vector2(0.06f, 0.09f));
                string caption = !room.IsVisited ? "?" : room.Type == LevelRoomType.Boss ? "王" : room.Type == LevelRoomType.Npc ? "商" :
                    room.Type == LevelRoomType.Loot ? "箱" : room.IsCleared == true ? "✓" : "战";
                var label = ProjectUiFactory.Label(cell, caption, 18, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Stretch(label.rectTransform, 0);
            }
        }

        /// <summary>
        /// 请求打开本局背包弹窗。
        /// </summary>
        private void OpenInventory()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.TryOpenInventory() == true)
                run.SetTouchMove(Vector2.zero);
        }

        /// <summary>
        /// 按单局面板状态打开对应的模态窗口，失败时归还面板状态。
        /// </summary>
        /// <param name="kind">准备打开的面板类型。</param>
        /// <returns>窗口打开任务。</returns>
        private async UniTaskVoid OpenActivePanelAsync(ExpeditionPanelKind kind)
        {
            if (run == null || openingPanel) return;
            openingPanel = true;
            ExpeditionRun current = run;
            LifetimeScope scope = OpenScope;
            IUIModule ui = GameEntry.Get<IUIModule>();
            int windowId = kind == ExpeditionPanelKind.Inventory ? ProjectServices.ExpeditionInventoryWindow :
                ProjectServices.ExpeditionMerchantWindow;
            try
            {
                GameWindow opened = ui == null ? null : await ui.OpenWindowAsync(windowId, new GameWindowOpenArgs(current), scope.Token);
                if (opened == null)
                {
                    if (scope.IsActive && current.ActivePanel == kind) current.ClosePanel(kind);
                    return;
                }
                if (!scope.IsActive || !ReferenceEquals(run, current) || current.ActivePanel != kind)
                {
                    await ui.CloseWindowAsync(windowId);
                    return;
                }
                openedPanel = kind;
            }
            catch (System.OperationCanceledException)
            {
                if (scope.IsActive && ReferenceEquals(run, current) && current.ActivePanel == kind) current.ClosePanel(kind);
            }
            catch (System.Exception error)
            {
                GameEntry.Get<ILogModule>()?.Error("BattleHud", "打开局内面板失败：" + error.Message);
                if (scope.IsActive && ReferenceEquals(run, current) && current.ActivePanel == kind) current.ClosePanel(kind);
            }
            finally
            {
                openingPanel = false;
            }
        }

        /// <summary>
        /// 仅在本局游玩阶段提交交互请求。
        /// </summary>
        private void RequestInteraction()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.IsChoosingRogue == false) run.RequestInteraction();
        }

        /// <summary>
        /// 在选择窗口被系统关闭后重新打开当前待选奖励。
        /// </summary>
        private void OpenPendingChoice()
        {
            if (run == null || !run.IsChoosingRogue || run.PendingRogueChoice == null) return;
            IUIModule ui = GameEntry.Get<IUIModule>();
            if (ui != null) ui.OpenWindowAsync(ProjectServices.RogueChoiceWindow, new GameWindowOpenArgs(run)).Forget();
        }

        /// <summary>
        /// 为摇杆创建可随 Canvas 缩放的圆形控件。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">控件名称。</param>
        /// <param name="color">圆盘颜色。</param>
        /// <returns>圆形控件矩形。</returns>
        private static RectTransform CreateDisc(Transform parent, string name, Color color)
        {
            var node = new GameObject(name, typeof(RectTransform), typeof(JoystickDiscGraphic));
            node.transform.SetParent(parent, false);
            node.GetComponent<JoystickDiscGraphic>().color = color;
            return (RectTransform)node.transform;
        }

        /// <summary>
        /// 使用相对锚点布局不同屏幕比例。
        /// </summary>
        /// <param name="rect">布局目标。</param>
        /// <param name="min">左下锚点。</param>
        /// <param name="max">右上锚点。</param>
        private static void Place(RectTransform rect, Vector2 min, Vector2 max)
        {
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
