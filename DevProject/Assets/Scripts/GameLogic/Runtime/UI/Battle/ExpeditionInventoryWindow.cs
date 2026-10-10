using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 局内背包与安全箱模态窗口，集中展示装备操作和属性预览。
    /// </summary>
    public sealed class ExpeditionInventoryWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform list;
        private RectTransform details;
        private UnityEngine.UI.Text title;
        private UnityEngine.UI.Text message;
        private UnityEngine.UI.Button equip;
        private UnityEngine.UI.Button safe;
        private string selectedId;
        private int filter;
        private int region;

        /// <summary>
        /// 在安全区创建双列滚动物品列表、滚动详情和明确操作按钮。
        /// </summary>
        protected override void Init()
        {
            RectTransform safeArea = ProjectUiFactory.Fill(RootTransform, "InventorySafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safeArea, "InventoryPanel", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            Place(panel, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 7f);
            ProjectUiFactory.Stretch(layout, 12f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 8f);
            ProjectUiFactory.Height(header, 58f);
            title = ProjectUiFactory.Label(header, "局内背包", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            RectTransform regions = ProjectUiFactory.Horizontal(layout, "Regions", 8f);
            ProjectUiFactory.Height(regions, 48f);
            ProjectUiFactory.ActionButton(regions, "背包", () => SelectRegion(0));
            ProjectUiFactory.ActionButton(regions, "安全箱", () => SelectRegion(1));
            RectTransform filters = ProjectUiFactory.Horizontal(layout, "Filters", 6f);
            ProjectUiFactory.Height(filters, 48f);
            string[] names = { "全部", "武器", "装备", "物品" };
            for (int index = 0; index < names.Length; index++)
            {
                int selected = index;
                ProjectUiFactory.ActionButton(filters, names[index], () => SelectFilter(selected));
            }
            UnityEngine.UI.ScrollRect itemScroll = ProjectUiFactory.Scroll(layout, "Items", out list);
            UnityEngine.UI.LayoutElement itemSize = itemScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            itemSize.flexibleHeight = 1.15f;
            itemSize.minHeight = 72f;
            UnityEngine.UI.ScrollRect detailScroll = ProjectUiFactory.Scroll(layout, "Details", out details);
            UnityEngine.UI.LayoutElement detailSize = detailScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailSize.flexibleHeight = 1f;
            detailSize.minHeight = 72f;
            RectTransform actions = ProjectUiFactory.Horizontal(layout, "Actions", 8f);
            ProjectUiFactory.Height(actions, 66f);
            equip = ProjectUiFactory.ActionButton(actions, "装备", Equip);
            safe = ProjectUiFactory.ActionButton(actions, "存入安全箱", ToggleSafe);
            message = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(message.rectTransform, 64f);
        }

        /// <summary>
        /// 绑定本局面板状态，并在窗口结束时解除订阅与释放所属面板。
        /// </summary>
        /// <param name="args">含单局对象的窗口参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            run = args.UserData as ExpeditionRun;
            if (run == null) return;
            ExpeditionRun current = run;
            current.Changed += Refresh;
            OpenScope.Defer(() =>
            {
                current.Changed -= Refresh;
                current.ClosePanel(ExpeditionPanelKind.Inventory);
                if (ReferenceEquals(run, current)) run = null;
            });
        }

        /// <summary>
        /// 首次显示时填充背包快照。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 切换背包或安全箱区域。
        /// </summary>
        /// <param name="value">目标区域索引。</param>
        private void SelectRegion(int value)
        {
            region = value;
            selectedId = null;
            Refresh();
        }

        /// <summary>
        /// 切换当前物品分类。
        /// </summary>
        /// <param name="value">目标分类索引。</param>
        private void SelectFilter(int value)
        {
            filter = value;
            selectedId = null;
            Refresh();
        }

        /// <summary>
        /// 选择一个物品并刷新其详情。
        /// </summary>
        /// <param name="id">物品实例身份。</param>
        private void SelectItem(string id)
        {
            selectedId = id;
            Refresh();
        }

        /// <summary>
        /// 根据当前背包快照刷新双列卡片及详情。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (run.ActivePanel != ExpeditionPanelKind.Inventory || !run.IsRunning)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            title.text = region == 0 ? "局内背包" : "安全箱 " + run.SafeCount + " / " + run.SafeCapacity;
            message.text = run.Message;
            ProjectUiFactory.Clear(list);
            int count = 0;
            RectTransform row = null;
            ProjectItem selected = null;
            foreach (ProjectItem item in run.Inventory)
            {
                if (run.IsProtected(item.Id) != (region == 1) || !Matches(item)) continue;
                if (item.Id == selectedId) selected = item;
                if (count % 2 == 0)
                {
                    row = ProjectUiFactory.Horizontal(list, "Row", 8f);
                    ProjectUiFactory.Height(row, 108f);
                }
                string id = item.Id;
                string caption = ProjectEquipmentText.Title(item) + (item.Equipped ? "\n已穿戴" : "") +
                    (run.IsProtected(id) ? "\n安全箱" : "");
                UnityEngine.UI.Button card = ProjectUiFactory.ActionButton(row, caption, () => SelectItem(id));
                UnityEngine.UI.Text cardText = card.GetComponentInChildren<UnityEngine.UI.Text>();
                cardText.fontSize = 21;
                cardText.color = ProjectEquipmentText.QualityColor(item);
                count++;
            }
            if (count % 2 != 0)
            {
                RectTransform spacer = ProjectUiFactory.Panel(row, "EmptyCell", Color.clear);
                spacer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            }
            if (count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(list, "此区域暂无物品", 23, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Height(empty.rectTransform, 78f);
            }
            if (selected == null) selectedId = null;
            RefreshDetails(selected);
            equip.GetComponentInChildren<UnityEngine.UI.Text>().text = selected?.Equipped == true ? "卸下" : "装备";
            equip.interactable = run.CanManageInventory && selected != null && selected.Type != ProjectItemType.Item &&
                !run.IsProtected(selected.Id) && (selected.Equipped || run.PlayerLevel >= selected.RequiredLevel);
            safe.GetComponentInChildren<UnityEngine.UI.Text>().text = selected != null && run.IsProtected(selected.Id) ? "移出安全箱" : "存入安全箱";
            safe.interactable = run.CanManageInventory && selected != null && !selected.Equipped &&
                (run.IsProtected(selected.Id) || run.SafeCount < run.SafeCapacity);
        }

        /// <summary>
        /// 显示选中物品、同部位当前装备，以及穿脱后的角色属性差值。
        /// </summary>
        /// <param name="item">当前选中物品。</param>
        private void RefreshDetails(ProjectItem item)
        {
            ProjectUiFactory.Clear(details);
            string text = item == null ? "选择物品查看属性和词条" : "选中物品\n" + ProjectEquipmentText.ColoredTitle(item) + "\n" +
                ProjectEquipmentText.Details(item);
            if (item != null && item.Type != ProjectItemType.Item)
            {
                text += "\n\n" + ProjectEquipmentText.EquippedDetails(item, run.Inventory);
                string action = item.Equipped ? "卸下后" : item.RequiredLevel > run.PlayerLevel ? "满足等级后穿戴" : "穿戴后";
                if (run.IsProtected(item.Id)) text += "\n需先移出安全箱才能穿戴。";
                if (run.TryPreviewEquipment(item, out ProjectCharacterStats current, out ProjectCharacterStats preview))
                    text += "\n\n属性预览（当前 → " + action + "）\n" + ExpeditionEquipmentComparison.Format(current, preview);
                else text += "\n\n属性预览暂不可用。";
            }
            UnityEngine.UI.Text label = ProjectUiFactory.Label(details, text, 22, TextAnchor.UpperLeft, Color.white);
            label.supportRichText = true;
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 90f;
        }

        /// <summary>
        /// 判断物品是否匹配当前分类。
        /// </summary>
        /// <param name="item">待筛选物品。</param>
        /// <returns>物品属于当前分类则为真。</returns>
        private bool Matches(ProjectItem item) => filter == 0 || filter == 1 && item.Type == ProjectItemType.Weapon ||
            filter == 2 && ProjectEquipmentRules.IsArmor(item.Type) || filter == 3 && item.Type == ProjectItemType.Item;

        /// <summary>
        /// 提交当前物品的装备或卸下操作。
        /// </summary>
        private void Equip()
        {
            if (run?.CanManageInventory != true || selectedId == null) return;
            run.TryEquip(selectedId);
            Refresh();
        }

        /// <summary>
        /// 提交当前物品的安全箱存取操作。
        /// </summary>
        private void ToggleSafe()
        {
            if (run?.CanManageInventory != true || selectedId == null) return;
            run.ToggleSafe(selectedId);
            Refresh();
        }

        /// <summary>
        /// 使用相对锚点将弹窗约束在安全区内。
        /// </summary>
        /// <param name="rect">待布局矩形。</param>
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
