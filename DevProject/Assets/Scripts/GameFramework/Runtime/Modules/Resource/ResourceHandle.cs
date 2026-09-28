using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// 资源句柄。包装 YooAsset 的 <see cref="AssetHandle"/>，提供 type-safe 的资源访问和显式 Release。
    /// 框架层使用本类型，避免业务直接耦合 YooAsset 类型。
    /// </summary>
    public readonly struct ResourceHandle<T> where T : UnityEngine.Object
    {
        // 内部持有 YooAsset 句柄；可能为 null（如同步加载失败时）
        private readonly AssetHandle _handle;

        /// <summary>从底层 YooAsset 句柄构造。由 ResourceModule 内部使用。</summary>
        public ResourceHandle(AssetHandle handle) { _handle = handle; }

        /// <summary>已加载的资源对象。未完成 / 失败 / 类型不匹配时返回 null。</summary>
        public T Asset => _handle?.AssetObject as T;

        /// <summary>句柄是否有效（已分配且未释放）。</summary>
        public bool IsValid => _handle != null && _handle.IsValid;

        /// <summary>异步加载是否完成。</summary>
        public bool IsDone => _handle != null && _handle.IsDone;

        /// <summary>加载状态，便于调试。</summary>
        public EOperationStatus Status => _handle != null ? _handle.Status : EOperationStatus.None;

        /// <summary>释放句柄，递减底层引用计数。多次释放无副作用。</summary>
        public void Release() => _handle?.Release();
    }
}
