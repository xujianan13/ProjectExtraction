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
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 未清房不能推进，中间房出口仅显示下一房，每次成功推进只扣一次房间体力。
        /// </summary>
        /// <returns>真实清房、出口按钮与体力事务验证任务。</returns>
        [UnityTest]
        public IEnumerator RoomExit_RequiresCompletionAndChargesOneAdmission() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            int stamina = ProjectServices.Session.Stamina;
            Assert.That(run.TryContinueRoom(), Is.False);
            Assert.That(ProjectServices.Coordinator.TryEndRun(run, ProjectRunExitKind.Completed), Is.False);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
            await EnterExtractionConfirmationAsync(run);
            GameWindow exit = await WaitForWindowAsync(ProjectServices.RegionExitWindow);
            Assert.That(exit.Root.GetComponentsInChildren<Button>(false).Any(button =>
                button.GetComponentInChildren<Text>().text == "带回结束"), Is.False);
            long tick = run.World.TickIndex;
            Vector3 position = InputPersistencePlayer(run).Position;
            run.SetTouchMove(Vector2.right);
            run.Advance(1f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(position));
            ClickButton(exit, "留在本房");
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
            Assert.That(run.CurrentRoomIndex, Is.Zero);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick + 1));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.None), "关闭出口后仍在范围内，必须等离开再触碰才能重开。");
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(position));
            await EnterExtractionConfirmationAsync(run);
            await ClickExtractionConfirmationAsync("进入下一房");
            await WaitRunSavedAsync(run);
            Assert.That(run.CurrentRoomIndex, Is.EqualTo(1));
            Assert.That(run.CurrentRoomKind, Is.EqualTo(ExpeditionRoomKind.Chest));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina - PrototypeRules.RoomEntryCost));
            Assert.That(run.TryContinueRoom(), Is.False);
            Assert.That(ProjectServices.Coordinator.TryEndRun(run, ProjectRunExitKind.Completed), Is.False);
        });

        /// <summary>
        /// 完成不同房间的正式目标，不直接篡改完成或领取标记。
        /// </summary>
        /// <param name="run">当前正式单局。</param>
        /// <returns>本房目标和必要奖励保存完成任务。</returns>
        private static async UniTask CompleteCurrentRoomAsync(ExpeditionRun run)
        {
            if (run.IsCurrentRoomComplete) return;
            if (run.CurrentRoomKind == ExpeditionRoomKind.Chest)
            {
                foreach (var chest in run.CurrentRoom.Plan.Chests)
                {
                    if (run.CurrentRoom.Chests.Single(value => value.Id == chest.Id).Opened) continue;
                    Assert.That(run.World.TrySetPosition(run.Player, chest.Position), Is.True);
                    run.Advance(CombatWorld.TickSeconds);
                    await WaitRunSavedAsync(run);
                    await WaitForWindowAsync(ProjectServices.ChestLootWindow);
                    await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.ChestLootWindow);
                    await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
                }
            }
            else if (run.CurrentRoomKind != ExpeditionRoomKind.Npc)
            {
                foreach (string id in run.CurrentRoom.Slots.Select(slot => slot.SlotId).ToArray())
                    if (run.CurrentRoom.Slots.Single(slot => slot.SlotId == id).Phase == RegionSpawnPhase.Alive)
                        await DefeatSlotAsync(run, id);
                for (int index = 0; index < 30 && run.PendingBossChoice != null; index++)
                {
                    var choice = run.PendingBossChoice;
                    Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, 0), Is.True);
                    await WaitRunSavedAsync(run);
                }
            }
            await WaitRunSavedAsync(run);
            Assert.That(run.IsCurrentRoomComplete, Is.True, run.Message);
        }

        /// <summary>
        /// 真实击败最终 Boss 并领取全部奖励，等待自动终局且验证没有第二次手动通关。
        /// </summary>
        /// <param name="run">已经正式进入最终 Boss 房间的单局。</param>
        /// <returns>自动通关、唯一结果和历史记录确认任务。</returns>
        private static async UniTask CompleteFinalBossAsync(ExpeditionRun run)
        {
            Assert.That(run.IsFinalRoom, Is.True);
            Assert.That(run.CurrentRoomKind, Is.EqualTo(ExpeditionRoomKind.Boss));
            Assert.That(run.IsRunning, Is.True);
            Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
            await CompleteCurrentRoomAsync(run);
            Assert.That(run.CurrentRoom.BossDefeated, Is.True);
            Assert.That(run.CurrentRoom.BossRewardsCompleted, Is.True);
            Assert.That(run.PendingBossChoice, Is.Null);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(run.IsRunning, Is.False);
            Assert.That(ProjectServices.Coordinator.TryEndRun(run, ProjectRunExitKind.Completed), Is.False, "最终奖励确认后已经自动通关，不能再提交第二次出口终局。");
            ProjectSession session = ProjectServices.Session;
            Assert.That(session.LastResult.Outcome.RunId, Is.EqualTo(run.RunId));
            Assert.That(session.LastResult.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Completed));
            Assert.That(session.RunHistory.Count(entry => entry.RunId == run.RunId), Is.EqualTo(1));
        }

        /// <summary>
        /// 沿正式出口依次推进到首个指定类型房间。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <param name="kind">目标房间类型。</param>
        /// <returns>目标房间已经正式进入的任务。</returns>
        private static async UniTask AdvanceToRoomAsync(ExpeditionRun run, ExpeditionRoomKind kind)
        {
            for (int index = 0; index < run.RoomCount && run.CurrentRoomKind != kind; index++)
            {
                await EnterExtractionConfirmationAsync(run);
                Assert.That(run.TryContinueRoom(), Is.True, run.Message);
                await WaitRunSavedAsync(run);
                await UniTask.Yield();
            }
            Assert.That(run.CurrentRoomKind, Is.EqualTo(kind));
        }

        /// <summary>
        /// 等待业务候选确认，拒绝将尚未保存的内存变化作为成功证据。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>保存门禁释放任务。</returns>
        private static async UniTask WaitRunSavedAsync(ExpeditionRun run)
        {
            await UniTask.WaitUntil(() => !run.IsSavingRun).Timeout(TimeSpan.FromSeconds(15));
            Assert.That((await ProjectServices.Session.WaitForRunSavesAsync()).Success, Is.True);
        }

        /// <summary>
        /// 完成中间房并从范围外触碰上方出口，自动打开房间推进窗口。
        /// </summary>
        /// <param name="run">当前单局。</param>
        /// <returns>出口窗口就绪任务。</returns>
        private static async UniTask EnterExtractionConfirmationAsync(ExpeditionRun run)
        {
            Assert.That(run.IsFinalRoom, Is.False, "最终 Boss 全部领奖后自动通关，不再操作房间出口。");
            await CompleteCurrentRoomAsync(run);
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.EntryPosition), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            MoveToExtractionCenter(run);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            await WaitForWindowAsync(ProjectServices.RegionExitWindow);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.ExtractionConfirmation));
        }

        /// <summary>
        /// 将玩家放到本房配置的上方出口。
        /// </summary>
        /// <param name="run">当前单局。</param>
        private static void MoveToExtractionCenter(ExpeditionRun run) =>
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.ExitPosition), Is.True);

        /// <summary>
        /// 点击房间出口窗口中的真实按钮。
        /// </summary>
        /// <param name="caption">按钮文字前缀。</param>
        /// <returns>按钮操作后的逐帧任务。</returns>
        private static async UniTask ClickExtractionConfirmationAsync(string caption)
        {
            ClickButton(await WaitForWindowAsync(ProjectServices.RegionExitWindow), caption);
            await UniTask.Yield();
        }
    }
}
