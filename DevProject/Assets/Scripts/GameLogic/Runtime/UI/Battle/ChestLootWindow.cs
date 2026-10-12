using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 展示本次宝箱已经放入背包的物品，关闭后继续探索。
    /// </summary>
    public sealed class ChestLootWindow : GameWindow
    {
        private ExpeditionRun run;
        private UnityEngine.UI.Text guidance;
        private UnityEngine.UI.ScrollRect itemScroll;
        private UnityEngine.UI.ScrollRect detailScroll;
        private RectTransform items;
        private RectTransform details;
        private string selectedId;

        /// <summary>
        /// 在安全区内创建收获说明、可选物品列表、滚动详情和继续探索按钮。
        /// </summary>
        protected override void Init()
        {
            RectTransform safeArea = ProjectUiFactory.Fill(RootTransform, "ChestLootSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safeArea, "ChestLootPanel", new Color(0.055f, 0.09f, 0.14f, 0.98f));
            panel.anchorMin = new Vector2(0.04f, 0.04f);
            panel.anchorMax = new Vector2(0.96f, 0.96f);
            panel.offsetMin = panel.offsetMax = Vector2.zero;
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 8f);
            ProjectUiFactory.Stretch(layout, 14f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(layout, "获得物品", 44, TextAnchor.MiddleCenter, Color.white);
            title.name = "Title";
            ProjectUiFactory.Height(title.rectTransform, 76f);
            guidance = ProjectUiFactory.Label(layout, string.Empty, 32, TextAnchor.MiddleLeft, new Color(0.82f, 0.91f, 0.95f));
            guidance.name = "Guidance";
            guidance.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 88f;
            itemScroll = ProjectUiFactory.Scroll(layout, "Items", out items);
            UnityEngine.UI.LayoutElement itemSize = itemScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            itemSize.flexibleHeight = 0.6f;
            itemSize.minHeight = 144f;
            detailScroll = ProjectUiFactory.Scroll(layout, "Details", out details);
            UnityEngine.UI.LayoutElement detailSize = detailScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailSize.flexibleHeight = 1.4f;
            detailSize.minHeight = 144f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(layout, "继续探索", () => CloseSelfAsync().Forget());
            close.name = "ContinueButton";
            close.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 32;
            ProjectUiFactory.Height((RectTransform)close.transform, 86f);
        }

        /// <summary>
        /// 绑定本次收获，关闭或销毁窗口时解除订阅并归还宝箱面板暂停。
        /// </summary>
        /// <param name="args">含当前单局对象的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            selectedId = null;
            itemScroll.verticalNormalizedPosition = detailScroll.verticalNormalizedPosition = 1f;
            if (run == null) return;
            ExpeditionRun current = run;
            current.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.ChestLoot);
                if (ReferenceEquals(run, current)) run = null;
            });
        }

        /// <summary>
        /// 首次显示或重新聚焦时刷新本次已经保存的收获。
        /// </summary>
        /// <param name="args">本次窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 选择本次获得的物品，并将对应详情滚动到顶部。
        /// </summary>
        /// <param name="id">本次获得的物品实例身份。</param>
        private void SelectItem(string id)
        {
            selectedId = id;
            Refresh();
            detailScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// 根据本次收获快照刷新列表，默认直接展示首件物品完整详情。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (!run.IsRunning || run.ActivePanel != ExpeditionPanelKind.ChestLoot)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            IReadOnlyList<ProjectItem> loot = run.ChestLootItems;
            ProjectItem selected = null;
            foreach (ProjectItem item in loot)
                if (item.Id == selectedId) selected = item;
            if (selected == null && loot.Count > 0) selected = loot[0];
            selectedId = selected?.Id;
            guidance.text = "本次获得 " + loot.Count + " 件物品，已放入背包。\n当前暂停中，点击物品查看详情。";
            float position = itemScroll.verticalNormalizedPosition;
            ProjectUiFactory.Clear(items);
            foreach (ProjectItem item in loot)
            {
                string id = item.Id;
                string caption = (id == selectedId ? "已选 · " : string.Empty) + ProjectEquipmentText.Title(item) +
                    "\n等级 " + item.RequiredLevel + " · 售价 " + item.SellGold + " 金币";
                UnityEngine.UI.Button card = ProjectUiFactory.ActionButton(items, caption, () => SelectItem(id));
                card.name = "ChestLootItem_" + id;
                ProjectUiFactory.Height((RectTransform)card.transform, 144f);
                card.targetGraphic.color = id == selectedId ? new Color(0.08f, 0.12f, 0.18f) : new Color(0.055f, 0.09f, 0.14f);
                UnityEngine.UI.Text cardText = card.GetComponentInChildren<UnityEngine.UI.Text>();
                cardText.fontSize = 32;
                cardText.color = ProjectEquipmentText.QualityColor(item);
                cardText.resizeTextForBestFit = true;
                cardText.resizeTextMinSize = 28;
                cardText.resizeTextMaxSize = 32;
            }
            itemScroll.verticalNormalizedPosition = position;
            ProjectUiFactory.Clear(details);
            if (selected == null)
            {
                AddDetail("DetailsText", "本次宝箱没有获得物品。", Color.white);
                return;
            }
            AddDetail("DetailTitle", ProjectEquipmentText.Title(selected), ProjectEquipmentText.QualityColor(selected));
            AddDetail("DetailsText", ProjectEquipmentText.Details(selected) + "\n\n售价 " + selected.SellGold + " 金币", Color.white);
        }

        /// <summary>
        /// 添加随文本换行和可用宽度计算高度的详情块。
        /// </summary>
        /// <param name="name">可供界面诊断查询的节点名称。</param>
        /// <param name="value">玩家可读详情。</param>
        /// <param name="color">文字颜色。</param>
        private void AddDetail(string name, string value, Color color)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(details, value, 32, TextAnchor.UpperLeft, color);
            label.name = name;
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 42f;
        }
    }
}
