using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.UI.Home;
using GameLogic.UI.Run;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证真实场景结算写入最近战绩，并从 Home 打开只读窗口。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 撤离结算成功后战绩和可滚动明细在 Home 中可见。
        /// </summary>
        /// <returns>场景切换、保存与窗口显示的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator History_ExtractionAppearsInHomeReadOnlyWindow() => UniTask.ToCoroutine(async () =>
        {
            GameEntry.StopFramework();
            var saves = new SaveModule();
            Assert.That(saves.ConfigureBackend(new MemorySaveBackend()).Success, Is.True);
            GameEntry.Register<ISaveModule>(saves);
            GameEntry.Register<IInputModule>(new HeadlessInputModule());
            GameEntry.RegisterDefaults();
            GameEntry.StartFramework();
            await InitializeEditorPackageAsync();
            ProjectContent.Register(GameEntry.Get<ISceneModule>(), GameEntry.Get<IUIModule>());
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            ProjectSession session = ProjectServices.Session;
            Assert.That(session.RunHistory, Is.Empty);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun run = session.CurrentRun;
            string runId = run.RunId;
            await TriggerExtractionAsync(run);
            Assert.That(session.RunHistory.Count, Is.EqualTo(1));
            ProjectRunHistoryEntry entry = session.RunHistory[0];
            Assert.That(entry.RunId, Is.EqualTo(runId));
            Assert.That(entry.Outcome, Is.EqualTo(ProjectRunHistoryOutcome.Extracted));
            Assert.That(entry.LevelId, Is.EqualTo(1));
            Assert.That(entry.EndUtc, Is.GreaterThan(0));
            Assert.That(entry.Statistics.HasData, Is.True);
            Assert.That(entry.HasNetAssetValue, Is.True);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            HomeWindow home = GameEntry.Get<IUIModule>().OpenedWindows.OfType<HomeWindow>().Single();
            ClickButton(home, "战绩");
            GameWindow window = await WaitForWindowAsync(ProjectServices.RunHistoryWindow);
            Assert.That(window, Is.TypeOf<RunHistoryWindow>());
            ScrollRect[] scrolls = window.Root.GetComponentsInChildren<ScrollRect>(true);
            foreach (string name in new[] { "LevelSummaries", "HistoryEntries", "HistoryDetails" })
                Assert.That(scrolls.Any(scroll => scroll.name == name), Is.True, name);
            Text[] labels = window.Root.GetComponentsInChildren<Text>(true);
            Assert.That(labels.Any(label => label.text.Contains("撤离率")), Is.True);
            Assert.That(labels.Any(label => label.text.Contains("净资产变化")), Is.True);
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            Assert.That(scrolls.All(scroll => scroll.viewport.rect.height > 0), Is.True);
            ScrollRect summaryScroll = scrolls.Single(scroll => scroll.name == "LevelSummaries");
            Vector2 originalSize = summaryScroll.content.sizeDelta;
            summaryScroll.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 360f);
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            foreach (Text label in summaryScroll.content.GetComponentsInChildren<Text>(true))
                Assert.That(label.rectTransform.rect.height + 1f, Is.GreaterThanOrEqualTo(label.preferredHeight), label.text);
            summaryScroll.content.sizeDelta = originalSize;
            ClickButton(window, "关闭");
            await WaitForAsync(() => GameEntry.Get<IUIModule>().OpenedWindows.All(opened => opened.Id != ProjectServices.RunHistoryWindow));
            Assert.That(session.RunHistory.Count, Is.EqualTo(1));
        });

        /// <summary>
        /// 结算保存失败时已提交战绩保持为空，冷恢复重试仅写入一次死亡记录。
        /// </summary>
        /// <returns>保存故障、冷恢复和重复确认的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator History_FailedSettlementRetryAddsOneDeathEntry() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupAsync(backend);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            ProjectSession session = ProjectServices.Session;
            coordinator.Changed += FailSettlement;

            /// <summary>
            /// 在结算阶段注入持久化写入失败。
            /// </summary>
            void FailSettlement()
            {
                if (coordinator.Phase == ProjectRunPhase.Settling) backend.FailWrites = true;
            }

            LogAssert.ignoreFailingMessages = true;
            string runId = session.CurrentRun.RunId;
            TriggerDeath(session.CurrentRun);
            await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
            Assert.That(session.RunHistory, Is.Empty);
            coordinator.Changed -= FailSettlement;
            await coordinator.CloseAsync();
            GameEntry.StopFramework();
            backend.FailWrites = false;
            await SetupAsync(backend);
            session = ProjectServices.Session;
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.ResultsReady));
            Assert.That(session.RunHistory.Count, Is.EqualTo(1));
            Assert.That(session.RunHistory[0].RunId, Is.EqualTo(runId));
            Assert.That(session.RunHistory[0].Outcome, Is.EqualTo(ProjectRunHistoryOutcome.Died));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(session.RunHistory.Count, Is.EqualTo(1));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.False);
            Assert.That(session.RunHistory.Count, Is.EqualTo(1));
        });
    }
}
