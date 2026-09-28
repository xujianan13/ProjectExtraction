namespace GameFramework
{
    /// <summary>
    /// 业务服务管理模块。负责服务注册、按 Order 有序 Init / Shutdown 与按类型查询。
    /// 业务通过 <see cref="GameEntry.Get{IServiceModule}"/> 访问；具体服务实现放在 GameLogic 层。
    /// </summary>
    public interface IServiceModule : IModule
    {
        /// <summary>是否已调用 <see cref="InitAll"/>。</summary>
        bool IsInitialized { get; }

        /// <summary>
        /// 注册业务服务。须在 <see cref="InitAll"/> 之前调用；同类型已注册则替换。
        /// </summary>
        /// <typeparam name="T">服务类型（用作查询 Key，请使用具体实现类型）。</typeparam>
        void RegisterService<T>(T service) where T : class, IAppService;

        /// <summary>获取已注册的服务；未注册返回 null。</summary>
        T Get<T>() where T : class, IAppService;

        /// <summary>按 Order 升序初始化所有已注册服务。注册完成后由 GameMain 调用一次。</summary>
        void InitAll();

        /// <summary>按 Order 降序卸载所有服务并清空注册表。</summary>
        void ShutdownAll();
    }
}
