using System;
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
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 关卡背包只展示所得，固定出战装备独立列出，物品详情不提供穿脱操作。
        /// </summary>
        /// <returns>真实背包、固定装备与死亡结算验证任务。</returns>
        [UnityTest]
        public IEnumerator Inventory_ReadOnlyBagSeparatesProtectedEntryEquipment() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            string[] entryIds = run.EntryEquipment.Select(item => item.Id).ToArray();
            Assert.That(entryIds, Is.Not.Empty);
            Assert.That(run.Inventory, Is.Empty);
            ExpeditionDrop drop = AddInteractionDrop(run, "fixed-equipment-bag-loot", InputPersistencePlayer(run).Position);
            run.Advance(CombatWorld.TickSeconds);
            SelectInteractionLoot(run, drop.Item.Id);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            CombatActorSnapshot before = InputPersistencePlayer(run);
            Assert.That(run.TryOpenInventory(), Is.True);
            GameWindow bag = await WaitForWindowAsync(ProjectServices.ExpeditionInventoryWindow);
            Assert.That(bag.Root.GetComponentsInChildren<Button>(false).Any(button =>
                new[] { "装备", "卸下" }.Contains(button.GetComponentInChildren<Text>().text)), Is.False);
            Assert.That(run.Inventory.Select(item => item.Id), Is.EquivalentTo(new[] { drop.Item.Id }));
            Assert.That(run.Inventory.All(item => !item.Equipped), Is.True);
            ClickButton(bag, "出战装备");
            StringAssert.Contains("固定出战装备", string.Join("\n", bag.Root.GetComponentsInChildren<Text>(false).Select(text => text.text)));
            Assert.That(InputPersistencePlayer(run).Attack, Is.EqualTo(before.Attack));
            Assert.That(run.EntryEquipment.Select(item => item.Id), Is.EquivalentTo(entryIds));
            await GameEntry.Get<IUIModule>().CloseWindowAsync(bag.Id);
            TriggerDeath(run);
            await ConfirmPendingDeathAsync(run);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Select(item => item.Id), Is.EquivalentTo(entryIds));
            Assert.That(ProjectServices.Session.LastResult.LostItems.Select(item => item.Id), Is.EquivalentTo(new[] { drop.Item.Id }));
        });

        /// <summary>
        /// 商人购买只收入关卡背包，固定装备和战斗属性不变，通关后仅结算一次购买支出。
        /// </summary>
        /// <returns>购买、通关和仓库到账验证任务。</returns>
        [UnityTest]
        public IEnumerator MerchantWindow_PurchaseAddsToBagWithoutChangingEntryEquipment() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Gold = 10000;
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await OpenRegionMerchantAsync(run);
            string[] entryIds = run.EntryEquipment.Select(item => item.Id).ToArray();
            CombatActorSnapshot before = InputPersistencePlayer(run);
            ExpeditionMerchantOffer offer = run.MerchantOffers.First();
            Assert.That(run.TryBuyMerchantOffer(offer.Id), Is.True);
            await WaitRunSavedAsync(run);
            Assert.That(run.Inventory.Count(item => item.Id == offer.Item.Id), Is.EqualTo(1));
            Assert.That(run.Inventory.Single(item => item.Id == offer.Item.Id).Equipped, Is.False);
            Assert.That(run.EntryEquipment.Select(item => item.Id), Is.EquivalentTo(entryIds));
            Assert.That(InputPersistencePlayer(run).Attack, Is.EqualTo(before.Attack));
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.ExpeditionMerchantWindow);
            await TriggerExtractionAsync(run);
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Count(item => item.Id == offer.Item.Id), Is.EqualTo(1));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Session.Warehouse.Count(item => item.Id == offer.Item.Id), Is.EqualTo(1));
            Assert.That(ProjectServices.Session.Gold, Is.EqualTo(initial.Gold - offer.Price));
        });

    }
}
