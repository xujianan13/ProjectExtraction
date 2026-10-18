using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Advertising;
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
        /// 首次入场要求准入加第一房体力，体力不足时不创建活动局；下一房不足时不推进。
        /// </summary>
        /// <returns>入场拒绝、成功入场和房间收费门禁验证任务。</returns>
        [UnityTest]
        public IEnumerator Admission_RequiresEntryAndRoomStaminaWithoutPartialCharge() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Stamina = PrototypeRules.EntryCost - 1;
            await SetupAsync(new MemorySaveBackend(), initial);
            int stamina = ProjectServices.Session.Stamina;
            Assert.That(ProjectServices.Coordinator.RequiredEntryStamina, Is.EqualTo(PrototypeRules.EntryCost));
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.False);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
            Assert.That(ProjectServices.Session.ActiveRunId, Is.Empty);
            initial.Stamina = PrototypeRules.EntryCost;
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            await EnterExtractionConfirmationAsync(run);
            Assert.That(run.TryContinueRoom(), Is.False);
            Assert.That(run.CurrentRoomIndex, Is.Zero);
            Assert.That(ProjectServices.Session.Stamina, Is.Zero);
            StringAssert.Contains("体力", run.Message);
        });

        /// <summary>
        /// 未领取宝箱暂离后免费续关，保持同一冻结奖励和独立背包，不重新收取关卡准入费用。
        /// </summary>
        /// <returns>宝箱检查点、主城门禁与零费用续关验证任务。</returns>
        [UnityTest]
        public IEnumerator Suspension_UnclaimedChestResumesSameRunForZeroStamina() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            Assert.That(await coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await AdvanceToRoomAsync(run, ExpeditionRoomKind.Chest);
            string runId = run.RunId;
            string chest = JsonUtility.ToJson(run.CurrentRoom.Plan.Chests.Single());
            int room = run.CurrentRoomIndex;
            Assert.That(run.TryOpenPause(), Is.True);
            Assert.That(await coordinator.SuspendRunAsync(run), Is.True);
            Assert.That(coordinator.Phase, Is.EqualTo(ProjectRunPhase.HomeReady));
            Assert.That(ProjectServices.Session.ActiveRunId, Is.EqualTo(runId));
            Assert.That(coordinator.CanStart, Is.False);
            Assert.That(coordinator.RequiredResumeStamina, Is.Zero);
            int stamina = ProjectServices.Session.Stamina;
            Assert.That(await coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            Assert.That(run.RunId, Is.EqualTo(runId));
            Assert.That(run.CurrentRoomIndex, Is.EqualTo(room));
            Assert.That(JsonUtility.ToJson(run.CurrentRoom.Plan.Chests.Single()), Is.EqualTo(chest));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
            Assert.That(run.CurrentRoom.Chests.Single().Opened, Is.False);
        });

        /// <summary>
        /// NPC 房暂离后续关推进到下一房，保留已确认交易物品且只扣下一房的一次体力。
        /// </summary>
        /// <returns>NPC 离开、交易状态和下一房准入验证任务。</returns>
        [UnityTest]
        public IEnumerator Suspension_NpcRoomContinuesAtNextRoomWithCommittedPurchase() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Gold = 10000;
            await SetupAsync(new MemorySaveBackend(), initial);
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            Assert.That(await coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await OpenRegionMerchantAsync(run);
            ExpeditionMerchantOffer offer = run.MerchantOffers.First();
            Assert.That(run.TryBuyMerchantOffer(offer.Id), Is.True);
            await WaitRunSavedAsync(run);
            int room = run.CurrentRoomIndex;
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.ExpeditionMerchantWindow);
            Assert.That(run.TryOpenPause(), Is.True);
            Assert.That(await coordinator.SuspendRunAsync(run), Is.True);
            int stamina = ProjectServices.Session.Stamina;
            Assert.That(coordinator.RequiredResumeStamina, Is.EqualTo(PrototypeRules.RoomEntryCost));
            Assert.That(await coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            Assert.That(run.CurrentRoomIndex, Is.EqualTo(room + 1));
            Assert.That(run.Inventory.Count(item => item.Id == offer.Item.Id), Is.EqualTo(1));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina - PrototypeRules.RoomEntryCost));
            Assert.That(run.GoldSpent, Is.EqualTo(offer.Price));
        });

    }
}
