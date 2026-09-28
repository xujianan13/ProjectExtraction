using System;
using System.Threading;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Timer
{
    /// <summary>TimerModule 一次性/循环/下一帧定时器与取消逻辑测试。</summary>
    [TestFixture]
    public sealed class TimerModuleTests
    {
        private TimerModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _module = new TimerModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>Once 应在累加 deltaTime 达到 delay 后触发一次。</summary>
        [Test]
        public void Once_TriggersAfterDelay()
        {
            var count = 0;
            _module.Once(1f, () => count++);
            _module.OnUpdate(0.5f);
            Assert.AreEqual(0, count);
            _module.OnUpdate(0.5f);
            Assert.AreEqual(1, count);
        }

        /// <summary>Once(0) 应在下一 tick 立即触发。</summary>
        [Test]
        public void Once_ZeroDelay_TriggersOnNextTick()
        {
            var count = 0;
            _module.Once(0f, () => count++);
            _module.OnUpdate(0f);
            Assert.AreEqual(1, count);
        }

        /// <summary>Once 负 delay 应视为 0。</summary>
        [Test]
        public void Once_NegativeDelay_TreatedAsZero()
        {
            var count = 0;
            _module.Once(-1f, () => count++);
            _module.OnUpdate(0f);
            Assert.AreEqual(1, count);
        }

        /// <summary>Loop 应按 interval 重复触发指定次数。</summary>
        [Test]
        public void Loop_TriggersRepeatedly()
        {
            var count = 0;
            _module.Loop(0.5f, () => count++, repeatCount: 3);
            _module.OnUpdate(1.5f);
            Assert.AreEqual(3, count);
        }

        /// <summary>Loop repeatCount=-1 应无限循环。</summary>
        [Test]
        public void Loop_InfiniteRepeatCount_KeepsRunning()
        {
            var count = 0;
            _module.Loop(0.1f, () => count++);
            _module.OnUpdate(1f);
            Assert.AreEqual(10, count);
        }

        /// <summary>Loop interval 小于等于 0 应抛 ArgumentOutOfRangeException。</summary>
        [Test]
        public void Loop_InvalidInterval_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _module.Loop(0f, () => { }));
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _module.Loop(-1f, () => { }));
        }

        /// <summary>Loop repeatCount=0 应抛 ArgumentOutOfRangeException。</summary>
        [Test]
        public void Loop_ZeroRepeatCount_Throws()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => _module.Loop(1f, () => { }, repeatCount: 0));
        }

        /// <summary>NextFrame 应在创建后的下一个 OnUpdate 触发。</summary>
        [Test]
        public void NextFrame_TriggersOnFollowingFrame()
        {
            var count = 0;
            _module.NextFrame(() => count++);
            _module.OnUpdate(0f);
            Assert.AreEqual(1, count);
        }

        /// <summary>当前派发回调内创建的 NextFrame 不得同帧触发，应在下一次 OnUpdate 触发。</summary>
        [Test]
        public void NextFrame_ScheduledDuringDispatch_TriggersOnFollowingUpdate()
        {
            var count = 0;
            _module.Once(0f, () => _module.NextFrame(() => count++));

            _module.OnUpdate(0f);
            Assert.AreEqual(0, count);

            _module.OnUpdate(0f);
            Assert.AreEqual(1, count);
        }

        /// <summary>Cancel 有效句柄应返回 true，重复取消返回 false。</summary>
        [Test]
        public void Cancel_ValidHandle_ReturnsTrue()
        {
            var handle = _module.Once(10f, () => { });
            Assert.IsTrue(_module.Cancel(handle));
            Assert.IsFalse(_module.Cancel(handle));
        }

        /// <summary>Cancel 无效句柄（Id=0）应返回 false。</summary>
        [Test]
        public void Cancel_InvalidHandle_ReturnsFalse()
        {
            Assert.IsFalse(_module.Cancel(default));
        }

        /// <summary>CancelAll 应停止所有定时器。</summary>
        [Test]
        public void CancelAll_StopsAllTimers()
        {
            var count = 0;
            _module.Loop(0.1f, () => count++);
            _module.CancelAll();
            _module.OnUpdate(1f);
            Assert.AreEqual(0, count);
        }

        /// <summary>Shutdown 后重新初始化模块时，不得保留上一生命周期的回调。</summary>
        [Test]
        public void OnShutdown_ClearsScheduledCallbacks()
        {
            var count = 0;
            _module.Once(0f, () => count++);

            _module.OnShutdown();
            _module.OnInit();
            _module.OnUpdate(0f);

            Assert.AreEqual(0, count);
        }

        /// <summary>单帧大 deltaTime 应追帧触发多次 Loop。</summary>
        [Test]
        public void OnUpdate_LargeDeltaTime_CatchesUpMultipleTicks()
        {
            var count = 0;
            _module.Loop(0.5f, () => count++, repeatCount: 4);
            _module.OnUpdate(2f);
            Assert.AreEqual(4, count);
        }

        /// <summary>Once/Loop/NextFrame 传入 null callback 应抛 ArgumentNullException。</summary>
        [Test]
        public void Once_NullCallback_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _module.Once(1f, null));
            Assert.Throws<System.ArgumentNullException>(() => _module.Loop(1f, null));
            Assert.Throws<System.ArgumentNullException>(() => _module.NextFrame(null));
        }

        /// <summary>DelayAsync 应在累加 deltaTime 达到 delay 后完成。</summary>
        [Test]
        public void DelayAsync_TriggersAfterDelay()
        {
            var task = _module.DelayAsync(1f);
            _module.OnUpdate(0.5f);
            Assert.IsFalse(task.GetAwaiter().IsCompleted);
            _module.OnUpdate(0.5f);
            task.GetAwaiter().GetResult();
        }

        /// <summary>DelayAsync(0) 应在下一 tick 立即完成。</summary>
        [Test]
        public void DelayAsync_ZeroDelay_CompletesOnNextTick()
        {
            var task = _module.DelayAsync(0f);
            _module.OnUpdate(0f);
            task.GetAwaiter().GetResult();
        }

        /// <summary>DelayAsync 取消时应抛 OperationCanceledException。</summary>
        [Test]
        public void DelayAsync_Cancelled_ThrowsOperationCanceledException()
        {
            using var cts = new CancellationTokenSource();
            var task = _module.DelayAsync(10f, cts.Token);
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
        }

        /// <summary>NextFrameAsync 应在创建后的下一帧完成。</summary>
        [Test]
        public void NextFrameAsync_WaitsOneFrame()
        {
            var task = _module.NextFrameAsync();
            _module.OnUpdate(0f);
            task.GetAwaiter().GetResult();
        }

        /// <summary>NextFrameAsync 取消时应抛 OperationCanceledException。</summary>
        [Test]
        public void NextFrameAsync_Cancelled_ThrowsOperationCanceledException()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                _module.NextFrameAsync(cts.Token).GetAwaiter().GetResult());
        }
        /// <summary>
        /// 取消全部定时器时，异步等待应立即取消。
        /// </summary>
        [Test]
        public void CancelAll_CancelsAsyncWaiters()
        {
            var delayed = _module.DelayAsync(10f);
            var next = _module.NextFrameAsync();
            _module.CancelAll();
            Assert.Throws<OperationCanceledException>(() => delayed.GetAwaiter().GetResult());
            Assert.Throws<OperationCanceledException>(() => next.GetAwaiter().GetResult());
        }

        /// <summary>
        /// 定时回调内关闭模块时，应取消其他等待且不再读取已清空的列表。
        /// </summary>
        [Test]
        public void CallbackShutdown_CancelsWaitersWithoutIndexError()
        {
            _module.NextFrame(() => _module.OnShutdown());
            var waiting = _module.DelayAsync(10f);
            Assert.DoesNotThrow(() => _module.OnUpdate(0f));
            Assert.Throws<OperationCanceledException>(() => waiting.GetAwaiter().GetResult());
        }

    }
}
