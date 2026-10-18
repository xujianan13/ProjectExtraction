using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Common;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 房间检查点只记录玩家与业务事实，未完成怪物房重开后同一槽位不重复发经验或物品。
        /// </summary>
        /// <returns>真实击杀、检查点编码、暂离与同局恢复验证任务。</returns>
        [UnityTest]
        public IEnumerator RoomCheckpoint_RestartsCombatAndDeduplicatesPreviouslyRewardedEnemy() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            Assert.That(await coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            string slot = run.CurrentRoom.Slots.First().SlotId;
            PlaceCheckpointTargetNearEntry(run, slot);
            await DefeatSlotAsync(run, slot);
            Assert.That(run.IsCurrentRoomComplete, Is.False, "此用例需要保留至少一个未完成目标。");
            int experience = run.PlayerExperience, level = run.PlayerLevel, kills = run.KillExperience;
            string[] items = run.Inventory.Select(item => item.Id).ToArray();
            Assert.That(run.TryCaptureState(out var snapshot, out string error), Is.True, error);
            Assert.That(snapshot.PlayerState, Is.Not.Null);
            Assert.That(snapshot.Rooms.Single(room => room.Plan.Request.RegionIndex == run.CurrentRoomIndex).RewardedSlotIds, Does.Contain(slot));
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out var parts, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(parts, out var decoded, out error), Is.True, error);
            Assert.That(decoded.PlayerState.Health, Is.EqualTo(snapshot.PlayerState.Health));
            StringAssert.DoesNotContain("\"World\"", JsonUtility.ToJson(decoded));
            string runId = run.RunId;
            Assert.That(run.TryOpenPause(), Is.True);
            Assert.That(await coordinator.SuspendRunAsync(run), Is.True);
            int stamina = ProjectServices.Session.Stamina;
            long resumeCost = coordinator.RequiredResumeStamina;
            Assert.That(resumeCost, Is.EqualTo(PrototypeRules.RoomEntryCost));
            Assert.That(await coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            Assert.That(run.RunId, Is.EqualTo(runId));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina - resumeCost));
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(run.CurrentRoom.Plan.EntryPosition));
            Assert.That(run.CurrentRoom.Slots.Single(value => value.SlotId == slot).Phase,
                Is.EqualTo(GameLogic.Gameplay.Level.RegionSpawnPhase.Alive));
            Assert.That(run.CurrentRoom.Slots.Single(value => value.SlotId == slot).RewardClaimed, Is.True,
                "重建敌人必须继承已发奖槽位账本。");
            PlaceCheckpointTargetNearEntry(run, slot);
            await DefeatSlotAsync(run, slot);
            Assert.That(run.CurrentRoom.Slots.Where(value => value.RewardClaimed).Select(value => value.SlotId), Is.EquivalentTo(new[] { slot }),
                "去重验证期间不能同时击杀尚未发奖的其他目标。");
            Assert.That(run.KillExperience, Is.EqualTo(kills));
            Assert.That(run.PlayerExperience, Is.EqualTo(experience));
            Assert.That(run.PlayerLevel, Is.EqualTo(level));
            Assert.That(run.Inventory.Select(item => item.Id), Is.EquivalentTo(items));
        });

        /// <summary>
        /// 将去重验证目标放在入口空地，隔开其他怪物以避免真实范围攻击同时产生新收益。
        /// </summary>
        /// <param name="run">当前已经完成准入的单局。</param>
        /// <param name="slotId">需要两次击杀的同一稳定槽位。</param>
        private static void PlaceCheckpointTargetNearEntry(ExpeditionRun run, string slotId)
        {
            int actorId = run.CurrentRoom.Slots.Single(value => value.SlotId == slotId).CurrentActorId;
            Assert.That(run.World.TryFindActor(actorId, out CombatEntityRef target), Is.True);
            Assert.That(run.World.TrySetPosition(target, run.CurrentRoom.Plan.EntryPosition + Vector3.forward * 3f), Is.True);
        }

        /// <summary>
        /// 开箱确认保存后展示冻结物品并暂停世界，关闭可继续，重启不重复展示或领取。
        /// </summary>
        /// <returns>真实宝箱、延迟保存、获得窗口和恢复单局的验证任务。</returns>
        [UnityTest]
        public IEnumerator RoomChest_UsesFrozenItemsAndConfirmsBeforeShowingLoot() => UniTask.ToCoroutine(async () =>
        {
            var backend = new HeldInputSaveBackend();
            try
            {
                await SetupAsync(backend);
                Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
                ExpeditionRun run = ProjectServices.Session.CurrentRun;
                await AdvanceToRoomAsync(run, GameLogic.Gameplay.Level.ExpeditionRoomKind.Chest);
                var chest = run.CurrentRoom.Plan.Chests.First();
                ProjectItem[] expected = chest.Items.Select(item => item.Copy()).ToArray();
                string[] expectedItems = expected.Select(item => JsonUtility.ToJson(item)).ToArray();
                int before = run.Inventory.Count;
                backend.HoldWrites();
                Assert.That(run.World.TrySetPosition(run.Player, chest.Position), Is.True);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.SelectedInteractionId, Is.EqualTo(chest.Id));
                IUIModule ui = GameEntry.Get<IUIModule>();
                Assert.That(run.IsSavingRun, Is.True);
                Assert.That(backend.WaitingWrite, Is.True);
                Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
                Assert.That(run.ChestLootItems, Is.Empty, "尚未确认保存的内存领取不能发布获得列表。");
                Assert.That(ui.OpenedWindows.All(window => window.Id != ProjectServices.ChestLootWindow), Is.True);
                await UniTask.Yield();
                Assert.That(ui.OpenedWindows.All(window => window.Id != ProjectServices.ChestLootWindow), Is.True);
                backend.ReleaseWrites();
                await WaitRunSavedAsync(run);
                GameWindow loot = await WaitForWindowAsync(ProjectServices.ChestLootWindow);
                Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ChestLoot));
                Assert.That(run.Inventory.Count, Is.EqualTo(before + expected.Length));
                Assert.That(run.ChestLootItems.Select(item => JsonUtility.ToJson(item)), Is.EqualTo(expectedItems));
                foreach (ProjectItem item in expected)
                    Assert.That(JsonUtility.ToJson(run.Inventory.Single(value => value.Id == item.Id)), Is.EqualTo(JsonUtility.ToJson(item)));
                Assert.That(loot.Root.GetComponentsInChildren<Text>().Single(value => value.name == "DetailsText").text,
                    Does.StartWith(ProjectEquipmentText.Details(expected[0])), "获得窗口默认展示首件实际入包物品的完整详情。");
                Assert.That(loot.Root.GetComponentsInChildren<Button>().Count(value => value.name.StartsWith("ChestLootItem_")),
                    Is.EqualTo(expected.Length));
                ProjectItem selected = expected.Last();
                loot.Root.GetComponentsInChildren<Button>().Single(value => value.name == "ChestLootItem_" + selected.Id).onClick.Invoke();
                Assert.That(loot.Root.GetComponentsInChildren<Text>().Single(value => value.name == "DetailsText").text,
                    Does.StartWith(ProjectEquipmentText.Details(selected)), "点击实际获得物品后详情必须切换到对应实例。");
                Assert.That(run.CanAcceptPlayerInput, Is.False);
                Assert.That(run.FeedbackPaused, Is.True);
                long tick = run.World.TickIndex, regionTick = run.CurrentRoom.EffectiveTick;
                double seconds = run.PresentationSeconds;
                CombatActorSnapshot frozen = InputPersistencePlayer(run);
                run.SetTouchMove(Vector2.right);
                run.RequestInteraction();
                run.Advance(1f);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
                Assert.That(run.CurrentRoom.EffectiveTick, Is.EqualTo(regionTick));
                Assert.That(run.PresentationSeconds, Is.EqualTo(seconds));
                Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(frozen.Position));
                Assert.That(run.Inventory.Count, Is.EqualTo(before + expected.Length));
                ClickButton(loot, "继续探索");
                await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None &&
                    ui.OpenedWindows.All(window => window.Id != ProjectServices.ChestLootWindow));
                Assert.That(run.ChestLootItems, Is.Empty);
                Assert.That(run.CanAcceptPlayerInput, Is.True);
                Assert.That(run.FeedbackPaused, Is.False);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick + 1), "关闭窗口后只推进新帧，不补跑暂停期间的时间。");
                Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(frozen.Position), "关闭窗口不能继承暂停期间的移动意图。");
                await WaitRunSavedAsync(run);
                Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.EntryPosition), Is.True);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.World.TrySetPosition(run.Player, chest.Position), Is.True);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None), "已领取宝箱离开后再触碰也不能重开。");
                Assert.That(run.Inventory.Count, Is.EqualTo(before + expected.Length));
                string runId = run.RunId;
                string[] inventory = run.Inventory.Select(item => JsonUtility.ToJson(item)).ToArray();
                Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
                GameEntry.StopFramework();
                await SetupAsync(backend);
                Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
                ExpeditionRun resumed = ProjectServices.Session.CurrentRun;
                await WaitForWindowAsync(ProjectServices.BattleHudWindow);
                Assert.That(resumed.RunId, Is.EqualTo(runId));
                Assert.That(resumed.Inventory.Select(item => JsonUtility.ToJson(item)), Is.EqualTo(inventory));
                Assert.That(resumed.TryCaptureState(out var checkpoint, out string checkpointError), Is.True, checkpointError);
                Assert.That(checkpoint.Rooms.SelectMany(room => room.Chests).Single(value => value.Id == chest.Id).ClaimedItemIds,
                    Is.EquivalentTo(expected.Select(item => item.Id)));
                Assert.That(resumed.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
                Assert.That(resumed.ChestLootItems, Is.Empty);
                Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.All(window => window.Id != ProjectServices.ChestLootWindow), Is.True);
                Assert.That(resumed.World.TrySetPosition(resumed.Player, chest.Position), Is.True);
                resumed.Advance(CombatWorld.TickSeconds);
                Assert.That(resumed.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None), "已领取宝箱续关后触碰也不能重开。");
                for (int attempt = 0; attempt < resumed.InteractionCount; attempt++)
                {
                    Assert.That(resumed.SelectedInteractionId, Is.Not.EqualTo(chest.Id), "已领取宝箱恢复后不能再次成为领取目标。");
                    resumed.CycleInteraction();
                }
                await WaitRunSavedAsync(resumed);
                Assert.That(resumed.Inventory.Select(item => JsonUtility.ToJson(item)), Is.EqualTo(inventory));
            }
            finally
            {
                backend.ReleaseWrites();
            }
        });
    }
}
