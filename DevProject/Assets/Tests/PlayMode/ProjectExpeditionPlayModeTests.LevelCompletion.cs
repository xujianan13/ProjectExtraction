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
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 按怪物、宝箱、怪物、商人、怪物和最终 Boss 顺序通关，房间体力逐次扣取且物品只到账一次。
        /// </summary>
        /// <returns>全关推进、奖励领取、历史及返城验证任务。</returns>
        [UnityTest]
        public IEnumerator LevelCompletion_LinearRoomsChargeAndSettleExactlyOnce() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectSession session = ProjectServices.Session;
            int stamina = session.Stamina;
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = session.CurrentRun;
            var expected = new[] { ExpeditionRoomKind.Combat, ExpeditionRoomKind.Chest, ExpeditionRoomKind.Combat,
                ExpeditionRoomKind.Npc, ExpeditionRoomKind.Combat, ExpeditionRoomKind.Boss };
            Assert.That(run.RoomCount, Is.EqualTo(expected.Length));
            for (int index = 0; index < run.RoomCount; index++)
            {
                Assert.That(run.CurrentRoomIndex, Is.EqualTo(index));
                Assert.That(run.CurrentRoomKind, Is.EqualTo(expected[index]));
                Assert.That(session.Stamina, Is.EqualTo(stamina - PrototypeRules.EntryCost - index * PrototypeRules.RoomEntryCost));
                if (run.IsFinalRoom)
                {
                    await CompleteFinalBossAsync(run);
                    break;
                }
                await EnterExtractionConfirmationAsync(run);
                Assert.That(run.TryContinueRoom(), Is.True, run.Message);
                await WaitRunSavedAsync(run);
            }
            string[] acquired = run.Inventory.Select(item => item.Id).ToArray();
            string[] entry = run.EntryEquipment.Select(item => item.Id).ToArray();
            Assert.That(ProjectServices.Coordinator.TryEndRun(run, ProjectRunExitKind.Completed), Is.False);
            Assert.That(session.LastResult.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Completed));
            Assert.That(session.LastResult.LostItems, Is.Empty);
            Assert.That(session.LastResult.RetainedItems.Select(item => item.Id), Is.EquivalentTo(entry.Concat(acquired)));
            Assert.That(session.RunHistory.Count(item => item.RunId == run.RunId), Is.EqualTo(1));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            foreach (string id in entry.Concat(acquired)) Assert.That(session.Warehouse.Count(item => item.Id == id), Is.EqualTo(1));
        });

    }
}
