using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Gameplay
{
    /// <summary>
    /// 验证玩法每轮作用域的共享、停止和最终释放边界。
    /// </summary>
    [TestFixture]
    public sealed class GameplayScopeTests
    {
        /// <summary>
        /// 初始化期间没有作用域，进入前为所有局部对象建立同一作用域与令牌。
        /// </summary>
        [Test]
        public void Enter_CreatesOneScopeSharedByRootFeatureAndModel()
        {
            var gameplay = new ScopeGameplay();
            Assert.IsNull(gameplay.GameplayScope);
            gameplay.InitGameplayAsync().GetAwaiter().GetResult();
            Assert.IsNull(gameplay.GameplayScope);

            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();

            var scope = gameplay.GameplayScope;
            Assert.AreEqual("Gameplay:scope-test", scope.Name);
            Assert.AreSame(scope, gameplay.Feature.EnteredScope);
            Assert.AreSame(scope, gameplay.Model.EnteredScope);
            Assert.AreEqual(scope.Token, gameplay.Feature.EnteredToken);
            Assert.AreEqual(scope.Token, gameplay.Model.EnteredToken);
            Assert.AreEqual(scope.Token, gameplay.RootToken);
            Assert.IsTrue(scope.IsActive);
            gameplay.ForceExit();
        }

        /// <summary>
        /// 退出开始即禁用真实事件、计时器和输入句柄，等待回调结束后释放登记与租约。
        /// </summary>
        [Test]
        public void Exit_StopsHandlesBeforeCallbacks_AndDisposesAfterAwait()
        {
            var events = new EventModule();
            var timers = new TimerModule();
            var input = new InputModule();
            events.OnInit();
            timers.OnInit();
            try
            {
                var gameplay = new ScopeGameplay { ExitGate = new UniTaskCompletionSource() };
                gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
                var scope = gameplay.GameplayScope;
                var id = new EventId(93001);
                int eventCount = 0, timerCount = 0, cleanupCount = 0;
                scope.Subscribe(events, id, () => eventCount++);
                scope.Once(timers, 1f, () => timerCount++);
                scope.BlockGameplay(input, "gameplay scope test");
                Assert.AreEqual(1, ReadInputBlockCount(input));
                Assert.IsTrue(scope.TryOwn(new CallbackDisposable(() => cleanupCount++)));

                var exit = gameplay.ExitGameplayAsync();
                Assert.IsTrue(gameplay.IsExiting);
                Assert.IsFalse(gameplay.IsActive);
                Assert.AreEqual(LifetimeScopeState.Stopping, scope.State);
                Assert.IsTrue(scope.Token.IsCancellationRequested);
                Assert.IsFalse(gameplay.ExitTokenWasCancelled);
                Assert.AreEqual(0, cleanupCount);
                Assert.AreEqual(1, ReadInputBlockCount(input));
                events.Publish(id);
                timers.OnUpdate(2f);
                gameplay.Update(0.1f);
                Assert.AreEqual(0, eventCount);
                Assert.AreEqual(0, timerCount);
                Assert.AreEqual(0, gameplay.UpdateCount);

                gameplay.ExitGate.TrySetResult();
                exit.GetAwaiter().GetResult();
                Assert.AreEqual(1, cleanupCount);
                Assert.AreEqual(0, ReadInputBlockCount(input));
                Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
                Assert.AreEqual(GameplayState.Ready, gameplay.State);
                Assert.AreSame(scope, gameplay.GameplayScope);
                gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
                Assert.AreNotSame(scope, gameplay.GameplayScope);
                Assert.IsTrue(gameplay.GameplayScope.IsActive);
                gameplay.ForceExit();
            }
            finally
            {
                timers.OnShutdown();
                events.OnShutdown();
            }
        }

        /// <summary>
        /// 正常退出后重新进入创建新作用域，旧退出任务迟到也不能释放新作用域。
        /// </summary>
        [Test]
        public void OldExitCompletion_DoesNotDisposeReenteredScope()
        {
            var gameplay = new ScopeGameplay { ExitGate = new UniTaskCompletionSource() };
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            var oldScope = gameplay.GameplayScope;
            var oldExit = gameplay.ExitGameplayAsync();
            gameplay.ForceExit();
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            var newScope = gameplay.GameplayScope;

            gameplay.ExitGate.TrySetResult();
            oldExit.GetAwaiter().GetResult();

            Assert.AreNotSame(oldScope, newScope);
            Assert.AreEqual(LifetimeScopeState.Disposed, oldScope.State);
            Assert.IsTrue(newScope.IsActive);
            Assert.IsTrue(gameplay.IsActive);
            Assert.AreEqual(GameplayState.Running, gameplay.State);
            gameplay.ForceExit();
        }

        /// <summary>
        /// 旧进入任务迟到完成不能激活旧轮次，也不能重置新轮次的进入状态。
        /// </summary>
        [Test]
        public void OldEnterCompletion_DoesNotChangeNewEnteringGeneration()
        {
            var firstGate = new UniTaskCompletionSource();
            var gameplay = new ScopeGameplay { EnterGate = firstGate };
            var oldEnter = gameplay.EnterGameplayAsync();
            var oldScope = gameplay.GameplayScope;
            gameplay.ForceExit();
            var secondGate = new UniTaskCompletionSource();
            gameplay.EnterGate = secondGate;
            var newEnter = gameplay.EnterGameplayAsync();
            var newScope = gameplay.GameplayScope;

            firstGate.TrySetResult();
            Assert.Throws<OperationCanceledException>(() => oldEnter.GetAwaiter().GetResult());
            Assert.IsTrue(gameplay.IsEntering);
            Assert.IsTrue(newScope.IsActive);
            Assert.AreEqual(LifetimeScopeState.Disposed, oldScope.State);
            secondGate.TrySetResult();
            newEnter.GetAwaiter().GetResult();
            Assert.IsTrue(gameplay.IsActive);
            gameplay.ForceExit();
        }

        /// <summary>
        /// 进入失败和进入令牌取消都释放已建立的作用域，随后可开始新轮次。
        /// </summary>
        [Test]
        public void FailedOrCancelledEnter_DisposesScopeAndAllowsRetry()
        {
            var gameplay = new ScopeGameplay { FailEnter = true };
            Assert.Throws<InvalidOperationException>(() => gameplay.EnterGameplayAsync().GetAwaiter().GetResult());
            var failedScope = gameplay.GameplayScope;
            Assert.AreEqual(LifetimeScopeState.Disposed, failedScope.State);
            Assert.AreEqual(GameplayState.Ready, gameplay.State);

            gameplay.FailEnter = false;
            using var source = new CancellationTokenSource();
            gameplay.CancelEnter = source;
            Assert.Throws<OperationCanceledException>(() => gameplay.EnterGameplayAsync(ct: source.Token).GetAwaiter().GetResult());
            var cancelledScope = gameplay.GameplayScope;
            Assert.AreNotSame(failedScope, cancelledScope);
            Assert.AreEqual(LifetimeScopeState.Disposed, cancelledScope.State);
            gameplay.CancelEnter = null;
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            Assert.IsTrue(gameplay.GameplayScope.IsActive);
            gameplay.ForceExit();
        }

        /// <summary>
        /// 外部取消只停止作用域，拥有者最终强制退出时才释放登记。
        /// </summary>
        [Test]
        public void ExternalCancellation_StopsButOwnerDisposes()
        {
            using var source = new CancellationTokenSource();
            var gameplay = new ScopeGameplay();
            gameplay.EnterGameplayAsync(ct: source.Token).GetAwaiter().GetResult();
            int cleanupCount = 0;
            var scope = gameplay.GameplayScope;
            scope.Defer(() => cleanupCount++);

            source.Cancel();
            gameplay.Update(0.1f);
            Assert.AreEqual(LifetimeScopeState.Stopping, scope.State);
            Assert.AreEqual(0, cleanupCount);
            Assert.AreEqual(0, gameplay.UpdateCount);
            gameplay.ForceExit();
            Assert.AreEqual(1, cleanupCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
        }

        /// <summary>
        /// 退出钩子异常仍释放本轮作用域并恢复可重新进入的状态。
        /// </summary>
        [Test]
        public void FailedExit_DisposesScopeAndAllowsRetry()
        {
            var gameplay = new ScopeGameplay { FailExit = true };
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            var failedScope = gameplay.GameplayScope;
            int cleanupCount = 0;
            failedScope.Defer(() => cleanupCount++);

            Assert.Throws<InvalidOperationException>(() => gameplay.ExitGameplayAsync().GetAwaiter().GetResult());
            Assert.AreEqual(1, cleanupCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, failedScope.State);
            Assert.AreEqual(GameplayState.Ready, gameplay.State);
            gameplay.FailExit = false;
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            Assert.AreNotSame(failedScope, gameplay.GameplayScope);
            Assert.IsTrue(gameplay.GameplayScope.IsActive);
            gameplay.ForceExit();
        }

        /// <summary>
        /// 强制退出与销毁时重复请求只执行一轮清理，回调失败也不会遗漏其他对象。
        /// </summary>
        [Test]
        public void ForceExitAndDestroy_ReentryAndThrowingCallbacksStillReleaseAll()
        {
            var gameplay = new ScopeGameplay { ReenterForceExit = true, ThrowOnForceExit = true };
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            var scope = gameplay.GameplayScope;
            int cleanupCount = 0;
            scope.Defer(() => cleanupCount++);
            gameplay.ForceExit();
            gameplay.ForceExit();
            Assert.AreEqual(1, gameplay.RootForceExitCount);
            Assert.AreEqual(1, gameplay.Feature.ForceExitCount);
            Assert.AreEqual(1, gameplay.Model.ForceExitCount);
            Assert.AreEqual(1, cleanupCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);

            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            var second = gameplay.GameplayScope;
            gameplay.DestroyGameplay();
            gameplay.DestroyGameplay();
            Assert.AreEqual(LifetimeScopeState.Disposed, second.State);
            Assert.IsTrue(gameplay.IsDestroyed);
            Assert.AreEqual(2, gameplay.RootForceExitCount);
            Assert.AreEqual(1, gameplay.RootDestroyCount);
        }

        /// <summary>
        /// 取消通知中请求销毁应在当前强制退出清理完成后执行一次。
        /// </summary>
        [Test]
        public void DestroyFromCancellation_CompletesAfterForceExitWithoutDuplicateCleanup()
        {
            var gameplay = new ScopeGameplay();
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            var scope = gameplay.GameplayScope;
            using var cancellation = scope.Token.Register(gameplay.DestroyGameplay);
            int cleanupCount = 0;
            scope.Defer(() => cleanupCount++);
            gameplay.ForceExit();
            Assert.IsTrue(gameplay.IsDestroyed);
            Assert.AreEqual(GameplayState.Destroyed, gameplay.State);
            Assert.AreEqual(1, gameplay.RootForceExitCount);
            Assert.AreEqual(1, gameplay.Feature.ForceExitCount);
            Assert.AreEqual(1, gameplay.Model.ForceExitCount);
            Assert.AreEqual(1, gameplay.RootDestroyCount);
            Assert.AreEqual(1, cleanupCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
        }

        /// <summary>
        /// 模型在当前帧强制退出后，不再继续更新功能模块和根节点。
        /// </summary>
        [Test]
        public void Update_ModelForceExit_StopsRemainingDispatch()
        {
            var gameplay = new ScopeGameplay();
            gameplay.EnterGameplayAsync().GetAwaiter().GetResult();
            gameplay.Model.ForceExitOnUpdate = true;
            gameplay.Update(0.1f);
            Assert.AreEqual(1, gameplay.Model.UpdateCount);
            Assert.AreEqual(0, gameplay.Feature.UpdateCount);
            Assert.AreEqual(0, gameplay.UpdateCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, gameplay.GameplayScope.State);
        }

        /// <summary>
        /// 读取输入模块持有的真实阻断句柄数量。
        /// </summary>
        /// <param name="input">被检查的输入模块。</param>
        /// <returns>输入阻断登记数量。</returns>
        private static int ReadInputBlockCount(InputModule input)
        {
            var field = typeof(InputModule).GetField("_blockCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (int)field.GetValue(input);
        }

        /// <summary>
        /// 为测试记录释放次数的租约。
        /// </summary>
        private sealed class CallbackDisposable : IDisposable
        {
            private readonly Action _callback;

            /// <summary>
            /// 保存释放回调。
            /// </summary>
            /// <param name="callback">释放时调用的回调。</param>
            public CallbackDisposable(Action callback) => _callback = callback;

            /// <summary>
            /// 释放测试租约。
            /// </summary>
            public void Dispose() => _callback();
        }

        /// <summary>
        /// 提供可控异步门闩与清理计数的玩法根节点。
        /// </summary>
        private sealed class ScopeGameplay : GameplayRoot<ScopeGameplay>
        {
            public readonly ScopeFeature Feature;
            public readonly ScopeModel Model;
            public UniTaskCompletionSource EnterGate;
            public UniTaskCompletionSource ExitGate;
            public CancellationTokenSource CancelEnter;
            public bool FailEnter;
            public bool FailExit;
            public bool ReenterForceExit;
            public bool ThrowOnForceExit;
            public bool ExitTokenWasCancelled;
            public int UpdateCount;
            public int RootForceExitCount;
            public int RootDestroyCount;
            public CancellationToken RootToken => GameplayToken;

            /// <summary>
            /// 建立测试玩法及其功能模块和模型。
            /// </summary>
            public ScopeGameplay() : base("scope-test")
            {
                Feature = AddFeature(new ScopeFeature());
                Model = AddModel(new ScopeModel());
            }

            /// <summary>
            /// 根据门闩、失败设置或外部取消模拟进入。
            /// </summary>
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">本轮取消令牌。</param>
            /// <returns>进入等待任务。</returns>
            protected override async UniTask OnEnterGameplayAsync(GameplayEnterData enterData, CancellationToken ct)
            {
                if (FailEnter) throw new InvalidOperationException("enter failed");
                CancelEnter?.Cancel();
                if (EnterGate != null) await EnterGate.Task;
            }

            /// <summary>
            /// 记录独立退出令牌并按需等待。
            /// </summary>
            /// <param name="ct">退出取消令牌。</param>
            /// <returns>退出等待任务。</returns>
            protected override async UniTask OnExitGameplayAsync(CancellationToken ct)
            {
                ExitTokenWasCancelled = ct.IsCancellationRequested;
                if (FailExit) throw new InvalidOperationException("exit failed");
                if (ExitGate != null) await ExitGate.Task;
            }

            /// <summary>
            /// 记录强制退出，按需触发重入和失败。
            /// </summary>
            protected override void OnForceExit()
            {
                RootForceExitCount++;
                if (ReenterForceExit) ForceExit();
                if (ThrowOnForceExit) throw new InvalidOperationException("force exit failed");
            }

            /// <summary>
            /// 记录销毁次数。
            /// </summary>
            protected override void OnDestroyGameplay() => RootDestroyCount++;

            /// <summary>
            /// 记录根节点更新次数。
            /// </summary>
            /// <param name="deltaTime">帧间隔。</param>
            protected override void OnUpdateGameplay(float deltaTime) => UpdateCount++;
        }

        /// <summary>
        /// 记录功能模块观察到的玩法作用域。
        /// </summary>
        private sealed class ScopeFeature : GameplayFeature<ScopeGameplay>
        {
            public LifetimeScope EnteredScope;
            public CancellationToken EnteredToken;
            public int ForceExitCount;
            public int UpdateCount;

            /// <summary>
            /// 捕获功能模块所用的本轮作用域。
            /// </summary>
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">本轮取消令牌。</param>
            /// <returns>已完成的进入任务。</returns>
            public override UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
            {
                EnteredScope = GameplayScope;
                EnteredToken = GameplayToken;
                return UniTask.CompletedTask;
            }

            /// <summary>
            /// 记录功能模块的清理次数。
            /// </summary>
            public override void ForceExit() => ForceExitCount++;

            /// <summary>
            /// 记录功能模块的更新次数。
            /// </summary>
            /// <param name="deltaTime">帧间隔。</param>
            public override void Update(float deltaTime) => UpdateCount++;
        }

        /// <summary>
        /// 记录模型观察到的玩法作用域并可在更新中强制退出。
        /// </summary>
        private sealed class ScopeModel : GameplayModel<ScopeGameplay>
        {
            public LifetimeScope EnteredScope;
            public CancellationToken EnteredToken;
            public bool ForceExitOnUpdate;
            public int ForceExitCount;
            public int UpdateCount;

            /// <summary>
            /// 捕获模型所用的本轮作用域。
            /// </summary>
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">本轮取消令牌。</param>
            /// <returns>已完成的进入任务。</returns>
            public override UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
            {
                EnteredScope = GameplayScope;
                EnteredToken = GameplayToken;
                return UniTask.CompletedTask;
            }

            /// <summary>
            /// 记录模型的清理次数。
            /// </summary>
            public override void ForceExit() => ForceExitCount++;

            /// <summary>
            /// 记录模型更新，按需从回调中退出玩法。
            /// </summary>
            /// <param name="deltaTime">帧间隔。</param>
            public override void Update(float deltaTime)
            {
                UpdateCount++;
                if (ForceExitOnUpdate) Root.ForceExit();
            }
        }
    }
}
