using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Scaffold;
using GameLogic.UI.Guide;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 使用真实窗口验证指南完成记录与暂停菜单重读。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 在 Home 翻完指南后持久化版本，暂停菜单重读时本局仍保持暂停。
        /// </summary>
        /// <returns>等待指南、场景与存档操作的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Guide_HomeCompletionAndPauseRereadKeepRunPaused() => UniTask.ToCoroutine(async () =>
        {
            await SetupRealHomeAsync(new MemorySaveBackend());
            ProjectSession session = ProjectServices.Session;
            IUIModule ui = GameEntry.Get<IUIModule>();
            Assert.That(session.Preferences.GuideVersion, Is.EqualTo(0));

            GameWindow home = await WaitForWindowAsync(ContentIds.HomeWindow);
            ClickButton(home, "新手指引");
            GameWindow guide = await WaitForWindowAsync(ProjectServices.GuideWindow);
            Assert.That(guide, Is.TypeOf<ProjectGuideWindow>());
            for (int page = 1; page < ProjectGuideConfig.All().Count; page++) ClickButton(guide, "下一项");
            ClickButton(guide, "已了解");
            await WaitForAsync(() => session.Preferences.GuideVersion == 1);
            await WaitForAsync(() => ui.OpenedWindows.All(window => window.Id != ProjectServices.GuideWindow));

            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun run = session.CurrentRun;
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            ClickButton(hud, "暂停");
            GameWindow pause = await WaitForWindowAsync(ProjectServices.PauseWindow);
            ClickButton(pause, "玩法帮助");
            guide = await WaitForWindowAsync(ProjectServices.GuideWindow);
            for (int page = 1; page < ProjectGuideConfig.All().Count; page++) ClickButton(guide, "下一项");
            ClickButton(guide, "已了解");
            await WaitForAsync(() => ui.OpenedWindows.All(window => window.Id != ProjectServices.GuideWindow));
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Pause));
            Assert.That(run.FeedbackPaused, Is.True);
        });
    }
}
