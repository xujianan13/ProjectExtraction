using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 最终领奖检查点在终局前中断后，零体力仍可免准入恢复并唯一通关，未领奖和死亡待选保持收费门禁。
        /// </summary>
        /// <returns>真实领奖边界、冷恢复准入和重复启动结算验证任务。</returns>
        [UnityTest]
        public IEnumerator CompletionCheckpoint_ZeroStaminaRecoversWithoutAdmissionOrDuplicateSettlement() => UniTask.ToCoroutine(async () =>
        {
            var originalBackend = new HeldInputSaveBackend();
            ExpeditionRunSnapshot completed = null;
            ExpeditionRunSnapshot awaitingLastReward;
            ProjectSaveData profile;
            try
            {
                await SetupAsync(originalBackend);
                Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
                ExpeditionRun run = ProjectServices.Session.CurrentRun;
                await AdvanceToRoomAsync(run, ExpeditionRoomKind.Boss);
                foreach (string slotId in run.CurrentRoom.Slots.Select(slot => slot.SlotId).ToArray())
                    await DefeatSlotAsync(run, slotId);
                while (run.PendingBossChoice.Round < run.PendingBossChoice.TotalRounds)
                {
                    var choice = run.PendingBossChoice;
                    Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, 0), Is.True);
                    await WaitRunSavedAsync(run);
                }
                Assert.That(run.TryCaptureState(out awaitingLastReward, out string error), Is.True, error);
                profile = CreateCompletionRecoveryProfile(ProjectServices.Session, run);
                var finalChoice = run.PendingBossChoice;
                originalBackend.HoldWrites();
                Assert.That(run.TryChooseBoss(finalChoice.OfferId, finalChoice.Revision, 0), Is.True);
                Assert.That(originalBackend.WaitingWrite, Is.True);
                Assert.That(run.CurrentRoom.BossRewardsCompleted, Is.True);
                Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
                Assert.That(run.TryCaptureState(out completed, out error), Is.True, error);
                Assert.That(completed.HasPendingOutcome, Is.False, "必须捕获最后领奖保存与 EndRun 之间的真实业务边界。");
            }
            finally { originalBackend.ReleaseWrites(); }
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);

            await LoadCompletionRecoveryAsync(awaitingLastReward, profile);
            Assert.That(ProjectServices.Coordinator.RequiredResumeStamina, Is.EqualTo(PrototypeRules.RoomEntryCost));
            Assert.That(ProjectServices.Coordinator.CanResume, Is.False, "尚未领完 Boss 奖励时仍需支付战斗房续关费用。");
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.False);

            Assert.That(ExpeditionSnapshotCodec.TryEncode(completed, out var completedParts, out string copyError), Is.True, copyError);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(completedParts, out var dead, out copyError), Is.True, copyError);
            dead.PlayerState.Health = 0f;
            dead.PlayerState.Buffs.Clear();
            dead.PendingPlayerDeathId = ++dead.PlayerState.DeathIdWatermark;
            await LoadCompletionRecoveryAsync(dead, profile);
            Assert.That(ProjectServices.Coordinator.RequiredResumeStamina, Is.EqualTo(PrototypeRules.RoomEntryCost));
            Assert.That(ProjectServices.Coordinator.CanResume, Is.False, "死亡待选不能借最终领奖标记免除战斗房续关费用。");
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.False);
            Assert.That(ProjectServices.Session.LastResult, Is.Null);

            MemorySaveBackend recoveredBackend = await LoadCompletionRecoveryAsync(completed, profile);
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            Assert.That(coordinator.RequiredResumeStamina, Is.Zero);
            Assert.That(coordinator.CanResume, Is.True, "最终领奖已确认的存活检查点不应被零体力挡住。");
            Assert.That(await coordinator.ResumeRunAsync(), Is.True);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectSession session = ProjectServices.Session;
            Assert.That(session.Stamina, Is.Zero);
            Assert.That(session.LastResult.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Completed));
            Assert.That(session.LastResult.Outcome.RunId, Is.EqualTo(completed.RunId));
            Assert.That(session.LastResult.LostItems, Is.Empty);
            string[] retained = completed.EntryEquipment.Concat(completed.Inventory).Select(item => item.Id).ToArray();
            Assert.That(session.LastResult.RetainedItems.Select(item => item.Id), Is.EquivalentTo(retained));
            Assert.That(session.RunHistory.Count(entry => entry.RunId == completed.RunId), Is.EqualTo(1));
            ProjectRunSnapshotReadResult settled = await session.ReadRunSnapshotAsync(completed.RunId);
            Assert.That(settled.Success && !settled.Missing, Is.True, settled.Error);
            Assert.That(settled.Snapshot.RoomAdmissionSerial, Is.EqualTo(completed.RoomAdmissionSerial));
            Assert.That(settled.Snapshot.PendingOutcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Completed));
            int experience = session.Experience, level = session.Level, points = session.SkillPoints;
            Assert.That(await coordinator.CloseAsync(), Is.True);
            GameEntry.StopFramework();
            await SetupAsync(recoveredBackend);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.ResultsReady));
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            Assert.That(ProjectServices.Session.Experience, Is.EqualTo(experience));
            Assert.That(ProjectServices.Session.Level, Is.EqualTo(level));
            Assert.That(ProjectServices.Session.SkillPoints, Is.EqualTo(points));
            Assert.That(ProjectServices.Session.RunHistory.Count(entry => entry.RunId == completed.RunId), Is.EqualTo(1));
            foreach (string id in retained) Assert.That(ProjectServices.Session.Warehouse.Count(item => item.Id == id), Is.EqualTo(1));
        });

        /// <summary>
        /// 从实际最终房账号状态建立匹配的运行账本，模拟最后领奖已保存但尚未记录终局的账号。
        /// </summary>
        /// <param name="session">已经确认最终 Boss 击杀经验的真实账号。</param>
        /// <param name="run">尚未领取最后一轮奖励的实际单局。</param>
        /// <returns>携入、成长和准入水位匹配且零体力的隔离账号候选。</returns>
        private static ProjectSaveData CreateCompletionRecoveryProfile(ProjectSession session, ExpeditionRun run)
        {
            ProjectSaveData profile = ProjectSaveData.CreateDefault();
            profile.Level = session.Level;
            profile.Experience = session.Experience;
            profile.SkillPoints = session.SkillPoints;
            profile.PowerRank = session.PowerRank;
            profile.VitalityRank = session.VitalityRank;
            profile.AgilityRank = session.AgilityRank;
            profile.Gold = session.Gold;
            profile.Warehouse = session.Warehouse.Concat(run.EntryEquipment).Select(item => item.Copy()).ToList();
            Assert.That(ProjectRunRecordRules.TryAdmit(profile, run.RunId, run.LevelId, run.Seed, run.EntryEquipment, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(profile, run.RunId, out error), Is.True, error);
            profile.ActiveRunAwardedKillExperience = run.KillExperience;
            profile.ActiveRoomAdmissionSerial = run.RoomAdmissionSerial;
            profile.Stamina = 0;
            Assert.That(ProjectSaveData.Validate(profile), Is.Null);
            return profile;
        }

        /// <summary>
        /// 将同一业务边界写入独立日志并通过真实协调器冷初始化，不调用关卡内部恢复捷径。
        /// </summary>
        /// <param name="snapshot">实际领取边界或死亡待选的独立检查点。</param>
        /// <param name="profile">匹配当前准入及永久经验水位的零体力账号。</param>
        /// <returns>用于继续重启核对唯一结算的独立内存后端。</returns>
        private static async UniTask<MemorySaveBackend> LoadCompletionRecoveryAsync(ExpeditionRunSnapshot snapshot, ProjectSaveData profile)
        {
            var persistence = GameTables.CfgRunPersistence.GetById(1);
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            GameEntry.StopFramework();
            var backend = new MemorySaveBackend();
            Assert.That((await backend.InitializeAsync()).Success, Is.True);
            Assert.That(ExpeditionSnapshotCodec.TryEncode(snapshot, out var components, out string error), Is.True, error);
            var journal = new SaveStateJournal(backend, "ProjectExtraction/prototype/local/run-state", persistence.CheckpointTicks, persistence.MaxStateBytes);
            Assert.That((await journal.CommitAsync(snapshot.RunId, components)).Success, Is.True);
            ProjectSaveData candidate = profile.Copy();
            candidate.ClockUtc = candidate.RecoveryUtc = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            await SetupAsync(backend, candidate);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.HomeReady));
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            return backend;
        }
    }
}
