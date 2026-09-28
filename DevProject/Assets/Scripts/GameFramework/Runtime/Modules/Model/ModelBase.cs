namespace GameFramework
{
    /// <summary>
    /// 跨场景业务数据模型基类。模型负责持有数据并封装数据来源，
    /// 由 <see cref="IModelModule"/> 统一驱动生命周期。
    /// </summary>
    public abstract class ModelBase
    {
        /// <summary>初始化顺序；数值越小越早初始化、越晚重置和卸载。</summary>
        public virtual int Order => 0;

        /// <summary>模型所持数据的所有者作用域。</summary>
        public virtual ModelScope Scope => ModelScope.Account;

        /// <summary>初始化模型的长期依赖；不应在此绑定具体账号或玩家数据。</summary>
        public virtual void OnInit() { }

        /// <summary>
        /// 每帧更新。默认无操作，仅确实需要时间推进的模型才应重写；热路径禁止 GC 分配。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public virtual void OnUpdate(float deltaTime) { }

        /// <summary>清理当前作用域数据，但保留模型实例及其长期依赖。</summary>
        /// <param name="reason">本次重置原因。</param>
        public virtual void OnReset(ModelResetReason reason) { }

        /// <summary>彻底释放模型的任务、订阅和长期资源。</summary>
        public virtual void OnShutdown() { }
    }
}
