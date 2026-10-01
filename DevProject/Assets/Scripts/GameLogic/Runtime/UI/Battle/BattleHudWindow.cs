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
    /// 局内生命、交互、探索小地图和安全箱界面。
    /// </summary>
    public sealed class BattleHudWindow : GameWindow
    {
        private ExpeditionRun run;
        private ProjectRunCoordinator coordinator;
        private UnityEngine.UI.Text interactionLabel;
        private UnityEngine.UI.Text status;
        private UnityEngine.UI.Text hint;
        private RectTransform map;
        private RectTransform bag;
        private UnityEngine.UI.Button resumeChoice;
        private UnityEngine.UI.Button interactButton;
        private RectTransform bagContent;
        private ExpeditionJoystick joystick;
        private int mapRoom = -1;
        private int mapCleared = -1;
        private int bagCount = -1;
        private int safeCount = -1;

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
            var bagButton = ProjectUiFactory.ActionButton(actions, "背包 / 安全箱", ToggleBag);
            bagButton.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 32;
            ProjectUiFactory.Height((RectTransform)bagButton.transform, 100f);
            resumeChoice = ProjectUiFactory.ActionButton(actions, "继续选择强化", OpenPendingChoice);
            resumeChoice.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 30;
            ProjectUiFactory.Height((RectTransform)resumeChoice.transform, 100f);
            resumeChoice.gameObject.SetActive(false);
            bag = ProjectUiFactory.Panel(content, "RunInventory", new Color(0.055f, 0.1f, 0.15f, 0.98f));
            content.gameObject.AddComponent<BattleHudLayout>().Configure(header, map, stick, actions, bag);
            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(bag, "Items", out bagContent);
            ProjectUiFactory.Stretch((RectTransform)scroll.transform, 12);
            bag.gameObject.SetActive(false);
        }

        /// <summary>
        /// 绑定当前单局和保存状态。
        /// </summary>
        /// <param name="args">包含单局实例的参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            joystick.Bind(run.SetTouchMove, () => run.CanAcceptPlayerInput && !bag.gameObject.activeSelf);
            run.Changed += Refresh;
            coordinator = ProjectServices.Coordinator;
            if (coordinator != null) coordinator.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                run.Changed -= Refresh;
                if (coordinator != null) coordinator.Changed -= Refresh;
                joystick.ResetInput();
                joystick.Bind(null);
                run.SetTouchMove(Vector2.zero);
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
            bool acceptsInput = run.CanAcceptPlayerInput && !bag.gameObject.activeSelf;
            joystick.enabled = acceptsInput;
            if (!acceptsInput) joystick.ResetInput();
            interactButton.interactable = acceptsInput;
            if (choosing)
            {
                bag.gameObject.SetActive(false);
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
            if (bag.gameObject.activeSelf && (bagCount != run.Inventory.Count || safeCount != run.SafeCount)) RefreshBag();
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
        /// 展开或关闭本局物品与安全箱列表。
        /// </summary>
        private void ToggleBag()
        {
            if (coordinator == null || coordinator.Phase != ProjectRunPhase.Playing || run == null || run.IsChoosingRogue) return;
            bag.gameObject.SetActive(!bag.gameObject.activeSelf);
            run?.SetTouchMove(Vector2.zero);
            if (bag.gameObject.activeSelf) RefreshBag();
            Refresh();
        }

        /// <summary>
        /// 每个物品按实例展示保护状态，点击未穿戴物品切换安全箱。
        /// </summary>
        private void RefreshBag()
        {
            if (run == null) return;
            ProjectUiFactory.Clear(bagContent);
            bagCount = run.Inventory.Count;
            safeCount = run.SafeCount;
            var title = ProjectUiFactory.Label(bagContent, "安全箱 " + safeCount + " / " + PrototypeRules.SafeSlots +
                " · 点击战利品切换保护", 24, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(title.rectTransform, 55);
            foreach (ProjectItem item in run.Inventory)
            {
                string id = item.Id;
                string label = (run.IsProtected(id) ? "[安全箱] " : "[背包] ") + ProjectEquipmentText.ColoredTitle(item) +
                    (item.Equipped ? " · 已穿戴" : string.Empty) + "\n" + ProjectEquipmentText.Details(item);
                var button = ProjectUiFactory.ActionButton(bagContent, label, () =>
                {
                    if (coordinator?.Phase != ProjectRunPhase.Playing || run.IsChoosingRogue) return;
                    run.ToggleSafe(id);
                    RefreshBag();
                });
                button.GetComponentInChildren<UnityEngine.UI.Text>().supportRichText = true;
                button.interactable = coordinator != null && coordinator.Phase == ProjectRunPhase.Playing && run.IsRunning && !run.IsChoosingRogue && !item.Equipped;
                ProjectUiFactory.Height((RectTransform)button.transform, ProjectEquipmentText.PreferredHeight(label, 120f));
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
