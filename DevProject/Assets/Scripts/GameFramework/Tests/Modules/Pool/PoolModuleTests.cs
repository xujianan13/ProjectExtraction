using System;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Pool
{
    /// <summary>PoolModule 注册、懒创建、租还与多命名池测试。</summary>
    [TestFixture]
    public sealed class PoolModuleTests
    {
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IPoolModule>(new PoolModule());
            GameEntry.StartFramework();
        }

        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>注册后 Rent / Return 应复用同一实例。</summary>
        [Test]
        public void RentReturn_DefaultPool_ReusesInstance()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<TestItem>(() => new TestItem(), prewarmCount: 1));

            var first = pools.Rent<TestItem>();
            pools.Return(first);
            var second = pools.Rent<TestItem>();

            Assert.AreSame(first, second);
        }

        /// <summary>默认池未注册时 Rent 应自动懒创建。</summary>
        [Test]
        public void Rent_DefaultPool_AutoCreates()
        {
            var pools = GameEntry.Get<IPoolModule>();
            Assert.IsFalse(pools.IsRegistered<AutoItem>());

            var item = pools.Rent<AutoItem>();

            Assert.IsNotNull(item);
            Assert.IsTrue(pools.IsRegistered<AutoItem>());
        }

        /// <summary>命名池未注册时 Rent 应抛 InvalidOperationException。</summary>
        [Test]
        public void Rent_NamedPool_NotRegistered_Throws()
        {
            var pools = GameEntry.Get<IPoolModule>();
            Assert.Throws<InvalidOperationException>(() => pools.Rent<TestItem>("missing"));
        }

        /// <summary>ConfigureDefault 后无参构造不可用类型也可懒创建。</summary>
        [Test]
        public void Rent_ConfigureDefault_UsesCustomFactory()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.ConfigureDefault(new PoolDescriptor<NoDefaultCtorItem>(() => new NoDefaultCtorItem(42)));

            var item = pools.GetOrCreatePoolConfigured<NoDefaultCtorItem>().Rent();

            Assert.AreEqual(42, item.Value);
            Assert.IsTrue(pools.IsRegistered<NoDefaultCtorItem>());
        }

        /// <summary>同类型不同 name 应隔离为独立池。</summary>
        [Test]
        public void Register_NamedPools_AreIsolated()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<TestItem>(() => new TestItem { Id = 1 }), name: "a");
            pools.Register(new PoolDescriptor<TestItem>(() => new TestItem { Id = 2 }), name: "b");

            var fromA = pools.Rent<TestItem>("a");
            var fromB = pools.Rent<TestItem>("b");

            Assert.AreEqual(1, fromA.Id);
            Assert.AreEqual(2, fromB.Id);
            Assert.IsTrue(pools.IsRegistered<TestItem>("a"));
            Assert.IsTrue(pools.IsRegistered<TestItem>("b"));
            Assert.IsFalse(pools.IsRegistered<TestItem>());
        }

        /// <summary>TryGetPool 未注册返回 false（不触发懒创建）。</summary>
        [Test]
        public void TryGetPool_NotRegistered_ReturnsFalse()
        {
            var pools = GameEntry.Get<IPoolModule>();
            Assert.IsFalse(pools.TryGetPool<TestItem>(out _));
        }

        /// <summary>重复注册应替换旧池，新 Rent 使用新工厂逻辑。</summary>
        [Test]
        public void Register_ReplaceExisting_UsesNewPool()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<TestItem>(() => new TestItem { Id = 1 }));
            pools.Register(new PoolDescriptor<TestItem>(() => new TestItem { Id = 2 }));

            var item = pools.Rent<TestItem>();
            Assert.AreEqual(2, item.Id);
        }

        /// <summary>GetPool 返回的实例与 Rent 共用同一底层池。</summary>
        [Test]
        public void GetPool_ReturnsSamePoolAsRent()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<TestItem>(() => new TestItem()));

            var pool = pools.GetOrCreatePool<TestItem>();
            var rented = pool.Rent();
            pools.Return(rented);
            var fromModule = pools.Rent<TestItem>();

            Assert.AreSame(rented, fromModule);
        }

        /// <summary>ReturnRuntime 应按实例运行时类型归还到各自默认池。</summary>
        [Test]
        public void ReturnRuntime_PolymorphicList_DispatchesByConcreteType()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<ChildA>(() => new ChildA()));
            pools.Register(new PoolDescriptor<ChildB>(() => new ChildB()));

            var a = pools.Rent<ChildA>();
            var b = pools.Rent<ChildB>();
            var list = new System.Collections.Generic.List<BaseItem> { a, b };

            for (int i = 0; i < list.Count; i++)
            {
                pools.ReturnRuntime(list[i]);
            }

            Assert.AreSame(a, pools.Rent<ChildA>());
            Assert.AreSame(b, pools.Rent<ChildB>());
        }

        /// <summary>IPooledObject.Release 应归还到正确池。</summary>
        [Test]
        public void IPooledObject_Release_ReturnsToCorrectPool()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<PooledChild>(() => new PooledChild()));

            var item = pools.Rent<PooledChild>();
            item.Release();

            Assert.AreSame(item, pools.Rent<PooledChild>());
        }

        /// <summary>默认池仅配置但未创建时，Return 不应隐式创建池。</summary>
        [Test]
        public void Return_ConfiguredButNotCreated_ThrowsWithoutCreatingPool()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.ConfigureDefault(new PoolDescriptor<NoDefaultCtorItem>(() => new NoDefaultCtorItem(42)));
            var item = new NoDefaultCtorItem(42);

            Assert.Throws<InvalidOperationException>(() => pools.Return(item));
            Assert.IsFalse(pools.IsRegistered<NoDefaultCtorItem>());
        }

        /// <summary>命名池租出的 IPooledObject.Release 应归还到命名池。</summary>
        [Test]
        public void IPooledObject_ReleaseNamedPool_ReturnsToNamedPool()
        {
            var pools = GameEntry.Get<IPoolModule>();
            pools.Register(new PoolDescriptor<PooledChild>(() => new PooledChild()), "named");

            var item = pools.Rent<PooledChild>("named");
            item.Release();

            Assert.AreSame(item, pools.Rent<PooledChild>("named"));
        }

        private class BaseItem
        {
        }

        private sealed class ChildA : BaseItem
        {
        }

        private sealed class ChildB : BaseItem
        {
        }

        private sealed class PooledChild : IPooledObject
        {
            private IPoolModule _poolModule;
            private string _name;

            public void BindPool(IPoolModule poolModule, string name)
            {
                _poolModule = poolModule;
                _name = name;
            }

            public void Release()
            {
                if (_poolModule == null) return;

                if (string.IsNullOrEmpty(_name))
                {
                    _poolModule.ReturnRuntime(this);
                }
                else
                {
                    _poolModule.Return(this, _name);
                }

                _poolModule = null;
                _name = null;
            }
        }

        private sealed class TestItem
        {
            public int Id;
        }

        private sealed class AutoItem
        {
        }

        private sealed class NoDefaultCtorItem
        {
            public int Value;

            public NoDefaultCtorItem(int value)
            {
                Value = value;
            }
        }
    }
}
