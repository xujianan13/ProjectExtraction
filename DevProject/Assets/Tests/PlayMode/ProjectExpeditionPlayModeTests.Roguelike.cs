using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.UI.Battle;
using GameLogic.UI.Roguelike;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 真实场景宝箱和独立单局授权的集成测试。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 真实关卡中的宝箱保留原战利品并开启三选一，窗口丢失后可恢复选择。
        /// </summary>
        /// <returns>等待场景和窗口异步更新的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator LootChest_OpensDefaultChoiceAndResumesOneTickAfterSelection() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectSession session = ProjectServices.Session;
            ProjectCharacterStats homeStats = session.CharacterStats;
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(GameEntry.Get<ISceneModule>().CurrentScene, Is.TypeOf<GameLogic.Scenes.Expedition.ExpeditionScene>());
            MoveToRoom(run, run.Maps.Rooms.First(room => room.Type == LevelRoomType.Loot).RoomId);
            int inventoryBeforeChest = run.Inventory.Count;
            var chestLayout = run.CurrentRoom.Plan.Definition.Layout;
            Assert.That(run.World.TrySetPosition(run.Player, new Vector3((float)chestLayout.ChestX, 0, (float)chestLayout.ChestZ)), Is.True);
            long before = run.World.TickIndex;
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds * 4f);
            Assert.That(run.World.TickIndex, Is.EqualTo(before + 1), "宝箱交互后剩余三个 tick 不得继续战斗。");
            Assert.That(run.CurrentRoom.ChestOpened, Is.True);
            Assert.That(run.Inventory.Count, Is.EqualTo(inventoryBeforeChest + 1), "原宝箱战利品必须保留。");
            Assert.That(run.IsChoosingRogue, Is.True);
            RogueChoiceSnapshot offer = run.PendingRogueChoice;
            Assert.That(offer.Options.Count, Is.EqualTo(3));
            Assert.That(offer.CanRefresh, Is.False);
            Assert.That(offer.ExtraUnlocked, Is.False);
            Assert.That(run.TryRefreshRogue(offer.OfferId, offer.Revision), Is.False);
            Assert.That(await run.RequestExtraRogueChoiceAsync(offer.OfferId, offer.Revision), Is.False);
            IUIModule ui = GameEntry.Get<IUIModule>();
            await UniTask.WaitUntil(() => ui.OpenedWindows.Any(window => window.Id == ProjectServices.RogueChoiceWindow))
                .Timeout(TimeSpan.FromSeconds(10));
            RogueChoiceWindow window = ui.OpenedWindows.OfType<RogueChoiceWindow>().Single();
            Assert.That(window.RootTransform.GetComponentsInChildren<Button>(false).Length, Is.EqualTo(3));
            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.False);
            run.Advance(10f);
            Assert.That(run.World.TickIndex, Is.EqualTo(before + 1));
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            run.Advance(10f);
            Assert.That(run.World.TickIndex, Is.EqualTo(before + 1), "后台恢复后未选择仍应暂停。");
            Assert.That(await ui.CloseWindowAsync(ProjectServices.RogueChoiceWindow), Is.True);
            Assert.That(run.IsChoosingRogue, Is.True);
            BattleHudWindow hud = ui.OpenedWindows.OfType<BattleHudWindow>().Single();
            ClickButton(hud, "继续选择强化");
            await UniTask.WaitUntil(() => ui.OpenedWindows.Any(item => item.Id == ProjectServices.RogueChoiceWindow))
                .Timeout(TimeSpan.FromSeconds(10));
            window = ui.OpenedWindows.OfType<RogueChoiceWindow>().Single();
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot beforeReward), Is.True);
            int inventoryBeforeReward = run.Inventory.Count;
            RogueRewardDefinition reward = offer.Options[0].Reward;
            int skillPoints = session.SkillPoints;
            int powerRank = session.PowerRank;
            int vitalityRank = session.VitalityRank;
            int agilityRank = session.AgilityRank;
            Button firstCard = window.RootTransform.GetComponentsInChildren<Button>(false).First();
            Assert.That(firstCard.interactable, Is.True);
            firstCard.onClick.Invoke();
            Assert.That(run.IsChoosingRogue, Is.False);
            AssertRewardApplied(reward, beforeReward, run, inventoryBeforeReward);
            string rewardedItemId = reward.Kind == RogueRewardKind.RandomLoot ? run.Inventory.Last().Id : null;
            Assert.That(session.SkillPoints, Is.EqualTo(skillPoints));
            Assert.That(session.PowerRank, Is.EqualTo(powerRank));
            Assert.That(session.VitalityRank, Is.EqualTo(vitalityRank));
            Assert.That(session.AgilityRank, Is.EqualTo(agilityRank));
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.False, "同一奖励不能重复领取。");
            Assert.That(run.World.TickIndex, Is.EqualTo(before + 1));
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TickIndex, Is.EqualTo(before + 2), "恢复后仅处理新提交的一个 tick。");
            await TriggerExtractionAsync(run);
            if (rewardedItemId != null)
                Assert.That(session.Warehouse.Any(item => item.Id == rewardedItemId), Is.True, "三选一物品应随撤离进入仓库。");
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(session.PowerRank, Is.EqualTo(powerRank));
            Assert.That(session.VitalityRank, Is.EqualTo(vitalityRank));
            Assert.That(session.AgilityRank, Is.EqualTo(agilityRank));
            Assert.That(session.CharacterStats.MaxHealth, Is.EqualTo(homeStats.MaxHealth));
            Assert.That(session.CharacterStats.Attack, Is.EqualTo(homeStats.Attack));
        });

        /// <summary>
        /// 刷新使旧代次失效，第四项仅凭异步回执解锁；停止新来源取消迟到授权。
        /// </summary>
        /// <returns>等待假授权回执与取消的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ExtraChoice_RefreshUnlockSelectAndCancelLateReceipt() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            var policy = new RogueChoicePolicy { RefreshEnabled = true, ExtraChoiceEnabled = true, MaxRefreshes = 1 };
            var provider = new DeferredRogueAccessProvider();
            Assert.That(ExpeditionRun.TryCreate(ProjectServices.Session, "rogue-test-1", 1, 42,
                new List<ProjectItem>(), out ExpeditionRun run, policy, provider), Is.True);
            using var assetScope = new LifetimeScope("RoguelikePrefabTest");
            using var ownedRun = run;
            ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(assetScope, GameEntry.Get<IResourceModule>());
            Assert.That(assets, Is.Not.Null);
            run.AttachScene(assets);
            Assert.That(run.MarkReady(), Is.True);
            Assert.That(run.Activate(), Is.True);
            Assert.That(run.TryBeginRogueChoice("source-1"), Is.True);
            RogueChoiceSnapshot first = run.PendingRogueChoice;
            Assert.That(first.Options.Count, Is.EqualTo(4));
            Assert.That(first.Options[3].IsExtra, Is.True);
            Assert.That(first.ExtraUnlocked, Is.False);
            Assert.That(run.TryChooseRogue(first.OfferId, first.Revision, 3), Is.False);
            Assert.That(run.TryRefreshRogue(first.OfferId, first.Revision), Is.True);
            RogueChoiceSnapshot refreshed = run.PendingRogueChoice;
            Assert.That(refreshed.Revision, Is.EqualTo(first.Revision + 1));
            Assert.That(refreshed.CanRefresh, Is.False);
            Assert.That(run.TryChooseRogue(first.OfferId, first.Revision, 0), Is.False);
            Assert.That(run.TryRefreshRogue(first.OfferId, first.Revision), Is.False);
            Assert.That(run.TryChooseRogue(refreshed.OfferId, refreshed.Revision, 3), Is.False);
            UniTask<bool> granting = run.RequestExtraRogueChoiceAsync(refreshed.OfferId, refreshed.Revision).Preserve();
            Assert.That(provider.Request, Is.Not.Null);
            Assert.That(run.IsChoosingRogue, Is.True);
            Assert.That(run.TryChooseRogue(refreshed.OfferId, refreshed.Revision, 0), Is.False);
            provider.Complete(true);
            Assert.That(await granting, Is.True);
            Assert.That(run.IsChoosingRogue, Is.True, "回执只能解锁，不能代替玩家选择。");
            Assert.That(run.PendingRogueChoice.ExtraUnlocked, Is.True);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot beforeReward), Is.True);
            int inventoryBeforeReward = run.Inventory.Count;
            RogueRewardDefinition reward = run.PendingRogueChoice.Options[3].Reward;
            Assert.That(run.TryChooseRogue(refreshed.OfferId, refreshed.Revision, 3), Is.True);
            AssertRewardApplied(reward, beforeReward, run, inventoryBeforeReward);
            Assert.That(run.IsChoosingRogue, Is.False);
            Assert.That(run.TryChooseRogue(refreshed.OfferId, refreshed.Revision, 3), Is.False);
            Assert.That(run.TryBeginRogueChoice("source-2"), Is.True);
            RogueChoiceSnapshot second = run.PendingRogueChoice;
            UniTask<bool> late = run.RequestExtraRogueChoiceAsync(second.OfferId, second.Revision).Preserve();
            Assert.That(provider.Request.OfferId, Is.EqualTo(second.OfferId));
            int inventoryBeforeStop = run.Inventory.Count;
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot beforeStop), Is.True);
            run.Stop();
            Assert.That(await late.Timeout(TimeSpan.FromSeconds(2)), Is.False);
            provider.Complete(true);
            Assert.That(run.IsChoosingRogue, Is.False);
            Assert.That(run.Inventory.Count, Is.EqualTo(inventoryBeforeStop));
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot afterStop), Is.True);
            Assert.That(afterStop.MaxHealth, Is.EqualTo(beforeStop.MaxHealth));
            Assert.That(afterStop.Attack, Is.EqualTo(beforeStop.Attack));
        });

        /// <summary>
        /// 沿真实出口的最短路径进入指定房间。
        /// </summary>
        /// <param name="run">正在游玩的单局。</param>
        /// <param name="targetRoomId">目标地图编号。</param>
        private static void MoveToRoom(ExpeditionRun run, int targetRoomId)
        {
            var previous = new Dictionary<int, int> { [run.CurrentRoom.RoomId] = 0 };
            var queue = new Queue<int>();
            queue.Enqueue(run.CurrentRoom.RoomId);
            while (queue.Count > 0 && !previous.ContainsKey(targetRoomId))
            {
                int id = queue.Dequeue();
                run.Maps.TryGetRoom(id, out LevelRoomState room);
                foreach (int next in room.Exits.Values)
                    if (!previous.ContainsKey(next))
                    {
                        previous[next] = id;
                        queue.Enqueue(next);
                    }
            }
            Assert.That(previous.ContainsKey(targetRoomId), Is.True);
            var path = new Stack<int>();
            for (int id = targetRoomId; id != run.CurrentRoom.RoomId; id = previous[id]) path.Push(id);
            while (path.Count > 0)
            {
                int destination = path.Pop();
                RoomDirection direction = run.CurrentRoom.Exits.First(pair => pair.Value == destination).Key;
                var geometry = run.CurrentRoom.Plan.Definition.Layout;
                float edge = (float)(geometry.HalfExtent - geometry.ExitCenterInset);
                Vector3 position = direction == RoomDirection.North ? new Vector3(0, 0, edge) :
                    direction == RoomDirection.South ? new Vector3(0, 0, -edge) :
                    direction == RoomDirection.East ? new Vector3(edge, 0, 0) : new Vector3(-edge, 0, 0);
                Assert.That(run.World.TrySetPosition(run.Player, position), Is.True);
                run.RequestInteraction();
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.CurrentRoom.RoomId, Is.EqualTo(destination));
            }
        }

        /// <summary>
        /// 触发真实窗口中指定文字的按钮。
        /// </summary>
        /// <param name="window">已打开的窗口。</param>
        /// <param name="caption">按钮文字。</param>
        private static void ClickButton(GameWindow window, string caption)
        {
            Button button = window.RootTransform.GetComponentsInChildren<Button>(false).Single(item =>
                item.GetComponentInChildren<Text>().text == caption);
            Assert.That(button.interactable, Is.True);
            button.onClick.Invoke();
        }

        /// <summary>
        /// 按奖励实际类型确认只兑现一次背包物品或战斗属性。
        /// </summary>
        /// <param name="reward">刚刚选择的奖励。</param>
        /// <param name="before">选择前的角色属性。</param>
        /// <param name="run">已兑现奖励的单局。</param>
        /// <param name="inventoryBefore">选择前背包数量。</param>
        private static void AssertRewardApplied(RogueRewardDefinition reward, CombatActorSnapshot before, ExpeditionRun run,
            int inventoryBefore)
        {
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
            if (reward.Kind == RogueRewardKind.RandomLoot)
            {
                Assert.That(run.Inventory.Count, Is.EqualTo(inventoryBefore + 1));
                return;
            }
            if (reward.Kind == RogueRewardKind.CombatAugment)
            {
                Assert.That(run.Inventory.Count, Is.EqualTo(inventoryBefore));
                Assert.That(run.RogueUpgrades.Single(item => item.Id == reward.Id).Rank, Is.EqualTo(reward.Rank));
                return;
            }
            Assert.That(reward.Kind, Is.EqualTo(RogueRewardKind.AttributeBonus));
            Assert.That(run.Inventory.Count, Is.EqualTo(inventoryBefore));
            Assert.That(AttributeValue(after, reward.Attribute), Is.GreaterThan(AttributeValue(before, reward.Attribute)));
        }

        /// <summary>
        /// 从公开快照读取指定局内属性。
        /// </summary>
        /// <param name="actor">角色快照。</param>
        /// <param name="attribute">奖励属性类型。</param>
        /// <returns>对应的最终战斗属性。</returns>
        private static float AttributeValue(CombatActorSnapshot actor, ProjectAttributeType attribute) => attribute switch
        {
            ProjectAttributeType.MaxHealth => actor.MaxHealth,
            ProjectAttributeType.Attack => actor.Attack,
            ProjectAttributeType.Defense => actor.Defense,
            ProjectAttributeType.CriticalRating => actor.CriticalRating,
            ProjectAttributeType.CriticalDamage => actor.CriticalDamage,
            ProjectAttributeType.DodgeChance => actor.DodgeChance,
            ProjectAttributeType.DamageIncrease => actor.DamageIncrease,
            ProjectAttributeType.DamageReduction => actor.DamageReduction,
            _ => float.NaN
        };

        /// <summary>
        /// 由测试控制授权回执的异步提供者。
        /// </summary>
        private sealed class DeferredRogueAccessProvider : IRogueChoiceAccessProvider
        {
            private UniTaskCompletionSource<RogueChoiceAccessReceipt> completion;
            public RogueChoiceAccessRequest Request { get; private set; }

            /// <summary>
            /// 保存请求并等待测试回执。
            /// </summary>
            /// <param name="request">本局签发的授权请求。</param>
            /// <param name="ct">请求取消令牌。</param>
            /// <returns>测试提交回执后的任务。</returns>
            public UniTask<RogueChoiceAccessReceipt> RequestAsync(RogueChoiceAccessRequest request, CancellationToken ct)
            {
                Request = request;
                completion = new UniTaskCompletionSource<RogueChoiceAccessReceipt>();
                return completion.Task;
            }

            /// <summary>
            /// 对最近一次请求返回匹配的授权回执。
            /// </summary>
            /// <param name="granted">是否授权第四项。</param>
            public void Complete(bool granted)
            {
                Assert.That(Request, Is.Not.Null);
                completion.TrySetResult(new RogueChoiceAccessReceipt(Request.RunId, Request.OfferId, Request.Revision,
                    Request.RequestId, granted, Guid.NewGuid().ToString("N")));
            }
        }
    }
}
