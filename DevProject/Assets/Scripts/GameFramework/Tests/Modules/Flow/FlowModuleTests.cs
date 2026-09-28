using NUnit.Framework;

namespace GameFramework.Tests.Modules.Flow
{
    /// <summary>FlowModule 启动、暂停、组合流程与默认注册测试。</summary>
    [TestFixture]
    public sealed class FlowModuleTests
    {
        private FlowModule _flows;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            GameEntry.Register<ILogModule>(new LogModule());
            _flows = new FlowModule();
            GameEntry.Register<IFlowModule>(_flows);
            GameEntry.StartFramework();
        }

        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
            _flows = null;
        }

        /// <summary>根流程应按帧推进直到成功完成；终态后从调度器移除，句柄状态变为 Idle。</summary>
        [Test]
        public void Start_RootFlow_TicksUntilSucceeded()
        {
            var flow = new CountingFlow(2);
            FlowHandle handle = _flows.Start(flow);

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(1, flow.TickCount);
            Assert.AreEqual(FlowState.Running, _flows.GetState(handle));

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(2, flow.TickCount);
            Assert.AreEqual(FlowState.Succeeded, flow.State);
            Assert.AreEqual(FlowState.Idle, _flows.GetState(handle));
            Assert.IsFalse(_flows.IsRunning(handle));
            Assert.IsFalse(_flows.TryGetFlow(handle, out _));
        }

        /// <summary>暂停后的根流程不应继续轮询，恢复后继续推进。</summary>
        [Test]
        public void PauseResume_RootFlow_StopsAndContinuesTicking()
        {
            var flow = new CountingFlow(1);
            FlowHandle handle = _flows.Start(flow);

            Assert.IsTrue(_flows.Pause(handle));
            _flows.OnUpdate(0.1f);
            Assert.AreEqual(0, flow.TickCount);
            Assert.AreEqual(FlowState.Paused, _flows.GetState(handle));

            Assert.IsTrue(_flows.Resume(handle));
            _flows.OnUpdate(0.1f);
            Assert.AreEqual(1, flow.TickCount);
            Assert.AreEqual(FlowState.Succeeded, flow.State);
            Assert.AreEqual(FlowState.Idle, _flows.GetState(handle));
        }

        /// <summary>顺序流程应当前子流程成功后再启动下一个子流程。</summary>
        [Test]
        public void SequenceFlow_RunsChildrenInOrder()
        {
            var first = new CountingFlow(1, "first");
            var second = new CountingFlow(1, "second");
            var sequence = FlowBuilder.Sequence(first, second);
            FlowHandle handle = _flows.Start(sequence);

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(FlowState.Succeeded, first.State);
            Assert.AreEqual(FlowState.Running, second.State);
            Assert.AreEqual(0, second.TickCount);
            Assert.AreEqual(FlowState.Running, _flows.GetState(handle));

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(FlowState.Succeeded, second.State);
            Assert.AreEqual(FlowState.Succeeded, sequence.State);
            Assert.AreEqual(FlowState.Idle, _flows.GetState(handle));
        }

        /// <summary>All 策略的并行流程应等待全部子流程成功。</summary>
        [Test]
        public void ParallelFlow_All_WaitsForEveryChild()
        {
            var slow = new CountingFlow(2, "slow");
            var fast = new CountingFlow(1, "fast");
            var parallel = FlowBuilder.Parallel(slow, fast);
            FlowHandle handle = _flows.Start(parallel);

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(FlowState.Running, slow.State);
            Assert.AreEqual(FlowState.Succeeded, fast.State);
            Assert.AreEqual(FlowState.Running, _flows.GetState(handle));

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(FlowState.Succeeded, slow.State);
            Assert.AreEqual(FlowState.Succeeded, parallel.State);
            Assert.AreEqual(FlowState.Idle, _flows.GetState(handle));
        }

        /// <summary>Any 策略的并行流程应在任一子流程成功后取消其他未完成子流程。</summary>
        [Test]
        public void ParallelFlow_Any_CancelsUnfinishedChildren()
        {
            var slow = new CountingFlow(2, "slow");
            var fast = new CountingFlow(1, "fast");
            var parallel = FlowBuilder.Parallel(FlowParallelPolicy.Any, slow, fast);
            FlowHandle handle = _flows.Start(parallel);

            _flows.OnUpdate(0.1f);

            Assert.AreEqual(FlowState.Canceled, slow.State);
            Assert.AreEqual(FlowState.Succeeded, fast.State);
            Assert.AreEqual(FlowState.Succeeded, parallel.State);
            Assert.AreEqual(FlowState.Idle, _flows.GetState(handle));
        }

        /// <summary>Cancel 后下一帧 compact，调度器不再保留该流程状态。</summary>
        [Test]
        public void Cancel_ThenUpdate_RemovesState()
        {
            var flow = new CountingFlow(5);
            FlowHandle handle = _flows.Start(flow);

            Assert.IsTrue(_flows.Cancel(handle));
            Assert.AreEqual(FlowState.Canceled, flow.State);
            Assert.AreEqual(FlowState.Canceled, _flows.GetState(handle));

            _flows.OnUpdate(0.1f);
            Assert.AreEqual(FlowState.Idle, _flows.GetState(handle));
        }

        /// <summary>默认模块注册应包含 FlowModule。</summary>
        [Test]
        public void RegisterDefaults_IncludesFlowModule()
        {
            FrameworkTestHelper.ResetFramework();

            GameEntry.RegisterDefaults();

            Assert.IsNotNull(GameEntry.Get<IFlowModule>());
        }

        private sealed class CountingFlow : FlowBase
        {
            private readonly int _targetTicks;

            public CountingFlow(int targetTicks, string name = null) : base(name)
            {
                _targetTicks = targetTicks;
            }

            public int TickCount { get; private set; }

            protected override FlowTickResult OnTick(float deltaTime)
            {
                TickCount++;
                return TickCount >= _targetTicks ? FlowTickResult.Succeeded : FlowTickResult.Running;
            }

            protected override void OnReset()
            {
                TickCount = 0;
            }
        }
    }
}
