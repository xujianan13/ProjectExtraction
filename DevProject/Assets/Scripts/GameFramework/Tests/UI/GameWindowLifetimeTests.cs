using System;
using System.Collections;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameFramework.Tests.UI
{
    /// <summary>
    /// 验证缓存窗口每轮打开作用域及异常清理。
    /// </summary>
    public sealed class GameWindowLifetimeTests
    {
        private GameWindowManager _manager;

        /// <summary>
        /// 销毁窗口及运行时对象。
        /// </summary>
        /// <returns>清理协程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _manager?.Shutdown();
            _manager = null;
            yield return null;
        }

        /// <summary>
        /// 缓存重新打开时更换轮次作用域，重复聚焦不重复绑定。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CachedWindow_ReopensWithFreshScope_RefocusKeepsCurrentScope()
        {
            CreateManager();
            var opening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => opening.IsCompleted);
            Assert.IsFalse(opening.IsFaulted);
            ProbeWindow window = (ProbeWindow)opening.Result;
            LifetimeScope first = window.CurrentOpenScope;
            LifetimeScope instance = window.CurrentInstanceScope;
            Assert.AreEqual(1, window.OpenCount);
            Assert.AreEqual(1, window.ShowCount);

            var refocus = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => refocus.IsCompleted);
            Assert.AreSame(window, refocus.Result);
            Assert.AreSame(first, window.CurrentOpenScope);
            Assert.AreEqual(1, window.OpenCount);
            Assert.AreEqual(2, window.ShowCount);

            var closing = _manager.CloseAsync(1).AsTask();
            yield return new WaitUntil(() => closing.IsCompleted);
            Assert.IsTrue(closing.Result);
            Assert.IsFalse(first.IsActive);
            Assert.IsNull(window.CurrentOpenScope);
            Assert.AreEqual(1, window.CleanedOpenCount);
            Assert.IsTrue(instance.IsActive);

            var reopening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => reopening.IsCompleted);
            Assert.AreSame(window, reopening.Result);
            Assert.AreNotSame(first, window.CurrentOpenScope);
            Assert.AreSame(instance, window.CurrentInstanceScope);
            Assert.AreEqual(2, window.OpenCount);
            Assert.AreEqual(3, window.ShowCount);
        }

        /// <summary>
        /// 缓存窗口打开失败时清理半初始化轮次并允许下次打开。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CachedWindow_FailedOpen_CleansRoundAndCanRetry()
        {
            CreateManager();
            var opening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => opening.IsCompleted);
            ProbeWindow window = (ProbeWindow)opening.Result;
            var closing = _manager.CloseAsync(1).AsTask();
            yield return new WaitUntil(() => closing.IsCompleted);
            window.FailNextOpen = true;

            var failed = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => failed.IsCompleted);
            Assert.IsTrue(failed.IsFaulted);
            Assert.AreEqual(GameWindowState.Closed, window.State);
            Assert.IsNull(window.CurrentOpenScope);
            Assert.AreEqual(2, window.CleanedOpenCount);
            Assert.IsTrue(window.CurrentInstanceScope.IsActive);

            var retry = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => retry.IsCompleted);
            Assert.IsFalse(retry.IsFaulted);
            Assert.AreSame(window, retry.Result);
            Assert.IsTrue(window.CurrentOpenScope.IsActive);
        }

        /// <summary>
        /// 关闭及最终销毁回调抛错时，两个作用域仍释放。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator CloseAndDestroyExceptions_StillReleaseScopes()
        {
            CreateManager();
            var opening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => opening.IsCompleted);
            ProbeWindow window = (ProbeWindow)opening.Result;
            LifetimeScope round = window.CurrentOpenScope;
            LifetimeScope instance = window.CurrentInstanceScope;
            window.FailClose = true;
            window.FailDestroy = true;

            var closing = _manager.CloseAsync(1).AsTask();
            yield return new WaitUntil(() => closing.IsCompleted);
            Assert.IsTrue(closing.Result);
            Assert.IsFalse(round.IsActive);
            Assert.AreEqual(1, window.CleanedOpenCount);
            Assert.IsTrue(instance.IsActive);

            var destroying = _manager.DestroyScopeAsync(GameWindowScope.Global).AsTask();
            yield return new WaitUntil(() => destroying.IsCompleted);
            Assert.IsFalse(destroying.IsFaulted);
            Assert.IsFalse(instance.IsActive);
            Assert.AreEqual(1, window.CleanedInstanceCount);
            Assert.AreEqual(GameWindowState.Destroyed, window.State);
        }

        /// <summary>
        /// 实例停止后排队的缓存重开不能再创建轮次或显示窗口。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator StoppedInstance_CachedReopenIsRejected()
        {
            CreateManager();
            var opening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => opening.IsCompleted);
            ProbeWindow window = (ProbeWindow)opening.Result;
            var closing = _manager.CloseAsync(1).AsTask();
            yield return new WaitUntil(() => closing.IsCompleted);
            window.CurrentInstanceScope.Stop();

            var reopening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => reopening.IsCompleted);
            Assert.IsTrue(reopening.IsFaulted);
            Assert.AreEqual(GameWindowState.Closed, window.State);
            Assert.IsNull(window.CurrentOpenScope);
            Assert.AreEqual(1, window.OpenCount);
            Assert.AreEqual(1, window.ShowCount);
        }

        /// <summary>
        /// 初始化失败后管理器销毁半初始化窗口并释放实例作用域。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator FailedInitialization_ReleasesInstanceScope()
        {
            FailingInitWindow.Last = null;
            var config = new GameWindowConfig(2, nameof(FailingInitWindow), nameof(FailingInitWindow), null, 10,
                GameWindowScope.Global, GameWindowCacheMode.Resident, 0f, false, false, Color.clear);
            var registry = new GameWindowRegistry();
            registry.Register<FailingInitWindow>(2);
            _manager = new GameWindowManager(new GameWindowConfigCatalog(new[] { config }), registry);
            _manager.Initialize();

            var opening = _manager.OpenAsync(2).AsTask();
            yield return new WaitUntil(() => opening.IsCompleted);
            Assert.IsTrue(opening.IsFaulted);
            Assert.IsNotNull(FailingInitWindow.Last);
            Assert.IsFalse(FailingInitWindow.Last.CapturedScope.IsActive);
            Assert.AreEqual(1, FailingInitWindow.Last.CleanupCount);
            Assert.IsTrue(FailingInitWindow.Last.DestroyCalled);
            Assert.IsFalse(_manager.TryGet(2, out _));
        }

        /// <summary>
        /// 根对象先被 Unity 销毁时，关闭仍释放作用域并淘汰失效缓存。
        /// </summary>
        /// <returns>验证窗口关闭和重新创建的协程。</returns>
        [UnityTest]
        public IEnumerator DestroyedRoot_CloseReleasesScopesAndAllowsFreshOpen()
        {
            CreateManager();
            var opening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => opening.IsCompleted);
            var window = (ProbeWindow)opening.Result;
            LifetimeScope instance = window.CurrentInstanceScope;
            LifetimeScope round = window.CurrentOpenScope;
            UnityEngine.Object.DestroyImmediate(window.Root);

            var closing = _manager.CloseAsync(1).AsTask();
            yield return new WaitUntil(() => closing.IsCompleted);
            Assert.IsFalse(closing.IsFaulted);
            Assert.IsTrue(closing.Result);
            Assert.AreEqual(GameWindowState.Destroyed, window.State);
            Assert.AreEqual(LifetimeScopeState.Disposed, instance.State);
            Assert.AreEqual(LifetimeScopeState.Disposed, round.State);
            Assert.IsFalse(_manager.TryGet(1, out _));

            var reopening = _manager.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => reopening.IsCompleted);
            Assert.IsFalse(reopening.IsFaulted);
            Assert.AreNotSame(window, reopening.Result);
            Assert.IsNotNull(reopening.Result.Root);
        }

        /// <summary>
        /// 创建常驻测试窗口管理器。
        /// </summary>
        private void CreateManager()
        {
            var config = new GameWindowConfig(1, nameof(ProbeWindow), nameof(ProbeWindow), null, 10, GameWindowScope.Global,
                GameWindowCacheMode.Resident, 0f, false, false, Color.clear);
            var registry = new GameWindowRegistry();
            registry.Register<ProbeWindow>(1);
            _manager = new GameWindowManager(new GameWindowConfigCatalog(new[] { config }), registry);
            _manager.Initialize();
        }

        /// <summary>
        /// 在初始化中抛错的测试窗口。
        /// </summary>
        private sealed class FailingInitWindow : GameWindow
        {
            internal static FailingInitWindow Last;
            internal LifetimeScope CapturedScope { get; private set; }
            internal int CleanupCount { get; private set; }
            internal bool DestroyCalled { get; private set; }

            /// <summary>
            /// 创建失败初始化测试窗口。
            /// </summary>
            public FailingInitWindow()
            {
                Last = this;
            }

            /// <summary>
            /// 登记清理后模拟初始化失败。
            /// </summary>
            protected override void Init()
            {
                CapturedScope = InstanceScope;
                InstanceScope.Defer(() => CleanupCount++);
                throw new InvalidOperationException("初始化失败");
            }

            /// <summary>
            /// 记录销毁回调。
            /// </summary>
            protected override void Destroy()
            {
                DestroyCalled = true;
            }
        }

        /// <summary>
        /// 暴露生命周期统计的测试窗口。
        /// </summary>
        private sealed class ProbeWindow : GameWindow
        {
            internal LifetimeScope CurrentOpenScope => OpenScope;
            internal LifetimeScope CurrentInstanceScope => InstanceScope;
            internal int OpenCount { get; private set; }
            internal int ShowCount { get; private set; }
            internal int CleanedOpenCount { get; private set; }
            internal int CleanedInstanceCount { get; private set; }
            internal bool FailNextOpen { get; set; }
            internal bool FailClose { get; set; }
            internal bool FailDestroy { get; set; }

            /// <summary>
            /// 创建测试窗口。
            /// </summary>
            public ProbeWindow()
            {
            }

            /// <summary>
            /// 登记实例清理。
            /// </summary>
            protected override void Init()
            {
                InstanceScope.Defer(() => CleanedInstanceCount++);
            }

            /// <summary>
            /// 登记轮次清理并按需模拟失败。
            /// </summary>
            /// <param name="args">打开参数。</param>
            protected override void Open(GameWindowOpenArgs args)
            {
                OpenCount++;
                OpenScope.Defer(() => CleanedOpenCount++);
                if (!FailNextOpen) return;
                FailNextOpen = false;
                throw new InvalidOperationException("打开失败");
            }

            /// <summary>
            /// 记录每次显示。
            /// </summary>
            /// <param name="args">打开参数。</param>
            protected override void Show(GameWindowOpenArgs args)
            {
                ShowCount++;
            }

            /// <summary>
            /// 按需模拟关闭失败。
            /// </summary>
            /// <param name="reason">关闭原因。</param>
            protected override void Close(GameWindowCloseReason reason)
            {
                if (FailClose) throw new InvalidOperationException("关闭失败");
            }

            /// <summary>
            /// 按需模拟销毁失败。
            /// </summary>
            protected override void Destroy()
            {
                if (FailDestroy) throw new InvalidOperationException("销毁失败");
            }
        }
    }
}
