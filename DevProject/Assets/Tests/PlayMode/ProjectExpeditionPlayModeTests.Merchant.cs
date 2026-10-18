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
        /// 触碰商人自动暂停，关闭后原地继续，离开范围再触碰重新打开且商品保持冻结。
        /// </summary>
        /// <returns>商人触碰、关闭和再次触碰的验证任务。</returns>
        [UnityTest]
        public IEnumerator MerchantTouch_PausesAndReopensOnlyAfterLeavingRange() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await OpenRegionMerchantAsync(run);
            GameWindow merchant = await WaitForWindowAsync(ProjectServices.ExpeditionMerchantWindow);
            string[] offers = run.MerchantOffers.Select(offer => JsonUtility.ToJson(offer.Item)).ToArray();
            Vector3 position = InputPersistencePlayer(run).Position;
            long tick = run.World.TickIndex;
            Assert.That(run.CanAcceptPlayerInput, Is.False);
            run.SetTouchMove(Vector2.right);
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(position));
            ClickButton(merchant, "关闭");
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick + 1));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None), "关闭商店后原地不能立即弹回。");
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(position));
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.EntryPosition), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            await WaitForWindowAsync(ProjectServices.ExpeditionMerchantWindow);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
            Assert.That(run.MerchantOffers.Select(offer => JsonUtility.ToJson(offer.Item)), Is.EqualTo(offers));
        });

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
        /// 触碰当前房间商人并等待访问事实保存后自动打开商店。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>商人面板可用任务。</returns>
        private static async UniTask OpenRegionMerchantAsync(ExpeditionRun run)
        {
            await AdvanceToRoomAsync(run, GameLogic.Gameplay.Level.ExpeditionRoomKind.Npc);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.Npcs.First().Position), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
        }
    }
}
