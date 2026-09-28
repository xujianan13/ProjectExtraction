using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameFramework.Tests.Core
{
    /// <summary>
    /// 验证生命周期作用域的清理顺序、重入和模块回调隔离。
    /// </summary>
    [TestFixture]
    public sealed class LifetimeScopeTests
    {
        /// <summary>
        /// 重复释放只清理一次，并按登记逆序执行。
        /// </summary>
        [Test]
        public void Dispose_CleansInReverseOrderOnce()
        {
            var order = new List<int>();
            var scope = new LifetimeScope();
            scope.Defer(() => order.Add(1));
            scope.Defer(() => order.Add(2));
            scope.Dispose();
            scope.Dispose();
            CollectionAssert.AreEqual(new[] { 2, 1 }, order);
            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
            Assert.IsTrue(scope.Token.IsCancellationRequested);
        }

        /// <summary>
        /// 单项清理失败仍释放其余登记，提前清理移除引用。
        /// </summary>
        [Test]
        public void Dispose_IsolatesCleanupErrorsAndEarlyRemoval()
        {
            var count = 0;
            var scope = new LifetimeScope();
            scope.Defer(() => count++);
            scope.Defer(() => throw new InvalidOperationException("test"));
            var early = scope.Defer(() => count++);
            early.Dispose();
            Assert.AreEqual(2, scope.RegistrationCount);
            scope.Dispose();
            Assert.AreEqual(2, count);
            Assert.AreEqual(0, scope.RegistrationCount);
        }

        /// <summary>
        /// 取消回调里释放本作用域不会提前释放取消源。
        /// </summary>
        [Test]
        public void Stop_CancellationCallbackCanDisposeSelf()
        {
            var scope = new LifetimeScope();
            var count = 0;
            scope.Defer(() => count++);
            using var callback = scope.Token.Register(scope.Dispose);
            scope.Stop();
            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
            Assert.AreEqual(1, count);
        }

        /// <summary>
        /// 子级停止时请求父级释放，父级等待子级完成后再清理自身。
        /// </summary>
        [Test]
        public void ChildCancellationCanDisposeParent()
        {
            var order = new List<string>();
            var parent = new LifetimeScope();
            var child = parent.CreateChild();
            parent.Defer(() => order.Add("parent"));
            child.Defer(() => order.Add("child"));
            using var callback = child.Token.Register(parent.Dispose);
            child.Stop();
            CollectionAssert.AreEqual(new[] { "child", "parent" }, order);
            Assert.AreEqual(LifetimeScopeState.Disposed, parent.State);
            Assert.AreEqual(0, parent.ChildCount);
        }

        /// <summary>
        /// 子级清理期间重入父级释放仍然保持子级先于父级。
        /// </summary>
        [Test]
        public void ChildCleanupCanDisposeParent()
        {
            var order = new List<string>();
            var parent = new LifetimeScope();
            var child = parent.CreateChild();
            parent.Defer(() => order.Add("parent"));
            child.Defer(() => order.Add("child-last"));
            child.Defer(() => { order.Add("child-first"); parent.Dispose(); });
            child.Dispose();
            CollectionAssert.AreEqual(new[] { "child-first", "child-last", "parent" }, order);
            Assert.AreEqual(LifetimeScopeState.Disposed, parent.State);
        }

        /// <summary>
        /// 父级停止后，尚未逐一停止的兄弟子级也立即失活。
        /// </summary>
        [Test]
        public void ParentStop_DisablesSiblingCallbacksDuringCancellation()
        {
            var parent = new LifetimeScope();
            var sibling = parent.CreateChild();
            var triggeringChild = parent.CreateChild();
            var called = 0;
            using var callback = triggeringChild.Token.Register(() => { if (sibling.IsActive) called++; });
            parent.Stop();
            Assert.AreEqual(0, called);
            Assert.IsFalse(sibling.IsActive);
            parent.Dispose();
        }

        /// <summary>
        /// 一次性计时器完成后释放登记，避免长生命周期持续持有闭包。
        /// </summary>
        [Test]
        public void Once_ForgetsRegistrationAfterFire()
        {
            var timers = new TimerModule();
            timers.OnInit();
            var scope = new LifetimeScope();
            var called = 0;
            scope.Once(timers, 0f, () => called++);
            Assert.AreEqual(1, scope.RegistrationCount);
            timers.OnUpdate(0.1f);
            Assert.AreEqual(1, called);
            Assert.AreEqual(0, scope.RegistrationCount);
            scope.Dispose();
            timers.OnShutdown();
        }
        /// <summary>
        /// 停止后交入的对象立即释放，也不能创建新子作用域。
        /// </summary>
        [Test]
        public void StoppedScope_ReleasesLateOwnershipImmediately()
        {
            var scope = new LifetimeScope();
            scope.Stop();
            var released = 0;
            Assert.IsFalse(scope.TryOwn(new CallbackDisposable(() => released++), out var registration));
            Assert.IsNull(registration);
            Assert.AreEqual(1, released);
            Assert.IsNull(scope.CreateChild());
            Assert.AreEqual(0, scope.RegistrationCount);
            scope.Dispose();
        }

        /// <summary>
        /// 同轮事件派发中关闭作用域后，后续包装回调不能继续执行业务逻辑。
        /// </summary>
        [Test]
        public void EventDispatch_StopSkipsLaterScopedCallback()
        {
            var events = new EventModule();
            events.OnInit();
            var scope = new LifetimeScope();
            var id = new EventId(92001);
            var called = 0;
            events.Subscribe(id, scope.Stop);
            scope.Subscribe(events, id, () => called++);
            events.Publish(id);
            Assert.AreEqual(0, called);
            scope.Dispose();
            events.OnShutdown();
        }

        private sealed class CallbackDisposable : IDisposable
        {
            private readonly Action _callback;

            /// <summary>
            /// 创建测试释放句柄。
            /// </summary>
            /// <param name="callback">释放时执行的回调。</param>
            public CallbackDisposable(Action callback) => _callback = callback;

            /// <summary>
            /// 执行释放回调。
            /// </summary>
            public void Dispose() => _callback();
        }
    }
}


