namespace GameFramework
{
    /// <summary>控制资源模块空闲缓存的 LRU 预算与维护频率。</summary>
    public sealed class ResourceCacheSettings
    {
        /// <summary>创建资源缓存设置。</summary>
        /// <param name="maxIdleEntries">最多保留的零引用缓存项；零表示不保留。</param>
        /// <param name="maxEstimatedIdleBytes">估算空闲内存上限；负数表示不启用该预算。</param>
        /// <param name="maxIdleSeconds">单项最大空闲秒数；负数表示不按时间淘汰。</param>
        /// <param name="maintenanceIntervalSeconds">LRU 维护间隔。</param>
        /// <param name="maxEvictionsPerMaintenance">单次维护最多淘汰数量，避免单帧尖峰。</param>
        public ResourceCacheSettings(
            int maxIdleEntries = 128,
            long maxEstimatedIdleBytes = -1,
            float maxIdleSeconds = 120f,
            float maintenanceIntervalSeconds = 1f,
            int maxEvictionsPerMaintenance = 8)
        {
            if (maxIdleEntries < 0) throw new System.ArgumentOutOfRangeException(nameof(maxIdleEntries));
            if (maxEstimatedIdleBytes < -1) throw new System.ArgumentOutOfRangeException(nameof(maxEstimatedIdleBytes));
            if (maxIdleSeconds < -1f) throw new System.ArgumentOutOfRangeException(nameof(maxIdleSeconds));
            if (maintenanceIntervalSeconds <= 0f)
                throw new System.ArgumentOutOfRangeException(nameof(maintenanceIntervalSeconds));
            if (maxEvictionsPerMaintenance <= 0)
                throw new System.ArgumentOutOfRangeException(nameof(maxEvictionsPerMaintenance));

            MaxIdleEntries = maxIdleEntries;
            MaxEstimatedIdleBytes = maxEstimatedIdleBytes;
            MaxIdleSeconds = maxIdleSeconds;
            MaintenanceIntervalSeconds = maintenanceIntervalSeconds;
            MaxEvictionsPerMaintenance = maxEvictionsPerMaintenance;
        }

        /// <summary>最多保留的零引用缓存项。</summary>
        public int MaxIdleEntries { get; }

        /// <summary>估算空闲内存上限；负数表示不限制。</summary>
        public long MaxEstimatedIdleBytes { get; }

        /// <summary>单项最大空闲秒数；负数表示不限制。</summary>
        public float MaxIdleSeconds { get; }

        /// <summary>LRU 维护间隔。</summary>
        public float MaintenanceIntervalSeconds { get; }

        /// <summary>单次维护最多淘汰数量。</summary>
        public int MaxEvictionsPerMaintenance { get; }
    }
}
