using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Warehouse
{
    /// <summary>
    /// 局外仓库窗口，展示真实物品并提交装备及出售事务。
    /// </summary>
    public sealed class WarehouseWindow : GameWindow
    {
        private RectTransform list;
        private UnityEngine.UI.Text details;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button equipButton;
        private UnityEngine.UI.Button sellButton;
        private ProjectSession session;
        private string selectedItemId;
        private int filter;
        private bool operating;

        /// <summary>
        /// 创建安全区内的仓库布局。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "WarehouseContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 62f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "仓库", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;

            RectTransform filters = ProjectUiFactory.Horizontal(layout, "Filters", 8f);
            ProjectUiFactory.Height(filters, 56f);
            string[] names = { "全部", "武器", "装备", "道具" };
            for (int index = 0; index < names.Length; index++)
            {
                int chosen = index;
                ProjectUiFactory.ActionButton(filters, names[index], () => SelectFilter(chosen));
            }

            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "Items", out list);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 160f;
            details = ProjectUiFactory.Label(layout, "选择物品查看详情", 23, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(details.rectTransform, 115f);
            RectTransform actions = ProjectUiFactory.Horizontal(layout, "Actions", 12f);
            ProjectUiFactory.Height(actions, 66f);
            equipButton = ProjectUiFactory.ActionButton(actions, "装备", () => EquipAsync().Forget());
            sellButton = ProjectUiFactory.ActionButton(actions, "出售", () => SellAsync().Forget());
            feedback = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(feedback.rectTransform, 55f);
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
                sellButton.interactable = false;
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
        /// 按筛选类型重建网格和详情。
        /// </summary>
        private void Refresh()
        {
            if (session == null) return;
            ProjectUiFactory.Clear(list);
            int count = 0;
            RectTransform row = null;
            bool selectedStillExists = false;
            foreach (ProjectItem item in session.Warehouse)
            {
                if (item.Id == selectedItemId) selectedStillExists = true;
                if (!Matches(item)) continue;
                if (count % 2 == 0)
                {
                    row = ProjectUiFactory.Horizontal(list, "Row", 8f);
                    ProjectUiFactory.Height(row, 74f);
                }
                string id = item.Id;
                string caption = item.Name + (item.Equipped ? "  已装备" : string.Empty);
                UnityEngine.UI.Button card = ProjectUiFactory.ActionButton(row, caption, () => SelectItem(id));
                card.GetComponentInChildren<UnityEngine.UI.Text>().fontSize = 21;
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
            ProjectItem selected = FindSelected();
            string attributes = selected?.Type == ProjectItemType.Weapon ? $"伤害 {selected.Damage:0.#}  范围 {selected.Range:0.#}  需求等级 {selected.RequiredLevel}" :
                selected?.Type == ProjectItemType.Equipment ? $"生命 +{selected.BonusHealth}  需求等级 {selected.RequiredLevel}" : "普通物品";
            details.text = selected == null ? "选择物品查看详情" :
                $"{selected.Name}  ·  {TypeName(selected.Type)}\n{attributes}\n出售可获 {selected.SellGold} 金币";
            equipButton.GetComponentInChildren<UnityEngine.UI.Text>().text = selected?.Equipped == true ? "卸下" : "装备";
            equipButton.interactable = selected != null && selected.Type != ProjectItemType.Item &&
                (selected.Equipped || session.Level >= selected.RequiredLevel) && !session.IsBusy && !operating;
            sellButton.interactable = selected != null && !selected.Equipped && selected.SellGold > 0 && !session.IsBusy && !operating;
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
        /// 选择当前物品。
        /// </summary>
        /// <param name="id">物品实例标识。</param>
        private void SelectItem(string id)
        {
            selectedItemId = id;
            Refresh();
        }

        /// <summary>
        /// 查找选中的真实物品。
        /// </summary>
        /// <returns>找到的物品；没有选择时返回空。</returns>
        private ProjectItem FindSelected()
        {
            foreach (ProjectItem item in session.Warehouse)
                if (item.Id == selectedItemId) return item;
            return null;
        }

        /// <summary>
        /// 判断物品是否属于当前筛选。
        /// </summary>
        /// <param name="item">待筛选物品。</param>
        /// <returns>是否显示。</returns>
        private bool Matches(ProjectItem item) => filter == 0 || filter == 1 && item.Type == ProjectItemType.Weapon ||
            filter == 2 && item.Type == ProjectItemType.Equipment || filter == 3 && item.Type == ProjectItemType.Item;

        /// <summary>
        /// 返回物品类别名称。
        /// </summary>
        /// <param name="type">物品类别。</param>
        /// <returns>显示名称。</returns>
        private static string TypeName(ProjectItemType type) => type == ProjectItemType.Weapon ? "武器" :
            type == ProjectItemType.Equipment ? "装备" : "道具";

        /// <summary>
        /// 提交装备事务并展示保存结果。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid EquipAsync()
        {
            if (session == null || session.IsBusy || operating || !equipButton.interactable) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.EquipAsync(selectedItemId);
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 提交出售事务并展示保存结果。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid SellAsync()
        {
            if (session == null || session.IsBusy || operating || !sellButton.interactable) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.SellAsync(selectedItemId);
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            feedback.text = result.Message;
            Refresh();
        }
    }
}
