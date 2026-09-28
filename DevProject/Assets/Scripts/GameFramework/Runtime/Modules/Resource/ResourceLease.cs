using System;
using System.Threading;

namespace GameFramework
{
    /// <summary>
    /// 一次独立的资源使用租约。持有期间资源不会被 LRU 淘汰；使用完毕后必须调用 <see cref="Dispose"/>。
    /// </summary>
    /// <typeparam name="T">Unity 资源类型。</typeparam>
    public sealed class ResourceLease<T> : IDisposable, IResourceLease where T : UnityEngine.Object
    {
        private readonly ResourceModule _owner;
        private readonly ResourceCacheEntry _entry;
        private readonly bool _pinned;
        private int _released;

        internal ResourceLease(ResourceModule owner, ResourceCacheEntry entry, bool pinned)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _entry = entry ?? throw new ArgumentNullException(nameof(entry));
            _pinned = pinned;
        }

        /// <summary>资源包名。</summary>
        public string PackageName => _entry.Key.PackageName;

        /// <summary>逻辑资源地址。</summary>
        public string Location => _entry.Key.Location;

        /// <summary>租约及其资源是否仍然有效。</summary>
        public bool IsValid => Volatile.Read(ref _released) == 0 && _owner.IsLeaseValid(_entry);

        /// <summary>租约持有的资源；租约释放或缓存失效后返回 null。</summary>
        public T Asset => IsValid ? _entry.Asset as T : null;

        UnityEngine.Object IResourceLease.Asset => Asset;

        /// <summary>释放本调用方的资源引用。重复调用没有副作用。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            _owner.ReleaseLease(_entry, _pinned);
        }
    }

    /// <summary>资源组内部用于统一持有不同泛型租约的稳定边界。</summary>
    internal interface IResourceLease : IDisposable
    {
        bool IsValid { get; }
        UnityEngine.Object Asset { get; }
    }
}
