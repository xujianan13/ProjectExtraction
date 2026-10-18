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
        /// 通关结果冻结实际击杀与物品收益，读取结果和历史不再次增加永久经验。
        /// </summary>
        /// <returns>结算统计、结果窗口与历史只读验证任务。</returns>
        [UnityTest]
        public IEnumerator Statistics_CompletionFreezesRewardsWithoutDoubleExperience() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await TriggerExtractionAsync(run);
            ProjectSession session = ProjectServices.Session;
            ProjectRunResult result = session.LastResult;
            Assert.That(result.Outcome.KillExperience, Is.GreaterThan(0));
            Assert.That(result.Outcome.Statistics.HasData, Is.True);
            int level = session.Level, experience = session.Experience;
            string frozen = JsonUtility.ToJson(result.Outcome.Statistics);
            GameWindow window = await WaitForWindowAsync(ProjectServices.ResultWindow);
            StringAssert.Contains("本局击杀经验", string.Join("\n", window.Root.GetComponentsInChildren<Text>(false).Select(text => text.text)));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(session.Level, Is.EqualTo(level));
            Assert.That(session.Experience, Is.EqualTo(experience));
            Assert.That(JsonUtility.ToJson(session.RunHistory.Single(value => value.RunId == run.RunId).Statistics), Is.EqualTo(frozen));
        });

    }
}
