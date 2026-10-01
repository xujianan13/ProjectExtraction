using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证项目的通用 UI 缩放配置覆盖反馈层与后续业务窗口。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 真实模块注册两个 Canvas 后，非法配置不改变有效参考尺寸，后续窗口沿用窗口根。
        /// </summary>
        /// <returns>等待项目初始化和业务窗口打开的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ProjectCanvasConfiguration_AppliesToFeedbackAndFutureWindows() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            IUIModule ui = GameEntry.Get<IUIModule>();
            Assert.That(ui, Is.Not.Null);
            CanvasScaler feedback = FindScaler("[GameUI]");
            CanvasScaler windows = FindScaler("[GameWindowRoot]");
            AssertProjectScale(feedback);
            AssertProjectScale(windows);

            Assert.That(ui.TryConfigureCanvas(new Vector2(float.NaN, 1920f)), Is.False);
            Assert.That(ui.TryConfigureCanvas(new Vector2(1080f, float.PositiveInfinity)), Is.False);
            Assert.That(ui.TryConfigureCanvas(new Vector2(0f, 1920f)), Is.False);
            Assert.That(ui.TryConfigureCanvas(new Vector2(1080f, 1920f), -0.01f), Is.False);
            Assert.That(ui.TryConfigureCanvas(new Vector2(1080f, 1920f), 1.01f), Is.False);
            AssertProjectScale(feedback);
            AssertProjectScale(windows);

            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            Assert.That(ui.OpenedWindows.Any(window => window.Id == ProjectServices.BattleHudWindow), Is.True);
            Assert.That(FindScaler("[GameWindowRoot]"), Is.SameAs(windows));
            AssertProjectScale(FindScaler("[GameUI]"));
            AssertProjectScale(windows);
        });

        /// <summary>
        /// 查找框架已经创建的指定 Canvas 根节点。
        /// </summary>
        /// <param name="name">已知框架根节点名称。</param>
        /// <returns>唯一匹配根节点上的缩放器。</returns>
        private static CanvasScaler FindScaler(string name) => UnityEngine.Object.FindObjectsOfType<CanvasScaler>(true)
            .Single(scaler => scaler.gameObject.name == name);

        /// <summary>
        /// 验证项目竖屏参考分辨率和宽高匹配模式。
        /// </summary>
        /// <param name="scaler">反馈层或业务窗口根节点的缩放器。</param>
        private static void AssertProjectScale(CanvasScaler scaler)
        {
            Assert.That(scaler, Is.Not.Null);
            Assert.That(scaler.uiScaleMode, Is.EqualTo(CanvasScaler.ScaleMode.ScaleWithScreenSize));
            Assert.That(scaler.referenceResolution, Is.EqualTo(new Vector2(1080f, 1920f)));
            Assert.That(scaler.screenMatchMode, Is.EqualTo(CanvasScaler.ScreenMatchMode.MatchWidthOrHeight));
            Assert.That(scaler.matchWidthOrHeight, Is.EqualTo(0.5f));
        }
    }
}
