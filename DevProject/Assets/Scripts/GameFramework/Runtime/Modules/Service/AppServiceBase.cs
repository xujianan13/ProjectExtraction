namespace GameFramework
{
    /// <summary>
    /// 业务服务基类，提供 <see cref="IAppService"/> 的默认实现。
    /// </summary>
    public abstract class AppServiceBase : IAppService
    {
        /// <inheritdoc />
        public virtual int Order => 0;

        /// <inheritdoc />
        public virtual void OnInit() { }

        /// <inheritdoc />
        public virtual void OnShutdown() { }

        /// <inheritdoc />
        public virtual void OnUpdate(float deltaTime) { }
    }
}
