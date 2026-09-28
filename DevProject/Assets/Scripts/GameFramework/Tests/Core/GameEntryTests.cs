using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameFramework.Tests.Core
{
    /// <summary>GameEntry 模块注册、获取与生命周期测试。</summary>
    [TestFixture]
    public sealed class GameEntryTests
    {
        [SetUp]
        public void SetUp() => FrameworkTestHelper.ResetFramework();

        [TearDown]
        public void TearDown() => FrameworkTestHelper.ResetFramework();

        /// <summary>未注册任何模块时，Get 应返回 null。</summary>
        [Test]
        public void Get_UnregisteredModule_ReturnsNull()
        {
            Assert.IsNull(GameEntry.Get<ILogModule>());
        }

        /// <summary>Register 后 Get 应返回同一实例。</summary>
        [Test]
        public void Register_AndGet_ReturnsSameInstance()
        {
            var module = new LogModule();
            GameEntry.Register<ILogModule>(module);
            Assert.AreSame(module, GameEntry.Get<ILogModule>());
        }

        /// <summary>省略泛型传入具体实例时，应自动映射到模块接口 Key。</summary>
        [Test]
        public void Register_ConcreteInstance_ResolvesModuleInterfaceKey()
        {
            var module = new LogModule();
            GameEntry.Register(module);
            Assert.AreSame(module, GameEntry.Get<ILogModule>());
        }

        /// <summary>同接口重复 Register 应替换旧实例。</summary>
        [Test]
        public void Register_SameInterface_ReplacesExisting()
        {
            var first = new LogModule();
            var second = new LogModule();
            GameEntry.Register<ILogModule>(first);
            GameEntry.Register<ILogModule>(second);
            Assert.AreSame(second, GameEntry.Get<ILogModule>());
        }

        /// <summary>StartFramework 之后禁止再 Register。</summary>
        [Test]
        public void Register_AfterStartFramework_Throws()
        {
            FrameworkTestHelper.SetupWithLog();
            Assert.Throws<System.InvalidOperationException>(() => GameEntry.Register<ILogModule>(new LogModule()));
        }

        /// <summary>RegisterDefaults 重复调用不应覆盖已注册模块。</summary>
        [Test]
        public void RegisterDefaults_IsIdempotent()
        {
            GameEntry.RegisterDefaults();
            var log1 = GameEntry.Get<ILogModule>();
            GameEntry.RegisterDefaults();
            var log2 = GameEntry.Get<ILogModule>();
            Assert.AreSame(log1, log2);
            Assert.IsNotNull(GameEntry.Get<IPoolModule>());
            Assert.IsNotNull(GameEntry.Get<IModelModule>());
        }

        /// <summary>StartFramework 重复调用不应重复触发 OnInit。</summary>
        [Test]
        public void StartFramework_IsIdempotent()
        {
            var tracker = new InitTrackerModule();
            GameEntry.Register<InitTrackerModule>(tracker);
            GameEntry.StartFramework();
            GameEntry.StartFramework();
            Assert.AreEqual(1, tracker.InitCount);
        }

        /// <summary>模块应按 Priority 升序初始化（低 Priority 先 Init）。</summary>
        [Test]
        public void ModuleInit_OrderedByPriority()
        {
            var high = new HighTrackerModule();
            var low = new LowTrackerModule();
            GameEntry.Register<HighTrackerModule>(high);
            GameEntry.Register<LowTrackerModule>(low);
            GameEntry.StartFramework();
            Assert.Less(low.InitOrder, high.InitOrder);
        }

        /// <summary>
        /// 初始化失败时回滚当前模块及之前已初始化的模块，失败后不更新，随后可以重试。
        /// </summary>
        [Test]
        public void ModuleInit_Failure_RollsBackAndAllowsRetry()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            var first = new RecordingModule<FirstMarker>("first", -10, events);
            var second = new RecordingModule<SecondMarker>("second", 0, events);
            var failing = new RecordingModule<FailingMarker>("failing", 10, events) { FailOnInit = true };
            manager.Register(first);
            manager.Register(second);
            manager.Register(failing);

            manager.UpdateAll(0.1f);
            Assert.IsEmpty(events);

            Assert.Throws<InvalidOperationException>(() => manager.InitAll());
            CollectionAssert.AreEqual(
                new[] { "first.init", "second.init", "failing.init", "failing.shutdown", "second.shutdown", "first.shutdown" }, events);

            manager.UpdateAll(0.1f);
            Assert.AreEqual(6, events.Count);

            failing.FailOnInit = false;
            events.Clear();
            manager.InitAll();
            manager.UpdateAll(0.1f);
            manager.ShutdownAll();
            CollectionAssert.AreEqual(
                new[] { "first.init", "second.init", "failing.init", "first.update", "second.update", "failing.update",
                    "failing.shutdown", "second.shutdown", "first.shutdown" }, events);
        }

        /// <summary>
        /// 未启动时卸载不应调用模块的关闭回调。
        /// </summary>
        [Test]
        public void ShutdownAll_BeforeStart_DoesNotShutdownModule()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.Register(new RecordingModule<FirstMarker>("module", 0, events));

            manager.ShutdownAll();
            manager.ShutdownAll();
            manager.UpdateAll(0.1f);

            Assert.IsEmpty(events);
        }

        /// <summary>
        /// 初始化失败并回滚后，显式卸载不应重复关闭已回滚模块。
        /// </summary>
        [Test]
        public void ShutdownAll_AfterFailedStart_DoesNotShutdownAgain()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            manager.Register(new RecordingModule<FirstMarker>("first", -10, events));
            manager.Register(new RecordingModule<FailingMarker>("failing", 0, events) { FailOnInit = true });

            Assert.Throws<InvalidOperationException>(() => manager.InitAll());
            manager.ShutdownAll();
            CollectionAssert.AreEqual(new[] { "first.init", "failing.init", "failing.shutdown", "first.shutdown" }, events);
        }

        /// <summary>
        /// 单个模块关闭失败时仍清理其余模块，并且下次关闭不重复调用。
        /// </summary>
        [Test]
        public void ShutdownAll_Failure_ClosesRemainingModulesAndClearsState()
        {
            var events = new List<string>();
            var manager = new ModuleManager();
            var first = new RecordingModule<FirstMarker>("first", -10, events);
            var failing = new RecordingModule<FailingMarker>("failing", 10, events) { FailOnShutdown = true };
            manager.Register(first);
            manager.Register(failing);
            manager.InitAll();
            events.Clear();

            Assert.DoesNotThrow(manager.ShutdownAll);
            CollectionAssert.AreEqual(new[] { "failing.shutdown", "first.shutdown" }, events);
            Assert.IsFalse(manager.IsInitialized);
            Assert.IsNull(manager.Get<RecordingModule<FirstMarker>>());
            manager.ShutdownAll();
            Assert.AreEqual(2, events.Count);
        }

        /// <summary>
        /// 标识第一个测试模块的注册类型。
        /// </summary>
        private sealed class FirstMarker { }

        /// <summary>
        /// 标识第二个测试模块的注册类型。
        /// </summary>
        private sealed class SecondMarker { }

        /// <summary>
        /// 标识失败测试模块的注册类型。
        /// </summary>
        private sealed class FailingMarker { }

        /// <summary>
        /// 用泛型标记区分模块注册键并记录生命周期事件。
        /// </summary>
        /// <typeparam name="TMarker">测试模块的注册类型标记。</typeparam>
        private sealed class RecordingModule<TMarker> : ModuleBase
        {
            private readonly string _name;
            private readonly List<string> _events;
            private readonly int _priority;

            /// <summary>
            /// 创建生命周期记录模块。
            /// </summary>
            /// <param name="name">模块名称。</param>
            /// <param name="priority">模块优先级。</param>
            /// <param name="events">生命周期事件列表。</param>
            public RecordingModule(string name, int priority, List<string> events)
            {
                _name = name;
                _priority = priority;
                _events = events;
            }

            /// <summary>
            /// 获取模块优先级。
            /// </summary>
            public override int Priority => _priority;

            /// <summary>
            /// 控制下次初始化是否失败。
            /// </summary>
            public bool FailOnInit { get; set; }

            /// <summary>
            /// 控制关闭时是否模拟失败。
            /// </summary>
            public bool FailOnShutdown { get; set; }

            /// <summary>
            /// 记录初始化，按设置模拟初始化失败。
            /// </summary>
            public override void OnInit()
            {
                _events.Add($"{_name}.init");
                if (FailOnInit) throw new InvalidOperationException("模拟模块初始化失败。");
            }

            /// <summary>
            /// 记录模块更新。
            /// </summary>
            /// <param name="deltaTime">距上一帧的时间间隔。</param>
            public override void OnUpdate(float deltaTime) => _events.Add($"{_name}.update");

            /// <summary>
            /// 记录模块关闭。
            /// </summary>
            public override void OnShutdown()
            {
                _events.Add($"{_name}.shutdown");
                if (FailOnShutdown) throw new InvalidOperationException("模拟模块关闭失败。");
            }
        }

        private sealed class HighTrackerModule : ModuleBase
        {
            public override int Priority => 100;
            public int InitOrder { get; private set; }

            public override void OnInit() => InitOrder = InitOrderTracker.Next();

            public override void OnShutdown() => InitOrderTracker.Reset();
        }

        private sealed class LowTrackerModule : ModuleBase
        {
            public override int Priority => -100;
            public int InitOrder { get; private set; }

            public override void OnInit() => InitOrder = InitOrderTracker.Next();

            public override void OnShutdown() => InitOrderTracker.Reset();
        }

        private static class InitOrderTracker
        {
            private static int _order;

            public static int Next() => ++_order;

            public static void Reset() => _order = 0;
        }

        private sealed class InitTrackerModule : ModuleBase
        {
            public int PriorityOverride { get; set; }
            public override int Priority => PriorityOverride;
            public int InitCount { get; private set; }

            public override void OnInit() => InitCount++;
        }
    }
}
