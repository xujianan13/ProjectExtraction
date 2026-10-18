using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 金币不足的购买不改变已冻结物品、库存和支出。
        /// </summary>
        /// <returns>真实商店拒绝交易任务。</returns>
        [UnityTest]
        public IEnumerator MerchantPurchase_InsufficientGoldKeepsFrozenStock() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Gold = 0;
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await OpenRegionMerchantAsync(run);
            var offer = run.MerchantOffers.First();
            string frozen = JsonUtility.ToJson(offer.Item);
            int count = run.Inventory.Count;
            Assert.That(run.TryBuyMerchantOffer(offer.Id), Is.False);
            Assert.That(run.GoldSpent, Is.Zero);
            Assert.That(run.Inventory.Count, Is.EqualTo(count));
            Assert.That(run.MerchantOffers.First().Sold, Is.False);
            Assert.That(JsonUtility.ToJson(run.MerchantOffers.First().Item), Is.EqualTo(frozen));
        });

        /// <summary>
        /// 应用后台或离开商人后，旧面板报价仍会重新校验。
        /// </summary>
        /// <returns>暂停和位置失效交易任务。</returns>
        [UnityTest]
        public IEnumerator MerchantPurchase_BackgroundAndDistanceRejectTransaction() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await OpenRegionMerchantAsync(run);
            var offer = run.MerchantOffers.First();
            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            Assert.That(run.TryBuyMerchantOffer(offer.Id), Is.False);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.EntryPosition), Is.True);
            Assert.That(run.TryBuyMerchantOffer(offer.Id), Is.False);
            Assert.That(run.GoldSpent, Is.Zero);
            Assert.That(run.MerchantOffers.First().Sold, Is.False);
        });

        /// <summary>
        /// 在当前区域通过实际交互进入商店并等待访问状态保存。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>商人面板可用任务。</returns>
        private static async UniTask OpenRegionMerchantAsync(ExpeditionRun run)
        {
            await AdvanceToRoomAsync(run, GameLogic.Gameplay.Level.ExpeditionRoomKind.Npc);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.Npcs.First().Position), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
        }
    }
}
