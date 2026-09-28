namespace GameFramework
{
    /// <summary>
    /// 全局对象池管理模块。按「类型 + 命名」索引 <see cref="ObjectPool{T}"/>，供全项目复用。
    /// 须在 <see cref="GameEntry.StartFramework"/> 之前注册模块。
    /// 默认池支持懒创建，命名池须显式 <see cref="Register{T}"/>。
    /// </summary>
    public interface IPoolModule : IModule
    {
        /// <summary>
        /// 配置某类型默认池的创建策略。仅在该类型默认池尚未创建时写入配置；
        /// 已创建后再次调用无效。
        /// </summary>
        void ConfigureDefault<T>(PoolDescriptor<T> descriptor) where T : class;

        /// <summary>按描述符注册或替换池。</summary>
        void Register<T>(PoolDescriptor<T> descriptor, string name = null) where T : class;

        /// <summary>注册已有 <see cref="ObjectPool{T}"/> 实例。</summary>
        void Register<T>(ObjectPool<T> pool, string name = null) where T : class;

        /// <summary>是否已实例化并注册指定池（TryGetPool 为 true）。</summary>
        bool IsRegistered<T>(string name = null) where T : class;

        /// <summary>尝试获取已注册池（不触发懒创建）。</summary>
        bool TryGetPool<T>(out ObjectPool<T> pool, string name = null) where T : class;

        /// <summary>
        /// 获取或创建默认池。优先已注册池，其次 <see cref="ConfigureDefault{T}"/>；
        /// 若均未配置则要求 <typeparamref name="T"/> 有无参构造并按 <see cref="PoolDefaults"/> 创建。
        /// </summary>
        ObjectPool<T> GetOrCreatePool<T>() where T : class, new();

        /// <summary>
        /// 获取或创建默认池（无 <c>new()</c> 约束）。
        /// 须已注册或已 <see cref="ConfigureDefault{T}"/>，否则抛异常。
        /// </summary>
        ObjectPool<T> GetOrCreatePoolConfigured<T>() where T : class;

        /// <summary>获取命名池；未注册时抛异常。</summary>
        ObjectPool<T> GetPool<T>(string name) where T : class;

        /// <summary>从默认池租出（自动懒创建，规则同 <see cref="GetOrCreatePool{T}"/>）。</summary>
        T Rent<T>() where T : class, new();

        /// <summary>从命名池租出；池须已注册。</summary>
        T Rent<T>(string name) where T : class;

        /// <summary>归还到已创建的默认池；池不存在时抛异常。</summary>
        void Return<T>(T item) where T : class;

        /// <summary>归还到命名池；池须已注册。</summary>
        void Return<T>(T item, string name) where T : class;

        /// <summary>
        /// 按实例的运行时类型归还到对应<strong>默认池</strong>。适用于 <c>List&lt;BaseClass&gt;</c> 等多态集合批量归还。
        /// 各具体子类型须已创建或显式注册默认池。
        /// </summary>
        /// <param name="item">待归还实例，不可为 null。</param>
        void ReturnRuntime(object item);
    }
}
