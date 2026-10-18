using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 局内 NPC 商店模态窗口，展示冻结商品和逐件购买状态。
    /// </summary>
    public sealed class ExpeditionMerchantWindow : GameWindow
    {
        private ExpeditionRun run;
        private RectTransform offers;
        private RectTransform details;
        private UnityEngine.UI.Text balance;
        private UnityEngine.UI.Text message;
        private UnityEngine.UI.Button purchase;
        private string selectedId;

        /// <summary>
        /// 在安全区创建可滚动商品列表、详情和购买确认入口。
        /// </summary>
        protected override void Init()
        {
            RectTransform safeArea = ProjectUiFactory.Fill(RootTransform, "MerchantSafeArea", Color.clear);
            RectTransform panel = ProjectUiFactory.Panel(safeArea, "MerchantPanel", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            Place(panel, new Vector2(0.04f, 0.04f), new Vector2(0.96f, 0.96f));
            RectTransform layout = ProjectUiFactory.Vertical(panel, "Layout", 8f);
            ProjectUiFactory.Stretch(layout, 12f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 8f);
            ProjectUiFactory.Height(header, 58f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "商店", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;
            balance = ProjectUiFactory.Label(layout, string.Empty, 23, TextAnchor.MiddleLeft, Color.white);
            ProjectUiFactory.Height(balance.rectTransform, 48f);
            UnityEngine.UI.ScrollRect offerScroll = ProjectUiFactory.Scroll(layout, "Offers", out offers);
            UnityEngine.UI.LayoutElement offerSize = offerScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            offerSize.flexibleHeight = 1.15f;
            offerSize.minHeight = 72f;
            UnityEngine.UI.ScrollRect detailScroll = ProjectUiFactory.Scroll(layout, "Details", out details);
            UnityEngine.UI.LayoutElement detailSize = detailScroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            detailSize.flexibleHeight = 1f;
            detailSize.minHeight = 72f;
            purchase = ProjectUiFactory.ActionButton(layout, "确认购买", Purchase);
            ProjectUiFactory.Height((RectTransform)purchase.transform, 66f);
            message = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.85f, 0.58f));
            ProjectUiFactory.Height(message.rectTransform, 64f);
        }

        /// <summary>
        /// 订阅商品与余额变化，窗口关闭时仅释放商店面板状态。
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
                current.ClosePanel(ExpeditionPanelKind.Merchant);
                if (ReferenceEquals(run, current)) run = null;
            });
        }

        /// <summary>
        /// 首次显示时填充商店快照。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 选择一个商品并显示价格、属性和购买条件。
        /// </summary>
        /// <param name="id">商品稳定身份。</param>
        private void SelectOffer(string id)
        {
            selectedId = id;
            Refresh();
        }

        /// <summary>
        /// 仅在数据变化或选择变化时重建商品卡片和详情。
        /// </summary>
        private void Refresh()
        {
            if (run == null) return;
            if (run.ActivePanel != ExpeditionPanelKind.Merchant || !run.IsRunning)
            {
                if (State == GameWindowState.Opened) CloseSelfAsync().Forget();
                return;
            }
            balance.text = "可用金币 " + run.AvailableGold;
            message.text = run.Message;
            ProjectUiFactory.Clear(offers);
            ExpeditionMerchantOffer selected = null;
            int count = 0;
            RectTransform row = null;
            foreach (ExpeditionMerchantOffer offer in run.MerchantOffers)
            {
                if (offer.Id == selectedId) selected = offer;
                if (count % 2 == 0)
                {
                    row = ProjectUiFactory.Horizontal(offers, "Row", 8f);
                    ProjectUiFactory.Height(row, 112f);
                }
                string id = offer.Id;
                ProjectItem item = offer.Item;
                string caption = ProjectEquipmentText.Title(item) + "\n" + offer.Price + " 金币" + (offer.Sold ? " · 已售" : "");
                UnityEngine.UI.Button card = ProjectUiFactory.ActionButton(row, caption, () => SelectOffer(id));
                UnityEngine.UI.Text cardText = card.GetComponentInChildren<UnityEngine.UI.Text>();
                cardText.fontSize = 21;
                cardText.color = offer.Sold ? new Color(0.6f, 0.65f, 0.7f) : ProjectEquipmentText.QualityColor(item);
                count++;
            }
            if (count % 2 != 0)
            {
                RectTransform spacer = ProjectUiFactory.Panel(row, "EmptyCell", Color.clear);
                spacer.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            }
            if (count == 0)
            {
                UnityEngine.UI.Text empty = ProjectUiFactory.Label(offers, "当前商店没有商品", 23, TextAnchor.MiddleCenter, Color.white);
                ProjectUiFactory.Height(empty.rectTransform, 78f);
            }
            if (selected == null) selectedId = null;
            RefreshDetails(selected);
            purchase.interactable = run.CanManageMerchant && selected != null && !selected.Sold && run.AvailableGold >= selected.Price;
            purchase.GetComponentInChildren<UnityEngine.UI.Text>().text = selected?.Sold == true ? "已售" : "确认购买";
        }

        /// <summary>
        /// 展示商品与固定出战装备，购买只收入关卡背包。
        /// </summary>
        /// <param name="offer">当前选中商品。</param>
        private void RefreshDetails(ExpeditionMerchantOffer offer)
        {
            ProjectUiFactory.Clear(details);
            ProjectItem item = offer?.Item;
            string text = item == null ? "选择商品查看详情" : "选中商品\n" + ProjectEquipmentText.ColoredTitle(item) + "\n价格 " +
                offer.Price + " 金币 · " + (offer.Sold ? "已售" : "可购买") + "\n" + ProjectEquipmentText.Details(item);
            if (item != null && item.Type != ProjectItemType.Item && item.Type != ProjectItemType.Collection)
            {
                text += "\n\n" + ProjectEquipmentText.EquippedDetails(item, run.EntryEquipment) +
                    "\n\n购买后收入关卡背包，带回主城后可装备，本关中不能更换出战装备。";
            }
            UnityEngine.UI.Text label = ProjectUiFactory.Label(details, text, 22, TextAnchor.UpperLeft, Color.white);
            label.supportRichText = true;
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().minHeight = 90f;
        }

        /// <summary>
        /// 明确确认购买当前选中商品。
        /// </summary>
        private void Purchase()
        {
            if (run?.CanManageMerchant != true || selectedId == null) return;
            run.TryBuyMerchantOffer(selectedId);
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
