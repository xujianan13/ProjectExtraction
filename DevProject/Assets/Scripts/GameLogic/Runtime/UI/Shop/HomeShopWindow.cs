using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Shop
{
    /// <summary>
    /// 局外金币商店，预览固定商品并在保存确认后更新余额和库存。
    /// </summary>
    public sealed class HomeShopWindow : GameWindow
    {
        private RectTransform offersContent;
        private RectTransform detailsContent;
        private UnityEngine.UI.Text wallet;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button buyButton;
        private UnityEngine.UI.Button retryButton;
        private UnityEngine.UI.Button warehouseButton;
        private UnityEngine.UI.Button closeButton;
        private ProjectSession session;
        private ProjectHomeShopOffer selected;
        private string selectedId;
        private int filter;
        private bool operating;

        /// <summary>
        /// 构建随安全区伸缩的商品列表、详情和交易入口。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "HomeShopContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 72f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "金币商店", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            closeButton = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            closeButton.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            wallet = ProjectUiFactory.Label(layout, string.Empty, 23, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.4f));
            ProjectUiFactory.Height(wallet.rectTransform, 52f);

            RectTransform filters = ProjectUiFactory.Horizontal(layout, "Filters", 8f);
            ProjectUiFactory.Height(filters, 64f);
            string[] names = { "全部", "武器", "装备", "物品" };
            for (int index = 0; index < names.Length; index++)
            {
                int chosen = index;
                ProjectUiFactory.ActionButton(filters, names[index], () => SelectFilter(chosen));
            }
            UnityEngine.UI.ScrollRect offers = ProjectUiFactory.Scroll(layout, "Offers", out offersContent);
            UnityEngine.UI.LayoutElement offersSize = offers.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            offersSize.flexibleHeight = 1f;
            offersSize.minHeight = 100f;
            UnityEngine.UI.ScrollRect details = ProjectUiFactory.Scroll(layout, "OfferDetails", out detailsContent);
            UnityEngine.UI.LayoutElement detailsSize = details.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailsSize.flexibleHeight = 1.1f;
            detailsSize.minHeight = 130f;
            feedback = ProjectUiFactory.Label(layout, string.Empty, 21, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(feedback.rectTransform, 60f);
            RectTransform actions = ProjectUiFactory.Horizontal(layout, "Actions", 12f);
            ProjectUiFactory.Height(actions, 88f);
            buyButton = ProjectUiFactory.ActionButton(actions, "购买", () => BuyAsync().Forget());
            retryButton = ProjectUiFactory.ActionButton(actions, "重试保存", () => RetryAsync().Forget());
            warehouseButton = ProjectUiFactory.ActionButton(actions, "前往仓库", () => OpenWarehouseAsync().Forget());
            retryButton.gameObject.SetActive(false);
        }

        /// <summary>
        /// 订阅会话变化，关闭窗口时解除订阅。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            if (session == null) return;
            session.Changed += Refresh;
            OpenScope.Defer(() => session.Changed -= Refresh);
        }

        /// <summary>
        /// 显示最新商品、余额和交易状态。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 用独立报价快照刷新列表，保留玩家选择和滚动位置。
        /// </summary>
        private void Refresh()
        {
            buyButton.interactable = false;
            warehouseButton.interactable = session != null && !session.IsBusy && !operating;
            closeButton.interactable = !operating;
            retryButton.gameObject.SetActive(session?.HasPendingSave == true);
            retryButton.interactable = session?.HasPendingSave == true && !operating;
            if (session == null)
            {
                feedback.text = "项目数据尚未就绪";
                return;
            }
            wallet.text = $"金币 {session.Gold}    等级 {session.Level}";
            var offers = session.HomeShopOffers;
            selected = null;
            ProjectHomeShopOffer first = null;
            ProjectHomeShopOffer firstPurchasable = null;
            foreach (ProjectHomeShopOffer offer in offers)
            {
                if (!Matches(offer.Item)) continue;
                if (first == null) first = offer;
                if (firstPurchasable == null && offer.CanPurchase) firstPurchasable = offer;
                if (offer.Id == selectedId) selected = offer;
            }
            selected ??= firstPurchasable ?? first;
            selectedId = selected?.Id;
            ProjectUiFactory.Clear(offersContent);
            foreach (ProjectHomeShopOffer offer in offers)
            {
                if (!Matches(offer.Item)) continue;
                string id = offer.Id;
                string stock = offer.StockRemaining < 0 ? "常备供货" : $"剩余 {offer.StockRemaining} 件";
                string caption = (id == selectedId ? "● " : string.Empty) + offer.Name +
                    $"\n{offer.PriceGold} 金币 · 等级 {offer.RequiredLevel} · {stock}";
                UnityEngine.UI.Button card = ProjectUiFactory.ActionButton(offersContent, caption, () => SelectOffer(id));
                ProjectUiFactory.Height((RectTransform)card.transform, 98f);
                UnityEngine.UI.Text cardText = card.GetComponentInChildren<UnityEngine.UI.Text>();
                cardText.fontSize = 22;
                cardText.color = ProjectEquipmentText.QualityColor(offer.Item);
            }
            if (first == null) AddLabel(offersContent, "此分类暂无商品", 23, Color.white, 64f);
            RefreshDetails();
            buyButton.interactable = selected?.CanPurchase == true && !session.IsBusy && !operating;
            if (session.HasPendingSave) feedback.text = "购买尚未保存，请重试保存后继续。";
        }

        /// <summary>
        /// 显示本次商品的实际属性、售价和购买限制。
        /// </summary>
        private void RefreshDetails()
        {
            ProjectUiFactory.Clear(detailsContent);
            if (selected == null)
            {
                AddLabel(detailsContent, "选择商品查看详情", 23, Color.white, 64f);
                return;
            }
            AddLabel(detailsContent, ProjectEquipmentText.Title(selected.Item), 27,
                ProjectEquipmentText.QualityColor(selected.Item), 62f);
            string attributes = ProjectEquipmentText.Details(selected.Item);
            AddLabel(detailsContent, attributes, 22, Color.white, ProjectEquipmentText.PreferredHeight(attributes));
            string limit = string.IsNullOrEmpty(selected.UnavailableReason) ? "购买后放入仓库，可前往仓库装备。" : selected.UnavailableReason;
            AddLabel(detailsContent, $"价格 {selected.PriceGold} 金币\n{limit}", 21, new Color(0.85f, 0.92f, 0.96f), 90f);
        }

        /// <summary>
        /// 创建具有明确高度的详情文本。
        /// </summary>
        /// <param name="parent">文本父节点。</param>
        /// <param name="value">显示内容。</param>
        /// <param name="size">字体大小。</param>
        /// <param name="color">字体颜色。</param>
        /// <param name="height">布局高度。</param>
        private static void AddLabel(RectTransform parent, string value, int size, Color color, float height)
        {
            UnityEngine.UI.Text label = ProjectUiFactory.Label(parent, value, size, TextAnchor.UpperLeft, color);
            ProjectUiFactory.Height(label.rectTransform, height);
        }

        /// <summary>
        /// 按物品类型筛选商品。
        /// </summary>
        /// <param name="item">商品对应的物品快照。</param>
        /// <returns>商品属于当前分类时为真。</returns>
        private bool Matches(ProjectItem item) => item != null && (filter == 0 || filter == 1 && item.Type == ProjectItemType.Weapon ||
            filter == 2 && ProjectEquipmentRules.IsArmor(item.Type) || filter == 3 && item.Type == ProjectItemType.Item);

        /// <summary>
        /// 切换商品分类并选择该分类中的第一件可购商品。
        /// </summary>
        /// <param name="value">分类索引。</param>
        private void SelectFilter(int value)
        {
            filter = value;
            selectedId = null;
            Refresh();
        }

        /// <summary>
        /// 切换正在预览的商品。
        /// </summary>
        /// <param name="id">商品配置标识。</param>
        private void SelectOffer(string id)
        {
            selectedId = id;
            Refresh();
        }

        /// <summary>
        /// 使用点击前的报价修订号提交单件购买。
        /// </summary>
        /// <returns>等待保存确认的窗口操作。</returns>
        private async UniTaskVoid BuyAsync()
        {
            if (session == null || selected == null || operating || !buyButton.interactable) return;
            string id = selected.Id;
            long revision = selected.PurchaseRevision;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.BuyHomeShopItemAsync(id, revision);
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 重试原购买候选，不重新扣款或创建物品。
        /// </summary>
        /// <returns>等待原保存完成的窗口操作。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (session == null || operating || !session.HasPendingSave) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.RetryPendingSaveAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 关闭商店后打开仓库，避免叠加多个局外模态窗口。
        /// </summary>
        /// <returns>导航完成的窗口操作。</returns>
        private async UniTaskVoid OpenWarehouseAsync()
        {
            if (session == null || session.IsBusy || operating) return;
            IUIModule ui = GameEntry.Get<IUIModule>();
            if (ui == null) return;
            await CloseSelfAsync();
            if (ProjectServices.Coordinator?.Phase == ProjectRunPhase.HomeReady && !session.IsBusy)
                await ui.OpenWindowAsync(2101);
        }
    }
}
