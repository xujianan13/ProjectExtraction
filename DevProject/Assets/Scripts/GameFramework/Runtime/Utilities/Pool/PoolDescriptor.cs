using System;

namespace GameFramework
{
    /// <summary>
    /// 对象池注册描述符，供 <see cref="IPoolModule.Register{T}"/> 使用。
    /// </summary>
    /// <typeparam name="T">池元素类型。</typeparam>
    public sealed class PoolDescriptor<T> where T : class
    {
        /// <summary>实例工厂。</summary>
        public Func<T> Factory { get; }

        /// <summary>Stack 初始容量。</summary>
        public int InitialCapacity { get; }

        /// <summary>闲置上限；0 表示不限制。</summary>
        public int MaxSize { get; }

        /// <summary>注册时预创建的闲置实例数。</summary>
        public int PrewarmCount { get; }

        /// <summary>租出后额外回调。</summary>
        public Action<T> OnRent { get; }

        /// <summary>归还前额外回调。</summary>
        public Action<T> OnReturn { get; }

        /// <summary>
        /// 创建池描述符。
        /// </summary>
        /// <param name="factory">实例工厂，不可为 null。</param>
        /// <param name="initialCapacity">Stack 初始容量。</param>
        /// <param name="maxSize">闲置上限；0 表示不限制。</param>
        /// <param name="prewarmCount">注册时预创建的闲置实例数。</param>
        /// <param name="onRent">租出后额外回调。</param>
        /// <param name="onReturn">归还前额外回调。</param>
        public PoolDescriptor(
            Func<T> factory,
            int initialCapacity = 8,
            int maxSize = 0,
            int prewarmCount = 0,
            Action<T> onRent = null,
            Action<T> onReturn = null)
        {
            Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            if (maxSize < 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
            if (prewarmCount < 0) throw new ArgumentOutOfRangeException(nameof(prewarmCount));

            InitialCapacity = initialCapacity;
            MaxSize = maxSize;
            PrewarmCount = prewarmCount;
            OnRent = onRent;
            OnReturn = onReturn;
        }
    }
}
