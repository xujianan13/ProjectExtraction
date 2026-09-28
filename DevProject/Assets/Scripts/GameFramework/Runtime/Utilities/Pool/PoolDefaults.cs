namespace GameFramework
{
    /// <summary>
    /// 自动创建默认对象池时使用的内置参数。可通过 <see cref="IPoolModule.ConfigureDefault{T}"/> 覆盖单类型配置。
    /// </summary>
    public static class PoolDefaults
    {
        /// <summary>Stack 初始容量。</summary>
        public const int InitialCapacity = 8;

        /// <summary>闲置上限；0 表示不限制。</summary>
        public const int MaxSize = 0;

        /// <summary>首次创建池时预分配的闲置实例数。</summary>
        public const int PrewarmCount = 0;
    }
}
