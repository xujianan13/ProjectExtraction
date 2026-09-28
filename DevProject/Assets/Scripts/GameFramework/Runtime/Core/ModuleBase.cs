namespace GameFramework
{
    /// <summary>
    /// 模块基类，提供 IModule 的默认空实现，业务模块继承此类后只覆盖需要的生命周期方法即可。
    /// </summary>
    public abstract class ModuleBase : IModule, IFixedUpdateModule, ILateUpdateModule
    {
        /// <summary>
        /// 默认优先级 0。具体模块应按职责覆盖此值（参考 IModule.Priority 的区间约定）。
        /// </summary>
        public virtual int Priority => 0;

        /// <inheritdoc />
        public virtual void OnInit() { }

        /// <inheritdoc />
        public virtual void OnUpdate(float deltaTime) { }

        /// <summary>
        /// 固定帧更新，默认空实现。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        public virtual void OnFixedUpdate(float deltaTime) { }

        /// <summary>
        /// 帧末更新，默认空实现。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public virtual void OnLateUpdate(float deltaTime) { }

        /// <inheritdoc />
        public virtual void OnShutdown() { }
    }
}
