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

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证 NPC 商店拒绝交易时不消耗金币或商品。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 余额不足时保留当前报价、库存和背包。
        /// </summary>
        /// <returns>真实 NPC 场景中的逐帧交易测试。</returns>
        [UnityTest]
        public IEnumerator MerchantPurchase_InsufficientGoldPreservesOfferAndInventory() => UniTask.ToCoroutine(async () =>
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var initial = new ProjectSaveData { Level = 1, Stamina = 100, Gold = 0, ClockUtc = now, RecoveryUtc = now };
            await SetupAsync(new MemorySaveBackend(), initial);
            using var scope = new LifetimeScope("MerchantInsufficientGold");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "merchant-poor-test", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            LevelRoomState room = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Npc);
            MoveToRoom(run, room.RoomId);
            var geometry = room.Plan.Definition.Layout;
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3((float)geometry.NpcX, 0, (float)geometry.NpcZ)), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
            ExpeditionMerchantOffer before = run.MerchantOffers.First();
            string itemBefore = JsonUtility.ToJson(before.Item);
            Assert.That(run.AvailableGold, Is.Zero);
            Assert.That(run.TryPurchase(before.Id), Is.False);
            Assert.That(run.GoldSpent, Is.Zero);
            Assert.That(run.AvailableGold, Is.Zero);
            Assert.That(run.Inventory, Is.Empty);
            Assert.That(run.MerchantOffers.First().Sold, Is.False);
            Assert.That(JsonUtility.ToJson(run.MerchantOffers.First().Item), Is.EqualTo(itemBefore));
            Assert.That(!room.TryGetNpcStock("relic", out int stock) || stock == room.Plan.NpcStock, Is.True);
        });

        /// <summary>
        /// 应用后台和离开 NPC 后均拒绝面板上的报价。
        /// </summary>
        /// <returns>真实 NPC 场景中的逐帧交易测试。</returns>
        [UnityTest]
        public IEnumerator MerchantPurchase_BackgroundOrAwayFromNpcPreservesOfferAndInventory() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("MerchantPurchaseGuards");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "merchant-guard-test", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            LevelRoomState room = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Npc);
            MoveToRoom(run, room.RoomId);
            var geometry = room.Plan.Definition.Layout;
            Vector3 npcPosition = new Vector3((float)geometry.NpcX, 0, (float)geometry.NpcZ);
            Assert.That(run.World.TrySetPosition(run.Player, npcPosition), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
            ExpeditionMerchantOffer offer = run.MerchantOffers.First();
            int goldBefore = run.AvailableGold;
            string itemBefore = JsonUtility.ToJson(offer.Item);

            GameEntry.Bootstrap().SendMessage("OnApplicationPause", true);
            try
            {
                Assert.That(GameEntry.IsInBackground, Is.True);
                Assert.That(run.TryPurchase(offer.Id), Is.False);
                Assert.That(run.GoldSpent, Is.Zero);
                Assert.That(run.Inventory, Is.Empty);
                Assert.That(run.MerchantOffers.First().Sold, Is.False);
            }
            finally
            {
                GameEntry.Bootstrap().SendMessage("OnApplicationPause", false);
            }
            Assert.That(GameEntry.IsInBackground, Is.False);
            Vector3 farPosition = run.RoomView.GetSpawnPosition(room.Exits.Keys.First());
            Assert.That((farPosition - npcPosition).sqrMagnitude,
                Is.GreaterThan(run.Layout.ObjectInteractionRadius * run.Layout.ObjectInteractionRadius));
            Assert.That(run.World.TrySetPosition(run.Player, farPosition), Is.True);
            Assert.That(run.TryPurchase(offer.Id), Is.False);
            Assert.That(run.GoldSpent, Is.Zero);
            Assert.That(run.AvailableGold, Is.EqualTo(goldBefore));
            Assert.That(run.Inventory, Is.Empty);
            Assert.That(run.MerchantOffers.First().Sold, Is.False);
            Assert.That(JsonUtility.ToJson(run.MerchantOffers.First().Item), Is.EqualTo(itemBefore));
            Assert.That(!room.TryGetNpcStock("relic", out int stock) || stock == room.Plan.NpcStock, Is.True);
        });
    }
}