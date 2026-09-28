namespace GameFramework
{
    /// <summary>描述一次资源获取的缓存与常驻策略。</summary>
    public readonly struct ResourceLoadOptions
    {
        /// <summary>资源在业务租约释放后的保留方式。</summary>
        public enum RetentionMode : byte
        {
            /// <summary>零引用后进入 LRU，等待预算或内存压力淘汰。</summary>
            Cacheable = 0,

            /// <summary>零引用后立即释放缓存持有的底层句柄。</summary>
            Transient = 1,

            /// <summary>租约存续期间额外标记为常驻；释放租约即可撤销常驻。</summary>
            Resident = 2
        }

        /// <summary>创建资源加载选项。</summary>
        /// <param name="retention">资源保留方式。</param>
        /// <param name="estimatedMemoryBytes">用于 LRU 预算的估算内存；零表示未知。</param>
        public ResourceLoadOptions(RetentionMode retention, long estimatedMemoryBytes = 0)
        {
            if (estimatedMemoryBytes < 0)
                throw new System.ArgumentOutOfRangeException(nameof(estimatedMemoryBytes));

            Retention = retention;
            EstimatedMemoryBytes = estimatedMemoryBytes;
        }

        /// <summary>资源保留方式；默认值为 <see cref="RetentionMode.Cacheable"/>。</summary>
        public RetentionMode Retention { get; }

        /// <summary>用于空闲缓存预算的估算内存字节数。</summary>
        public long EstimatedMemoryBytes { get; }

        /// <summary>默认可缓存选项。</summary>
        public static ResourceLoadOptions Cacheable => default;

        /// <summary>零引用即释放选项。</summary>
        public static ResourceLoadOptions Transient => new(RetentionMode.Transient);

        /// <summary>由返回租约持有的常驻选项。</summary>
        public static ResourceLoadOptions Resident => new(RetentionMode.Resident);
    }
}
