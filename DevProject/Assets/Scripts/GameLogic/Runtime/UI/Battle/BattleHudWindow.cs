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
            Place(header, new Vector2(0.04f, 0.83f), new Vector2(0.64f, 0.97f));
            status = ProjectUiFactory.Label(header, "", 27, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(status.rectTransform, 80);
            hint = ProjectUiFactory.Label(header, "", 22, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(hint.rectTransform, 75);
            map = ProjectUiFactory.Panel(content, "Minimap", new Color(0.04f, 0.07f, 0.1f, 0.7f));
            Place(map, new Vector2(0.67f, 0.79f), new Vector2(0.96f, 0.97f));
            RectTransform stick = ProjectUiFactory.Panel(content, "MoveStick", new Color(0.1f, 0.25f, 0.35f, 0.7f));
            Place(stick, new Vector2(0.05f, 0.04f), new Vector2(0.32f, 0.22f));
            joystick = stick.gameObject.AddComponent<ExpeditionJoystick>();
            var moveLabel = ProjectUiFactory.Label(stick, "移动", 27, TextAnchor.MiddleCenter, Color.white);
            ProjectUiFactory.Stretch(moveLabel.rectTransform, 0);
            var interact = ProjectUiFactory.ActionButton(content, "交互 / E", RequestInteraction);
            interactionLabel = interact.GetComponentInChildren<UnityEngine.UI.Text>();
            Place((RectTransform)interact.transform, new Vector2(0.69f, 0.07f), new Vector2(0.95f, 0.17f));
            var bagButton = ProjectUiFactory.ActionButton(content, "背包 / 安全箱", ToggleBag);
            Place((RectTransform)bagButton.transform, new Vector2(0.64f, 0.2f), new Vector2(0.95f, 0.28f));
            bag = ProjectUiFactory.Panel(content, "RunInventory", new Color(0.055f, 0.1f, 0.15f, 0.98f));
            Place(bag, new Vector2(0.06f, 0.3f), new Vector2(0.94f, 0.76f));
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
            joystick.Bind(run.SetTouchMove);
            run.Changed += Refresh;
            coordinator = ProjectServices.Coordinator;
            if (coordinator != null) coordinator.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                run.Changed -= Refresh;
                if (coordinator != null) coordinator.Changed -= Refresh;
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
            if (coordinator == null || coordinator.Phase != ProjectRunPhase.Playing) return;
            bag.gameObject.SetActive(!bag.gameObject.activeSelf);
            run?.SetTouchMove(Vector2.zero);
            if (bag.gameObject.activeSelf) RefreshBag();
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
                string label = (run.IsProtected(id) ? "[安全箱] " : "[背包] ") + item.Name + (item.Equipped ? " · 已穿戴" : "");
                var button = ProjectUiFactory.ActionButton(bagContent, label, () => { if (coordinator?.Phase != ProjectRunPhase.Playing) return; run.ToggleSafe(id); RefreshBag(); });
                button.interactable = coordinator != null && coordinator.Phase == ProjectRunPhase.Playing && run.IsRunning && !item.Equipped;
                ProjectUiFactory.Height((RectTransform)button.transform, 65);
            }
        }

        /// <summary>
        /// 仅在本局游玩阶段提交交互请求。
        /// </summary>
        private void RequestInteraction()
        {
            if (coordinator?.Phase == ProjectRunPhase.Playing) run?.RequestInteraction();
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
