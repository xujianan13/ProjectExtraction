using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.UI.Warehouse;
using UnityEngine;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 真实会话和持久化后端的仓库整理验证。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 锁定可保存且阻止出售，解锁后批量出售只移除明确的实例。
        /// </summary>
        /// <returns>仓库保存往返的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Warehouse_LockAndExplicitBatchPersist() => UniTask.ToCoroutine(async () =>
        {
            var backend = new MemorySaveBackend();
            await SetupRealHomeAsync(backend);
            ProjectSession session = ProjectServices.Session;
            ProjectHomeShopOffer offer = session.HomeShopOffers.First(value => value.CanPurchase);
            Assert.That((await session.BuyHomeShopItemAsync(offer.Id, offer.PurchaseRevision)).Success, Is.True);
            ProjectItem item = session.Warehouse.Last();
            int goldBeforeSale = session.Gold;
            Assert.That((await session.SetItemLockedAsync(item.Id, true)).Success, Is.True);
            Assert.That(session.Warehouse.Single(value => value.Id == item.Id).Locked, Is.True);
            Assert.That((await session.SellItemsAsync(new[] { item.Id })).Success, Is.False);
            Assert.That(session.Gold, Is.EqualTo(goldBeforeSale));
            Assert.That((await session.SetItemLockedAsync(item.Id, false)).Success, Is.True);
            Assert.That((await session.SellItemsAsync(new[] { item.Id })).Success, Is.True);
            Assert.That(session.Gold, Is.EqualTo(goldBeforeSale + item.SellGold));
            Assert.That(session.Warehouse.Any(value => value.Id == item.Id), Is.False);
            Assert.That(session.Warehouse.Any(value => value.Equipped), Is.True);

            await ProjectServices.Coordinator.CloseAsync();
            GameEntry.StopFramework();
            await SetupRealHomeAsync(backend);
            Assert.That(ProjectServices.Session.Gold, Is.EqualTo(goldBeforeSale + item.SellGold));
            Assert.That(ProjectServices.Session.Warehouse.Any(value => value.Id == item.Id), Is.False);
        });

        /// <summary>
        /// 写入失败后重试原出售候选，金币只增加一次。
        /// </summary>
        /// <returns>故障注入与重试的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Warehouse_FailedBatchRetryCreditsOnce() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupRealHomeAsync(backend);
            ProjectSession session = ProjectServices.Session;
            ProjectHomeShopOffer offer = session.HomeShopOffers.First(value => value.CanPurchase);
            Assert.That((await session.BuyHomeShopItemAsync(offer.Id, offer.PurchaseRevision)).Success, Is.True);
            ProjectItem item = session.Warehouse.Last();
            int gold = session.Gold;
            backend.FailWrites = true;
            LogAssert.ignoreFailingMessages = true;
            Assert.That((await session.SellItemsAsync(new[] { item.Id })).Success, Is.False);
            Assert.That(session.HasPendingSave, Is.True);
            Assert.That(session.Gold, Is.EqualTo(gold));
            Assert.That(session.Warehouse.Any(value => value.Id == item.Id), Is.True);
            Assert.That((await session.SellItemsAsync(new[] { item.Id })).Success, Is.False);
            backend.FailWrites = false;
            Assert.That((await session.RetryPendingSaveAsync()).Success, Is.True);
            Assert.That(session.Gold, Is.EqualTo(gold + item.SellGold));
            Assert.That(session.Warehouse.Any(value => value.Id == item.Id), Is.False);
            Assert.That((await session.SellItemsAsync(new[] { item.Id })).Success, Is.False);
            Assert.That(session.Gold, Is.EqualTo(gold + item.SellGold));
        });
        /// <summary>
        /// 从真实 Home 打开仓库，验证锁定、双件多选和批售确认的取消与完成路径。
        /// </summary>
        /// <returns>真实窗口与内存存档的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator WarehouseWindow_LockSelectionAndBatchConfirmation() => UniTask.ToCoroutine(async () =>
        {
            await SetupRealHomeAsync(new MemorySaveBackend());
            ProjectSession session = ProjectServices.Session;
            ProjectHomeShopOffer offer = session.HomeShopOffers.Single(value => value.Id == "basic_blade");
            Assert.That((await session.BuyHomeShopItemAsync(offer.Id, offer.PurchaseRevision)).Success, Is.True);
            ProjectItem first = session.Warehouse.Last();
            offer = session.HomeShopOffers.Single(value => value.Id == "basic_blade");
            Assert.That((await session.BuyHomeShopItemAsync(offer.Id, offer.PurchaseRevision)).Success, Is.True);
            ProjectItem second = session.Warehouse.Last();
            string unselectedId = session.Warehouse[0].Id;
            int goldBeforeSale = session.Gold;
            int totalGold = first.SellGold + second.SellGold;

            GameWindow home = await WaitForWindowAsync(ContentIds.HomeWindow);
            ClickButton(home, "仓库");
            GameWindow warehouse = await WaitForWindowAsync(2101);
            Assert.That(warehouse, Is.TypeOf<WarehouseWindow>());
            ClickWarehouseCardAt(warehouse, 1);
            ClickButton(warehouse, "锁定");
            await WaitForAsync(() => session.Warehouse.Single(item => item.Id == first.Id).Locked);
            Assert.That(session.Warehouse.Single(item => item.Id == first.Id).Equipped, Is.False);
            ClickButton(warehouse, "解锁");
            await WaitForAsync(() => !session.Warehouse.Single(item => item.Id == first.Id).Locked);

            ClickButton(warehouse, "多选");
            ClickWarehouseCardAt(warehouse, 1);
            ClickWarehouseCardAt(warehouse, 2);
            Assert.That(WarehouseSelectionSummary(warehouse), Is.EqualTo($"已选 2 件 · 合计 {totalGold} 金币（仅出售所选）"));
            ClickButton(warehouse, "出售已选");
            await WaitForAsync(() => GameObject.Find("Modal Dialog") != null);
            Assert.That(WarehouseConfirmationMessage(), Is.EqualTo($"出售所选 2 件物品，共获得 {totalGold} 金币。"));
            ClickWarehouseConfirmation("取消");
            await WaitForAsync(() => GameObject.Find("Modal Dialog") == null && FindButton(warehouse, "出售已选").interactable);
            Assert.That(session.Gold, Is.EqualTo(goldBeforeSale));
            Assert.That(session.Warehouse.Select(item => item.Id), Does.Contain(first.Id));
            Assert.That(session.Warehouse.Select(item => item.Id), Does.Contain(second.Id));
            Assert.That(WarehouseSelectionSummary(warehouse), Does.StartWith("已选 2 件"));

            ClickButton(warehouse, "出售已选");
            await WaitForAsync(() => GameObject.Find("Modal Dialog") != null);
            Assert.That(WarehouseConfirmationMessage(), Is.EqualTo($"出售所选 2 件物品，共获得 {totalGold} 金币。"));
            ClickWarehouseConfirmation("出售所选");
            await WaitForAsync(() => session.Warehouse.Count == 1 && session.Gold == goldBeforeSale + totalGold);
            Assert.That(session.Warehouse.Single().Id, Is.EqualTo(unselectedId));
            Assert.That(session.Warehouse.Any(item => item.Id == first.Id || item.Id == second.Id), Is.False);
        });

        /// <summary>
        /// 按当前库存顺序点击仓库卡片，每次重新获取刷新后的节点。
        /// </summary>
        /// <param name="warehouse">真实仓库窗口。</param>
        /// <param name="index">从零开始的卡片位置。</param>
        private static void ClickWarehouseCardAt(GameWindow warehouse, int index)
        {
            Transform content = warehouse.RootTransform.Find("WarehouseContent/Layout/Items/Viewport/Content");
            Assert.That(content, Is.Not.Null);
            UnityEngine.UI.Button[] cards = content.GetComponentsInChildren<UnityEngine.UI.Button>(false);
            Assert.That(cards.Length, Is.EqualTo(3));
            cards[index].onClick.Invoke();
        }

        /// <summary>
        /// 读取真实仓库当前的批售件数和总价提示。
        /// </summary>
        /// <param name="warehouse">真实仓库窗口。</param>
        /// <returns>选择汇总文本。</returns>
        private static string WarehouseSelectionSummary(GameWindow warehouse) => warehouse.Root
            .GetComponentsInChildren<UnityEngine.UI.Text>(true).Single(label => label.text.StartsWith("已选 ")).text;

        /// <summary>
        /// 读取框架内置确认弹窗的批量出售正文。
        /// </summary>
        /// <returns>弹窗正文。</returns>
        private static string WarehouseConfirmationMessage() => GameObject.Find("Modal Dialog")
            .transform.Find("Panel/Message").GetComponent<UnityEngine.UI.Text>().text;

        /// <summary>
        /// 点击框架内置确认弹窗的指定按钮。
        /// </summary>
        /// <param name="caption">按钮文字。</param>
        private static void ClickWarehouseConfirmation(string caption)
        {
            GameObject dialog = GameObject.Find("Modal Dialog");
            Assert.That(dialog, Is.Not.Null);
            UnityEngine.UI.Button button = dialog.GetComponentsInChildren<UnityEngine.UI.Button>(false)
                .Single(value => value.GetComponentInChildren<UnityEngine.UI.Text>().text == caption);
            button.onClick.Invoke();
        }
    }
}
