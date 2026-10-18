using System;
using System.Collections;
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
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// Boss 击败后的未开箱、部分领取和全部领取均可零体力恢复，触箱续领后仍须手动出口唯一结算。
        /// </summary>
        /// <returns>真实触箱、跨启动恢复、冻结候选与唯一入库验证任务。</returns>
        [UnityTest]
        public IEnumerator BossCheckpoint_ThreeRewardStagesResumeFreeAndRequireExplicitExit() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await AdvanceToRoomAsync(run, ExpeditionRoomKind.Boss);
            foreach (string slotId in run.CurrentRoom.Slots.Select(slot => slot.SlotId).ToArray()) await DefeatSlotAsync(run, slotId);
            Assert.That(run.CurrentRoom.BossDefeated, Is.True);
            Assert.That(run.PendingBossChoice, Is.Null, "击败 Boss 只解锁奖励箱，不得自动弹出选择。");
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
            Assert.That(run.TryCaptureState(out var defeated, out string error), Is.True, error);
            ProjectSaveData profile = CreateCompletionRecoveryProfile(ProjectServices.Session, run);
            int serial = run.RoomAdmissionSerial;
            MemorySaveBackend backend = await LoadCompletionRecoveryAsync(defeated, profile);
            Assert.That(ProjectServices.Coordinator.RequiredResumeStamina, Is.Zero);
            Assert.That(ProjectServices.Coordinator.CanResume, Is.True);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            AssertBossRecoveryWaiting(run, serial);
            Assert.That(run.CurrentRoom.Chests.Single(chest => chest.Id == BossRecoveryChest(run).Id).Opened, Is.False);
            Assert.That(run.TryCaptureState(out var beforeTouch, out error), Is.True, error);
            Assert.That(beforeTouch.BossLoot.Batches, Is.Empty);
            await TouchBossRecoveryChestAsync(run);
            var firstChoice = run.PendingBossChoice;
            Assert.That(firstChoice.TotalRounds, Is.GreaterThan(1), "实际配置须有多轮奖励才能覆盖部分领取恢复。");
            Assert.That(firstChoice.Options.Count, Is.EqualTo(3));
            string firstOffer = firstChoice.OfferId;
            int firstRevision = firstChoice.Revision;
            string[] firstOptions = firstChoice.Options.Select(item => JsonUtility.ToJson(item)).ToArray();
            run.CloseBossLoot();
            run = await RestartBossRecoveryAsync(backend, serial);
            await TouchBossRecoveryChestAsync(run);
            Assert.That(run.PendingBossChoice.OfferId, Is.EqualTo(firstOffer));
            Assert.That(run.PendingBossChoice.Revision, Is.EqualTo(firstRevision));
            Assert.That(run.PendingBossChoice.Options.Select(item => JsonUtility.ToJson(item)), Is.EqualTo(firstOptions));
            string firstItemId = run.PendingBossChoice.Options[0].Id;
            Assert.That(run.TryChooseBoss(firstOffer, firstRevision, 0), Is.True);
            await WaitRunSavedAsync(run);
            Assert.That(run.Inventory.Count(item => item.Id == firstItemId), Is.EqualTo(1));
            Assert.That(run.CurrentRoom.BossRewardsCompleted, Is.False);
            if (run.PendingBossChoice == null) await TouchBossRecoveryChestAsync(run);
            var nextChoice = run.PendingBossChoice;
            string nextOffer = nextChoice.OfferId;
            int nextRevision = nextChoice.Revision;
            string[] nextOptions = nextChoice.Options.Select(item => JsonUtility.ToJson(item)).ToArray();
            string[] partialItems = run.Inventory.Select(item => item.Id).ToArray();
            run.CloseBossLoot();
            run = await RestartBossRecoveryAsync(backend, serial);
            Assert.That(run.Inventory.Select(item => item.Id), Is.EquivalentTo(partialItems));
            await TouchBossRecoveryChestAsync(run);
            Assert.That(run.PendingBossChoice.OfferId, Is.EqualTo(nextOffer));
            Assert.That(run.PendingBossChoice.Revision, Is.EqualTo(nextRevision));
            Assert.That(run.PendingBossChoice.Options.Select(item => JsonUtility.ToJson(item)), Is.EqualTo(nextOptions));
            Assert.That(run.TryChooseBoss(firstOffer, firstRevision, 0), Is.False, "已领取轮次不能通过旧回调再次入包。");
            for (int round = 0; round < nextChoice.TotalRounds && !run.CurrentRoom.BossRewardsCompleted; round++)
            {
                if (run.PendingBossChoice == null) await TouchBossRecoveryChestAsync(run);
                var choice = run.PendingBossChoice;
                Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, 0), Is.True);
                await WaitRunSavedAsync(run);
            }
            Assert.That(run.CurrentRoom.BossRewardsCompleted, Is.True);
            Assert.That(run.Inventory.Count(item => item.Id == firstItemId), Is.EqualTo(1));
            run.CloseBossLoot();
            Assert.That(run.TryConfirmExtraction(), Is.False, "全部领奖仍须触碰出口并由玩家确认。");
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
            string[] retained = run.EntryEquipment.Concat(run.Inventory).Select(item => item.Id).ToArray();
            run = await RestartBossRecoveryAsync(backend, serial);
            Assert.That(run.CurrentRoom.BossRewardsCompleted, Is.True);
            Assert.That(run.PendingBossChoice, Is.Null);
            Assert.That(run.EntryEquipment.Concat(run.Inventory).Select(item => item.Id), Is.EquivalentTo(retained));
            Assert.That(run.TryConfirmExtraction(), Is.False);
            await EnterExtractionConfirmationAsync(run);
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
            await ClickExtractionConfirmationAsync("带回结束");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectSession session = ProjectServices.Session;
            Assert.That(session.Stamina, Is.Zero);
            Assert.That(session.LastResult.Outcome.RunId, Is.EqualTo(defeated.RunId));
            Assert.That(session.LastResult.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Completed));
            Assert.That(session.LastResult.LostItems, Is.Empty);
            Assert.That(session.LastResult.RetainedItems.Select(item => item.Id), Is.EquivalentTo(retained));
            Assert.That(session.RunHistory.Count(entry => entry.RunId == defeated.RunId), Is.EqualTo(1));
            ProjectRunSnapshotReadResult settled = await session.ReadRunSnapshotAsync(defeated.RunId);
            Assert.That(settled.Success && !settled.Missing, Is.True, settled.Error);
            Assert.That(settled.Snapshot.RoomAdmissionSerial, Is.EqualTo(serial));
            Assert.That(settled.Snapshot.PendingOutcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Completed));
            int experience = session.Experience, level = session.Level, points = session.SkillPoints;
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            GameEntry.StopFramework();
            await SetupAsync(backend);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.ResultsReady));
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            Assert.That(ProjectServices.Session.Experience, Is.EqualTo(experience));
            Assert.That(ProjectServices.Session.Level, Is.EqualTo(level));
            Assert.That(ProjectServices.Session.SkillPoints, Is.EqualTo(points));
            Assert.That(ProjectServices.Session.RunHistory.Count(entry => entry.RunId == defeated.RunId), Is.EqualTo(1));
            foreach (string id in retained) Assert.That(ProjectServices.Session.Warehouse.Count(item => item.Id == id), Is.EqualTo(1));

            Assert.That(ExpeditionSnapshotCodec.TryEncode(defeated, out var parts, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(parts, out var dead, out error), Is.True, error);
            dead.PlayerState.Health = 0f;
            dead.PlayerState.Buffs.Clear();
            dead.PendingPlayerDeathId = ++dead.PlayerState.DeathIdWatermark;
            await LoadCompletionRecoveryAsync(dead, profile);
            Assert.That(ProjectServices.Coordinator.RequiredResumeStamina, Is.EqualTo(PrototypeRules.RoomEntryCost));
            Assert.That(ProjectServices.Coordinator.CanResume, Is.False);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.False);
            ProjectSaveData paidDeath = profile.Copy();
            paidDeath.Stamina = PrototypeRules.RoomEntryCost;
            await LoadCompletionRecoveryAsync(dead, paidDeath);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
            ExpeditionRun deadRun = ProjectServices.Session.CurrentRun;
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            Assert.That(deadRun.RoomAdmissionSerial, Is.EqualTo(serial + 1));
            Assert.That(deadRun.IsAwaitingDeathChoice, Is.True);
            Assert.That(deadRun.World.TryGetActor(deadRun.Player, out var deadPlayer), Is.True);
            Assert.That(deadPlayer.Dead && deadPlayer.Health == 0f, Is.True, "Boss 已击败也不能绕过死亡待选而自动复活。");
            Assert.That(deadRun.PendingBossChoice, Is.Null);
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
        });

        /// <summary>
        /// 确认存活 Boss 检查点只恢复等待交互状态，不重生敌人或自动打开奖励与结果。
        /// </summary>
        /// <param name="run">刚完成正式续关的 Boss 单局。</param>
        /// <param name="serial">续关前已确认的房间准入序号。</param>
        private static void AssertBossRecoveryWaiting(ExpeditionRun run, int serial)
        {
            Assert.That(run.CurrentRoom.BossDefeated, Is.True);
            Assert.That(run.RoomAdmissionSerial, Is.EqualTo(serial));
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            Assert.That(run.CanAcceptPlayerInput, Is.True);
            Assert.That(run.PendingBossChoice, Is.Null);
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.Playing));
            Assert.That(GameEntry.Get<IUIModule>().OpenedWindows.All(window => window.Id != ProjectServices.BossLootWindow), Is.True);
            var actors = new System.Collections.Generic.List<CombatActorSnapshot>();
            run.World.GetActorsSnapshot(actors);
            Assert.That(actors.Select(actor => actor.ActorId), Is.EqualTo(new[] { 1 }), "已击败的 Boss 不得在恢复时重新生成。");
            Assert.That(run.World.TryGetActor(run.Player, out var beforeMove), Is.True);
            run.SetTouchMove(Vector2.right);
            run.Advance(CombatWorld.TickSeconds);
            run.SetTouchMove(Vector2.zero);
            Assert.That(run.World.TryGetActor(run.Player, out var afterMove), Is.True);
            Assert.That(Vector3.Distance(beforeMove.Position, afterMove.Position), Is.GreaterThan(0f), "未主动触箱或出口时应能继续移动。");
            Assert.That(run.PendingBossChoice, Is.Null);
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
        }

        /// <summary>
        /// 从正式 Boss 房间计划读取唯一的奖励箱。
        /// </summary>
        /// <param name="run">已经击败 Boss 的真实单局。</param>
        /// <returns>固定计划中的 Boss 奖励箱。</returns>
        private static RegionChestPlan BossRecoveryChest(ExpeditionRun run) =>
            run.CurrentRoom.Plan.Chests.Single(chest => chest.Kind == RegionChestKind.BossReward);

        /// <summary>
        /// 从入口范围外重新触碰奖励箱，只在真实位置与战斗帧边界激活领奖。
        /// </summary>
        /// <param name="run">允许移动和触箱的当前 Boss 单局。</param>
        /// <returns>固定候选已经保存并可供选择的任务。</returns>
        private static async UniTask TouchBossRecoveryChestAsync(ExpeditionRun run)
        {
            run.CloseBossLoot();
            Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRoom.Plan.EntryPosition), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TrySetPosition(run.Player, BossRecoveryChest(run).Position), Is.True);
            run.Advance(CombatWorld.TickSeconds);
            await WaitRunSavedAsync(run);
            await WaitForAsync(() => run.PendingBossChoice != null);
        }

        /// <summary>
        /// 正常关闭并重开同一后端，通过 Home 的真实续关门禁恢复 Boss 检查点。
        /// </summary>
        /// <param name="backend">保留已确认账号和检查点的独立后端。</param>
        /// <param name="serial">续关前不应增加的准入序号。</param>
        /// <returns>已经激活且等待玩家交互的同一单局。</returns>
        private static async UniTask<ExpeditionRun> RestartBossRecoveryAsync(MemorySaveBackend backend, int serial)
        {
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            GameEntry.StopFramework();
            await SetupAsync(backend);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.HomeReady));
            Assert.That(ProjectServices.Coordinator.RequiredResumeStamina, Is.Zero);
            Assert.That(ProjectServices.Coordinator.CanResume, Is.True);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            AssertBossRecoveryWaiting(run, serial);
            return run;
        }

        /// <summary>
        /// 从实际最终房账号状态建立匹配的运行账本，模拟已击败 Boss 且尚未通关的零体力账号。
        /// </summary>
        /// <param name="session">已经确认最终 Boss 击杀经验的真实账号。</param>
        /// <param name="run">已经击败 Boss 且尚未触碰奖励箱的实际单局。</param>
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
        /// <param name="profile">匹配当前准入及永久经验水位的隔离账号。</param>
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
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(candidate.Stamina));
            return backend;
        }
    }
}
