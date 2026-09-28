using GameFramework.Tests.Mocks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Service
{
    /// <summary>ServiceModule 业务服务注册、Init/Update/Shutdown 顺序与异常隔离测试。</summary>
    [TestFixture]
    public sealed class ServiceModuleTests
    {
        private ServiceModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _module = new ServiceModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            OrderedShutdownTracker.Reset();
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>RegisterService 后 Get 应返回同一实例。</summary>
        [Test]
        public void RegisterService_AndGet_ReturnsSameInstance()
        {
            var service = new TestAppService();
            _module.RegisterService<IAppService>(service);
            Assert.AreSame(service, _module.Get<IAppService>());
        }

        /// <summary>InitAll 之后禁止再 RegisterService。</summary>
        [Test]
        public void RegisterService_AfterInitAll_Throws()
        {
            _module.InitAll();
            Assert.Throws<System.InvalidOperationException>(() => _module.RegisterService(new TestAppService()));
        }

        /// <summary>InitAll 应按 Order 升序调用所有已注册服务的 OnInit。</summary>
        [Test]
        public void InitAll_CallsOnInitOnAllServices()
        {
            var first = new ServiceA(0);
            var second = new ServiceB(10);
            _module.RegisterService(first);
            _module.RegisterService(second);
            _module.InitAll();
            Assert.AreEqual(1, first.InitCount);
            Assert.AreEqual(1, second.InitCount);
            Assert.IsTrue(_module.IsInitialized);
        }

        /// <summary>InitAll 重复调用不应重复 OnInit。</summary>
        [Test]
        public void InitAll_IsIdempotent()
        {
            var service = new TestAppService();
            _module.RegisterService(service);
            _module.InitAll();
            _module.InitAll();
            Assert.AreEqual(1, service.InitCount);
        }

        /// <summary>InitAll 之前 OnUpdate 不应驱动服务。</summary>
        [Test]
        public void OnUpdate_BeforeInitAll_DoesNothing()
        {
            var service = new TestAppService();
            _module.RegisterService(service);
            _module.OnUpdate(0.1f);
            Assert.AreEqual(0, service.UpdateCount);
        }

        /// <summary>InitAll 之后 OnUpdate 应驱动服务 OnUpdate。</summary>
        [Test]
        public void OnUpdate_AfterInitAll_DrivesServices()
        {
            var service = new TestAppService();
            _module.RegisterService(service);
            _module.InitAll();
            _module.OnUpdate(0.1f);
            Assert.AreEqual(1, service.UpdateCount);
        }

        /// <summary>ShutdownAll 应按 Order 降序调用 OnShutdown。</summary>
        [Test]
        public void ShutdownAll_CallsOnShutdownInReverseOrder()
        {
            var first = new OrderedServiceA(0);
            var second = new OrderedServiceB(10);
            _module.RegisterService(first);
            _module.RegisterService(second);
            _module.InitAll();
            _module.ShutdownAll();
            Assert.Less(second.ShutdownOrder, first.ShutdownOrder);
            Assert.IsFalse(_module.IsInitialized);
        }

        /// <summary>单个服务 OnInit 抛异常不应阻断其他服务初始化。</summary>
        [Test]
        public void InitAll_ServiceThrows_DoesNotBlockOthers()
        {
            var failing = new ServiceA(0) { ThrowOnInit = true };
            var ok = new ServiceB(0);
            _module.RegisterService(failing);
            _module.RegisterService(ok);
            _module.InitAll();
            Assert.AreEqual(1, ok.InitCount);
        }

        /// <summary>RegisterService(null) 应抛 ArgumentNullException。</summary>
        [Test]
        public void RegisterService_Null_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => _module.RegisterService<TestAppService>(null));
        }

        private sealed class ServiceA : TestAppService
        {
            public ServiceA(int order) : base(order) { }
        }

        private sealed class ServiceB : TestAppService
        {
            public ServiceB(int order) : base(order) { }
        }

        private sealed class OrderedServiceA : IAppService
        {
            public int Order { get; }
            public int ShutdownOrder { get; private set; }

            public OrderedServiceA(int order) => Order = order;

            public void OnInit() { }
            public void OnUpdate(float deltaTime) { }

            public void OnShutdown() => ShutdownOrder = OrderedShutdownTracker.Next();
        }

        private sealed class OrderedServiceB : IAppService
        {
            public int Order { get; }
            public int ShutdownOrder { get; private set; }

            public OrderedServiceB(int order) => Order = order;

            public void OnInit() { }
            public void OnUpdate(float deltaTime) { }

            public void OnShutdown() => ShutdownOrder = OrderedShutdownTracker.Next();
        }

        private static class OrderedShutdownTracker
        {
            private static int _seq;

            public static int Next() => ++_seq;

            public static void Reset() => _seq = 0;
        }
        /// <summary>
        /// 初始化失败服务只清理一次，不参与后续更新和统一关闭。
        /// </summary>
        [Test]
        public void InitFailure_CleansUpAndSkipsUpdate()
        {
            var failing = new ServiceA(0) { ThrowOnInit = true };
            var healthy = new ServiceB(1);
            _module.RegisterService(failing);
            _module.RegisterService(healthy);
            _module.InitAll();
            _module.OnUpdate(0.1f);
            Assert.IsNull(_module.Get<ServiceA>());
            _module.ShutdownAll();
            Assert.AreEqual(1, failing.ShutdownCount);
            Assert.AreEqual(0, failing.UpdateCount);
            Assert.AreEqual(1, healthy.UpdateCount);
            Assert.AreEqual(1, healthy.ShutdownCount);
        }

    }
}
