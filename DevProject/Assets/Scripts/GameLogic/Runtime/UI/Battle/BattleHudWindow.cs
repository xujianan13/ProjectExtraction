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
        private UnityEngine.UI.Text targetSummary;
        private UnityEngine.UI.Button cycleButton;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.Text hint;
        private RectTransform map;
        private UnityEngine.UI.Button resumeChoice;
        private UnityEngine.UI.Button interactButton;
        private ExpeditionJoystick joystick;
        private BattleHudLayout layout;
        private int mapRoom = -1;
        private int mapCleared = -1;
        private int selectedMapRoomId = -1;
        private string selectedInteractionId;
        private string selectedMapDetail;
        private LevelMinimapProjection mapProjection;
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
            RectTransform actions = ProjectUiFactory.Vertical(content, "ControlActions", 8f);
            targetSummary = ProjectUiFactory.Label(actions, "附近暂无可交互目标", 22, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(targetSummary.rectTransform, 170f);
            cycleButton = ProjectUiFactory.ActionButton(actions, "切换目标 / Tab", CycleInteraction);
            cycleButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 26;
            ProjectUiFactory.Height((RectTransform)cycleButton.transform, 52f);
            actions.GetComponent<UnityEngine.UI.VerticalLayoutGroup>().childAlignment = TextAnchor.LowerCenter;
            interactButton = ProjectUiFactory.ActionButton(actions, "交互 / E", RequestInteraction);
            interactionLabel = interactButton.GetComponentInChildren<UnityEngine.UI.Text>();
            interactionLabel.fontSize = 32;
            ProjectUiFactory.Height((RectTransform)interactButton.transform, 90f);
            var bagButton = ProjectUiFactory.ActionButton(actions, "背包 / 安全箱", OpenInventory);
            bagButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 32;
            ProjectUiFactory.Height((RectTransform)bagButton.transform, 90f);
            RectTransform menuRow = ProjectUiFactory.Horizontal(actions, "MenuActions", 8f);
            ProjectUiFactory.Height(menuRow, 90f);
            var overviewButton = ProjectUiFactory.ActionButton(menuRow, "强化总览", OpenRogueOverview);
            overviewButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 24;
            ProjectUiFactory.ActionButton(menuRow, "暂停", OpenPause);
            resumeChoice = ProjectUiFactory.ActionButton(actions, "继续选择强化", OpenPendingChoice);
            resumeChoice.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 30;
            ProjectUiFactory.Height((RectTransform)resumeChoice.transform, 90f);
            resumeChoice.gameObject.SetActive(false);
            layout = content.gameObject.AddComponent<BattleHudLayout>();
            layout.Configure(header, map, stick, actions);
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
            hint.text = string.IsNullOrEmpty(selectedMapDetail) ? run.Message : run.Message + "\n" + selectedMapDetail;
            RefreshTarget();
            bool choosing = run.IsChoosingRogue && run.PendingRogueChoice != null;
            if (resumeChoice.gameObject.activeSelf != choosing)
            {
                resumeChoice.gameObject.SetActive(choosing);
                layout.Refresh();
            }
            bool acceptsInput = run.CanAcceptPlayerInput;
            joystick.enabled = acceptsInput;
            if (!acceptsInput) joystick.ResetInput();
            interactButton.interactable = acceptsInput && run.HasInteraction;
            cycleButton.interactable = acceptsInput && run.InteractionCount > 1;
            if (choosing)
            {
                run.SetTouchMove(Vector2.zero);
            }
            interactionLabel.text = run.InteractionLabel;
            LevelMinimapProjection projection = run.Maps.GetMinimapProjection();
            int cleared = 0;
            foreach (LevelMinimapRoom room in projection.Rooms) if (room.IsCleared == true) cleared++;
            if (mapRoom != run.Maps.CurrentRoomId)
            {
                selectedMapRoomId = -1;
                selectedMapDetail = null;
                hint.text = run.Message;
            }
            if (mapRoom != run.Maps.CurrentRoomId || cleared != mapCleared)
            {
                mapRoom = run.Maps.CurrentRoomId;
                mapCleared = cleared;
                RefreshMap(projection);
            }
            if (run.ActivePanel == ExpeditionPanelKind.None) openedPanel = ExpeditionPanelKind.None;
            else if (!openingPanel && openedPanel != run.ActivePanel) OpenActivePanelAsync(run.ActivePanel).Forget();
        }

        /// <summary>
        /// 只在附近目标身份变化时读取掉落副本，其余目标显示单局说明。
        /// </summary>
        private void RefreshTarget()
        {
            string id = run.SelectedInteractionId;
            if (selectedInteractionId != id)
            {
                selectedInteractionId = id;
                if (run.TryGetInteractionItem(out ProjectItem item))
                {
                    targetSummary.text = ProjectEquipmentText.LootSummary(item);
                    return;
                }
                targetSummary.text = string.Empty;
            }
            if (string.IsNullOrEmpty(run.SelectedLootId))
                targetSummary.text = string.IsNullOrEmpty(run.InteractionDescription) ? "附近暂无可交互目标" : run.InteractionDescription;
            else if (string.IsNullOrEmpty(targetSummary.text)) targetSummary.text = "目标信息暂不可用";
        }

        /// <summary>
        /// 显示或取消投影中一个地图格点的已知详情。
        /// </summary>
        /// <param name="roomId">玩家可见投影中的地图标识。</param>
        private void SelectMapRoom(int roomId)
        {
            if (mapProjection == null) return;
            if (selectedMapRoomId == roomId)
            {
                selectedMapRoomId = -1;
                selectedMapDetail = null;
            }
            else
            {
                bool found = false;
                foreach (LevelMinimapRoom room in mapProjection.Rooms)
                    if (room.RoomId == roomId)
                    {
                        selectedMapRoomId = roomId;
                        selectedMapDetail = MinimapRoomDetail(room);
                        found = true;
                        break;
                    }
                if (!found) return;
            }
            hint.text = string.IsNullOrEmpty(selectedMapDetail) ? run.Message : run.Message + "\n" + selectedMapDetail;
            RefreshMap(mapProjection);
        }

        /// <summary>
        /// 仅从玩家可见投影字段生成地图详情。
        /// </summary>
        /// <param name="room">已过滤的地图格点。</param>
        /// <returns>当前已知的地图说明。</returns>
        private static string MinimapRoomDetail(LevelMinimapRoom room)
        {
            if (!room.IsVisited || !room.Type.HasValue) return $"地图 {room.RoomId}：未知区域";
            string type = room.Type == LevelRoomType.Boss ? "首领房" : room.Type == LevelRoomType.Npc ? "商店" :
                room.Type == LevelRoomType.Loot ? "战利品房" : "战斗房";
            string state = room.IsCleared.HasValue ? room.IsCleared.Value ? " · 已清理" : " · 未清理" : string.Empty;
            return $"地图 {room.RoomId}：{type}{state}" + (room.IsCurrent ? " · 当前所在" : string.Empty);
        }

        /// <summary>
        /// 显示地图方格及已知连线，不绘制撤离点或路线。
        /// </summary>
        /// <param name="projection">已经过滤的信息投影。</param>
        private void RefreshMap(LevelMinimapProjection projection)
        {
            mapProjection = projection;
            if (selectedMapRoomId >= 0)
            {
                selectedMapDetail = null;
                foreach (LevelMinimapRoom room in projection.Rooms)
                    if (room.RoomId == selectedMapRoomId)
                    {
                        selectedMapDetail = MinimapRoomDetail(room);
                        break;
                    }
                if (selectedMapDetail == null) selectedMapRoomId = -1;
                hint.text = string.IsNullOrEmpty(selectedMapDetail) ? run.Message : run.Message + "\n" + selectedMapDetail;
            }
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
                    room.RoomId == selectedMapRoomId ? new Color(0.7f, 0.55f, 0.2f) :
                    room.IsCurrent ? new Color(0.1f, 0.7f, 0.8f) : new Color(0.2f, 0.3f, 0.4f));
                Vector2 center = positions[room.RoomId];
                Place(cell, center - new Vector2(0.06f, 0.09f), center + new Vector2(0.06f, 0.09f));
                string caption = !room.IsVisited ? "?" : room.Type == LevelRoomType.Boss ? "王" : room.Type == LevelRoomType.Npc ? "商" :
                    room.Type == LevelRoomType.Loot ? "箱" : room.IsCleared == true ? "✓" : "战";
                var label = ProjectUiFactory.Label(cell, caption, 18, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Stretch(label.rectTransform, 0);
                int roomId = room.RoomId;
                UnityEngine.UI.Button button = cell.gameObject.AddComponent<UnityEngine.UI.Button>();
                button.targetGraphic = cell.GetComponent<UnityEngine.UI.Image>();
                button.onClick.AddListener(() => SelectMapRoom(roomId));
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
        /// 请求打开本局强化总览弹窗。
        /// </summary>
        private void OpenRogueOverview()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.TryOpenRogueOverview() == true)
                run.SetTouchMove(Vector2.zero);
        }

        /// <summary>
        /// 打开暂停菜单并清空摇杆输入。
        /// </summary>
        private void OpenPause()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.TryOpenPause() == true)
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
                kind == ExpeditionPanelKind.Merchant ? ProjectServices.ExpeditionMerchantWindow :
                kind == ExpeditionPanelKind.Pause ? ProjectServices.PauseWindow : ProjectServices.RogueOverviewWindow;
            try
            {
                if (kind == ExpeditionPanelKind.ExtractionConfirmation)
                {
                    openedPanel = kind;
                    var answer = await ui.ConfirmAsync("确认撤离？",
                        "撤离后将结束本局，带回背包和安全箱中的全部物品，并获得撤离经验。是否现在撤离？",
                        "确认撤离", "继续探索", scope.Token).SuppressCancellationThrow();
                    if (answer.IsCanceled || !scope.IsActive || !ReferenceEquals(run, current) || current.ActivePanel != kind) return;
                    if (answer.Result && current.TryConfirmExtraction()) return;
                    current.ClosePanel(kind);
                    return;
                }
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
                if (opened.State != GameWindowState.Opened || opened.Root == null)
                {
                    current.ClosePanel(kind);
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
                if (scope.IsActive && ReferenceEquals(run, current) && current.ActivePanel != ExpeditionPanelKind.None &&
                    openedPanel != current.ActivePanel) Refresh();
            }
        }

        /// <summary>
        /// 在可操作时切换附近目标。
        /// </summary>
        private void CycleInteraction()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing && run?.CanAcceptPlayerInput == true && run.InteractionCount > 1)
                run.CycleInteraction();
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
