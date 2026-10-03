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
using GameLogic.UI.Run;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证单局统计来自真实战斗与探索，并随终局保存到结算界面。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// Boss 房战斗事件只计一次，三选一和跨房回访不重复增加击杀或探索。
        /// </summary>
        /// <returns>真实资源、战斗步进和跨图探索的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Statistics_BossCombatChoiceAndRevisitMatchPublishedEvents() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("RunStatisticsCombat");
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "statistics-combat", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run), Is.True);
            using var ownedRun = run;
            run.AttachScene(assets);
            Assert.That(run.MarkReady() && run.Activate(), Is.True);
            var events = new List<CombatEvent>();
            run.CombatEventPublished += events.Add;
            var healing = new CombatAugmentDefinition
            {
                Id = "statistics-actual-heal", Trigger = CombatTriggerKind.Damaged, TargetSelf = true, Chance = 1f
            };
            healing.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Heal, Value = 2f });
            Assert.That(run.World.TryReplaceAugments(run.Player, "statistics-test", new[] { healing }), Is.True);
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3(0f, 0f, -8f)), Is.True);
            CombatEntityRef attacker = run.World.CreateActor(new CombatActorSpec
            {
                ActorId = 90000, RoomId = run.CurrentRoom.RoomId, Team = 2, Position = new Vector3(0f, 0f, -9f),
                MaxHealth = 100000f, Attack = 20f, Radius = 0.3f, AttackRange = 2f, AggroRange = 10f,
                MoveSpeed = 0f, BasicSkillId = 2
            });
            Assert.That(run.World.TryGetActor(attacker, out _), Is.True);
            for (int tick = 0; tick < 100 && !events.Any(fact => fact.Kind == CombatEventKind.Heal && fact.TargetActorId == 1); tick++)
                run.Advance(CombatWorld.TickSeconds);
            Assert.That(events.Any(fact => fact.Kind == CombatEventKind.Damage && fact.TargetActorId == 1 && fact.Value > 0), Is.True);
            Assert.That(events.Any(fact => fact.Kind == CombatEventKind.Heal && fact.TargetActorId == 1 && fact.Value > 0), Is.True);
            LevelRoomState bossRoom = run.Maps.Rooms.Single(room => room.Type == LevelRoomType.Boss);
            MoveToRoom(run, bossRoom.RoomId);
            var actors = new List<CombatActorSnapshot>();
            run.World.GetActorsSnapshot(actors);
            CombatActorSnapshot boss = actors.Single(actor => actor.RoomId == bossRoom.RoomId && actor.ActorId != 1);
            Assert.That(run.World.TrySetPosition(run.Player, boss.Position - Vector3.forward * 2.5f), Is.True);
            for (int tick = 0; tick < 10; tick++) run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 10000), Is.True);
            Assert.That(run.World.TrySetPosition(run.Player, boss.Position - Vector3.forward * 1.5f), Is.True);
            for (int tick = 0; tick < 120 && !bossRoom.IsCleared; tick++) run.Advance(CombatWorld.TickSeconds);
            Assert.That(bossRoom.IsCleared, Is.True);
            ProjectRunStatistics snapshot = run.CaptureStatistics();
            Assert.That(snapshot.HasData, Is.True);
            Assert.That(snapshot.TotalRooms, Is.EqualTo(run.Maps.Rooms.Count));
            Assert.That(snapshot.VisitedRooms, Is.EqualTo(run.Maps.Rooms.Count(room => room.IsVisited)));
            Assert.That(snapshot.ClearedCombatRooms, Is.EqualTo(1));
            Assert.That(snapshot.EnemyKills, Is.EqualTo(1));
            Assert.That(snapshot.BossRoomKills, Is.EqualTo(1));
            Assert.That(snapshot.DamageDealt, Is.EqualTo(events.Where(fact => fact.Kind == CombatEventKind.Damage &&
                fact.ActorId == 1 && fact.TargetActorId != 1).Sum(fact => (double)fact.Value)).Within(0.001));
            Assert.That(snapshot.DamageTaken, Is.EqualTo(events.Where(fact => fact.Kind == CombatEventKind.Damage &&
                fact.TargetActorId == 1).Sum(fact => (double)fact.Value)).Within(0.001));
            Assert.That(snapshot.HealingReceived, Is.EqualTo(events.Where(fact => fact.Kind == CombatEventKind.Heal &&
                fact.TargetActorId == 1).Sum(fact => (double)fact.Value)).Within(0.001));
            Assert.That(snapshot.DamageDealt, Is.GreaterThan(0));
            Assert.That(snapshot.DamageTaken, Is.GreaterThan(0));
            Assert.That(snapshot.HealingReceived, Is.GreaterThan(0));
            Assert.That(run.IsChoosingRogue, Is.True);
            long pausedTick = run.World.TickIndex;
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(pausedTick));
            Assert.That(run.CaptureStatistics().DamageDealt, Is.EqualTo(snapshot.DamageDealt));
            var offer = run.PendingRogueChoice;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.False);
            Assert.That(run.CaptureStatistics().RogueChoices, Is.EqualTo(1));
            MoveToRoom(run, bossRoom.Exits.Values.First());
            MoveToRoom(run, bossRoom.RoomId);
            ProjectRunStatistics revisited = run.CaptureStatistics();
            Assert.That(revisited.VisitedRooms, Is.EqualTo(run.Maps.Rooms.Count(room => room.IsVisited)));
            Assert.That(revisited.ClearedCombatRooms, Is.EqualTo(1));
            Assert.That(revisited.EnemyKills, Is.EqualTo(1));
            Assert.That(revisited.BossRoomKills, Is.EqualTo(1));
            Assert.That(revisited.RogueChoices, Is.EqualTo(1));
            snapshot.EnemyKills = 999;
            Assert.That(run.CaptureStatistics().EnemyKills, Is.EqualTo(1), "公开快照不能改动单局权威状态。");
        });

        /// <summary>
        /// 撤离结果冻结入场估值与统计，并在真实滚动窗口完整显示收益明细。
        /// </summary>
        /// <returns>场景切换、终局保存和结果窗口布局的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Statistics_ExtractionFreezesEconomyAndDisplaysReadableDetails() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            ProjectRunStatistics entry = run.CaptureStatistics();
            Assert.That(entry.EntryItemCount, Is.EqualTo(run.Inventory.Count));
            Assert.That(entry.EntryItemSellValue, Is.EqualTo(run.Inventory.Sum(item => (long)item.SellGold)));
            await TriggerExtractionAsync(run);
            ProjectRunResult result = ProjectServices.Session.LastResult;
            ProjectRunStatistics frozen = result.Outcome.Statistics;
            Assert.That(frozen.HasData, Is.True);
            Assert.That(frozen.VisitedRooms, Is.GreaterThanOrEqualTo(entry.VisitedRooms));
            Assert.That(frozen.EntryItemSellValue, Is.EqualTo(entry.EntryItemSellValue));
            ProjectRunEconomySummary economy = ProjectRunEconomy.Calculate(result);
            Assert.That(economy.HasEntrySnapshot, Is.True);
            Assert.That(economy.EntrySellValue, Is.EqualTo(entry.EntryItemSellValue));
            Assert.That(economy.RetainedSellValue, Is.EqualTo(result.RetainedItems.Sum(item => (long)item.SellGold)));
            Assert.That(economy.NetAssetValue, Is.EqualTo(economy.RetainedSellValue - economy.EntrySellValue - result.Outcome.GoldSpent));
            int visited = frozen.VisitedRooms;
            ProjectRunResult copy = result.Copy();
            copy.Outcome.Statistics.VisitedRooms = 999;
            Assert.That(result.Outcome.Statistics.VisitedRooms, Is.EqualTo(visited));
            RunResultWindow window = GameEntry.Get<IUIModule>().OpenedWindows.OfType<RunResultWindow>().Single();
            ScrollRect[] scrolls = window.Root.GetComponentsInChildren<ScrollRect>(true);
            Assert.That(scrolls.Length, Is.EqualTo(2));
            ScrollRect details = scrolls.Single(scroll => scroll.name == "ResultItems");
            Text[] labels = details.content.GetComponentsInChildren<Text>(true);
            foreach (string caption in new[] { "本局表现", "探索房间", "物品与支出", "入场装备估值", "携回物品估值", "资产变化" })
                Assert.That(labels.Any(label => label.text.Contains(caption)), Is.True, caption);
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            Assert.That(details.viewport.rect.height, Is.GreaterThan(0));
            Text[] detailBodies = labels.Where(value => value.text.Contains("探索房间") || value.text.Contains("入场装备估值")).ToArray();
            foreach (Text label in detailBodies)
                Assert.That(label.rectTransform.rect.height + 1f, Is.GreaterThanOrEqualTo(label.preferredHeight), label.text);
            Vector2 originalSize = details.content.sizeDelta;
            details.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 360f);
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            foreach (Text label in detailBodies)
            {
                Assert.That(label.rectTransform.rect.width, Is.GreaterThan(0f), label.text);
                Assert.That(label.rectTransform.rect.height + 1f, Is.GreaterThanOrEqualTo(label.preferredHeight), label.text);
            }
            details.content.sizeDelta = originalSize;
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            ClickButton(window, "返回主界面");
            await WaitForPhaseAsync(ProjectRunPhase.HomeReady);
            Assert.That(ProjectServices.Session.LastResult.Viewed, Is.True);
        });
    }
}




