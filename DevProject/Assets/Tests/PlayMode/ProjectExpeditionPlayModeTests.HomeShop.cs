using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.UI.Shop;
using GameLogic.UI.Warehouse;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 使用真实主界面与局外商店验证购买、补给和保存恢复。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 购买武器后从仓库装备并出战，死亡丢失武器后仍可再次购买并入场。
        /// </summary>
        /// <returns>真实窗口、战斗和结算的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator HomeShop_BuysEquipsDiesAndReplenishesForNextRun() => UniTask.ToCoroutine(async () =>
        {
            await SetupRealHomeAsync(new MemorySaveBackend());
            ProjectSession session = ProjectServices.Session;
            IUIModule ui = GameEntry.Get<IUIModule>();
            int initialGold = session.Gold;
            ProjectHomeShopOffer offer = session.HomeShopOffers.First(value => value.CanPurchase && value.Item.Type == ProjectItemType.Weapon);
            int initialItemCount = session.Warehouse.Count;
            string starterId = session.Warehouse.Single(item => item.Equipped).Id;

            GameWindow home = await WaitForWindowAsync(ContentIds.HomeWindow);
            ClickButton(home, "商店");
            GameWindow shop = await WaitForWindowAsync(ProjectServices.HomeShopWindow);
            Assert.That(shop, Is.TypeOf<HomeShopWindow>());
            ClickButton(shop, "购买");
            await WaitForAsync(() => session.Gold == initialGold - offer.PriceGold);
            Assert.That(session.Warehouse.Count, Is.EqualTo(initialItemCount + 1));
            ProjectItem first = session.Warehouse.Last();
            Assert.That(first.Name, Is.EqualTo(offer.Item.Name));
            Assert.That(first.Equipped, Is.False);

            ClickButton(shop, "前往仓库");
            GameWindow warehouse = await WaitForWindowAsync(2101);
            Assert.That(warehouse, Is.TypeOf<WarehouseWindow>());
            Assert.That(ui.OpenedWindows.Any(window => window.Id == ProjectServices.HomeShopWindow), Is.False);
            SelectWarehouseItem(warehouse, first.Name);
            ClickWarehouseEquipButton(warehouse);
            await WaitForAsync(() => session.Warehouse.Any(item => item.Id == first.Id && item.Equipped));
            ClickButton(warehouse, "关闭");
            await WaitForAsync(() => ui.OpenedWindows.All(window => window.Id != 2101));

            ClickButton(home, "出战");
            await WaitForPhaseAsync(ProjectRunPhase.Playing);
            ExpeditionRun run = session.CurrentRun;
            Assert.That(run.Inventory.Any(item => item.Id == first.Id && item.Equipped), Is.True);
            TriggerDeath(run);
            await ConfirmPendingDeathAsync(run);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(session.LastResult.Outcome.Extracted, Is.False);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(session.Warehouse.Any(item => item.Id == first.Id), Is.False);
            Assert.That(session.Gold, Is.EqualTo(initialGold - offer.PriceGold));

            home = await WaitForWindowAsync(ContentIds.HomeWindow);
            ClickButton(home, "商店");
            shop = await WaitForWindowAsync(ProjectServices.HomeShopWindow);
            ClickButton(shop, "购买");
            await WaitForAsync(() => session.Gold == initialGold - offer.PriceGold * 2);
            ProjectItem replacement = session.Warehouse.Single(item => item.Type == ProjectItemType.Weapon && item.Id != starterId);
            Assert.That(replacement.Id, Is.Not.EqualTo(first.Id));
            ClickButton(shop, "前往仓库");
            warehouse = await WaitForWindowAsync(2101);
            SelectWarehouseItem(warehouse, replacement.Name);
            ClickWarehouseEquipButton(warehouse);
            await WaitForAsync(() => session.Warehouse.Any(item => item.Id == replacement.Id && item.Equipped));
            ClickButton(warehouse, "关闭");
            await WaitForAsync(() => ui.OpenedWindows.All(window => window.Id != 2101));
            ClickButton(home, "出战");
            await WaitForPhaseAsync(ProjectRunPhase.Playing);
            Assert.That(session.CurrentRun.Inventory.Any(item => item.Id == replacement.Id && item.Equipped), Is.True);
        });

        /// <summary>
        /// 购买写入失败后由商店按钮重试原交易，跨会话读取时只扣一次金币并交付一件物品。
        /// </summary>
        /// <returns>真实商店窗口、故障注入和重新打开存档的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator HomeShop_FailedSaveRetryPersistsSinglePurchase() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupRealHomeAsync(backend);
            ProjectSession session = ProjectServices.Session;
            int initialGold = session.Gold;
            int initialCount = session.Warehouse.Count;
            ProjectHomeShopOffer offer = session.HomeShopOffers.First(value => value.CanPurchase);
            GameWindow home = await WaitForWindowAsync(ContentIds.HomeWindow);
            ClickButton(home, "商店");
            GameWindow shop = await WaitForWindowAsync(ProjectServices.HomeShopWindow);
            backend.FailWrites = true;
            LogAssert.ignoreFailingMessages = true;
            ClickButton(shop, "购买");
            await WaitForAsync(() => session.HasPendingSave);
            Assert.That(session.Gold, Is.EqualTo(initialGold));
            Assert.That(session.Warehouse.Count, Is.EqualTo(initialCount));
            Assert.That(FindButton(shop, "购买").interactable, Is.False);

            backend.FailWrites = false;
            await WaitForAsync(() => FindButton(shop, "重试保存").interactable);
            ClickButton(shop, "重试保存");
            await WaitForAsync(() => !session.HasPendingSave && session.Gold == initialGold - offer.PriceGold);
            Assert.That(session.Warehouse.Count, Is.EqualTo(initialCount + 1));
            string purchasedId = session.Warehouse.Last().Id;

            await ProjectServices.Coordinator.CloseAsync();
            GameEntry.StopFramework();
            await SetupRealHomeAsync(backend);
            Assert.That(ProjectServices.Session.Gold, Is.EqualTo(initialGold - offer.PriceGold));
            Assert.That(ProjectServices.Session.Warehouse.Count, Is.EqualTo(initialCount + 1));
            Assert.That(ProjectServices.Session.Warehouse.Count(item => item.Id == purchasedId), Is.EqualTo(1));
        });

        /// <summary>
        /// 使用真实 Home 场景建立隔离存档，保留可点击的主界面入口。
        /// </summary>
        /// <param name="backend">本次测试使用的隔离存储。</param>
        /// <returns>项目初始化和真实 Home 就绪任务。</returns>
        private static async UniTask SetupRealHomeAsync(ISaveBackend backend)
        {
            GameEntry.StopFramework();
            var saves = new SaveModule();
            Assert.That(saves.ConfigureBackend(backend).Success, Is.True);
            GameEntry.Register<ISaveModule>(saves);
            GameEntry.Register<IInputModule>(new HeadlessInputModule());
            GameEntry.RegisterDefaults();
            GameEntry.StartFramework();
            await InitializeEditorPackageAsync();
            ProjectContent.Register(GameEntry.Get<ISceneModule>(), GameEntry.Get<IUIModule>());
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.TypeOf<GameLogic.Scenes.Home.HomeScene>());
        }

        /// <summary>
        /// 有界等待窗口打开并取得真实窗口对象。
        /// </summary>
        /// <param name="windowId">已注册窗口标识。</param>
        /// <returns>已经打开的窗口。</returns>
        private static async UniTask<GameWindow> WaitForWindowAsync(int windowId)
        {
            IUIModule ui = GameEntry.Get<IUIModule>();
            await WaitForAsync(() => ui.OpenedWindows.Any(window => window.Id == windowId));
            return ui.OpenedWindows.Single(window => window.Id == windowId);
        }

        /// <summary>
        /// 通过窗口中唯一的可见文本查找按钮。
        /// </summary>
        /// <param name="window">真实窗口。</param>
        /// <param name="caption">按钮完整文本。</param>
        /// <returns>匹配的按钮。</returns>
        private static Button FindButton(GameWindow window, string caption) => window.Root.GetComponentsInChildren<Button>(true)
            .Single(button => button.GetComponentInChildren<Text>()?.text == caption);


        /// <summary>
        /// 仅点击仓库操作栏的装备按钮，避开同名分类按钮。
        /// </summary>
        /// <param name="warehouse">仓库窗口。</param>
        private static void ClickWarehouseEquipButton(GameWindow warehouse)
        {
            Transform actions = warehouse.RootTransform.Find("WarehouseContent/Layout/Actions");
            Assert.That(actions, Is.Not.Null);
            Button equip = actions.GetComponentsInChildren<Button>(false).Single(button =>
                button.GetComponentInChildren<Text>()?.text == "装备");
            Assert.That(equip.interactable, Is.True);
            equip.onClick.Invoke();
        }
        /// <summary>
        /// 选中仓库中最后一张同名卡片，确保选到新购买的物品。
        /// </summary>
        /// <param name="warehouse">仓库窗口。</param>
        /// <param name="itemName">购买所得物品名称。</param>
        private static void SelectWarehouseItem(GameWindow warehouse, string itemName)
        {
            Button card = warehouse.Root.GetComponentsInChildren<Button>().Last(button =>
                button.GetComponentInChildren<Text>()?.text.Contains(itemName) == true);
            card.onClick.Invoke();
        }

        /// <summary>
        /// 有界等待异步界面操作及会话变化。
        /// </summary>
        /// <param name="condition">期望达成的状态。</param>
        /// <returns>逐帧等待任务。</returns>
        private static async UniTask WaitForAsync(Func<bool> condition)
        {
            float until = Time.realtimeSinceStartup + 15f;
            while (!condition() && Time.realtimeSinceStartup < until) await UniTask.Yield();
            Assert.That(condition(), Is.True, "等待界面或会话状态超时。");
        }
    }
}
