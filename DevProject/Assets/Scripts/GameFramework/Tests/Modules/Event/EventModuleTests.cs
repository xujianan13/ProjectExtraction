using NUnit.Framework;

namespace GameFramework.Tests.Modules.Event
{
    /// <summary>EventModule 事件订阅、发布、类型校验与派发安全测试。</summary>
    [TestFixture]
    public sealed class EventModuleTests
    {
        private static readonly EventId TestEvent = new(10001);
        private static readonly EventId VoidEvent = new(10002);

        private EventModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _module = new EventModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>带 payload 的 Publish 应正确传递数据给 handler。</summary>
        [Test]
        public void Publish_WithPayload_InvokesHandler()
        {
            var received = default(TestPayload);
            _module.Subscribe<TestPayload>(TestEvent, p => received = p);
            _module.Publish(TestEvent, new TestPayload { Value = 42 });
            Assert.AreEqual(42, received.Value);
        }

        /// <summary>无订阅者时 Publish 应安全空操作。</summary>
        [Test]
        public void Publish_NoSubscribers_DoesNothing()
        {
            Assert.DoesNotThrow(() => _module.Publish(TestEvent, new TestPayload { Value = 1 }));
        }

        /// <summary>无 payload 事件 Publish 应触发 Action handler。</summary>
        [Test]
        public void Publish_VoidEvent_InvokesHandler()
        {
            var count = 0;
            _module.Subscribe(VoidEvent, () => count++);
            _module.Publish(VoidEvent);
            Assert.AreEqual(1, count);
        }

        /// <summary>Unsubscribe 后 handler 不应再被调用。</summary>
        [Test]
        public void Unsubscribe_RemovesHandler()
        {
            var count = 0;
            void Handler() => count++;
            _module.Subscribe(VoidEvent, Handler);
            _module.Unsubscribe(VoidEvent, Handler);
            _module.Publish(VoidEvent);
            Assert.AreEqual(0, count);
        }

        /// <summary>Subscribe 传入 null handler 应抛 ArgumentNullException。</summary>
        [Test]
        public void Subscribe_NullHandler_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _module.Subscribe<TestPayload>(TestEvent, null));
            Assert.Throws<System.ArgumentNullException>(() => _module.Subscribe(VoidEvent, null));
        }

        /// <summary>无 payload 通道上 Publish 带 payload 应抛类型不匹配异常。</summary>
        [Test]
        public void Publish_PayloadTypeMismatch_Throws()
        {
            _module.Subscribe(VoidEvent, () => { });
            Assert.Throws<System.InvalidOperationException>(() => _module.Publish(VoidEvent, new TestPayload()));
        }

        /// <summary>同一 EventId 混用不同 struct payload 类型应抛异常。</summary>
        [Test]
        public void Subscribe_DifferentPayloadTypes_Throws()
        {
            _module.Subscribe<TestPayload>(TestEvent, _ => { });
            Assert.Throws<System.InvalidOperationException>(() => _module.Subscribe<OtherPayload>(TestEvent, _ => { }));
        }

        /// <summary>派发期间 Subscribe 的新 handler 应延后到本次派发结束后生效。</summary>
        [Test]
        public void Publish_DuringDispatch_DeferredSubscribeApplied()
        {
            var count = 0;
            void First(TestPayload p)
            {
                count++;
                _module.Subscribe<TestPayload>(TestEvent, Second);
            }
            void Second(TestPayload p) => count++;
            _module.Subscribe<TestPayload>(TestEvent, First);
            _module.Publish(TestEvent, new TestPayload());
            _module.Publish(TestEvent, new TestPayload());
            Assert.AreEqual(3, count);
        }

        /// <summary>单个 handler 抛异常不应阻断同通道其他 handler。</summary>
        [Test]
        public void Publish_HandlerException_DoesNotBlockOthers()
        {
            var count = 0;
            _module.Subscribe(VoidEvent, () => throw new System.InvalidOperationException("fail"));
            _module.Subscribe(VoidEvent, () => count++);
            _module.Publish(VoidEvent);
            Assert.AreEqual(1, count);
        }

        /// <summary>Clear 应移除指定 EventId 的全部 handler。</summary>
        [Test]
        public void Clear_RemovesAllHandlers()
        {
            var count = 0;
            _module.Subscribe(VoidEvent, () => count++);
            _module.Clear(VoidEvent);
            _module.Publish(VoidEvent);
            Assert.AreEqual(0, count);
        }

        /// <summary>ClearAll 应清空所有事件通道。</summary>
        [Test]
        public void ClearAll_RemovesAllChannels()
        {
            var count = 0;
            _module.Subscribe(VoidEvent, () => count++);
            _module.ClearAll();
            _module.Publish(VoidEvent);
            Assert.AreEqual(0, count);
        }

        private struct TestPayload
        {
            public int Value;
        }

        private struct OtherPayload
        {
            public float Value;
        }
    }
}
