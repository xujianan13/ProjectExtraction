using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Core
{
    /// <summary>
    /// 验证应用、场景和普通组件的真实生命周期边界。
    /// </summary>
    public sealed class LifetimeIntegrationTests
    {
        /// <summary>
        /// 清除前一项验证留下的框架状态。
        /// </summary>
        [SetUp]
        public void SetUp() => FrameworkTestHelper.ResetFramework();

        /// <summary>
        /// 确保测试结束后关闭本轮框架。
        /// </summary>
        [TearDown]
        public void TearDown() => FrameworkTestHelper.ResetFramework();

        /// <summary>
        /// 应用登记必须在模块仍可用时清理，重启必须创建新作用域。
        /// </summary>
        [Test]
        public void ApplicationScope_ReleasesBeforeShutdown_AndRenewsOnRestart()
        {
            var calls = new List<string>();
            GameEntry.Register(new ScopeModule(calls));
            GameEntry.StartFramework();
            var first = GameEntry.ApplicationScope;
            GameEntry.StartFramework();
            Assert.AreSame(first, GameEntry.ApplicationScope);
            GameEntry.StopFramework();
            CollectionAssert.AreEqual(new[] { "init", "cleanup", "shutdown" }, calls);
            Assert.IsTrue(first.Token.IsCancellationRequested);
            GameEntry.Register(new ScopeModule(calls));
            GameEntry.StartFramework();
            Assert.AreNotSame(first, GameEntry.ApplicationScope);
            Assert.IsTrue(GameEntry.ApplicationScope.IsActive);
            CollectionAssert.AreEqual(new[] { "init", "cleanup", "shutdown", "init" }, calls);
        }

        /// <summary>
        /// 子作用域取消回调触发框架关闭时，先完成子级和应用清理，再关闭模块。
        /// </summary>
        [Test]
        public void ChildCancellation_StopsFrameworkAfterAllApplicationCleanup()
        {
            var calls = new List<string>();
            GameEntry.Register(new ScopeModule(calls));
            GameEntry.StartFramework();
            var app = GameEntry.ApplicationScope;
            var child = app.CreateChild("shutdown-trigger");
            child.Defer(() => calls.Add("child"));
            using var cancellation = child.Token.Register(GameEntry.StopFramework);
            child.Stop();
            CollectionAssert.AreEqual(new[] { "init", "child", "cleanup", "shutdown" }, calls);
            Assert.AreEqual(LifetimeScopeState.Disposed, app.State);
            Assert.IsFalse(GameEntry.IsRunning);
        }

        /// <summary>
        /// 模块初始化失败时先清理应用登记，再执行模块回滚，随后允许重新启动。
        /// </summary>
        [Test]
        public void ApplicationScope_FailedInit_CleansBeforeModuleRollback()
        {
            var calls = new List<string>();
            var module = new ScopeModule(calls) { FailInit = true };
            GameEntry.Register(module);
            Assert.Throws<InvalidOperationException>(() => GameEntry.StartFramework());
            CollectionAssert.AreEqual(new[] { "init", "cleanup", "shutdown" }, calls);
            Assert.AreEqual(LifetimeScopeState.Disposed, GameEntry.ApplicationScope.State);
            var failedScope = GameEntry.ApplicationScope;
            module.FailInit = false;
            GameEntry.StartFramework();
            Assert.AreNotSame(failedScope, GameEntry.ApplicationScope);
            Assert.IsTrue(GameEntry.IsRunning);
        }

        /// <summary>
        /// 退出开始取消业务任务，退出钩子仍持有资源，等待期间不再更新玩法。
        /// </summary>
        [Test]
        public void SceneExit_StopsBeforeHook_DisposesAfterAwait()
        {
            var scene = new ScopeScene { ExitGate = new UniTaskCompletionSource() };
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            var scope = scene.SceneScope;
            var exit = scene.ExitSceneAsync();
            Assert.IsTrue(scope.Token.IsCancellationRequested);
            Assert.IsFalse(scene.ExitTokenCancelled);
            Assert.AreEqual(0, scene.CleanupCount);
            scene.Update(1f);
            Assert.AreEqual(0, scene.UpdateCount);
            scene.ExitGate.TrySetResult();
            exit.GetAwaiter().GetResult();
            Assert.AreEqual(1, scene.CleanupCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            Assert.AreNotSame(scope, scene.SceneScope);
            Assert.IsTrue(scene.SceneScope.IsActive);
            scene.ForceExit();
        }

        /// <summary>
        /// 取消回调强制退出并重新进入时，旧退出流程不能调用新一轮退出钩子。
        /// </summary>
        [Test]
        public void SceneExit_ReentryDuringStop_DoesNotExitNewGeneration()
        {
            var scene = new ScopeScene();
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            var first = scene.SceneScope;
            first.Token.Register(() =>
            {
                scene.ForceExit();
                scene.EnterSceneAsync().GetAwaiter().GetResult();
            });
            scene.ExitSceneAsync().GetAwaiter().GetResult();
            Assert.AreNotSame(first, scene.SceneScope);
            Assert.IsTrue(scene.IsActive);
            Assert.IsTrue(scene.SceneScope.IsActive);
            Assert.AreEqual(0, scene.ExitCount);
            scene.ForceExit();
        }

        /// <summary>
        /// 部分进入失败和强制退出钩子失败仍释放本轮登记。
        /// </summary>
        [Test]
        public void SceneFailure_AndThrowingForceExit_ReleaseRegistrations()
        {
            var scene = new ScopeScene { FailEnter = true };
            Assert.Throws<InvalidOperationException>(() => scene.EnterSceneAsync().GetAwaiter().GetResult());
            Assert.AreEqual(1, scene.CleanupCount);
            Assert.IsFalse(scene.IsEntering);
            scene.FailEnter = false;
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            scene.FailForceExit = true;
            Assert.Throws<InvalidOperationException>(() => scene.ForceExit());
            Assert.AreEqual(2, scene.CleanupCount);
            Assert.IsFalse(scene.IsActive);
            Assert.IsFalse(scene.IsEntering);
            Assert.IsFalse(scene.IsExiting);
        }

        /// <summary>
        /// 同一轮的强制退出只执行一次业务钩子，下一轮可再次执行。
        /// </summary>
        [Test]
        public void ForceExit_IsIdempotentWithinGeneration()
        {
            var scene = new ScopeScene();
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            scene.ForceExit();
            scene.ForceExit();
            Assert.AreEqual(1, scene.ForceExitCount);
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            scene.ForceExit();
            Assert.AreEqual(2, scene.ForceExitCount);
        }

        /// <summary>
        /// 初始化期间禁止改动模块清单，失败时仍回滚应用登记和当前模块。
        /// </summary>
        [Test]
        public void InitRegistration_IsRejectedAndRolledBack()
        {
            var calls = new List<string>();
            GameEntry.Register(new ScopeModule(calls) { RegisterDuringInit = true });
            Assert.Throws<InvalidOperationException>(() => GameEntry.StartFramework());
            Assert.IsFalse(GameEntry.IsRunning);
            Assert.AreEqual(LifetimeScopeState.Disposed, GameEntry.ApplicationScope.State);
            CollectionAssert.AreEqual(new[] { "init", "cleanup", "shutdown" }, calls);
        }

        /// <summary>
        /// 强制退出后的旧进入任务迟到完成不能激活场景或清除新一轮的进入状态。
        /// </summary>
        [Test]
        public void SceneOldEnterCompletion_DoesNotAffectNewGeneration()
        {
            var firstGate = new UniTaskCompletionSource();
            var scene = new ScopeScene { EnterGate = firstGate };
            var oldEnter = scene.EnterSceneAsync();
            var oldScope = scene.SceneScope;
            scene.ForceExit();
            var secondGate = new UniTaskCompletionSource();
            scene.EnterGate = secondGate;
            var newEnter = scene.EnterSceneAsync();
            var newScope = scene.SceneScope;
            firstGate.TrySetResult();
            Assert.Throws<OperationCanceledException>(() => oldEnter.GetAwaiter().GetResult());
            Assert.IsTrue(scene.IsEntering);
            Assert.IsTrue(newScope.IsActive);
            Assert.IsFalse(oldScope.IsActive);
            secondGate.TrySetResult();
            newEnter.GetAwaiter().GetResult();
            Assert.IsTrue(scene.IsActive);
            scene.ForceExit();
        }

        /// <summary>
        /// 强制退出后的旧退出任务完成不能结束新一轮作用域。
        /// </summary>
        [Test]
        public void SceneOldExitCompletion_DoesNotDisposeNewGeneration()
        {
            var scene = new ScopeScene { ExitGate = new UniTaskCompletionSource() };
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            var oldExit = scene.ExitSceneAsync();
            scene.ForceExit();
            scene.EnterSceneAsync().GetAwaiter().GetResult();
            var newScope = scene.SceneScope;
            scene.ExitGate.TrySetResult();
            oldExit.GetAwaiter().GetResult();
            Assert.IsTrue(scene.IsActive);
            Assert.IsTrue(newScope.IsActive);
            scene.ForceExit();
        }

        /// <summary>
        /// 模块关闭时强制退出场景并释放登记。
        /// </summary>
        [Test]
        public void FrameworkShutdown_DisposesCurrentSceneScope()
        {
            FrameworkTestHelper.SetupForSceneTests();
            var scene = new ScopeScene();
            var scenes = GameEntry.Get<ISceneModule>();
            scenes.RegisterScene(scene);
            scenes.ChangeSceneAsync(scene).GetAwaiter().GetResult();
            GameEntry.StopFramework();
            Assert.AreEqual(1, scene.CleanupCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, scene.SceneScope.State);
        }

        /// <summary>
        /// 默认隐藏不清理实例作用域；显式禁用结束模式在下次启用时创建新一轮。
        /// </summary>
        /// <returns>跨 Unity 销毁帧的验证过程。</returns>
        [UnityTest]
        public IEnumerator BehaviourScope_HonorsDisableMode_AndExternalDestroy()
        {
            var root = new GameObject("LifetimeBehaviourTest");
            var owner = root.AddComponent<LifetimeScopeBehaviour>();
            var first = owner.Scope;
            root.SetActive(false);
            Assert.IsTrue(first.IsActive);
            root.SetActive(true);
            Assert.AreSame(first, owner.Scope);
            owner.DisposeOnDisable = true;
            root.SetActive(false);
            Assert.AreEqual(LifetimeScopeState.Disposed, first.State);
            root.SetActive(true);
            var second = owner.Scope;
            Assert.AreNotSame(first, second);
            Assert.IsTrue(second.IsActive);
            UnityEngine.Object.Destroy(root);
            yield return null;
            Assert.AreEqual(LifetimeScopeState.Disposed, second.State);
        }

        /// <summary>
        /// 在应用初始化中登记清理行为的测试模块。
        /// </summary>
        private sealed class ScopeModule : ModuleBase
        {
            private readonly List<string> _calls;
            public bool FailInit;
            public bool RegisterDuringInit;

            /// <summary>
            /// 保存测试过程记录容器。
            /// </summary>
            /// <param name="calls">调用顺序记录。</param>
            public ScopeModule(List<string> calls) => _calls = calls;

            /// <summary>
            /// 登记必须在关闭前执行的清理操作。
            /// </summary>
            public override void OnInit()
            {
                _calls.Add("init");
                GameEntry.ApplicationScope.Defer(() => _calls.Add("cleanup"));
                if (RegisterDuringInit) GameEntry.Register<ILogModule>(new LogModule());
                if (FailInit) throw new InvalidOperationException("测试初始化失败。");
            }

            /// <summary>
            /// 记录模块关闭顺序。
            /// </summary>
            public override void OnShutdown() => _calls.Add("shutdown");
        }

        /// <summary>
        /// 支持控制进入和退出完成时机的测试场景。
        /// </summary>
        private sealed class ScopeScene : SceneBase
        {
            public bool FailEnter;
            public bool FailForceExit;
            public bool ExitTokenCancelled;
            public int CleanupCount;
            public int UpdateCount;
            public int ExitCount;
            public int ForceExitCount;
            public UniTaskCompletionSource EnterGate;
            public UniTaskCompletionSource ExitGate;

            /// <summary>
            /// 创建测试场景。
            /// </summary>
            public ScopeScene() : base("LifetimeScene") { }

            /// <summary>
            /// 登记本轮清理，并按测试配置等待或失败。
            /// </summary>
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">本轮进入令牌。</param>
            /// <returns>进入任务。</returns>
            protected override async UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
            {
                SceneScope.Defer(() => CleanupCount++);
                if (FailEnter) throw new InvalidOperationException("测试进入失败。");
                if (EnterGate != null) await EnterGate.Task;
            }

            /// <summary>
            /// 记录退出令牌并等待显式放行。
            /// </summary>
            /// <param name="ct">独立退出令牌。</param>
            /// <returns>退出任务。</returns>
            protected override async UniTask OnExitSceneAsync(CancellationToken ct)
            {
                ExitCount++;
                ExitTokenCancelled = ct.IsCancellationRequested;
                if (ExitGate != null) await ExitGate.Task;
            }

            /// <summary>
            /// 按配置模拟业务强制退出失败。
            /// </summary>
            protected override void OnForceExit()
            {
                ForceExitCount++;
                if (FailForceExit) throw new InvalidOperationException("测试强制退出失败。");
            }

            /// <summary>
            /// 记录玩法更新次数。
            /// </summary>
            /// <param name="deltaTime">本帧游戏时间。</param>
            protected override void OnUpdate(float deltaTime) => UpdateCount++;
        }
    }
}
