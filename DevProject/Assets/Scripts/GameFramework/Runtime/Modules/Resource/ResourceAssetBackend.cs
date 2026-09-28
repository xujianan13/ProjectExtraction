using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameFramework
{
    /// <summary>把 YooAsset 句柄隔离为可替换的资源加载边界，便于验证并发与生命周期状态机。</summary>
    internal interface IResourceAssetBackend
    {
        bool IsPackageReady(string packageName);
        IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object;
        UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct);
    }

    /// <summary>资源缓存只依赖的最小底层操作集合。</summary>
    internal interface IResourceAssetOperation
    {
        bool IsValid { get; }
        bool IsDone { get; }
        float Progress { get; }
        EOperationStatus Status { get; }
        string Error { get; }
        UnityEngine.Object Asset { get; }
        UniTask WaitAsync(CancellationToken ct);
        void Release();
    }

    internal sealed class YooAssetResourceBackend : IResourceAssetBackend
    {
        public bool IsPackageReady(string packageName)
        {
            return !string.IsNullOrEmpty(packageName) &&
                   YooAssets.TryGetPackage(packageName, out ResourcePackage package) &&
                   package.PackageValid;
        }

        public IResourceAssetOperation LoadAsync<T>(string packageName, string location)
            where T : UnityEngine.Object
        {
            ResourcePackage package = YooAssets.GetPackage(packageName);
            return new YooAssetResourceOperation(package.LoadAssetAsync<T>(location));
        }

        public async UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct)
        {
            if (!YooAssets.TryGetPackage(packageName, out ResourcePackage package))
                return;

            AsyncOperationBase operation = package.UnloadUnusedAssetsAsync();
            await YooAssetAwaitExtensions.AwaitAsync(operation, ct);
        }
    }

    internal sealed class YooAssetResourceOperation : IResourceAssetOperation
    {
        private readonly AssetHandle _handle;

        public YooAssetResourceOperation(AssetHandle handle)
        {
            _handle = handle ?? throw new ArgumentNullException(nameof(handle));
        }

        public bool IsValid => _handle.IsValid;
        public bool IsDone => _handle.IsDone;
        public float Progress => _handle.Progress;
        public EOperationStatus Status => _handle.Status;
        public string Error => _handle.Error;
        public UnityEngine.Object Asset => _handle.AssetObject;

        public UniTask WaitAsync(CancellationToken ct)
        {
            return YooAssetAwaitExtensions.AwaitAsync(_handle, ct);
        }

        public void Release()
        {
            if (_handle.IsValid)
                _handle.Release();
        }
    }

    internal enum ResourceCacheEntryState : byte
    {
        Loading,
        Ready,
        Failed,
        Evicted
    }

    internal readonly struct ResourceCacheKey : IEquatable<ResourceCacheKey>
    {
        public ResourceCacheKey(string packageName, int packageEpoch, string location, Type assetType)
        {
            PackageName = packageName;
            PackageEpoch = packageEpoch;
            Location = location;
            AssetType = assetType;
        }

        public string PackageName { get; }
        public int PackageEpoch { get; }
        public string Location { get; }
        public Type AssetType { get; }

        public bool Equals(ResourceCacheKey other)
        {
            return PackageEpoch == other.PackageEpoch &&
                   AssetType == other.AssetType &&
                   string.Equals(PackageName, other.PackageName, StringComparison.Ordinal) &&
                   string.Equals(Location, other.Location, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is ResourceCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = StringComparer.Ordinal.GetHashCode(PackageName ?? string.Empty);
                hash = (hash * 397) ^ PackageEpoch;
                hash = (hash * 397) ^ StringComparer.Ordinal.GetHashCode(Location ?? string.Empty);
                hash = (hash * 397) ^ (AssetType?.GetHashCode() ?? 0);
                return hash;
            }
        }
    }

    internal sealed class ResourceProgressObserver
    {
        public ResourceProgressObserver(IProgress<float> progress)
        {
            Progress = progress;
        }

        public IProgress<float> Progress { get; }
        public bool IsActive { get; set; } = true;
    }

    internal sealed class ResourceCacheEntry
    {
        public ResourceCacheEntry(ResourceCacheKey key)
        {
            Key = key;
            Completion = new UniTaskCompletionSource<ResourceCacheEntry>();
            LruNode = new System.Collections.Generic.LinkedListNode<ResourceCacheEntry>(this);
        }

        public ResourceCacheKey Key { get; }
        public ResourceCacheEntryState State { get; set; } = ResourceCacheEntryState.Loading;
        public IResourceAssetOperation Operation { get; set; }
        public UnityEngine.Object Asset { get; set; }
        public ResourceLoadError Failure { get; set; }
        public UniTaskCompletionSource<ResourceCacheEntry> Completion { get; }
        public System.Collections.Generic.LinkedListNode<ResourceCacheEntry> LruNode { get; }
        public System.Collections.Generic.List<ResourceProgressObserver> ProgressObservers { get; } = new(2);
        public int ActiveLeaseCount { get; set; }
        public int PinCount { get; set; }
        public int WaiterCount { get; set; }
        public bool CacheRequested { get; set; }
        public bool AbandonWhenLoaded { get; set; }
        public long EstimatedMemoryBytes { get; set; }
        public float IdleSince { get; set; }
        public float LastReportedProgress { get; set; } = -1f;
        public bool IsInLru { get; set; }
    }
}
