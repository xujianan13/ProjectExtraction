using System;
using NUnit.Framework;

namespace GameFramework.Tests.Utilities.Pool
{
    /// <summary>ObjectPool 租还、预分配、上限与 IPoolable 回调测试。</summary>
    [TestFixture]
    public sealed class ObjectPoolTests
    {
        /// <summary>空池租出应通过工厂创建新实例。</summary>
        [Test]
        public void Rent_EmptyPool_CreatesViaFactory()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem());
            var item = pool.Rent();

            Assert.IsNotNull(item);
            Assert.AreEqual(1, pool.CountAll);
            Assert.AreEqual(1, pool.CountActive);
            Assert.AreEqual(0, pool.CountInactive);
        }

        /// <summary>归还后再租出应复用同一实例。</summary>
        [Test]
        public void Rent_AfterReturn_ReusesInstance()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem());
            var first = pool.Rent();
            pool.Return(first);
            var second = pool.Rent();

            Assert.AreSame(first, second);
            Assert.AreEqual(1, pool.CountAll);
            Assert.AreEqual(1, pool.CountActive);
        }

        /// <summary>构造时 prewarm 应预置闲置实例。</summary>
        [Test]
        public void Prewarm_OnConstruct_FillsInactive()
        {
            var pool = new ObjectPool<TestItem>(8, () => new TestItem(), prewarmCount: 3);

            Assert.AreEqual(3, pool.CountAll);
            Assert.AreEqual(3, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive);
        }

        /// <summary>Warmup 应在上限内追加闲置实例。</summary>
        [Test]
        public void Warmup_AddsInactiveUpToMaxSize()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem(), maxSize: 2);
            pool.Warmup(5);

            Assert.AreEqual(2, pool.CountInactive);
            Assert.AreEqual(2, pool.CountAll);
        }

        /// <summary>闲置达 maxSize 后归还的对象不再入池，CountAll 递减。</summary>
        [Test]
        public void Return_ExceedsMaxSize_DropsInstance()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem(), maxSize: 1, prewarmCount: 1);
            var a = pool.Rent();
            var b = pool.Rent();
            Assert.AreEqual(2, pool.CountAll);

            pool.Return(a);
            Assert.AreEqual(1, pool.CountInactive);
            Assert.AreEqual(2, pool.CountAll);

            pool.Return(b);
            Assert.AreEqual(1, pool.CountInactive);
            Assert.AreEqual(1, pool.CountAll);
        }

        /// <summary>Clear 仅移除闲置实例，已租出实例仍计入 CountAll。</summary>
        [Test]
        public void Clear_RemovesInactiveOnly()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem(), prewarmCount: 2);
            var rented = pool.Rent();
            pool.Clear();

            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(1, pool.CountAll);
            Assert.AreEqual(1, pool.CountActive);
            pool.Return(rented);
            Assert.AreEqual(1, pool.CountInactive);
        }

        /// <summary>实现 IPoolable 的类型应在租还时触发回调。</summary>
        [Test]
        public void RentReturn_IPoolable_InvokesCallbacks()
        {
            var pool = new ObjectPool<PoolableItem>(4, () => new PoolableItem());
            var item = pool.Rent();
            Assert.AreEqual(1, item.RentCount);

            pool.Return(item);
            Assert.AreEqual(1, item.ReturnCount);
        }

        /// <summary>构造传入的 onRent / onReturn 应与 IPoolable 一并触发。</summary>
        [Test]
        public void RentReturn_ActionCallbacks_Invoked()
        {
            int rentCalls = 0;
            int returnCalls = 0;
            var pool = new ObjectPool<TestItem>(
                4,
                () => new TestItem(),
                _ => rentCalls++,
                _ => returnCalls++);

            var item = pool.Rent();
            pool.Return(item);

            Assert.AreEqual(1, rentCalls);
            Assert.AreEqual(1, returnCalls);
            Assert.IsNotNull(item);
        }

        /// <summary>factory 为 null 应抛 ArgumentNullException。</summary>
        [Test]
        public void Construct_NullFactory_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new ObjectPool<TestItem>(4, null));
        }

        /// <summary>factory 返回 null 应抛 InvalidOperationException。</summary>
        [Test]
        public void Rent_NullFactoryResult_Throws()
        {
            var pool = new ObjectPool<TestItem>(4, () => null);

            Assert.Throws<InvalidOperationException>(() => pool.Rent());
            Assert.AreEqual(0, pool.CountAll);
        }

        /// <summary>归还 null 应抛 ArgumentNullException。</summary>
        [Test]
        public void Return_Null_Throws()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem());
            Assert.Throws<ArgumentNullException>(() => pool.Return(null));
        }

        /// <summary>非本池租出的实例归还时应抛 InvalidOperationException。</summary>
        [Test]
        public void Return_ExternalObject_Throws()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem());
            var item = new TestItem();

            Assert.Throws<InvalidOperationException>(() => pool.Return(item));
            Assert.AreEqual(0, pool.CountAll);
            Assert.AreEqual(0, pool.CountInactive);
        }

        /// <summary>同一实例重复归还时应抛 InvalidOperationException。</summary>
        [Test]
        public void Return_SameInstanceTwice_Throws()
        {
            var pool = new ObjectPool<TestItem>(4, () => new TestItem());
            var item = pool.Rent();
            pool.Return(item);

            Assert.Throws<InvalidOperationException>(() => pool.Return(item));
            Assert.AreEqual(1, pool.CountAll);
            Assert.AreEqual(1, pool.CountInactive);
        }

        private sealed class TestItem
        {
        }

        private sealed class PoolableItem : IPoolable
        {
            public int RentCount;
            public int ReturnCount;

            public void OnRent() => RentCount++;

            public void OnReturn() => ReturnCount++;
        }
        /// <summary>
        /// 租出回调失败时，池应丢弃无法交给调用方的实例并保持计数一致。
        /// </summary>
        [Test]
        public void RentCallbackFailure_DropsUnreachableItem()
        {
            var pool = new ObjectPool<TestItem>(0, () => new TestItem(), onRent: _ => throw new InvalidOperationException());
            Assert.Throws<InvalidOperationException>(() => pool.Rent());
            Assert.AreEqual(0, pool.CountAll);
            Assert.AreEqual(0, pool.CountActive);
        }

        /// <summary>
        /// 归还回调失败时，池应丢弃该实例并保持计数一致。
        /// </summary>
        [Test]
        public void ReturnCallbackFailure_DropsItem()
        {
            var pool = new ObjectPool<TestItem>(0, () => new TestItem(), onReturn: _ => throw new InvalidOperationException());
            var item = pool.Rent();
            Assert.Throws<InvalidOperationException>(() => pool.Return(item));
            Assert.AreEqual(0, pool.CountAll);
            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(0, pool.CountActive);
        }

    }
}
