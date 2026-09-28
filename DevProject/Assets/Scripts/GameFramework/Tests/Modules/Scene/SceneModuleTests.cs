using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework.Tests.Mocks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Modules.Scene
{
    /// <summary>SceneModule 场景注册、切换、栈管理与事件发布测试。</summary>
    [TestFixture]
    public sealed class SceneModuleTests
    {
        private ISceneModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupForSceneTests();
            _module = GameEntry.Get<ISceneModule>();
        }

        [TearDown]
        public void TearDown() => FrameworkTestHelper.ResetFramework();

        /// <summary>RegisterScene 后 GetScene 应返回同一实例。</summary>
        [Test]
        public void RegisterScene_AndGetScene_Works()
        {
            var scene = new TestScene("A");
            _module.RegisterScene(scene);
            Assert.AreSame(scene, _module.GetScene("A"));
        }

        /// <summary>重复 RegisterScene 同名场景应被忽略。</summary>
        [Test]
        public void RegisterScene_DuplicateName_IsIgnored()
        {
            _module.RegisterScene(new TestScene("A"));
            _module.RegisterScene(new TestScene("A"));
            Assert.AreSame(_module.GetScene("A").Name, "A");
        }

        /// <summary>未使用的场景 UnregisterScene 应成功移除。</summary>
        [Test]
        public void UnregisterScene_NotInUse_RemovesScene()
        {
            var scene = new TestScene("A");
            _module.RegisterScene(scene);
            _module.UnregisterScene(scene);
            Assert.IsNull(_module.GetScene("A"));
        }

        /// <summary>当前激活场景使用中时 UnregisterScene 应被拒绝。</summary>
        [UnityTest]
        public IEnumerator UnregisterScene_ActiveScene_IsRejected()
        {
            var scene = new TestScene("A");
            _module.RegisterScene(scene);
            yield return _module.ChangeSceneAsync(scene).ToCoroutine();
            _module.UnregisterScene(scene);
            Assert.IsNotNull(_module.GetScene("A"));
        }

        /// <summary>ChangeSceneAsync 应切换 CurrentScene 并触发 Exit/Enter。</summary>
        [UnityTest]
        public IEnumerator ChangeSceneAsync_SwitchesCurrentScene()
        {
            var sceneA = new TestScene("A");
            var sceneB = new TestScene("B");
            _module.RegisterScene(sceneA);
            _module.RegisterScene(sceneB);

            yield return _module.ChangeSceneAsync(sceneA).ToCoroutine();
            Assert.AreSame(sceneA, _module.CurrentScene);
            Assert.AreEqual(1, sceneA.EnterCount);

            yield return _module.ChangeSceneAsync(sceneB).ToCoroutine();
            Assert.AreSame(sceneB, _module.CurrentScene);
            Assert.AreEqual(1, sceneA.ExitCount);
            Assert.AreEqual(1, sceneB.EnterCount);
        }

        /// <summary>PushScene/PopScene 应正确维护场景栈深度与 CurrentScene。</summary>
        [UnityTest]
        public IEnumerator PushAndPopScene_ManagesStack()
        {
            var sceneA = new TestScene("A");
            var sceneB = new TestScene("B");
            _module.RegisterScene(sceneA);
            _module.RegisterScene(sceneB);

            yield return _module.ChangeSceneAsync(sceneA).ToCoroutine();
            yield return _module.PushSceneAsync(sceneB).ToCoroutine();
            Assert.AreEqual(1, _module.StackDepth);
            Assert.AreSame(sceneB, _module.CurrentScene);

            yield return _module.PopSceneAsync().ToCoroutine();
            Assert.AreEqual(0, _module.StackDepth);
            Assert.AreSame(sceneA, _module.CurrentScene);
        }

        /// <summary>空栈 PopScene 应记录错误且不抛异常。</summary>
        [Test]
        public void PopScene_EmptyStack_LogsError()
        {
            Assert.DoesNotThrow(() => _module.PopScene());
        }

        /// <summary>
        /// 进入尚未完成时退出，应取消旧切换并等待进入结束。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator ExitCurrentSceneAsync_DuringEnter_WaitsAndLeavesNoActiveScene()
        {
            var scene = new DeferredScene("slow");
            _module.RegisterScene(scene);

            var changing = _module.ChangeSceneAsync(scene);
            yield return null;
            Assert.IsTrue(scene.IsEntering);

            var exiting = _module.ExitCurrentSceneAsync();
            yield return null;
            Assert.IsTrue(_module.IsChanging);

            scene.CompleteEnter();
            yield return exiting.ToCoroutine();
            Assert.AreEqual(UniTaskStatus.Canceled, changing.Status);
            Assert.IsNull(_module.CurrentScene);
            Assert.IsFalse(scene.IsActive);
            Assert.AreEqual(1, scene.ForceExitCount);
            Assert.AreEqual(0, scene.ExitCount);
        }

        /// <summary>
        /// 关闭模块后，迟到的进入回调不得重新激活场景。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator Shutdown_DuringEnter_LateCompletionCannotReactivateScene()
        {
            var scene = new DeferredScene("slow");
            _module.RegisterScene(scene);
            var changing = _module.ChangeSceneAsync(scene);
            yield return null;

            GameEntry.StopFramework();
            Assert.AreEqual(1, scene.ForceExitCount);
            scene.CompleteEnter();
            yield return null;
            yield return null;

            Assert.AreEqual(UniTaskStatus.Canceled, changing.Status);
            Assert.IsNull(_module.CurrentScene);
            Assert.IsFalse(scene.IsActive);
            Assert.AreEqual(1, scene.ForceExitCount);
        }

        /// <summary>
        /// Pop 目标重新进入失败时，应清理刚从栈中弹出的场景。
        /// </summary>
        /// <returns>供 Unity 测试运行器逐帧执行的迭代器。</returns>
        [UnityTest]
        public IEnumerator PopSceneAsync_WhenReenterFails_CleansPoppedScene()
        {
            var first = new FailingReentryScene("first");
            var second = new TestScene("second");
            _module.RegisterScene(first);
            _module.RegisterScene(second);
            yield return _module.ChangeSceneAsync(first).ToCoroutine();
            yield return _module.PushSceneAsync(second).ToCoroutine();

            var popping = _module.PopSceneAsync();
            yield return null;

            Assert.AreEqual(UniTaskStatus.Faulted, popping.Status);
            Assert.Throws<System.InvalidOperationException>(() => popping.GetAwaiter().GetResult());
            Assert.IsNull(_module.CurrentScene);
            Assert.AreEqual(1, first.ForceExitCount);
        }

        private sealed class FailingReentryScene : SceneBase
        {
            private int _enterCount;

            /// <summary>
            /// 创建重新进入失败的测试场景。
            /// </summary>
            /// <param name="name">场景名称。</param>
            public FailingReentryScene(string name) : base(name) { }

            public int ForceExitCount { get; private set; }

            /// <inheritdoc />
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>进入任务。</returns>
            protected override UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
            {
                _enterCount++;
                return _enterCount == 1 ? UniTask.CompletedTask : UniTask.FromException(new System.InvalidOperationException("reentry failed"));
            }

            /// <inheritdoc />
            protected override void OnForceExit()
            {
                ForceExitCount++;
            }
        }

        private sealed class DeferredScene : SceneBase
        {
            private readonly UniTaskCompletionSource _enter = new();

            /// <summary>
            /// 创建延迟进入的测试场景。
            /// </summary>
            /// <param name="name">场景名称。</param>
            public DeferredScene(string name) : base(name) { }

            public int ExitCount { get; private set; }
            public int ForceExitCount { get; private set; }

            /// <summary>
            /// 放行进入操作。
            /// </summary>
            public void CompleteEnter() => _enter.TrySetResult();

            /// <inheritdoc />
            /// <param name="enterData">进入参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>延迟进入任务。</returns>
            protected override async UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
            {
                await _enter.Task;
            }

            /// <inheritdoc />
            /// <param name="ct">取消令牌。</param>
            /// <returns>退出任务。</returns>
            protected override UniTask OnExitSceneAsync(CancellationToken ct)
            {
                ExitCount++;
                return UniTask.CompletedTask;
            }

            /// <inheritdoc />
            protected override void OnForceExit()
            {
                ForceExitCount++;
            }
        }

        /// <summary>场景切换完成应发布 SceneEvents.Changed 事件。</summary>
        [UnityTest]
        public IEnumerator ChangeSceneAsync_PublishesChangedEvent()
        {
            var events = GameEntry.Get<IEventModule>();
            SceneChangedEvent? received = null;
            events.Subscribe<SceneChangedEvent>(SceneEvents.Changed, e => received = e);

            var scene = new TestScene("A");
            _module.RegisterScene(scene);
            yield return _module.ChangeSceneAsync(scene).ToCoroutine();

            Assert.IsTrue(received.HasValue);
            Assert.AreEqual("A", received.Value.ToName);
            Assert.AreEqual(SceneChangeMode.Replace, received.Value.Mode);
        }
    }
}
