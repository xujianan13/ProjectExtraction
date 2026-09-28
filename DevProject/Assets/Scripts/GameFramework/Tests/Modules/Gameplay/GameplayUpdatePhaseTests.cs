using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Gameplay
{
    /// <summary>
    /// 玩法更新阶段的顺序、暂停和生命周期测试。
    /// </summary>
    [TestFixture]
    public sealed class GameplayUpdatePhaseTests
    {
        private GameplayModule _module;

        /// <summary>
        /// 启动测试所需的最小框架。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            _module = new GameplayModule();
            GameEntry.Register<IGameplayModule>(_module);
            GameEntry.StartFramework();
        }

        /// <summary>
        /// 清理测试框架。
        /// </summary>
        [TearDown]
        public void TearDown() => FrameworkTestHelper.ResetFramework();

        /// <summary>
        /// 三个阶段均按模型、功能、根节点顺序执行。
        /// </summary>
        [Test]
        public void ActiveGameplay_DispatchesEachPhaseInOrder()
        {
            var gameplay = new PhaseGameplay();
            _module.StartAsync(gameplay).GetAwaiter().GetResult();

            _module.OnFixedUpdate(0.02f);
            _module.OnUpdate(0.1f);
            _module.OnLateUpdate(0.1f);

            CollectionAssert.AreEqual(new[] { "model:fixed", "feature:fixed", "root:fixed",
                "model:update", "feature:update", "root:update",
                "model:late", "feature:late", "root:late" }, gameplay.Events);
        }

        /// <summary>
        /// 进入前、退出后不驱动玩法，暂停期间不驱动任何玩法阶段，恢复后继续。
        /// </summary>
        [Test]
        public void LifecycleAndPause_GateAllGameplayPhases()
        {
            var gameplay = new PhaseGameplay();
            _module.OnFixedUpdate(0.02f);
            _module.OnLateUpdate(0.1f);
            Assert.IsEmpty(gameplay.Events);
            var handle = _module.StartAsync(gameplay).GetAwaiter().GetResult();

            _module.SetPaused(true);
            _module.OnFixedUpdate(0.02f);
            _module.OnUpdate(0.1f);
            _module.OnLateUpdate(0.1f);
            Assert.IsEmpty(gameplay.Events);

            _module.SetPaused(false);
            _module.OnFixedUpdate(0.02f);
            Assert.AreEqual(3, gameplay.Events.Count);
            _module.StopAsync(handle).GetAwaiter().GetResult();
            _module.OnFixedUpdate(0.02f);
            _module.OnLateUpdate(0.1f);
            Assert.AreEqual(3, gameplay.Events.Count);
        }

        /// <summary>
        /// 模型在回调中退出玩法后，不再驱动功能和根节点。
        /// </summary>
        [Test]
        public void ExitInModelCallback_SkipsRemainingParts()
        {
            var gameplay = new PhaseGameplay();
            gameplay.Model.OnFixed = gameplay.ForceExit;
            _module.StartAsync(gameplay).GetAwaiter().GetResult();

            _module.OnFixedUpdate(0.02f);

            CollectionAssert.AreEqual(new[] { "model:fixed" }, gameplay.Events);
        }

        /// <summary>
        /// 模型在回调中暂停后，不再派发同轮功能和根节点。
        /// </summary>
        [Test]
        public void PauseInModelCallback_SkipsRemainingParts()
        {
            var gameplay = new PhaseGameplay();
            gameplay.Model.OnFixed = () => _module.SetPaused(true);
            _module.StartAsync(gameplay).GetAwaiter().GetResult();

            _module.OnFixedUpdate(0.02f);

            CollectionAssert.AreEqual(new[] { "model:fixed" }, gameplay.Events);
        }

        /// <summary>
        /// 玩法回调关闭框架后，其他玩法不能收到旧轮派发。
        /// </summary>
        [Test]
        public void StopFrameworkInCallback_SkipsFollowingGameplay()
        {
            var first = new PhaseGameplay();
            var second = new PhaseGameplay();
            first.Model.OnFixed = GameEntry.StopFramework;
            _module.StartAsync(first).GetAwaiter().GetResult();
            _module.StartAsync(second).GetAwaiter().GetResult();
            _module.OnFixedUpdate(0.02f);

            CollectionAssert.AreEqual(new[] { "model:fixed" }, first.Events);
            Assert.IsEmpty(second.Events);
        }

        /// <summary>
        /// 可选阶段接口不改变旧 IModule 注册键及旧 IGameplay 实现要求。
        /// </summary>
        [Test]
        public void OptionalInterfaces_PreserveLegacyRegistration()
        {
            var manager = new ModuleManager();
            var module = new PhaseModule();
            manager.Register(module);
            Assert.AreSame(module, manager.Get<PhaseModule>());
            manager.InitAll();
            manager.FixedUpdateAll(0.02f);
            manager.LateUpdateAll(0.1f);
            Assert.AreEqual(1, module.FixedCount);
            Assert.AreEqual(1, module.LateCount);
            manager.ShutdownAll();

            var stoppingManager = new ModuleManager();
            var stopper = new PhaseModule { PriorityValue = -10 };
            var skipped = new SkippedPhaseModule { PriorityValue = 10 };
            stopper.OnFixed = stoppingManager.ShutdownAll;
            stoppingManager.Register(stopper);
            stoppingManager.Register(skipped);
            stoppingManager.InitAll();
            stoppingManager.FixedUpdateAll(0.02f);
            Assert.AreEqual(0, skipped.FixedCount);

            var legacy = new LegacyGameplay();
            _module.StartAsync(legacy).GetAwaiter().GetResult();
            _module.OnFixedUpdate(0.02f);
            _module.OnUpdate(0.1f);
            _module.OnLateUpdate(0.1f);
            Assert.AreEqual(1, legacy.UpdateCount);
        }

        /// <summary>
        /// 固定帧异常沿用玩法更新失败时的强制停止行为。
        /// </summary>
        [Test]
        public void FixedUpdateFailure_ForceStopsOnlyFailingGameplay()
        {
            var failing = new PhaseGameplay();
            var healthy = new PhaseGameplay();
            failing.Model.OnFixed = () => throw new InvalidOperationException("fixed failed");
            _module.StartAsync(failing).GetAwaiter().GetResult();
            _module.StartAsync(healthy).GetAwaiter().GetResult();

            Assert.DoesNotThrow(() => _module.OnFixedUpdate(0.02f));

            Assert.IsTrue(failing.IsDestroyed);
            CollectionAssert.AreEqual(new[] { "model:fixed", "feature:fixed", "root:fixed" }, healthy.Events);
            Assert.AreEqual(1, _module.Count);
        }

        private class PhaseModule : ModuleBase
        {
            public int PriorityValue = 100;
            public override int Priority => PriorityValue;
            public int FixedCount;
            public int LateCount;
            public Action OnFixed;

            /// <summary>
            /// 记录固定帧并运行测试回调。
            /// </summary>
            /// <param name="deltaTime">固定帧时间间隔。</param>
            public override void OnFixedUpdate(float deltaTime)
            {
                FixedCount++;
                OnFixed?.Invoke();
            }

            /// <summary>
            /// 记录帧末调用。
            /// </summary>
            /// <param name="deltaTime">帧末时间间隔。</param>
            public override void OnLateUpdate(float deltaTime) => LateCount++;
        }

        private sealed class SkippedPhaseModule : PhaseModule { }

        private sealed class PhaseGameplay : GameplayRoot<PhaseGameplay>
        {
            public readonly List<string> Events = new();
            public readonly PhaseModel Model;

            /// <summary>
            /// 创建记录三个阶段的玩法。
            /// </summary>
            public PhaseGameplay()
            {
                Model = AddModel(new PhaseModel());
                AddFeature(new PhaseFeature());
            }

            /// <summary>
            /// 玩法普通帧更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            protected override void OnUpdateGameplay(float deltaTime) => Events.Add("root:update");

            /// <summary>
            /// 玩法固定帧更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            protected override void OnFixedUpdateGameplay(float deltaTime) => Events.Add("root:fixed");

            /// <summary>
            /// 玩法帧末更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            protected override void OnLateUpdateGameplay(float deltaTime) => Events.Add("root:late");
        }

        private sealed class PhaseModel : GameplayModel<PhaseGameplay>
        {
            public Action OnFixed;

            /// <summary>
            /// 局部对象普通帧更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public override void Update(float deltaTime) => Root.Events.Add("model:update");

            /// <summary>
            /// 局部对象固定帧更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public override void FixedUpdate(float deltaTime)
            {
                Root.Events.Add("model:fixed");
                OnFixed?.Invoke();
            }

            /// <summary>
            /// 局部对象帧末更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public override void LateUpdate(float deltaTime) => Root.Events.Add("model:late");
        }

        private sealed class PhaseFeature : GameplayFeature<PhaseGameplay>
        {
            /// <summary>
            /// 功能模块普通帧更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public override void Update(float deltaTime) => Root.Events.Add("feature:update");

            /// <summary>
            /// 功能模块固定帧更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public override void FixedUpdate(float deltaTime) => Root.Events.Add("feature:fixed");

            /// <summary>
            /// 功能模块帧末更新。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public override void LateUpdate(float deltaTime) => Root.Events.Add("feature:late");
        }

        private sealed class LegacyGameplay : IGameplay
        {
            private readonly PhaseGameplay _inner = new();
            public int UpdateCount;
            public string Name => _inner.Name;
            public GameplayState State => _inner.State;
            public bool IsInited => _inner.IsInited;
            public bool IsEntering => _inner.IsEntering;
            public bool IsExiting => _inner.IsExiting;
            public bool IsActive => _inner.IsActive;
            public bool IsDestroyed => _inner.IsDestroyed;

            /// <summary>
            /// 初始化旧接口玩法。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>对应的生命周期任务。</returns>
            public UniTask InitGameplayAsync(CancellationToken ct = default) => _inner.InitGameplayAsync(ct);

            /// <summary>
            /// 进入旧接口玩法。
            /// </summary>
            /// <param name="enterData">玩法进入参数。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>对应的生命周期任务。</returns>
            public UniTask EnterGameplayAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
                => _inner.EnterGameplayAsync(enterData, ct);

            /// <summary>
            /// 退出旧接口玩法。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>对应的生命周期任务。</returns>
            public UniTask ExitGameplayAsync(CancellationToken ct = default) => _inner.ExitGameplayAsync(ct);

            /// <summary>
            /// 强制退出旧接口玩法。
            /// </summary>
            public void ForceExit() => _inner.ForceExit();

            /// <summary>
            /// 销毁旧接口玩法。
            /// </summary>
            public void DestroyGameplay() => _inner.DestroyGameplay();

            /// <summary>
            /// 更新旧接口玩法。
            /// </summary>
            /// <param name="deltaTime">当前阶段的时间间隔。</param>
            public void Update(float deltaTime) => UpdateCount++;
        }
    }
}
