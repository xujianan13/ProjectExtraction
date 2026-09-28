using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Core
{
    /// <summary>
    /// 验证框架运行状态、暂停原因叠加和 Unity 物理时间的恢复。
    /// </summary>
    public sealed class FrameworkPauseTests
    {
        private GameEntry _entry;
        private GameplayModule _gameplay;
        private TimerModule _timers;
        private SceneModule _scenes;
        private FlowModule _flows;
        private GameObject _bodyObject;
        private float _originalTimeScale;

        /// <summary>
        /// 创建测试宿主及需要遵守玩法暂停的模块。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            _originalTimeScale = Time.timeScale;
            _entry = GameEntry.Bootstrap();
            _entry.SendMessage("OnApplicationFocus", true);
            _entry.SendMessage("OnApplicationPause", false);
            _gameplay = GameEntry.Register<IGameplayModule>(new GameplayModule()) as GameplayModule;
            _timers = GameEntry.Register<ITimerModule>(new TimerModule()) as TimerModule;
            _scenes = GameEntry.Register<ISceneModule>(new SceneModule()) as SceneModule;
            _flows = GameEntry.Register<IFlowModule>(new FlowModule()) as FlowModule;
        }

        /// <summary>
        /// 释放宿主与物理对象，并恢复测试前的时间速度。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            GameEntry.StopFramework();
            if (_bodyObject != null) Object.DestroyImmediate(_bodyObject);
            if (_entry != null) Object.DestroyImmediate(_entry.gameObject);
            Time.timeScale = _originalTimeScale;
        }

        /// <summary>
        /// 宿主存在与模块成功启动是独立状态，直接销毁宿主也必须关闭模块。
        /// </summary>
        [Test]
        public void IsRunning_TracksInitializationAndHostDestruction()
        {
            Assert.IsTrue(GameEntry.HasHost);
            Assert.IsFalse(GameEntry.IsRunning);
            GameEntry.StartFramework();
            Assert.IsTrue(GameEntry.IsRunning);
            Object.DestroyImmediate(_entry.gameObject);
            Assert.IsFalse(GameEntry.IsRunning);
            Assert.IsFalse(GameEntry.HasHost);
            Assert.IsNull(GameEntry.Get<ITimerModule>());
        }

        /// <summary>
        /// 玩法暂停停止默认场景、流程和计时回调，但系统计时仍可执行。
        /// </summary>
        [Test]
        public void GameplayPause_BlocksGameplayTicksButAllowsSystemTimer()
        {
            GameEntry.StartFramework();
            var scene = new CountingScene();
            var flow = new CountingFlow();
            _scenes.RegisterScene(scene);
            _scenes.ChangeSceneAsync(scene).GetAwaiter().GetResult();
            _flows.Start(flow);
            int gameplayCallbacks = 0;
            int systemCallbacks = 0;
            _timers.NextFrame(() => gameplayCallbacks++);
            _timers.NextFrame(() => systemCallbacks++, unscaledTime: true);
            _gameplay.SetPaused(true);

            _scenes.OnUpdate(1f);
            _flows.OnUpdate(1f);
            _timers.OnUpdate(1f);
            Assert.AreEqual(0, scene.TickCount);
            Assert.AreEqual(0, flow.TickCount);
            Assert.AreEqual(0, gameplayCallbacks);
            Assert.AreEqual(1, systemCallbacks);

            _gameplay.SetPaused(false);
            _scenes.OnUpdate(0.1f);
            _flows.OnUpdate(0.1f);
            _timers.OnUpdate(0.1f);
            Assert.AreEqual(1, scene.TickCount);
            Assert.AreEqual(1, flow.TickCount);
            Assert.AreEqual(1, gameplayCallbacks);
        }

        /// <summary>
        /// 后台与业务暂停叠加时不提前恢复，全部解除后恢复原时间速度和物理模拟。
        /// </summary>
        /// <returns>等待真实物理帧的测试协程。</returns>
        [UnityTest]
        public IEnumerator BackgroundPause_StopsPhysicsAndRestoresOriginalSpeed()
        {
            GameEntry.StartFramework();
            Time.timeScale = 0.5f;
            _bodyObject = new GameObject("Pause test body");
            Rigidbody body = _bodyObject.AddComponent<Rigidbody>();
            body.useGravity = false;
            body.velocity = Vector3.right;
            _entry.SendMessage("OnApplicationFocus", false);
            _entry.SendMessage("OnApplicationPause", true);
            _gameplay.SetPaused(true);
            Vector3 position = body.position;
            yield return new WaitForSecondsRealtime(0.06f);
            Assert.AreEqual(position, body.position);
            Assert.AreEqual(0f, Time.timeScale);

            _entry.SendMessage("OnApplicationFocus", true);
            _entry.SendMessage("OnApplicationPause", false);
            Assert.IsTrue(GameEntry.IsGameplayPaused);
            Assert.AreEqual(0f, Time.timeScale);
            _gameplay.SetPaused(false);
            Assert.AreEqual(0.5f, Time.timeScale);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.Greater(body.position.x, position.x);
        }

        /// <summary>
        /// 框架在暂停期间关闭也应归还时间速度。
        /// </summary>
        [Test]
        public void StopFramework_WhilePaused_RestoresTimeScale()
        {
            GameEntry.StartFramework();
            Time.timeScale = 0.25f;
            _gameplay.SetPaused(true);
            Assert.AreEqual(0f, Time.timeScale);
            GameEntry.StopFramework();
            Assert.AreEqual(0.25f, Time.timeScale);
            Assert.IsFalse(GameEntry.IsRunning);
        }

        /// <summary>
        /// 记录逻辑场景收到的更新次数。
        /// </summary>
        private sealed class CountingScene : SceneBase
        {
            /// <summary>
            /// 创建暂停测试场景。
            /// </summary>
            public CountingScene() : base("PauseScene") { }

            /// <summary>
            /// 累计更新次数。
            /// </summary>
            public int TickCount { get; private set; }

            /// <summary>
            /// 记录场景更新。
            /// </summary>
            /// <param name="deltaTime">本帧经过的玩法时间。</param>
            protected override void OnUpdate(float deltaTime) => TickCount++;
        }

        /// <summary>
        /// 记录流程收到的更新次数。
        /// </summary>
        private sealed class CountingFlow : FlowBase
        {
            /// <summary>
            /// 累计更新次数。
            /// </summary>
            public int TickCount { get; private set; }

            /// <summary>
            /// 记录流程更新并继续运行。
            /// </summary>
            /// <param name="deltaTime">本帧经过的玩法时间。</param>
            /// <returns>保持运行的流程结果。</returns>
            protected override FlowTickResult OnTick(float deltaTime)
            {
                TickCount++;
                return FlowTickResult.Running;
            }
        }
    }
}
