using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Modules.Gameplay
{
    /// <summary>GameplayModule 多玩法管理、停止清理与默认注册测试。</summary>
    [TestFixture]
    public sealed class GameplayModuleTests
    {
        private GameplayModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            GameEntry.Register<ILogModule>(new LogModule());
            _module = new GameplayModule();
            GameEntry.Register<IGameplayModule>(_module);
            GameEntry.StartFramework();
        }

        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
            _module = null;
        }

        /// <summary>StartAsync 应进入玩法并返回可查询的有效句柄。</summary>
        [Test]
        public void StartAsync_StartsGameplayAndReturnsHandle()
        {
            var gameplay = new CountingGameplay("battle");

            GameplayHandle handle = _module.StartAsync(gameplay).GetAwaiter().GetResult();

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(1, _module.Count);
            Assert.IsTrue(gameplay.IsActive);
            Assert.AreEqual(1, gameplay.InitCount);
            Assert.AreEqual(1, gameplay.EnterCount);
            Assert.IsTrue(_module.TryGetGameplay(handle, out var managed));
            Assert.AreSame(gameplay, managed);
        }

        /// <summary>多个玩法实例应能同时被 Update 驱动。</summary>
        [Test]
        public void OnUpdate_MultipleGameplay_UpdatesAllActiveInstances()
        {
            var world = new CountingGameplay("world");
            var battle = new CountingGameplay("battle");
            _module.StartAsync(world).GetAwaiter().GetResult();
            _module.StartAsync(battle).GetAwaiter().GetResult();

            _module.OnUpdate(0.1f);

            Assert.AreEqual(1, world.UpdateCount);
            Assert.AreEqual(1, battle.UpdateCount);
        }

        /// <summary>StopAsync 应退出、销毁并移除指定玩法实例。</summary>
        [Test]
        public void StopAsync_ExitsDestroysAndRemovesGameplay()
        {
            var gameplay = new CountingGameplay("battle");
            GameplayHandle handle = _module.StartAsync(gameplay).GetAwaiter().GetResult();

            _module.StopAsync(handle).GetAwaiter().GetResult();

            Assert.AreEqual(0, _module.Count);
            Assert.IsFalse(_module.Contains(handle));
            Assert.AreEqual(1, gameplay.ExitCount);
            Assert.AreEqual(1, gameplay.DestroyCount);
            Assert.IsTrue(gameplay.IsDestroyed);
        }

        /// <summary>StopAllAsync 应停止并销毁所有受管理玩法。</summary>
        [Test]
        public void StopAllAsync_StopsEveryGameplay()
        {
            var world = new CountingGameplay("world");
            var battle = new CountingGameplay("battle");
            _module.StartAsync(world).GetAwaiter().GetResult();
            _module.StartAsync(battle).GetAwaiter().GetResult();

            _module.StopAllAsync().GetAwaiter().GetResult();

            Assert.AreEqual(0, _module.Count);
            Assert.IsTrue(world.IsDestroyed);
            Assert.IsTrue(battle.IsDestroyed);
            Assert.AreEqual(1, world.ExitCount);
            Assert.AreEqual(1, battle.ExitCount);
        }

        /// <summary>Update 抛异常的玩法应被强制停止并从模块移除。</summary>
        [Test]
        public void OnUpdate_WhenGameplayThrows_ForceStopsEntry()
        {
            var gameplay = new ThrowingGameplay("broken");
            _module.StartAsync(gameplay).GetAwaiter().GetResult();

            Assert.DoesNotThrow(() => _module.OnUpdate(0.1f));

            Assert.AreEqual(0, _module.Count);
            Assert.IsTrue(gameplay.IsDestroyed);
            Assert.AreEqual(1, gameplay.ForceExitCount);
            Assert.AreEqual(1, gameplay.DestroyCount);
        }

        /// <summary>重复管理同一个玩法实例应抛异常。</summary>
        [Test]
        public void StartAsync_SameGameplayTwice_Throws()
        {
            var gameplay = new CountingGameplay("battle");
            _module.StartAsync(gameplay).GetAwaiter().GetResult();

            Assert.Throws<InvalidOperationException>(() =>
                _module.StartAsync(gameplay).GetAwaiter().GetResult());
        }

        /// <summary>null 玩法实例不允许启动。</summary>
        [Test]
        public void StartAsync_NullGameplay_Throws()
        {
            Assert.Throws<ArgumentNullException>(() =>
                _module.StartAsync(null).GetAwaiter().GetResult());
        }

        /// <summary>默认模块注册应包含 GameplayModule。</summary>
        [Test]
        public void RegisterDefaults_IncludesGameplayModule()
        {
            FrameworkTestHelper.ResetFramework();

            GameEntry.RegisterDefaults();

            Assert.IsNotNull(GameEntry.Get<IGameplayModule>());
        }

        /// <summary>
        /// 并发停止等待同一退出操作，退出及销毁仅执行一次。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator StopAsync_ConcurrentCalls_ExitAndDestroyOnce()
        {
            var gameplay = new DeferredGameplay(false);
            var handle = _module.StartAsync(gameplay).GetAwaiter().GetResult();

            var first = _module.StopAsync(handle);
            var second = _module.StopAsync(handle);
            yield return null;

            Assert.AreEqual(1, gameplay.ExitCount);
            Assert.AreEqual(0, gameplay.DestroyCount);
            gameplay.CompleteExit();
            yield return first.ToCoroutine();
            yield return second.ToCoroutine();

            Assert.AreEqual(1, gameplay.ExitCount);
            Assert.AreEqual(1, gameplay.DestroyCount);
            Assert.AreEqual(0, _module.Count);
        }

        /// <summary>
        /// 进入未完成时停止，应等待旧进入回调再销毁玩法。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator StopAsync_DuringEnter_WaitsBeforeDestroy()
        {
            var gameplay = new DeferredGameplay(true);
            var handle = _module.Start(gameplay);
            yield return null;

            var first = _module.StopAsync(handle);
            var second = _module.StopAsync(handle);
            Assert.AreEqual(0, gameplay.DestroyCount);

            gameplay.CompleteEnter();
            yield return first.ToCoroutine();
            yield return second.ToCoroutine();

            Assert.IsTrue(gameplay.IsDestroyed);
            Assert.AreEqual(GameplayState.Destroyed, gameplay.State);
            Assert.AreEqual(1, gameplay.DestroyCount);
            Assert.AreEqual(0, _module.Count);
        }

        /// <summary>
        /// 关闭模块与进行中的停止交错时，不重复退出或销毁。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator Shutdown_DuringStop_WaitsForSingleExit()
        {
            var gameplay = new DeferredGameplay(false);
            var handle = _module.StartAsync(gameplay).GetAwaiter().GetResult();
            var stopping = _module.StopAsync(handle);
            yield return null;

            GameEntry.StopFramework();
            Assert.AreEqual(1, gameplay.DestroyCount);
            yield return stopping.ToCoroutine();
            gameplay.CompleteExit();
            yield return null;

            Assert.AreEqual(1, gameplay.ExitCount);
            Assert.AreEqual(1, gameplay.DestroyCount);
            Assert.AreEqual(GameplayState.Destroyed, gameplay.State);
        }

        /// <summary>
        /// 关闭模块会同步销毁尚在进入中的玩法，迟到回调不得改写销毁状态。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator Shutdown_DuringEnter_DestroysSynchronously()
        {
            var gameplay = new DeferredGameplay(true);
            _module.Start(gameplay);
            yield return null;

            GameEntry.StopFramework();
            Assert.AreEqual(1, gameplay.DestroyCount);
            Assert.AreEqual(GameplayState.Destroyed, gameplay.State);
            gameplay.CompleteEnter();
            yield return null;
            yield return null;

            Assert.AreEqual(1, gameplay.DestroyCount);
            Assert.AreEqual(GameplayState.Destroyed, gameplay.State);
            Assert.IsFalse(gameplay.IsActive);
        }

        private sealed class DeferredGameplay : GameplayRoot<DeferredGameplay>
        {
            private readonly UniTaskCompletionSource _enter;
            private readonly UniTaskCompletionSource _exit = new();

            /// <summary>
            /// 创建可延迟生命周期的测试玩法。
            /// </summary>
            /// <param name="waitOnEnter">是否等待手动放行进入。</param>
            public DeferredGameplay(bool waitOnEnter) : base("deferred")
            {
                if (waitOnEnter) _enter = new UniTaskCompletionSource();
            }

            public int ExitCount { get; private set; }
            public int DestroyCount { get; private set; }

            /// <summary>
            /// 放行进入操作。
            /// </summary>
            public void CompleteEnter() => _enter?.TrySetResult();

            /// <summary>
            /// 放行退出操作。
            /// </summary>
            public void CompleteExit() => _exit.TrySetResult();

            /// <inheritdoc />
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>延迟进入任务。</returns>
            protected override async UniTask OnEnterGameplayAsync(GameplayEnterData enterData, CancellationToken ct)
            {
                if (_enter != null) await _enter.Task;
            }

            /// <inheritdoc />
            /// <param name="ct">取消令牌。</param>
            /// <returns>延迟退出任务。</returns>
            protected override async UniTask OnExitGameplayAsync(CancellationToken ct)
            {
                ExitCount++;
                await _exit.Task;
            }

            /// <inheritdoc />
            protected override void OnDestroyGameplay()
            {
                DestroyCount++;
            }
        }

        private class CountingGameplay : GameplayRoot<CountingGameplay>
        {
            public CountingGameplay(string name) : base(name)
            {
            }

            public int InitCount { get; private set; }

            public int EnterCount { get; private set; }

            public int ExitCount { get; private set; }

            public int UpdateCount { get; private set; }

            public int ForceExitCount { get; private set; }

            public int DestroyCount { get; private set; }

            protected override UniTask OnInitGameplayAsync(CancellationToken ct)
            {
                InitCount++;
                return UniTask.CompletedTask;
            }

            protected override UniTask OnEnterGameplayAsync(GameplayEnterData enterData, CancellationToken ct)
            {
                EnterCount++;
                return UniTask.CompletedTask;
            }

            protected override UniTask OnExitGameplayAsync(CancellationToken ct)
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }

            protected override void OnUpdateGameplay(float deltaTime)
            {
                UpdateCount++;
            }

            protected override void OnForceExit()
            {
                ForceExitCount++;
            }

            protected override void OnDestroyGameplay()
            {
                DestroyCount++;
            }
        }

        private sealed class ThrowingGameplay : CountingGameplay
        {
            public ThrowingGameplay(string name) : base(name)
            {
            }

            protected override void OnUpdateGameplay(float deltaTime)
            {
                throw new InvalidOperationException("Gameplay update failed.");
            }
        }
    }
}
