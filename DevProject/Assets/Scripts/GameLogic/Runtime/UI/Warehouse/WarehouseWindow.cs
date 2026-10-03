using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Warehouse
{
    /// <summary>
    /// 局外仓库窗口，展示真实物品并提交装备、锁定和出售事务。
    /// </summary>
    public sealed class WarehouseWindow : GameWindow
    {
        private RectTransform list;
        private RectTransform detailsContent;
        private UnityEngine.UI.ScrollRect detailsScroll;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Text selectionSummary;
        private UnityEngine.UI.Button equipButton;
        private UnityEngine.UI.Button sellButton;
        private UnityEngine.UI.Button lockButton;
        private UnityEngine.UI.Button batchButton;
        private UnityEngine.UI.Button retryButton;
        private UnityEngine.UI.Button sortButton;
        private UnityEngine.UI.Button selectButton;
        private ProjectSession session;
        private string selectedItemId;
        private readonly HashSet<string> selectedIds = new();
        private int filter;
        private ProjectWarehouseSort sort;
        private bool multiSelect;
        private bool operating;

        /// <summary>
        /// 创建安全区内的仓库布局。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "WarehouseContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 10f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 68f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "仓库", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;

            RectTransform filters = ProjectUiFactory.Horizontal(layout, "Filters", 8f);
            ProjectUiFactory.Height(filters, 64f);
            string[] names = { "全部", "武器", "装备", "物品" };
            for (int index = 0; index < names.Length; index++)
            {
                int chosen = index;
                ProjectUiFactory.ActionButton(filters, names[index], () => SelectFilter(chosen));
            }
            RectTransform tools = ProjectUiFactory.Horizontal(layout, "Tools", 8f);
            ProjectUiFactory.Height(tools, 62f);
            sortButton = ProjectUiFactory.ActionButton(tools, "排序：默认", CycleSort);
            selectButton = ProjectUiFactory.ActionButton(tools, "多选", ToggleMultiSelect);

            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "Items", out list);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 100f;
            detailsScroll = ProjectUiFactory.Scroll(layout, "ItemDetails", out detailsContent);
            UnityEngine.UI.LayoutElement detailsSize = detailsScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailsSize.flexibleHeight = 1.1f;
            detailsSize.minHeight = 130f;
            selectionSummary = ProjectUiFactory.Label(layout, string.Empty, 21, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(selectionSummary.rectTransform, 42f);
            RectTransform actions = ProjectUiFactory.Horizontal(layout, "Actions", 10f);
            ProjectUiFactory.Height(actions, 76f);
            equipButton = ProjectUiFactory.ActionButton(actions, "装备", () => EquipAsync().Forget());
            lockButton = ProjectUiFactory.ActionButton(actions, "锁定", () => SetLockedAsync().Forget());
            sellButton = ProjectUiFactory.ActionButton(actions, "出售", () => SellAsync().Forget());
            RectTransform batchActions = ProjectUiFactory.Horizontal(layout, "BatchActions", 10f);
            ProjectUiFactory.Height(batchActions, 68f);
            batchButton = ProjectUiFactory.ActionButton(batchActions, "出售已选", () => SellSelectedAsync().Forget());
            retryButton = ProjectUiFactory.ActionButton(batchActions, "重试保存", () => RetryAsync().Forget());
            retryButton.gameObject.SetActive(false);
            feedback = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(feedback.rectTransform, 46f);
        }

        /// <summary>
        /// 监听仓库数据变化并在关闭时解除。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            if (session == null)
            {
                feedback.text = "项目数据尚未就绪";
                equipButton.interactable = false;
                lockButton.interactable = false;
                sellButton.interactable = false;
                batchButton.interactable = false;
                return;
            }
            session.Changed += Refresh;
            OpenScope.Defer(() => session.Changed -= Refresh);
        }

        /// <summary>
        /// 显示当前库存。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 按筛选和排序重建网格，保留明确选中的实例身份。
        /// </summary>
        private void Refresh()
        {
            if (session == null) return;
            IReadOnlyList<ProjectItem> warehouse = session.Warehouse;
            var sellable = new HashSet<string>();
            foreach (ProjectItem item in warehouse)
                if (!item.Equipped && !item.Locked && item.SellGold > 0) sellable.Add(item.Id);
            selectedIds.RemoveWhere(id => !sellable.Contains(id));
            ProjectUiFactory.Clear(list);
            int count = 0;
            RectTransform row = null;
            bool selectedStillExists = false;
            foreach (ProjectItem item in ProjectWarehouseRules.Sort(warehouse, sort))
            {
                if (!Matches(item)) continue;
                if (item.Id == selectedItemId) selectedStillExists = true;
                if (count % 2 == 0)
                {
                    row = ProjectUiFactory.Horizontal(list, "Row", 8f);
                    ProjectUiFactory.Height(row, 74f);
                }
                string id = item.Id;
                string caption = (selectedIds.Contains(id) ? "☑ " : id == selectedItemId ? "● " : string.Empty) +
                    ProjectEquipmentText.Title(item) + (item.Equipped ? "  已装备" : string.Empty) +
                    (item.Locked ? "  已锁定" : string.Empty);
                UnityEngine.UI.Button card = ProjectUiFactory.ActionButton(row, caption, () => SelectItem(id));
                UnityEngine.UI.Text cardText = card.GetComponentInChildren<UnityEngine.UI.Text>();
                cardText.fontSize = 20;
                cardText.color = ProjectEquipmentText.QualityColor(item);
                count++;
            }
            if (count % 2 != 0)
            {
                RectTransform spacer = ProjectUiFactory.Panel(row, "EmptyCell", Color.clear);
                spacer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            }
            if (!selectedStillExists) selectedItemId = null;
            if (count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(list, "此分类暂无物品", 23, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Height(empty.rectTransform, 88f);
            }
            ProjectItem selected = FindSelected(warehouse);
            RefreshDetails(selected);
            equipButton.GetComponentInChildren<UnityEngine.UI.Text>().text = selected?.Equipped == true ? "卸下" : "装备";
            lockButton.GetComponentInChildren<UnityEngine.UI.Text>().text = selected?.Locked == true ? "解锁" : "锁定";
            equipButton.interactable = selected != null && selected.Type != ProjectItemType.Item &&
                (selected.Equipped || session.Level >= selected.RequiredLevel) && !session.IsBusy && !operating;
            lockButton.interactable = selected != null && !session.IsBusy && !operating;
            sellButton.interactable = selected != null && !selected.Equipped && !selected.Locked &&
                selected.SellGold > 0 && !session.IsBusy && !operating;
            int selectedCount = 0;
            long selectedGold = 0;
            foreach (ProjectItem item in warehouse)
                if (selectedIds.Contains(item.Id)) { selectedCount++; selectedGold += item.SellGold; }
            selectionSummary.text = multiSelect ? $"已选 {selectedCount} 件 · 合计 {selectedGold} 金币（仅出售所选）" : "点击多选后选择要批量出售的物品";
            batchButton.interactable = multiSelect && selectedCount > 0 && !session.IsBusy && !operating;
            retryButton.gameObject.SetActive(session.HasPendingSave);
            retryButton.interactable = session.HasPendingSave && !operating;
            selectButton.GetComponentInChildren<UnityEngine.UI.Text>().text = multiSelect ? "结束多选" : "多选";
            string[] sortNames = { "默认", "品质", "等级", "出售价格" };
            sortButton.GetComponentInChildren<UnityEngine.UI.Text>().text = "排序：" + sortNames[(int)sort];
            if (session.HasPendingSave) feedback.text = "仓库操作尚未保存，请重试原保存。";
        }

        /// <summary>
        /// 切换仓库分类。
        /// </summary>
        /// <param name="value">分类索引。</param>
        private void SelectFilter(int value)
        {
            filter = value;
            selectedItemId = null;
            Refresh();
        }

        /// <summary>
        /// 按默认、品质、等级和出售价格循环排序。
        /// </summary>
        private void CycleSort()
        {
            sort = (ProjectWarehouseSort)(((int)sort + 1) % 4);
            Refresh();
        }

        /// <summary>
        /// 开关明确的多选模式并清除上次选择。
        /// </summary>
        private void ToggleMultiSelect()
        {
            multiSelect = !multiSelect;
            selectedIds.Clear();
            Refresh();
        }

        /// <summary>
        /// 选择物品预览；多选模式还会切换该实例的出售标记。
        /// </summary>
        /// <param name="id">物品实例身份。</param>
        private void SelectItem(string id)
        {
            selectedItemId = id;
            if (multiSelect)
            {
                ProjectItem item = FindSelected(session.Warehouse);
                if (item == null || item.Equipped || item.Locked || item.SellGold <= 0)
                    feedback.text = "只能选择未穿戴、未锁定且可出售的物品。";
                else if (!selectedIds.Add(id)) selectedIds.Remove(id);
            }
            Refresh();
        }

        /// <summary>
        /// 查找选中的真实物品快照。
        /// </summary>
        /// <param name="warehouse">当前仓库快照。</param>
        /// <returns>找到的物品；没有选择时返回空。</returns>
        private ProjectItem FindSelected(IReadOnlyList<ProjectItem> warehouse)
        {
            foreach (ProjectItem item in warehouse)
                if (item.Id == selectedItemId) return item;
            return null;
        }

        /// <summary>
        /// 判断物品是否属于当前筛选。
        /// </summary>
        /// <param name="item">待筛选物品。</param>
        /// <returns>是否显示。</returns>
        private bool Matches(ProjectItem item) => filter == 0 || filter == 1 && item.Type == ProjectItemType.Weapon ||
            filter == 2 && ProjectEquipmentRules.IsArmor(item.Type) || filter == 3 && item.Type == ProjectItemType.Item;

        /// <summary>
        /// 在可滚动区域显示选中物品的属性和操作状态。
        /// </summary>
        /// <param name="item">当前选中的物品。</param>
        private void RefreshDetails(ProjectItem item)
        {
            ProjectUiFactory.Clear(detailsContent);
            if (item == null)
            {
                UnityEngine.UI.Text prompt = ProjectUiFactory.Label(detailsContent, "选择物品查看详情", 23, TextAnchor.MiddleLeft, Color.white);
                ProjectUiFactory.Height(prompt.rectTransform, 64f);
                return;
            }
            UnityEngine.UI.Text heading = ProjectUiFactory.Label(detailsContent, ProjectEquipmentText.Title(item), 27,
                TextAnchor.MiddleLeft, ProjectEquipmentText.QualityColor(item));
            ProjectUiFactory.Height(heading.rectTransform, 62f);
            string value = ProjectEquipmentText.Details(item);
            UnityEngine.UI.Text attributes = ProjectUiFactory.Label(detailsContent, value, 22, TextAnchor.UpperLeft, Color.white);
            ProjectUiFactory.Height(attributes.rectTransform, ProjectEquipmentText.PreferredHeight(value));
            string status = item.Locked ? "已锁定：仅防出售，仍可穿戴；死亡/放弃仍会丢失" : "未锁定";
            UnityEngine.UI.Text sale = ProjectUiFactory.Label(detailsContent, $"出售可获 {item.SellGold} 金币 · {status}", 21,
                TextAnchor.MiddleLeft, new Color(0.8f, 0.9f, 0.96f));
            ProjectUiFactory.Height(sale.rectTransform, 74f);
            detailsScroll.verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// 提交装备事务并展示保存结果。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid EquipAsync()
        {
            if (session == null || session.IsBusy || operating || !equipButton.interactable) return;
            string id = selectedItemId;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.EquipAsync(id);
            Finish(result);
        }

        /// <summary>
        /// 保存选中物品的锁定或解锁状态。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid SetLockedAsync()
        {
            if (session == null || session.IsBusy || operating || !lockButton.interactable) return;
            ProjectItem item = FindSelected(session.Warehouse);
            if (item == null) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.SetItemLockedAsync(item.Id, !item.Locked);
            Finish(result);
        }

        /// <summary>
        /// 单件高品质物品先确认，再提交出售。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid SellAsync()
        {
            if (session == null || session.IsBusy || operating || !sellButton.interactable) return;
            ProjectItem item = FindSelected(session.Warehouse);
            if (item == null) return;
            operating = true;
            Refresh();
            if (item.Quality >= ProjectItemQuality.Epic)
            {
                IUIModule ui = GameEntry.Get<IUIModule>();
                LifetimeScope scope = OpenScope;
                if (ui == null)
                {
                    FinishCancelled();
                    return;
                }
                var answer = await ui.ConfirmAsync("确认出售", $"出售 {item.Name} 可获得 {item.SellGold} 金币。",
                    "出售", "取消", scope.Token).SuppressCancellationThrow();
                if (answer.IsCanceled || !scope.IsActive)
                {
                    operating = false;
                    return;
                }
                if (!answer.Result)
                {
                    FinishCancelled();
                    return;
                }
            }
            ProjectOperationResult result = await session.SellAsync(item.Id);
            Finish(result);
        }

        /// <summary>
        /// 显示明确选中件数和总价后确认整批出售。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid SellSelectedAsync()
        {
            if (session == null || session.IsBusy || operating || !batchButton.interactable) return;
            var ids = new List<string>(selectedIds);
            long total = 0;
            foreach (ProjectItem item in session.Warehouse)
                if (selectedIds.Contains(item.Id)) total += item.SellGold;
            operating = true;
            Refresh();
            IUIModule ui = GameEntry.Get<IUIModule>();
            LifetimeScope scope = OpenScope;
            if (ui == null)
            {
                FinishCancelled();
                return;
            }
            var answer = await ui.ConfirmAsync("确认批量出售", $"出售所选 {ids.Count} 件物品，共获得 {total} 金币。",
                "出售所选", "取消", scope.Token).SuppressCancellationThrow();
            if (answer.IsCanceled || !scope.IsActive)
            {
                operating = false;
                return;
            }
            if (!answer.Result)
            {
                FinishCancelled();
                return;
            }
            ProjectOperationResult result = await session.SellItemsAsync(ids);
            if (result.Success) selectedIds.Clear();
            Finish(result);
        }

        /// <summary>
        /// 只重试原保存候选，不重新执行出售和锁定。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (session == null || operating || !session.HasPendingSave) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.RetryPendingSaveAsync();
            Finish(result);
        }

        /// <summary>
        /// 在窗口仍打开时显示事务结果。
        /// </summary>
        /// <param name="result">保存后的操作结果。</param>
        private void Finish(ProjectOperationResult result)
        {
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 确认取消后恢复窗口交互。
        /// </summary>
        private void FinishCancelled()
        {
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            Refresh();
        }
    }
}
