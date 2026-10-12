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
        /// 完整快照恢复同一地图、角色位置、生成计划和未购买库存。
        /// </summary>
        /// <returns>真实资源与恢复单局的验证任务。</returns>
        [UnityTest]
        public IEnumerator RegionState_RoundTripKeepsWorldPlanAndFrozenOffers() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            run.SetTouchMove(Vector2.right);
            run.Advance(CombatWorld.TickSeconds * 5);
            run.SetTouchMove(Vector2.zero);
            Assert.That(run.World.TryStartSkill(run.Player, 1, default), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot snapshot, out string error), Is.True, error);
            Assert.That(snapshot.World.Casts, Is.Not.Empty);
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out var parts, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(parts, out var decoded, out error), Is.True, error);
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, decoded, out ExpeditionRun restored, out error), Is.True, error);
            using (restored)
            {
                using var scope = new LifetimeScope("RestoredRegionScene");
                ExpeditionVisualAssets assets = await ExpeditionVisualAssets.LoadAsync(scope, GameEntry.Get<IResourceModule>());
                restored.AttachScene(assets);
                Assert.That(restored.TryCaptureState(out ExpeditionRunSnapshot attached, out error), Is.True, error);
                Assert.That(JsonUtility.ToJson(attached.World), Is.EqualTo(JsonUtility.ToJson(snapshot.World)),
                    "场景绑定不能清空恢复中的施法、位移或动画提示。");
                Assert.That(restored.RunId, Is.EqualTo(run.RunId));
                Assert.That(restored.World.TickIndex, Is.EqualTo(run.World.TickIndex));
                Assert.That(JsonUtility.ToJson(restored.CurrentRegion.Plan), Is.EqualTo(JsonUtility.ToJson(run.CurrentRegion.Plan)));
                Assert.That(restored.World.TryGetActor(restored.Player, out CombatActorSnapshot actual), Is.True);
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot expected), Is.True);
                Assert.That(actual.Position, Is.EqualTo(expected.Position));
                Assert.That(actual.Health, Is.EqualTo(expected.Health));
                Assert.That(restored.CurrentRegion.Plan.Npcs.SelectMany(value => value.Offers).Select(value => value.Item.Id),
                    Is.EquivalentTo(run.CurrentRegion.Plan.Npcs.SelectMany(value => value.Offers).Select(value => value.Item.Id)));
            }
        });

        /// <summary>
        /// 开箱确认保存后展示冻结物品并暂停世界，关闭可继续，重启不重复展示或领取。
        /// </summary>
        /// <returns>真实宝箱、延迟保存、获得窗口和恢复单局的验证任务。</returns>
        [UnityTest]
        public IEnumerator RegionChest_UsesFrozenItemsAndDoesNotGrantSkillChoice() => UniTask.ToCoroutine(async () =>
        {
            var backend = new HeldInputSaveBackend();
            try
            {
                await SetupAsync(backend);
                Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
                ExpeditionRun run = ProjectServices.Session.CurrentRun;
                var chest = run.CurrentRegion.Plan.Chests.First();
                ProjectItem[] expected = chest.Items.Select(item => item.Copy()).ToArray();
                string[] expectedItems = expected.Select(item => JsonUtility.ToJson(item)).ToArray();
                int before = run.Inventory.Count;
                Assert.That(run.World.TrySetPosition(run.Player, chest.Position), Is.True);
                run.Advance(CombatWorld.TickSeconds);
                await WaitRunSavedAsync(run);
                await WaitForWindowAsync(ProjectServices.BattleHudWindow);
                for (int attempt = 0; attempt < run.InteractionCount && run.SelectedInteractionId != chest.Id; attempt++) run.CycleInteraction();
                Assert.That(run.SelectedInteractionId, Is.EqualTo(chest.Id));
                IUIModule ui = GameEntry.Get<IUIModule>();
                backend.HoldWrites();
                run.RequestInteraction();
                run.Advance(CombatWorld.TickSeconds);
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
                Assert.That(run.PendingRogueChoice, Is.Null);
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
                long tick = run.World.TickIndex, regionTick = run.CurrentRegion.EffectiveTick;
                double seconds = run.PresentationSeconds;
                CombatActorSnapshot frozen = InputPersistencePlayer(run);
                run.SetTouchMove(Vector2.right);
                run.RequestInteraction();
                run.Advance(1f);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
                Assert.That(run.CurrentRegion.EffectiveTick, Is.EqualTo(regionTick));
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
                Assert.That(resumed.CurrentRegion.Chests.Single(value => value.Id == chest.Id).ClaimedItemIds,
                    Is.EquivalentTo(expected.Select(item => item.Id)));
                Assert.That(resumed.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
                Assert.That(resumed.ChestLootItems, Is.Empty);
                Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.All(window => window.Id != ProjectServices.ChestLootWindow), Is.True);
                resumed.Advance(CombatWorld.TickSeconds);
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
