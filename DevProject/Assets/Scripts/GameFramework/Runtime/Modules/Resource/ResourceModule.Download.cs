using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// 隔离 YooAsset 下载器以验证下载生命周期。
    /// </summary>
    internal interface IResourceDownloadBackend
    {
        /// <summary>
        /// 检查指定资源包是否具有激活清单。
        /// </summary>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>包是否就绪。</returns>
        bool IsPackageReady(string packageName);
        /// <summary>
        /// 读取当前激活的资源清单版本。
        /// </summary>
        /// <param name="packageName">资源包名称。</param>
        /// <returns>当前清单版本。</returns>
        string GetPackageVersion(string packageName);
        /// <summary>
        /// 尝试创建限定目标的下载器。
        /// </summary>
        /// <param name="packageName">资源包名称。</param>
        /// <param name="request">预下载请求。</param>
        /// <param name="operation">创建的下载操作。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否成功创建。</returns>
        bool TryCreate(string packageName, ResourceDownloadRequest request, out IResourceDownloadOperation operation, out string error);
    }

    /// <summary>
    /// 下载器的最小操作接口。
    /// </summary>
    internal interface IResourceDownloadOperation
    {
        /// <summary>
        /// 下载是否结束。
        /// </summary>
        bool IsDone { get; }
        /// <summary>
        /// 下载是否成功。
        /// </summary>
        bool Succeeded { get; }
        /// <summary>
        /// 失败原因。
        /// </summary>
        string Error { get; }
        /// <summary>
        /// 当前进度。
        /// </summary>
        ResourceDownloadProgress Progress { get; }
        /// <summary>
        /// 计划下载文件数。
        /// </summary>
        int TotalCount { get; }
        /// <summary>
        /// 开始本次下载。
        /// </summary>
        void Start();
        /// <summary>
        /// 取消本次下载。
        /// </summary>
        void Cancel();
    }

    /// <summary>
    /// 使用 YooAsset 实现资源 Bundle 预下载。
    /// </summary>
    internal sealed class YooAssetDownloadBackend : IResourceDownloadBackend
    {
        /// <inheritdoc />
        /// <param name="packageName">资源包名称。</param>
        /// <returns>是否已有激活清单。</returns>
        public bool IsPackageReady(string packageName)
            => YooAssets.TryGetPackage(packageName, out ResourcePackage package) && package.PackageValid;

        /// <inheritdoc />
        /// <param name="packageName">资源包名称。</param>
        /// <returns>当前清单版本。</returns>
        public string GetPackageVersion(string packageName)
            => YooAssets.GetPackage(packageName).GetPackageVersion();

        /// <inheritdoc />
        /// <param name="packageName">资源包名称。</param>
        /// <param name="request">下载请求。</param>
        /// <param name="operation">创建的下载器。</param>
        /// <param name="error">失败说明。</param>
        /// <returns>是否成功创建。</returns>
        public bool TryCreate(string packageName, ResourceDownloadRequest request, out IResourceDownloadOperation operation, out string error)
        {
            operation = null;
            error = null;
            ResourcePackage package = YooAssets.GetPackage(packageName);
            ResourceDownloaderOperation downloader;
            string[] locations = request.Locations;
            string[] tags = request.Tags;
            if (locations != null)
            {
                var infos = new List<AssetInfo>(locations.Length);
                foreach (string location in locations)
                {
                    if (string.IsNullOrWhiteSpace(location))
                    {
                        error = "资源地址不能为空。";
                        return false;
                    }
                    if (!package.IsLocationValid(location))
                    {
                        error = $"资源地址 '{location}' 无效。";
                        return false;
                    }
                    AssetInfo info = package.GetAssetInfo(location);
                    if (!info.IsValid)
                    {
                        error = $"资源地址 '{location}' 无效：{info.Error}";
                        return false;
                    }
                    infos.Add(info);
                }
                downloader = package.CreateResourceDownloader(new BundleDownloaderOptions(infos.ToArray(), true, request.MaximumConcurrency, request.RetryCount));
            }
            else
            {
                foreach (string tag in tags)
                {
                    AssetInfo[] infos = package.GetAssetInfos(tag);
                    if (infos == null || infos.Length == 0)
                    {
                        error = $"标签 '{tag}' 没有匹配资源。";
                        return false;
                    }
                }
                downloader = package.CreateResourceDownloader(new ResourceDownloaderOptions(tags, request.MaximumConcurrency, request.RetryCount));
            }
            operation = new YooAssetDownloadOperation(downloader);
            return true;
        }
    }

    /// <summary>
    /// YooAsset 下载器的状态适配器。
    /// </summary>
    internal sealed class YooAssetDownloadOperation : IResourceDownloadOperation
    {
        private readonly ResourceDownloaderOperation _operation;

        /// <summary>
        /// 包装 YooAsset 下载操作。
        /// </summary>
        /// <param name="operation">YooAsset 下载操作。</param>
        public YooAssetDownloadOperation(ResourceDownloaderOperation operation)
        {
            _operation = operation;
        }

        public bool IsDone => _operation.IsDone;
        public bool Succeeded => _operation.Status == EOperationStatus.Succeeded;
        public string Error => _operation.Error;
        public int TotalCount => _operation.TotalDownloadCount;
        public ResourceDownloadProgress Progress => new(_operation.CurrentDownloadCount, _operation.TotalDownloadCount,
            _operation.CurrentDownloadBytes, _operation.TotalDownloadBytes);
        public void Start() => _operation.StartDownload();
        public void Cancel() => _operation.CancelDownload();
    }

    /// <summary>
    /// 资源模块的 Bundle 预下载实现。
    /// </summary>
    public sealed partial class ResourceModule
    {
        private IResourceDownloadBackend _downloadBackend;
        private readonly Dictionary<string, bool> _downloadSupport = new(StringComparer.Ordinal);

        /// <inheritdoc />
        /// <param name="packageName">已激活的资源包名称。</param>
        /// <param name="supported">文件系统是否支持独立准备 Bundle 文件。</param>
        public void ConfigureDownloadSupport(string packageName, bool supported)
        {
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("包名不能为空。", nameof(packageName));
            if (!_managedInitialized || _managedShuttingDown || !_downloadBackend.IsPackageReady(packageName))
                throw new InvalidOperationException($"资源包 '{packageName}' 尚未激活。");
            _downloadSupport[packageName] = supported;
        }

        /// <inheritdoc />
        /// <param name="request">按地址或标签限定目标的下载请求。</param>
        /// <param name="progress">可选文件数与字节进度接收器。</param>
        /// <param name="ct">本次请求的取消令牌。</param>
        /// <returns>明确区分成功、失败、取消和不支持的结果。</returns>
        public async UniTask<ResourceDownloadResult> DownloadAsync(ResourceDownloadRequest request,
            IProgress<ResourceDownloadProgress> progress = null, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested || _managedShuttingDown)
                return new ResourceDownloadResult(ResourceDownloadStatus.Cancelled, "下载已取消。", 0);
            if (!_managedInitialized || request == null || request.MaximumConcurrency < 1 || request.RetryCount < 0 ||
                (request.Locations == null) == (request.Tags == null) || !HasValidTargets(request))
                return DownloadFailure("下载请求无效或资源模块尚未运行。", 0);

            string packageName = string.IsNullOrWhiteSpace(request.PackageName) ? DefaultPackageName : request.PackageName;
            if (string.IsNullOrWhiteSpace(packageName) || !_downloadBackend.IsPackageReady(packageName))
                return DownloadFailure($"资源包 '{packageName}' 尚未激活。", 0);
            if (!_downloadSupport.TryGetValue(packageName, out bool supported) || !supported)
                return new ResourceDownloadResult(ResourceDownloadStatus.Unsupported, $"资源包 '{packageName}' 未启用落盘预下载。", 0);

            string version;
            IResourceDownloadOperation operation;
            string error;
            try
            {
                version = _downloadBackend.GetPackageVersion(packageName);
                if (!_downloadBackend.TryCreate(packageName, request, out operation, out error))
                    return DownloadFailure(error, 0);
            }
            catch (Exception ex)
            {
                return DownloadFailure(ex.Message, 0);
            }

            int totalCount = operation.TotalCount;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _managedShutdown.Token);
            try
            {
                linked.Token.ThrowIfCancellationRequested();
                if (!string.Equals(version, _downloadBackend.GetPackageVersion(packageName), StringComparison.Ordinal))
                    return DownloadFailure("创建下载器时资源清单已变化。", totalCount);
                operation.Start();
                while (!operation.IsDone)
                {
                    progress?.Report(operation.Progress);
                    await UniTask.Yield(PlayerLoopTiming.Update, linked.Token);
                    if (!_downloadBackend.IsPackageReady(packageName) ||
                        !string.Equals(version, _downloadBackend.GetPackageVersion(packageName), StringComparison.Ordinal))
                    {
                        operation.Cancel();
                        return DownloadFailure("下载期间资源清单已变化。", totalCount);
                    }
                }
                linked.Token.ThrowIfCancellationRequested();
                if (!_downloadBackend.IsPackageReady(packageName) ||
                    !string.Equals(version, _downloadBackend.GetPackageVersion(packageName), StringComparison.Ordinal))
                    return DownloadFailure("下载完成时资源清单已变化。", totalCount);
                progress?.Report(operation.Progress);
                linked.Token.ThrowIfCancellationRequested();
                if (!_downloadBackend.IsPackageReady(packageName) ||
                    !string.Equals(version, _downloadBackend.GetPackageVersion(packageName), StringComparison.Ordinal))
                    return DownloadFailure("进度回调期间资源清单已变化。", totalCount);
                return operation.Succeeded
                    ? new ResourceDownloadResult(ResourceDownloadStatus.Success, null, totalCount)
                    : DownloadFailure(operation.Error, totalCount, true);
            }
            catch (OperationCanceledException)
            {
                operation.Cancel();
                return new ResourceDownloadResult(ResourceDownloadStatus.Cancelled, "下载已取消。", totalCount);
            }
            catch (Exception ex)
            {
                operation.Cancel();
                return DownloadFailure(ex.Message, totalCount, true);
            }
        }

        /// <summary>
        /// 检查请求含有非空目标。
        /// </summary>
        /// <param name="request">预下载请求。</param>
        /// <returns>目标是否有效。</returns>
        private static bool HasValidTargets(ResourceDownloadRequest request)
        {
            string[] targets = request.Locations ?? request.Tags;
            if (targets == null || targets.Length == 0) return false;
            foreach (string target in targets)
                if (string.IsNullOrWhiteSpace(target)) return false;
            return true;
        }

        /// <summary>
        /// 记录并创建失败结果。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <param name="totalCount">计划下载文件数。</param>
        /// <param name="canRetry">是否适合重试。</param>
        /// <returns>失败结果。</returns>
        private ResourceDownloadResult DownloadFailure(string error, int totalCount, bool canRetry = false)
        {
            _log?.Error(Tag, error);
            return new ResourceDownloadResult(ResourceDownloadStatus.Failure, error, totalCount, canRetry);
        }
    }
}
