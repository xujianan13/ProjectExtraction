using System;
using System.Collections.Generic;
using System.Threading;

namespace GameFramework
{
    /// <summary>统一持有资源组中成功加载的所有租约。</summary>
    public sealed class ResourceGroupLease : IDisposable
    {
        private readonly Dictionary<string, IResourceLease> _leases;
        private readonly IReadOnlyList<ResourceLoadError> _failures;
        private int _released;

        internal ResourceGroupLease(
            string name,
            Dictionary<string, IResourceLease> leases,
            IReadOnlyList<ResourceLoadError> failures)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _leases = leases ?? throw new ArgumentNullException(nameof(leases));
            _failures = failures ?? Array.Empty<ResourceLoadError>();
        }

        /// <summary>资源组名称。</summary>
        public string Name { get; }

        /// <summary>成功取得的资源数量。</summary>
        public int Count => Volatile.Read(ref _released) == 0 ? _leases.Count : 0;

        /// <summary>BestEffort 模式下未能加载的资源错误。</summary>
        public IReadOnlyList<ResourceLoadError> Failures => _failures;

        /// <summary>资源组租约是否尚未释放。</summary>
        public bool IsValid => Volatile.Read(ref _released) == 0;

        /// <summary>
        /// 按条目 ID 获取资源。返回对象只在本资源组租约存续期间有效，调用方不得脱离租约长期保存。
        /// </summary>
        public bool TryGet<T>(string id, out T asset) where T : UnityEngine.Object
        {
            asset = null;
            if (!IsValid || string.IsNullOrEmpty(id))
                return false;
            if (!_leases.TryGetValue(id, out IResourceLease lease) || !lease.IsValid)
                return false;

            asset = lease.Asset as T;
            return asset != null;
        }

        /// <summary>释放资源组持有的全部租约。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            var leases = new List<IResourceLease>(_leases.Values);
            for (int index = leases.Count - 1; index >= 0; index--)
                leases[index].Dispose();
            _leases.Clear();
        }
    }
}
