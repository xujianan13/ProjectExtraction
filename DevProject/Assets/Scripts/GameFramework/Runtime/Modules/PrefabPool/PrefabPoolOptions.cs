using System;

namespace GameFramework
{
    /// <summary>
    /// Prefab 实例池的预热和闲置保留设置；配置在创建后保持不变。
    /// </summary>
    public sealed class PrefabPoolOptions
    {
        /// <summary>
        /// 创建实例池设置。
        /// </summary>
        /// <param name="prewarmCount">创建池时预热的闲置实例数量，不超过闲置上限。</param>
        /// <param name="maxInactive">最多保留的闲置实例数；零表示归还时直接销毁。</param>
        public PrefabPoolOptions(int prewarmCount = 0, int maxInactive = 32)
        {
            if (prewarmCount < 0) throw new ArgumentOutOfRangeException(nameof(prewarmCount));
            if (maxInactive < 0) throw new ArgumentOutOfRangeException(nameof(maxInactive));
            PrewarmCount = prewarmCount;
            MaxInactive = maxInactive;
        }

        /// <summary>
        /// 获取初始预热数量。
        /// </summary>
        public int PrewarmCount { get; }

        /// <summary>
        /// 获取闲置实例数量上限；活跃实例不受该上限限制。
        /// </summary>
        public int MaxInactive { get; }
    }
}
