namespace GameFramework.Tests.Mocks
{
    /// <summary>
    /// 可追踪生命周期的测试用业务服务。
    /// </summary>
    internal class TestAppService : IAppService
    {
        public int Order { get; }
        public int InitCount { get; private set; }
        public int UpdateCount { get; private set; }
        public int ShutdownCount { get; private set; }
        public bool ThrowOnInit { get; set; }
        public bool ThrowOnUpdate { get; set; }
        public bool ThrowOnShutdown { get; set; }

        public TestAppService(int order = 0) => Order = order;

        public void OnInit()
        {
            InitCount++;
            if (ThrowOnInit) throw new System.InvalidOperationException("Init failed");
        }

        public void OnShutdown()
        {
            ShutdownCount++;
            if (ThrowOnShutdown) throw new System.InvalidOperationException("Shutdown failed");
        }

        public void OnUpdate(float deltaTime)
        {
            UpdateCount++;
            if (ThrowOnUpdate) throw new System.InvalidOperationException("Update failed");
        }
    }
}
