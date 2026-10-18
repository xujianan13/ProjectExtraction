using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 关卡背包模态窗口，分开展示本关所得与固定出战装备。
    /// </summary>
    public sealed class ExpeditionInventoryWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform list;
        private RectTransform details;
        private UnityEngine.UI.Text message;
        private string selectedId;
        private int filter;

        /// <summary>
        /// 在安全区创建只读的双列物品列表、固定装备分类与滚动详情。
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
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "关卡背包", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            RectTransform filters = ProjectUiFactory.Horizontal(layout, "Filters", 6f);
            ProjectUiFactory.Height(filters, 48f);
            string[] names = { "全部", "武器", "防具", "物品", "出战装备" };
            for (int index = 0; index < names.Length; index++)
            {
                int selected = index;
                UnityEngine.UI.Button button = ProjectUiFactory.ActionButton(filters, names[index], () => SelectFilter(selected));
                UnityEngine.UI.Text label = button.GetComponentInChildren<UnityEngine.UI.Text>();
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 16;
                label.resizeTextMaxSize = 25;
            }
            UnityEngine.UI.ScrollRect itemScroll = ProjectUiFactory.Scroll(layout, "Items", out list);
            UnityEngine.UI.LayoutElement itemSize = itemScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            itemSize.flexibleHeight = 1.15f;
            itemSize.minHeight = 72f;
            UnityEngine.UI.ScrollRect detailScroll = ProjectUiFactory.Scroll(layout, "Details", out details);
            UnityEngine.UI.LayoutElement detailSize = detailScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailSize.flexibleHeight = 1f;
            detailSize.minHeight = 72f;
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
            message.text = filter == 4 ? "出战装备固定，本关中不能更换，结束本局后保留。" :
                "本关获得 " + run.Inventory.Count + " 件物品；带回主城后可装备。\n出战装备不在关卡背包中，本关中不能更换。";
            ProjectUiFactory.Clear(list);
            int count = 0;
            RectTransform row = null;
            ProjectItem selected = null;
            foreach (ProjectItem item in filter == 4 ? run.EntryEquipment : run.Inventory)
            {
                if (!Matches(item)) continue;
                if (item.Id == selectedId) selected = item;
                if (count % 2 == 0)
                {
                    row = ProjectUiFactory.Horizontal(list, "Row", 8f);
                    ProjectUiFactory.Height(row, 108f);
                }
                string id = item.Id;
                string caption = ProjectEquipmentText.Title(item) + (filter == 4 ? "\n固定出战装备" : string.Empty);
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
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(list, "此分类暂无物品", 23, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Height(empty.rectTransform, 78f);
            }
            if (selected == null) selectedId = null;
            RefreshDetails(selected);
        }

        /// <summary>
        /// 显示物品属性及固定出战装备的对照，关卡中不提供穿脱操作。
        /// </summary>
        /// <param name="item">当前选中物品。</param>
        private void RefreshDetails(ProjectItem item)
        {
            ProjectUiFactory.Clear(details);
            string text = item == null ? "选择物品查看属性和词条" : "选中物品\n" + ProjectEquipmentText.ColoredTitle(item) + "\n" +
                ProjectEquipmentText.Details(item);
            if (item != null && (item.Type == ProjectItemType.Weapon || ProjectEquipmentRules.IsArmor(item.Type)))
            {
                text += filter == 4 ? "\n\n固定出战装备，本关中不能更换。" :
                    "\n\n" + ProjectEquipmentText.EquippedDetails(item, run.EntryEquipment) + "\n\n带回主城后可装备，本关中不能更换出战装备。";
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
        private bool Matches(ProjectItem item) => filter == 0 || filter == 4 || filter == 1 && item.Type == ProjectItemType.Weapon ||
            filter == 2 && ProjectEquipmentRules.IsArmor(item.Type) ||
            filter == 3 && (item.Type == ProjectItemType.Item || item.Type == ProjectItemType.Collection);

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
