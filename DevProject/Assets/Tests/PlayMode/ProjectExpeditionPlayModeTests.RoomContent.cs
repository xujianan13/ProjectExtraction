using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证房间内容计划在实际资源、跨图、战斗和奖励中的连接。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 未清怪时遍历全图，表现几何和敌人数量遵循计划，回访不重复生成。
        /// </summary>
        /// <returns>包含真实房间资源的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator RoomPlans_DriveGeometryAndSpawnsAcrossUnclearedRevisits() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("RoomPlansIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "rooms-test", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            using var presentation = new ExpeditionPresentation(run, assets);
            run.AttachScene(assets, presentation.Root);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            var actors = new List<CombatActorSnapshot>();
            var identities = new Dictionary<int, CombatEntityRef[]>();
            var plans = run.Maps.Rooms.ToDictionary(room => room.RoomId, room => room.Plan);
            foreach (LevelRoomState room in run.Maps.Rooms)
            {
                MoveToRoom(run, room.RoomId);
                Assert.That(room.Plan, Is.SameAs(plans[room.RoomId]));
                Assert.That(run.RoomView.Root.transform.Find("Floor").GetComponent<Renderer>().bounds.size.x,
                    Is.EqualTo((float)room.Plan.Definition.Layout.HalfExtent * 2f).Within(0.001f));
                Assert.That(run.Layout.Obstacles.Count, Is.EqualTo(room.Plan.Definition.Obstacles.Count));
                Assert.That(run.RoomView.Root.transform.Cast<Transform>().Count(child => child.name == "Obstacle"),
                    Is.EqualTo(room.Plan.Definition.Obstacles.Count));
                run.World.GetActorsSnapshot(actors);
                CombatActorSnapshot[] local = actors.Where(actor => actor.RoomId == room.RoomId && actor.ActorId != 1).ToArray();
                Assert.That(local.Length, Is.EqualTo(room.Plan.Spawns.Sum(group => group.Count)));
                identities.Add(room.RoomId, local.Select(actor => actor.Entity).ToArray());
                Assert.That(room.IsCleared, Is.False, "无须清怪即可通行所有房间。");
            }
            foreach (LevelRoomState room in run.Maps.Rooms.Reverse())
            {
                MoveToRoom(run, room.RoomId);
                run.World.GetActorsSnapshot(actors);
                CollectionAssert.AreEqual(identities[room.RoomId], actors.Where(actor => actor.RoomId == room.RoomId && actor.ActorId != 1)
                    .Select(actor => actor.Entity).ToArray());
            }
            Assert.That(run.World.Faulted, Is.False);
        });

        /// <summary>
        /// 商店预览不消耗金币，逐件购买后回访保留商品与售出状态。
        /// </summary>
        /// <returns>实际 NPC 交互验证任务。</returns>
        [UnityTest]
        public IEnumerator RoomMerchant_UsesPlanPricePoolAndPersistentStock() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("MerchantRoomIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "merchant-test", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            LevelRoomState room = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Npc);
            MoveToRoom(run, room.RoomId);
            var geometry = room.Plan.Definition.Layout;
            Vector3 position = new Vector3((float)geometry.NpcX, 0, (float)geometry.NpcZ);
            int goldBefore = run.AvailableGold;
            Assert.That(goldBefore, Is.GreaterThanOrEqualTo(room.Plan.NpcPrice * room.Plan.NpcStock));
            ExpeditionMerchantOffer[] planned = run.MerchantOffers.ToArray();
            Assert.That(planned.Length, Is.EqualTo(room.Plan.NpcStock));
            Assert.That(planned.All(offer => offer.Price == room.Plan.NpcPrice && !offer.Sold), Is.True);
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant));
            Assert.That(run.GoldSpent, Is.Zero);
            Assert.That(run.Inventory, Is.Empty);

            string originalPreview = JsonUtility.ToJson(planned[0].Item);
            ProjectItem mutablePreview = planned[0].Item;
            mutablePreview.Name = "修改预览";
            mutablePreview.Id = "修改身份";
            mutablePreview.BaseAttributes.Clear();
            mutablePreview.Affixes.Clear();
            Assert.That(JsonUtility.ToJson(run.MerchantOffers[0].Item), Is.EqualTo(originalPreview));
            for (int i = 0; i < planned.Length; i++)
            {
                Assert.That(run.TryPurchase(planned[i].Id), Is.True);
                Assert.That(run.TryPurchase(planned[i].Id), Is.False, "已售商品不能重复扣费。");
                Assert.That(run.GoldSpent, Is.EqualTo(room.Plan.NpcPrice * (i + 1)));
                Assert.That(run.Inventory.Count, Is.EqualTo(i + 1));
            }
            Assert.That(run.Inventory.All(item => !item.Equipped), Is.True);
            Assert.That(run.Inventory.All(item => GameTables.CfgLootPool.GetAll().Any(row =>
                row.PoolID == room.Plan.LootPoolID && row.DefinitionId == item.DefinitionId)), Is.True);
            Assert.That(room.TryGetNpcStock("relic", out int stock) && stock == 0, Is.True);
            RoomDirection exitDirection = room.Exits.Keys.First();
            float edge = (float)(geometry.HalfExtent - geometry.ExitCenterInset);
            Vector3 exitPosition = exitDirection == RoomDirection.North ? new Vector3(0, 0, edge) :
                exitDirection == RoomDirection.South ? new Vector3(0, 0, -edge) :
                exitDirection == RoomDirection.East ? new Vector3(edge, 0, 0) : new Vector3(-edge, 0, 0);
            Assert.That(run.World.TrySetPosition(run.Player, exitPosition), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.CurrentRoom.RoomId, Is.EqualTo(room.RoomId), "商店未关闭时不能换图。");
            run.ClosePanel(ExpeditionPanelKind.Merchant);
            int neighbor = room.Exits.Values.First();
            MoveToRoom(run, neighbor);
            MoveToRoom(run, room.RoomId);
            ExpeditionMerchantOffer[] revisited = run.MerchantOffers.ToArray();
            CollectionAssert.AreEqual(planned.Select(offer => offer.Id).ToArray(), revisited.Select(offer => offer.Id).ToArray());
            CollectionAssert.AreEqual(planned.Select(offer => JsonUtility.ToJson(offer.Item)).ToArray(),
                revisited.Select(offer => JsonUtility.ToJson(offer.Item)).ToArray());
            Assert.That(revisited.All(offer => offer.Sold), Is.True);
            Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Merchant), "售罄后仍可浏览商品。");
            Assert.That(run.Inventory.Count, Is.EqualTo(room.Plan.NpcStock));
            Assert.That(run.AvailableGold, Is.EqualTo(goldBefore - room.Plan.NpcPrice * room.Plan.NpcStock));
            Assert.That(run.TryPurchase(revisited[0].Id), Is.False);
        });

        /// <summary>
        /// Boss 死亡只生成一次必掉奖励，回访保留死亡、清房和奖励记录。
        /// </summary>
        /// <returns>实际战斗和跨图回访任务。</returns>
        [UnityTest]
        public IEnumerator RoomBoss_DropsOnceAndStaysDefeatedAfterRevisit() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("BossRoomIntegration");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "boss-room-test", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            LevelRoomState room = run.Maps.Rooms.Single(value => value.Type == LevelRoomType.Boss);
            MoveToRoom(run, room.RoomId);
            var actors = new List<CombatActorSnapshot>();
            run.World.GetActorsSnapshot(actors);
            CombatActorSnapshot boss = actors.Single(actor => actor.RoomId == room.RoomId && actor.ActorId != 1);
            Assert.That(run.TryGetEnemyDefinition(boss.ActorId, out EnemyDefinition definition), Is.True);
            Assert.That(CombatEnemySkillConfig.Create(definition.ID).Length, Is.GreaterThanOrEqualTo(3));
            var basicCues = new HashSet<int>(GameTables.CfgCombatTimelineNodes.GetAll().Where(node => node.SkillID == definition.SkillID)
                .SelectMany(node => node.EffectIDs).Select(id => GameTables.CfgCombatEffects.GetById(id))
                .Where(effect => effect.Kind == (int)CombatEffectKind.Cue).Select(effect => effect.DefinitionID));
            bool sawAdditionalSkill = false;
            run.CombatEventPublished += fact =>
            {
                if (fact.ActorId == boss.ActorId && fact.Kind == CombatEventKind.Cue && !basicCues.Contains(fact.DefinitionId))
                    sawAdditionalSkill = true;
            };
            Assert.That(run.World.TrySetPosition(run.Player, boss.Position - Vector3.forward * 2.5f), Is.True);
            for (int tick = 0; tick < 10 && !sawAdditionalSkill; tick++) run.Advance(CombatWorld.TickSeconds);
            Assert.That(sawAdditionalSkill, Is.True, "Boss 必须实际选择并执行附加近身技能。");
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 10000), Is.True);
            Assert.That(run.World.TrySetPosition(run.Player, boss.Position - Vector3.forward * 1.5f), Is.True);
            for (int tick = 0; tick < 120 && !room.IsCleared; tick++) run.Advance(CombatWorld.TickSeconds);
            Assert.That(room.IsCleared, Is.True);
            ExpeditionDrop reward = run.Drops.Single(drop => drop.RoomId == room.RoomId);
            Assert.That(GameTables.CfgLootPool.GetAll().Any(row => row.PoolID == definition.DropPoolID &&
                row.DefinitionId == reward.Item.DefinitionId), Is.True);
            int experience = run.KillExperience;
            Assert.That(run.IsChoosingRogue, Is.True, "Boss 首次清房必出三选一。");
            long pausedTick = run.World.TickIndex;
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(pausedTick));
            var offer = run.PendingRogueChoice;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
            MoveToRoom(run, room.Exits.Values.First());
            MoveToRoom(run, room.RoomId);
            Assert.That(room.IsCleared, Is.True);
            Assert.That(run.Drops.Count(drop => drop.RoomId == room.RoomId), Is.EqualTo(1));
            Assert.That(run.Drops.Single(drop => drop.RoomId == room.RoomId), Is.SameAs(reward));
            Assert.That(run.KillExperience, Is.EqualTo(experience));
            Assert.That(run.IsChoosingRogue, Is.False, "回访不重复发清房奖励。");
            run.World.GetActorsSnapshot(actors);
            Assert.That(actors.Any(actor => actor.RoomId == room.RoomId && actor.ActorId != 1 && !actor.Dead), Is.False);
        });
    }
}
