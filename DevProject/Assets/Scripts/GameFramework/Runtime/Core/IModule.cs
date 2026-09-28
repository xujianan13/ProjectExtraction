namespace GameFramework
{
    /// <summary>
    /// 模块统一接口：所有框架模块都必须实现，由 ModuleManager 按 Priority 顺序驱动生命周期。
    /// </summary>
    public interface IModule
    {
        /// <summary>
        /// 模块优先级，数值越小越早初始化、越晚卸载。约定区间：基础设施 -1000，基础服务 -500~-100，业务支持 -50~0，上层服务 0~+100。
        /// </summary>
        int Priority { get; }

        /// <summary>
        /// 模块初始化，由 ModuleManager 在启动时按优先级升序调用一次。
        /// </summary>
        void OnInit();

        /// <summary>
        /// 每帧更新，由 GameEntry.Update 驱动。注意：此方法位于热路径，禁止 GC 分配。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        void OnUpdate(float deltaTime);

        /// <summary>
        /// 模块卸载，由 ModuleManager 在应用退出时按优先级降序调用一次。
        /// </summary>
        void OnShutdown();
    }
}
