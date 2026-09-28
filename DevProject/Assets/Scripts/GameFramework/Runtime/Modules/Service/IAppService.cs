namespace GameFramework
{
    /// <summary>
    /// 业务服务契约（非 <see cref="IModule"/>）。由 GameLogic 实现，通过 <see cref="IServiceModule"/> 注册与获取。
    /// 服务在场景切换期间保持存活，与互斥的 <see cref="IScene"/> 互补。
    /// </summary>
    public interface IAppService
    {
        /// <summary>初始化顺序，越小越早 OnInit、越晚 OnShutdown。</summary>
        int Order { get; }

        /// <summary>服务初始化，在 <see cref="IServiceModule.InitAll"/> 时按 Order 升序调用一次。</summary>
        void OnInit();

        /// <summary>服务卸载，在 <see cref="IServiceModule.ShutdownAll"/> 时按 Order 降序调用一次。</summary>
        void OnShutdown();

        /// <summary>
        /// 每帧更新，由 ServiceModule 在 InitAll 之后按 Order 升序驱动。
        /// 注意：位于热路径，禁止 GC 分配。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        void OnUpdate(float deltaTime);
    }
}
