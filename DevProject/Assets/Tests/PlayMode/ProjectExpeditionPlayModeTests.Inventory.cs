using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 局内装备、安全箱、等级门槛与属性预览共用权威状态，预览不能改动物品。
        /// </summary>
        /// <returns>使用真实配置和资源的背包操作测试。</returns>
        [UnityTest]
        public IEnumerator Inventory_EquipmentPreviewSafeCapacityAndPauseRemainConsistent() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("InventoryIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            var random = new System.Random(901);
            ProjectItem first = ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Weapon, 1, ProjectItemQuality.Common);
            ProjectItem next = ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Weapon, 1, ProjectItemQuality.Epic);
            ProjectItem armor = ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            ProjectItem locked = ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Weapon, 2, ProjectItemQuality.Common);
            first.Equipped = true;
            var items = new List<ProjectItem> { first, next, armor, locked };
            for (int index = 0; index <= PrototypeRules.SafeSlots; index++)
                items.Add(ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Armor, 1, ProjectItemQuality.Common));
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "inventory-test", 1, 42, items, out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 7), Is.True);
            Assert.That(run.TryOpenInventory(), Is.True);
            long tick = run.World.TickIndex;
            run.SetTouchMove(Vector2.right);
            run.Advance(1);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(run.TryBeginRogueChoice("blocked-by-inventory"), Is.False);
            Assert.That(run.TryPreviewEquipment(next, out var before, out var preview), Is.True);
            run.World.TryGetActor(run.Player, out var actorBefore);
            Assert.That(before.Attack, Is.EqualTo(actorBefore.Attack));
            Assert.That(run.TryEquip(next.Id), Is.True);
            run.World.TryGetActor(run.Player, out var actorAfter);
            Assert.That(actorAfter.Attack, Is.EqualTo(preview.Attack));
            Assert.That(run.Inventory.Single(item => item.Id == next.Id).Equipped, Is.True);
            Assert.That(run.Inventory.Single(item => item.Id == first.Id).Equipped, Is.False);
            Assert.That(run.TryEquip(locked.Id), Is.False);
            Assert.That(run.Inventory.Single(item => item.Id == next.Id).Equipped, Is.True);
            Assert.That(run.ToggleSafe(next.Id), Is.False, "穿戴中的装备不能放入安全箱。");
            Assert.That(run.TryEquip(armor.Id), Is.True);
            run.World.TryGetActor(run.Player, out var armored);
            Assert.That(armored.Health, Is.EqualTo(actorAfter.Health), "提高上限不能凭空恢复生命。");
            Assert.That(armored.MaxHealth, Is.GreaterThan(actorAfter.MaxHealth));
            Assert.That(run.ToggleSafe(first.Id), Is.True);
            Assert.That(run.TryEquip(first.Id), Is.False, "安全箱中的装备需要先取出。");
            foreach (ProjectItem item in items.Skip(4).Take(run.SafeCapacity - 1)) Assert.That(run.ToggleSafe(item.Id), Is.True);
            Assert.That(run.ToggleSafe(items.Last().Id), Is.False);
            Assert.That(run.SafeCount, Is.EqualTo(run.SafeCapacity));
            ProjectItem snapshot = run.Inventory.Single(item => item.Id == next.Id);
            snapshot.Equipped = false;
            snapshot.BaseAttributes.Clear();
            Assert.That(run.Inventory.Single(item => item.Id == next.Id).Equipped, Is.True);
            Assert.That(run.GetRetainedItems(false).All(item => run.IsProtected(item.Id) && !item.Equipped), Is.True);
            Assert.That(run.GetRetainedItems(true).Count, Is.EqualTo(items.Count));
            run.SetPaused(true);
            Assert.That(run.TryEquip(armor.Id), Is.False);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            run.Advance(1);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick), "关闭背包不能释放应用暂停。");
            run.SetPaused(false);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick + 1));
            Assert.That(run.TryBeginRogueChoice("after-inventory"), Is.True);
            Assert.That(run.TryOpenInventory(), Is.False);
            run.ClosePanel(ExpeditionPanelKind.Inventory);
            run.Advance(1);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick + 1), "旧面板关闭不能结束三选一暂停。");
        });

        /// <summary>
        /// 真正的背包窗口穿脱和安全箱操作能够进入死亡结算并在返场后清理窗口。
        /// </summary>
        /// <returns>从出战到窗口操作及死亡返场的完整验证。</returns>
        [UnityTest]
        public IEnumerator InventoryWindow_UnloadsStarterIntoSafeAndDeathRetainsIt() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            string starterId = run.Inventory.Single(item => item.Equipped).Id;
            IUIModule ui = GameEntry.Get<IUIModule>();
            Assert.That(run.TryOpenInventory(), Is.True);
            for (int frame = 0; frame < 60 && !ui.OpenedWindows.Any(window => window.Id == ProjectServices.ExpeditionInventoryWindow); frame++)
                await UniTask.Yield();
            GameWindow bag = ui.OpenedWindows.Single(window => window.Id == ProjectServices.ExpeditionInventoryWindow);
            Assert.That(bag.Config.UseMask, Is.True);
            Canvas.ForceUpdateCanvases();
            Assert.That(bag.Root.GetComponentsInChildren<UnityEngine.UI.ScrollRect>().All(scroll =>
                scroll.viewport != null && scroll.content != null && scroll.viewport.rect.height > 0), Is.True);
            string starterName = run.Inventory.Single(item => item.Id == starterId).Name;
            bag.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button =>
                button.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains(starterName)).onClick.Invoke();
            bag.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button =>
                button.GetComponentInChildren<UnityEngine.UI.Text>().text == "卸下").onClick.Invoke();
            Assert.That(run.Inventory.Single(item => item.Id == starterId).Equipped, Is.False);
            bag.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button =>
                button.GetComponentInChildren<UnityEngine.UI.Text>().text == "存入安全箱").onClick.Invoke();
            Assert.That(run.IsProtected(starterId), Is.True);
            Assert.That(await ui.CloseWindowAsync(ProjectServices.ExpeditionInventoryWindow), Is.True);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            Assert.That(run.CanAcceptPlayerInput, Is.True);
            TriggerDeath(run);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Single().Id, Is.EqualTo(starterId));
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Single().Equipped, Is.False);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Session.Warehouse.Any(item => item.Id == starterId && !item.Equipped), Is.True);
            Assert.That(ui.OpenedWindows.Any(window => window.Id == ProjectServices.ExpeditionInventoryWindow ||
                window.Id == ProjectServices.ExpeditionMerchantWindow), Is.False);
        });
        /// <summary>
        /// 商店实际窗口只在确认按钮点击后购买，交易物品和金币随撤离结算。
        /// </summary>
        /// <returns>真实商店窗口和撤离保存的集成测试。</returns>
        [UnityTest]
        public IEnumerator MerchantWindow_ConfirmationPurchasesAndExtractionSettlesOnce() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            int balance = ProjectServices.Session.Gold;
            LevelRoomState room = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Npc);
            MoveToRoom(run, room.RoomId);
            var geometry = room.Plan.Definition.Layout;
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3((float)geometry.NpcX, 0, (float)geometry.NpcZ)), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            IUIModule ui = GameEntry.Get<IUIModule>();
            for (int frame = 0; frame < 60 && !ui.OpenedWindows.Any(window => window.Id == ProjectServices.ExpeditionMerchantWindow); frame++)
                await UniTask.Yield();
            GameWindow shop = ui.OpenedWindows.Single(window => window.Id == ProjectServices.ExpeditionMerchantWindow);
            Assert.That(shop.Config.UseMask, Is.True);
            ExpeditionMerchantOffer offer = run.MerchantOffers.First();
            Assert.That(run.GoldSpent, Is.Zero);
            shop.Root.GetComponentsInChildren<UnityEngine.UI.Button>().First(button =>
                button.GetComponentInChildren<UnityEngine.UI.Text>().text.Contains(offer.Item.Name)).onClick.Invoke();
            Assert.That(run.GoldSpent, Is.Zero);
            shop.Root.GetComponentsInChildren<UnityEngine.UI.Button>().Single(button =>
                button.GetComponentInChildren<UnityEngine.UI.Text>().text == "确认购买").onClick.Invoke();
            Assert.That(run.GoldSpent, Is.EqualTo(offer.Price));
            Assert.That(run.Inventory.Any(item => item.Id == offer.Item.Id), Is.True);
            Assert.That(await ui.CloseWindowAsync(ProjectServices.ExpeditionMerchantWindow), Is.True);
            await TriggerExtractionAsync(run);
            Assert.That(ProjectServices.Session.Gold, Is.EqualTo(balance - offer.Price));
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Count(item => item.Id == offer.Item.Id), Is.EqualTo(1));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Session.Warehouse.Count(item => item.Id == offer.Item.Id), Is.EqualTo(1));
            Assert.That(ProjectServices.Session.Gold, Is.EqualTo(balance - offer.Price));
        });
    }
}
