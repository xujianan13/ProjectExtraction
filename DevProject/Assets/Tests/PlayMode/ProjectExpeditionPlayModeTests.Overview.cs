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
        /// 击杀经验即时提升永久等级并发放技能点，不等待终局结算。
        /// </summary>
        /// <returns>真实击杀与升级入账验证任务。</returns>
        [UnityTest]
        public IEnumerator Progression_KillImmediatelyLevelsPermanentProfileAndAwardsSkillPoints() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.Experience = PrototypeRules.ExperienceRequired(initial.Level) - 1;
            initial.SkillPoints = 0;
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await DefeatSlotAsync(run, run.CurrentRoom.Slots.First().SlotId);
            Assert.That(run.PlayerLevel, Is.GreaterThan(initial.Level));
            Assert.That(run.SkillPoints, Is.EqualTo((run.PlayerLevel - initial.Level) * PrototypeRules.SkillPointsPerLevel));
            Assert.That(ProjectServices.Session.Level, Is.EqualTo(run.PlayerLevel));
            Assert.That(ProjectServices.Session.SkillPoints, Is.EqualTo(run.SkillPoints));
            Assert.That(ProjectServices.Session.LastResult, Is.Null);
            Assert.That(run.KillExperience, Is.GreaterThan(0));
            Assert.That(run.IsClaimingBossLoot, Is.False);
        });

        /// <summary>
        /// 总览显示配置中的全部线性房间，打开关闭独立冻结战斗，不改变关卡背包。
        /// </summary>
        /// <returns>真实成长窗口与只读房间计划验证任务。</returns>
        [UnityTest]
        public IEnumerator Progression_ShowsLinearRoomsAndPausesWithoutInventoryMutation() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            var rooms = run.GetRoomOverview();
            Assert.That(rooms.Count, Is.EqualTo(run.RoomCount));
            Assert.That(rooms.Select(room => room.RoomIndex), Is.EqualTo(Enumerable.Range(0, run.RoomCount)));
            Assert.That(rooms.Last().IsFinal, Is.True);
            Assert.That(run.TryOpenProgression(), Is.True);
            GameWindow window = await WaitForWindowAsync(ProjectServices.ProgressionWindow);
            long tick = run.World.TickIndex;
            int items = run.Inventory.Count;
            run.Advance(10f);
            Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            Assert.That(run.Inventory.Count, Is.EqualTo(items));
            string content = string.Join("\n", window.Root.GetComponentsInChildren<Text>(false).Select(text => text.text));
            StringAssert.Contains("永久等级", content);
            StringAssert.Contains("可用技能点", content);
            foreach (var room in rooms) StringAssert.Contains(room.Name, content);
            await GameEntry.Get<IUIModule>().CloseWindowAsync(window.Id);
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
            Assert.That(run.CanAcceptPlayerInput, Is.True);
        });

        /// <summary>
        /// 技能点在本局直接升级永久技能，死亡结算和返回主城均保留已经提交的成长。
        /// </summary>
        /// <returns>永久技能提升和失败结算验证任务。</returns>
        [UnityTest]
        public IEnumerator Progression_UpgradePersistsThroughDeathSettlement() => UniTask.ToCoroutine(async () =>
        {
            ProjectSaveData initial = ProjectSaveData.CreateDefault();
            initial.SkillPoints = 20;
            await SetupAsync(new MemorySaveBackend(), initial);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.TryOpenProgression(), Is.True);
            await WaitForWindowAsync(ProjectServices.ProgressionWindow);
            ProjectSkill skill = run.PermanentSkills.First(value => value.Rank < value.MaxRank);
            int rank = skill.Rank, points = run.SkillPoints, cost = skill.NextCost;
            Assert.That((await run.UpgradePermanentSkillAsync(skill.Id)).Success, Is.True);
            await WaitRunSavedAsync(run);
            Assert.That(run.PermanentSkills.Single(value => value.Id == skill.Id).Rank, Is.EqualTo(rank + 1));
            Assert.That(run.SkillPoints, Is.EqualTo(points - cost));
            await GameEntry.Get<IUIModule>().CloseWindowAsync(ProjectServices.ProgressionWindow);
            TriggerDeath(run);
            await ConfirmPendingDeathAsync(run);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Session.Skills.Single(value => value.Id == skill.Id).Rank, Is.EqualTo(rank + 1));
            Assert.That(ProjectServices.Session.SkillPoints, Is.EqualTo(points - cost));
        });

    }
}
