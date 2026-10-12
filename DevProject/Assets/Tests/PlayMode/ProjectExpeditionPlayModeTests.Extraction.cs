using System;
using System.Collections;
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
        /// 区域出口只在尾王全部领奖完成后开放，留在本区不重复发奖。
        /// </summary>
        /// <returns>真实战斗、奖励与出口窗口验证任务。</returns>
        [UnityTest]
        public IEnumerator RegionExit_CancelKeepsWorldAndContinueDoesNotChargeAgain() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.CurrentRegion.ExitEnabled, Is.False);
            Assert.That(run.TryContinueRegion(), Is.False);
            await EnterExtractionConfirmationAsync(run);
            int stamina = ProjectServices.Session.Stamina;
            string identity = run.RunId;
            int items = run.Inventory.Count;
            int gold = ProjectServices.Session.Gold;
            GameWindow exit = await WaitForWindowAsync(ProjectServices.RegionExitWindow);
            string summary = exit.Root.GetComponentsInChildren<UnityEngine.UI.Text>(false)
                .Single(value => value.name == "PendingReturnSummary").text;
            StringAssert.Contains("待带回物品 " + items + " 件", summary);
            StringAssert.Contains("回收估值 " + run.Inventory.Sum(value => (long)value.SellGold) + " 金币", summary);
            StringAssert.Contains("尚未登记收藏", summary);
            Assert.That(ProjectServices.Session.Gold, Is.EqualTo(gold), "查看带回估值不应兑现金币。");
            long tick = run.World.TickIndex;
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            await ClickExtractionConfirmationAsync("留在本区");
            Assert.That(run.IsRunning && run.CurrentRegion.ExitEnabled, Is.True);
            Assert.That(run.Inventory.Count, Is.EqualTo(items));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None));
            await EnterExtractionConfirmationAsync(run);
            await ClickExtractionConfirmationAsync("继续下一区域");
            await WaitRunSavedAsync(run);
            Assert.That(run.CurrentRegionIndex, Is.EqualTo(1));
            Assert.That(run.RunId, Is.EqualTo(identity));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
            Assert.That(run.Inventory.Count, Is.EqualTo(items));
            Assert.That(run.CurrentRegion.ExitEnabled, Is.False);
            Assert.That(run.TryContinueRegion(), Is.False);
        });

        /// <summary>
        /// 带回结束兑现全部持有物，重试入口不会重复结算。
        /// </summary>
        /// <returns>真实终局和结果保存任务。</returns>
        [UnityTest]
        public IEnumerator RegionExit_ConfirmedReturnRetainsAllItemsOnce() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await EnterExtractionConfirmationAsync(run);
            string runId = run.RunId;
            string[] expected = run.Inventory.Select(value => value.Id).ToArray();
            await ClickExtractionConfirmationAsync("带回结束");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(ProjectServices.Session.LastResult.Outcome.Extracted, Is.True);
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Select(value => value.Id), Is.EquivalentTo(expected));
            Assert.That(ProjectServices.Session.LastResult.LostItems, Is.Empty);
            Assert.That(ProjectServices.Session.RunHistory.Count(value => value.RunId == runId), Is.EqualTo(1));
            Assert.That(run.TryConfirmExtraction(), Is.False);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Session.RunHistory.Count(value => value.RunId == runId), Is.EqualTo(1));
        });

        /// <summary>
        /// 退出位置失效或应用后台时，即使面板仍存在也不允许带回。
        /// </summary>
        /// <returns>后台暂停与权威位置的验证任务。</returns>
        [UnityTest]
        public IEnumerator RegionExit_BackgroundAndDistanceRejectStaleConfirmation() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await EnterExtractionConfirmationAsync(run);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            Assert.That(run.TryConfirmExtraction(), Is.False);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRegion.Plan.EntryPosition), Is.True);
            Assert.That(run.TryConfirmExtraction(), Is.False);
            Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
            await ClickExtractionConfirmationAsync("留在本区");
        });

        /// <summary>
        /// 通过真实伤害击败当前区域尾王，并逐轮领取已保存的候选。
        /// </summary>
        /// <param name="run">需要完成尾王的单局。</param>
        /// <returns>尾王和全部奖励完成任务。</returns>
        private static async UniTask CompleteRegionBossAsync(ExpeditionRun run)
        {
            if (run.CurrentRegion.BossRewardsCompleted) return;
            RegionSpawnSlotPlan bossPlan = run.CurrentRegion.Plan.SpawnSlots.Single(value => value.Kind == RegionEnemyKind.Boss);
            RegionSpawnSlotState bossSlot = run.CurrentRegion.Slots.Single(value => value.SlotId == bossPlan.SlotId);
            Assert.That(run.World.TryFindActor(bossSlot.CurrentActorId, out CombatEntityRef boss), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 100000), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Defense, 100000), Is.True);
            for (int tick = 0; tick < 600 && !run.CurrentRegion.BossDefeated; tick++)
            {
                await WaitRunSavedAsync(run);
                if (run.PendingRogueChoice != null)
                {
                    var choice = run.PendingRogueChoice;
                    Assert.That(run.TryChooseRogue(choice.OfferId, choice.Revision, 0), Is.True);
                    continue;
                }
                Assert.That(TryPlacePlayerNearActor(run, boss), Is.True);
                run.Advance(CombatWorld.TickSeconds);
            }
            Assert.That(run.CurrentRegion.BossDefeated, Is.True, run.Message);
            for (int attempt = 0; attempt < 30 && run.IsRunning && !run.CurrentRegion.BossRewardsCompleted; attempt++)
            {
                await WaitRunSavedAsync(run);
                if (run.PendingRogueChoice != null)
                {
                    var skill = run.PendingRogueChoice;
                    Assert.That(run.TryChooseRogue(skill.OfferId, skill.Revision, 0), Is.True);
                }
                else if (run.PendingBossChoice != null)
                {
                    var loot = run.PendingBossChoice;
                    Assert.That(run.TryChooseBoss(loot.OfferId, loot.Revision, 0), Is.True);
                }
                else await UniTask.Yield();
            }
            await WaitRunSavedAsync(run);
            Assert.That(run.CurrentRegion.BossRewardsCompleted, Is.True, run.Message);
        }

        /// <summary>
        /// 等待业务候选确认，拒绝把尚在保存的内存变化当作操作完成。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>保存门禁释放任务。</returns>
        private static async UniTask WaitRunSavedAsync(ExpeditionRun run)
        {
            await UniTask.WaitUntil(() => !run.IsSavingRun).Timeout(TimeSpan.FromSeconds(15));
            Assert.That((await ProjectServices.Session.WaitForRunSavesAsync()).Success, Is.True);
        }

        /// <summary>
        /// 到达已完成区域的出口并明确请求打开选择窗口。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>出口窗口就绪任务。</returns>
        private static async UniTask EnterExtractionConfirmationAsync(ExpeditionRun run)
        {
            await CompleteRegionBossAsync(run);
            await WaitRunSavedAsync(run);
            MoveToExtractionCenter(run);
            run.Advance(CombatWorld.TickSeconds);
            run.RequestInteraction();
            run.Advance(CombatWorld.TickSeconds);
            await UniTask.WaitUntil(() => GameEntry.Get<IUIModule>().OpenedWindows.Any(value => value.Id == ProjectServices.RegionExitWindow))
                .Timeout(TimeSpan.FromSeconds(10));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
        }

        /// <summary>
        /// 将玩家移到当前区域的配置出口。
        /// </summary>
        /// <param name="run">当前单局。</param>
        private static void MoveToExtractionCenter(ExpeditionRun run) =>
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRegion.Plan.ExitPosition), Is.True);

        /// <summary>
        /// 点击当前区域出口的真实按钮并等待窗口关闭。
        /// </summary>
        /// <param name="caption">带回结束、继续下一区域或留在本区。</param>
        /// <returns>窗口操作完成任务。</returns>
        private static async UniTask ClickExtractionConfirmationAsync(string caption)
        {
            GameWindow window = GameEntry.Get<IUIModule>().OpenedWindows.Single(value => value.Id == ProjectServices.RegionExitWindow);
            ClickButton(window, caption);
            await UniTask.Yield();
        }
    }
}
