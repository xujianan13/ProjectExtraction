using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// YooAsset 3.0.1-beta 更新后端。检查阶段预取目标 Manifest，执行阶段下载并按请求决定立即或延迟激活。
    /// </summary>
    public sealed class YooAssetUpdateBackend : IUpdateBackend
    {
        private const string Tag = "Update";

        private readonly IUpdatePackageOptionsProvider _optionsProvider;
        private readonly IUpdatePackageSettingsSource _settingsSource;
        private readonly IVersionStateStore _versionStateStore;

        private ILogModule _log;
        private ResourcePackage _preparedPackage;
        private ResourceDownloaderOperation _preparedDownloader;
        private string _preparedLocalVersion;
        private string _preparedRemoteVersion;
        private int _preparedTimeoutSeconds;
        private UpdateActivationMode _preparedActivationMode;
        private string _currentFile;
        private long _lastSampleBytes;
        private double _lastSampleTime;
        private bool _isInitialized;
        private string _preparedSettingsJson;

        /// <summary>
        /// 本次激活前已验证的包级配置原文，供启动器与 Bundle 内配置比对。
        /// </summary>
        public string ActivatedSettingsJson { get; private set; }

        /// <summary>
        /// 创建 YooAsset 更新后端。
        /// </summary>
        /// <param name="optionsProvider">按更新请求创建 YooAsset 初始化参数的提供器。</param>
        /// <param name="settingsSource">候选版本包级配置的旁路来源。</param>
        /// <param name="versionStateStore">记录上次可用版本的持久化仓库。</param>
        public YooAssetUpdateBackend(IUpdatePackageOptionsProvider optionsProvider,
            IUpdatePackageSettingsSource settingsSource = null, IVersionStateStore versionStateStore = null)
        {
            _optionsProvider = optionsProvider ?? throw new ArgumentNullException(nameof(optionsProvider));
            _settingsSource = settingsSource;
            _versionStateStore = versionStateStore;
        }

        /// <summary>
        /// 后端进度变化时触发。
        /// </summary>
        public event Action<UpdateProgress> ProgressChanged;

        /// <summary>
        /// 获取框架日志服务并初始化更新后端。
        /// </summary>
        public void Initialize()
        {
            if (_isInitialized) return;
            _log = GameEntry.Get<ILogModule>();
            _isInitialized = true;
        }

        /// <summary>
        /// 取消下载并释放更新后端状态。
        /// </summary>
        public void Shutdown()
        {
            if (!_isInitialized) return;
            ResetPreparedDownload(true);
            _log = null;
            _isInitialized = false;
        }

        /// <summary>
        /// 检查内容包版本并预取目标清单，生成下载计划。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>检查得到的更新计划。</returns>
        public async UniTask<UpdatePlan> CheckAsync(
            UpdatePackageRequest request,
            CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            EnsureInitialized();
            ResetPreparedDownload(true);
            ActivatedSettingsJson = null;

            try
            {
                RaiseEmptyProgress(UpdateProgressPhase.InitializePackage);
                ResourcePackage package = await GetOrInitializePackageAsync(request, ct);
                string localVersion = package.PackageValid ? package.GetPackageVersion() : string.Empty;
                if (!string.IsNullOrEmpty(localVersion)) _versionStateStore?.InitializeCurrent(request.PackageName, localVersion);

                RaiseEmptyProgress(UpdateProgressPhase.RequestPackageVersion);
                string remoteVersion = await GetRemoteVersionAsync(package, request, ct);
                if (string.Equals(localVersion, remoteVersion, StringComparison.Ordinal))
                {
                    _preparedPackage = package;
                    _preparedLocalVersion = localVersion;
                    _preparedRemoteVersion = remoteVersion;
                    _preparedTimeoutSeconds = request.TimeoutSeconds;
                    _preparedActivationMode = request.ActivationMode;
                    return new UpdatePlan(
                        request.PackageName,
                        localVersion,
                        remoteVersion,
                        0,
                        0,
                        false,
                        request.ActivationMode == UpdateActivationMode.NextBoot,
                        false);
                }

                string candidateSettingsJson = await LoadAndValidateSettingsAsync(request, remoteVersion, ct);

                RaiseEmptyProgress(UpdateProgressPhase.LoadPackageManifest);
                var prefetchOptions = new PrefetchManifestOptions(remoteVersion, request.TimeoutSeconds);
                var prefetchOperation = package.PrefetchManifestAsync(prefetchOptions);
                await YooAssetOperationAwaiter.AwaitAsync(prefetchOperation, ct);
                if (prefetchOperation.Status != EOperationStatus.Succeeded)
                    throw CreateCheckException(UpdateErrorCode.PackageManifestLoadFailed, prefetchOperation.Error);

                RaiseEmptyProgress(UpdateProgressPhase.PrepareDownload);
                ResourceDownloaderOptions downloadOptions = CreateDownloaderOptions(request);
                var downloader = prefetchOperation.CreateResourceDownloader(downloadOptions);

                _preparedPackage = package;
                _preparedDownloader = downloader;
                _preparedLocalVersion = localVersion;
                _preparedRemoteVersion = remoteVersion;
                _preparedTimeoutSeconds = request.TimeoutSeconds;
                _preparedActivationMode = request.ActivationMode;
                _preparedSettingsJson = candidateSettingsJson;

                return new UpdatePlan(
                    request.PackageName,
                    localVersion,
                    remoteVersion,
                    downloader.TotalDownloadCount,
                    downloader.TotalDownloadBytes,
                    false,
                    request.ActivationMode == UpdateActivationMode.NextBoot,
                    true);
            }
            catch
            {
                ResetPreparedDownload(true);
                throw;
            }
        }

        /// <summary>
        /// 将不可变的更新请求转换为 YooAsset 下载选项，不修改候选清单或当前版本。
        /// </summary>
        /// <param name="request">已校验的更新请求。</param>
        /// <returns>整包或指定标签的下载选项。</returns>
        internal static ResourceDownloaderOptions CreateDownloaderOptions(UpdatePackageRequest request)
        {
            if (request.DownloadTags == null)
                return new ResourceDownloaderOptions(request.DownloadMaxConcurrency, request.RetryCount);
            var tags = new string[request.DownloadTags.Count];
            for (int i = 0; i < tags.Length; i++) tags[i] = request.DownloadTags[i];
            return new ResourceDownloaderOptions(tags, request.DownloadMaxConcurrency, request.RetryCount);
        }

        /// <summary>
        /// 执行预取计划，下载资源并按激活模式处理清单。
        /// </summary>
        /// <param name="plan">已确认有效的更新计划。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>更新执行结果。</returns>
        public async UniTask<UpdateExecutionResult> ExecuteAsync(
            UpdatePlan plan,
            CancellationToken ct = default)
        {
            EnsureInitialized();
            if (!MatchesPreparedPlan(plan))
            {
                return UpdateExecutionResult.Failure(
                    plan.LocalVersion,
                    UpdateErrorCode.InvalidPlan,
                    "YooAsset 后端没有与该计划匹配的预取 Manifest。",
                    false);
            }

            if (!plan.HasUpdate)
            {
                string activeVersion = _preparedPackage.PackageValid
                    ? _preparedPackage.GetPackageVersion()
                    : plan.LocalVersion;
                RaiseEmptyProgress(UpdateProgressPhase.Completed);
                ResetPreparedDownload(false);
                return UpdateExecutionResult.NoUpdate(activeVersion);
            }

            if (_preparedDownloader.TotalDownloadCount > 0)
            {
                UpdateExecutionResult? downloadFailure = await DownloadAsync(plan, ct);
                if (downloadFailure.HasValue) return downloadFailure.Value;
            }

            if (_preparedActivationMode != UpdateActivationMode.Immediate)
            {
                RaiseEmptyProgress(UpdateProgressPhase.Completed);
                string activeVersion = _preparedPackage.PackageValid
                    ? _preparedPackage.GetPackageVersion()
                    : plan.LocalVersion;
                UpdateExecutionResult result = _preparedActivationMode == UpdateActivationMode.NextBoot
                    ? UpdateExecutionResult.RestartRequired(activeVersion, plan.RemoteVersion)
                    : UpdateExecutionResult.Prepared(activeVersion, plan.RemoteVersion);
                ResetPreparedDownload(false);
                return result;
            }

            ct.ThrowIfCancellationRequested();
            RaiseProgress(new UpdateProgress(
                UpdateProgressPhase.Verify,
                plan.DownloadBytes,
                plan.DownloadBytes,
                plan.DownloadCount,
                plan.DownloadCount,
                0,
                _currentFile,
                0));

            RaiseProgress(new UpdateProgress(
                UpdateProgressPhase.Activate,
                plan.DownloadBytes,
                plan.DownloadBytes,
                plan.DownloadCount,
                plan.DownloadCount,
                0,
                _currentFile,
                0));
            var manifestOptions = new LoadPackageManifestOptions(plan.RemoteVersion, _preparedTimeoutSeconds);
            var manifestOperation = _preparedPackage.LoadPackageManifestAsync(manifestOptions);
            await YooAssetOperationAwaiter.AwaitAsync(manifestOperation);
            if (manifestOperation.Status != EOperationStatus.Succeeded)
            {
                string error = manifestOperation.Error;
                LogFailure(plan, UpdateErrorCode.ActivationFailed, error);
                ResetPreparedDownload(false);
                return UpdateExecutionResult.Failure(
                    plan.LocalVersion,
                    UpdateErrorCode.ActivationFailed,
                    error,
                    true);
            }

            string activatedVersion = _preparedPackage.GetPackageVersion();
            ActivatedSettingsJson = _preparedSettingsJson;
            RaiseProgress(new UpdateProgress(
                UpdateProgressPhase.Completed,
                plan.DownloadBytes,
                plan.DownloadBytes,
                plan.DownloadCount,
                plan.DownloadCount,
                0,
                _currentFile,
                0));
            ResetPreparedDownload(false);
            return UpdateExecutionResult.Success(activatedVersion);
        }

        /// <summary>
        /// 从持久化事务状态读取上次可用版本并重新激活其清单。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="timeoutSeconds">清单加载超时秒数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>旧版清单恢复结果；失败时持久化状态保持不变。</returns>
        public async UniTask<UpdateExecutionResult> RestoreLastGoodAsync(string packageName,
            int timeoutSeconds = UpdatePackageRequest.DefaultTimeoutSeconds, CancellationToken ct = default)
        {
            EnsureInitialized();
            if (string.IsNullOrWhiteSpace(packageName) || timeoutSeconds <= 0)
                return UpdateExecutionResult.Failure(string.Empty, UpdateErrorCode.InvalidRequest, "恢复参数无效。", false);
            if (_versionStateStore == null)
                return UpdateExecutionResult.Failure(string.Empty, UpdateErrorCode.InvalidRequest, "没有配置版本状态仓库。", false);

            ResetPreparedDownload(true);
            ActivatedSettingsJson = null;
            PackageVersionState state = _versionStateStore.Load(packageName);
            if (string.IsNullOrEmpty(state.LastGoodVersion))
                return UpdateExecutionResult.Failure(string.Empty, UpdateErrorCode.InvalidRequest, "没有上次可用版本。", false);

            ResourcePackage package;
            try
            {
                var request = new UpdatePackageRequest(packageName, timeoutSeconds: timeoutSeconds);
                package = await GetOrInitializePackageAsync(request, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return UpdateExecutionResult.Cancelled(string.Empty);
            }
            catch (UpdateException exception)
            {
                return UpdateExecutionResult.Failure(string.Empty, exception.ErrorCode, exception.Message, exception.CanRetry);
            }
            catch (Exception exception)
            {
                return UpdateExecutionResult.Failure(string.Empty, UpdateErrorCode.PackageInitializationFailed, exception.Message, true);
            }

            string activeVersion = package.PackageValid ? package.GetPackageVersion() : string.Empty;
            if (string.Equals(activeVersion, state.LastGoodVersion, StringComparison.Ordinal))
                return UpdateExecutionResult.NoUpdate(activeVersion);

            if (ct.IsCancellationRequested) return UpdateExecutionResult.Cancelled(activeVersion);
            try
            {
                var operation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(state.LastGoodVersion, timeoutSeconds));
                await YooAssetOperationAwaiter.AwaitAsync(operation);
                if (operation.Status != EOperationStatus.Succeeded)
                    return UpdateExecutionResult.Failure(activeVersion, UpdateErrorCode.ActivationFailed, operation.Error, true);
                return UpdateExecutionResult.Success(package.GetPackageVersion());
            }
            catch (Exception exception)
            {
                return UpdateExecutionResult.Failure(activeVersion, UpdateErrorCode.ActivationFailed, exception.Message, true);
            }
        }

        /// <summary>
        /// 读取候选版本旁路配置并验证宿主兼容性。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="remoteVersion">候选内容版本。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>通过验证的配置原文。</returns>
        private async UniTask<string> LoadAndValidateSettingsAsync(UpdatePackageRequest request,
            string remoteVersion, CancellationToken ct)
        {
            if (_settingsSource == null)
                throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible,
                    "没有候选版本配置来源，不能安全激活远端清单。", false);

            string json;
            try
            {
                json = await _settingsSource.LoadSettingsAsync(request, remoteVersion, ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (UpdateException)
            {
                throw;
            }
            catch (Exception exception)
            {
                UpdateErrorCode code = MapNetworkError(UpdateErrorCode.DownloadFailed, exception.Message);
                throw new UpdateException(code, $"读取候选版本配置失败：{exception.Message}", true, exception);
            }

            PackageRuntimeSettings settings;
            try
            {
                settings = PackageRuntimeSettings.FromJsonStrict(json);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is FormatException)
            {
                throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible,
                    $"候选版本配置无效：{exception.Message}", false, exception);
            }

            if (!settings.TryValidate(1, 1, request.HostApplicationVersion, out string error))
                throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible,
                    $"候选版本与宿主不兼容：{error}", false);
            return json;
        }

        /// <summary>
        /// 获取并初始化指定内容包。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>可用的 YooAsset 内容包。</returns>
        private async UniTask<ResourcePackage> GetOrInitializePackageAsync(
            UpdatePackageRequest request,
            CancellationToken ct)
        {
            if (!YooAssets.IsInitialized) YooAssets.Initialize();

            ResourcePackage package = YooAssets.TryGetPackage(request.PackageName, out var existingPackage)
                ? existingPackage
                : YooAssets.CreatePackage(request.PackageName);

            if (package.InitializeStatus == EOperationStatus.Succeeded) return package;
            if (package.InitializeStatus == EOperationStatus.Processing)
                throw CreateCheckException(
                    UpdateErrorCode.PackageInitializationFailed,
                    "内容包正在被其他流程初始化。");

            InitializePackageOptions options = _optionsProvider.CreateOptions(request);
            if (options == null)
                throw CreateCheckException(
                    UpdateErrorCode.PackageInitializationFailed,
                    "内容包初始化参数工厂返回了 null。");

            var initializeOperation = package.InitializePackageAsync(options);
            await YooAssetOperationAwaiter.AwaitAsync(initializeOperation, ct);
            if (initializeOperation.Status != EOperationStatus.Succeeded)
                throw CreateCheckException(UpdateErrorCode.PackageInitializationFailed, initializeOperation.Error);
            return package;
        }

        /// <summary>
        /// 读取请求指定或服务器发布的内容包版本。
        /// </summary>
        /// <param name="package">已初始化的内容包。</param>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>目标内容版本。</returns>
        private static async UniTask<string> GetRemoteVersionAsync(
            ResourcePackage package,
            UpdatePackageRequest request,
            CancellationToken ct)
        {
            if (!string.IsNullOrEmpty(request.TargetVersion)) return request.TargetVersion;

            var options = new RequestPackageVersionOptions(true, request.TimeoutSeconds);
            var operation = package.RequestPackageVersionAsync(options);
            await YooAssetOperationAwaiter.AwaitAsync(operation, ct);
            if (operation.Status != EOperationStatus.Succeeded)
                throw CreateCheckException(UpdateErrorCode.PackageVersionRequestFailed, operation.Error);
            if (string.IsNullOrEmpty(operation.PackageVersion))
                throw CreateCheckException(
                    UpdateErrorCode.PackageVersionRequestFailed,
                    "远端返回的内容包版本为空。");
            return operation.PackageVersion;
        }

        /// <summary>
        /// 下载预取计划中的资源并汇报进度。
        /// </summary>
        /// <param name="plan">待执行的更新计划。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>下载失败时返回失败结果，成功时返回空值。</returns>
        private async UniTask<UpdateExecutionResult?> DownloadAsync(UpdatePlan plan, CancellationToken ct)
        {
            _currentFile = string.Empty;
            _lastSampleBytes = 0;
            _lastSampleTime = Time.realtimeSinceStartupAsDouble;
            _preparedDownloader.DownloadProgressChanged += OnDownloadProgressChanged;
            _preparedDownloader.DownloadFileStarted += OnDownloadFileStarted;

            CancellationTokenRegistration registration = default;
            if (ct.CanBeCanceled)
            {
                registration = ct.RegisterWithoutCaptureExecutionContext(
                    state => ((ResourceDownloaderOperation)state).CancelDownload(),
                    _preparedDownloader);
            }

            try
            {
                RaiseProgress(new UpdateProgress(
                    UpdateProgressPhase.Download,
                    0,
                    plan.DownloadBytes,
                    0,
                    plan.DownloadCount,
                    0,
                    string.Empty,
                    0));
                _preparedDownloader.StartDownload();
                await YooAssetOperationAwaiter.AwaitAsync(_preparedDownloader, ct);
            }
            catch (OperationCanceledException)
            {
                ResetPreparedDownload(true);
                return UpdateExecutionResult.Cancelled(plan.LocalVersion);
            }
            finally
            {
                registration.Dispose();
                UnsubscribeDownloaderEvents();
            }

            if (_preparedDownloader.Status == EOperationStatus.Succeeded)
                return null;

            string error = _preparedDownloader.Error;
            UpdateErrorCode errorCode = MapDownloadError(error);
            LogFailure(plan, errorCode, error);
            ResetPreparedDownload(false);
            return UpdateExecutionResult.Failure(plan.LocalVersion, errorCode, error, true);
        }

        /// <summary>
        /// 检查执行计划是否匹配已经预取的清单。
        /// </summary>
        /// <param name="plan">待执行的更新计划。</param>
        /// <returns>匹配时返回 true。</returns>
        private bool MatchesPreparedPlan(UpdatePlan plan)
        {
            return _preparedPackage != null &&
                   string.Equals(_preparedPackage.PackageName, plan.PackageName, StringComparison.Ordinal) &&
                   string.Equals(_preparedLocalVersion, plan.LocalVersion, StringComparison.Ordinal) &&
                   string.Equals(_preparedRemoteVersion, plan.RemoteVersion, StringComparison.Ordinal);
        }

        /// <summary>
        /// 将 YooAsset 下载进度转换为框架进度。
        /// </summary>
        /// <param name="args">下载进度事件参数。</param>
        private void OnDownloadProgressChanged(DownloadProgressChangedEventArgs args)
        {
            long downloadedBytes = Math.Min(args.CurrentDownloadBytes, args.TotalDownloadBytes);
            int downloadedCount = Math.Min(args.CurrentDownloadCount, args.TotalDownloadCount);
            double now = Time.realtimeSinceStartupAsDouble;
            double elapsed = now - _lastSampleTime;
            long bytesPerSecond = elapsed > 0.1
                ? Math.Max(0, (long)((downloadedBytes - _lastSampleBytes) / elapsed))
                : 0;

            if (elapsed > 0.1)
            {
                _lastSampleBytes = downloadedBytes;
                _lastSampleTime = now;
            }

            RaiseProgress(new UpdateProgress(
                UpdateProgressPhase.Download,
                downloadedBytes,
                args.TotalDownloadBytes,
                downloadedCount,
                args.TotalDownloadCount,
                bytesPerSecond,
                _currentFile,
                0));
        }

        /// <summary>
        /// 记录正在下载的文件。
        /// </summary>
        /// <param name="args">文件开始下载事件参数。</param>
        private void OnDownloadFileStarted(DownloadFileStartedEventArgs args)
        {
            _currentFile = args.FileName ?? string.Empty;
        }

        /// <summary>
        /// 发送指定阶段的空进度。
        /// </summary>
        /// <param name="phase">更新阶段。</param>
        private void RaiseEmptyProgress(UpdateProgressPhase phase)
        {
            RaiseProgress(new UpdateProgress(phase, 0, 0, 0, 0, 0, null, 0));
        }

        /// <summary>
        /// 通知订阅者更新进度。
        /// </summary>
        /// <param name="progress">当前更新进度。</param>
        private void RaiseProgress(UpdateProgress progress)
        {
            ProgressChanged?.Invoke(progress);
        }

        /// <summary>
        /// 清除预取状态，并按需取消下载。
        /// </summary>
        /// <param name="cancelDownload">是否取消仍在运行的下载。</param>
        private void ResetPreparedDownload(bool cancelDownload)
        {
            if (_preparedDownloader != null)
            {
                UnsubscribeDownloaderEvents();
                if (cancelDownload && _preparedDownloader.Status == EOperationStatus.Processing)
                    _preparedDownloader.CancelDownload();
            }

            _preparedPackage = null;
            _preparedDownloader = null;
            _preparedLocalVersion = null;
            _preparedRemoteVersion = null;
            _preparedSettingsJson = null;
            _preparedTimeoutSeconds = 0;
            _preparedActivationMode = UpdateActivationMode.Immediate;
            _currentFile = null;
            _lastSampleBytes = 0;
            _lastSampleTime = 0;
        }

        /// <summary>
        /// 取消订阅下载器事件。
        /// </summary>
        private void UnsubscribeDownloaderEvents()
        {
            if (_preparedDownloader == null) return;
            _preparedDownloader.DownloadProgressChanged -= OnDownloadProgressChanged;
            _preparedDownloader.DownloadFileStarted -= OnDownloadFileStarted;
        }

        /// <summary>
        /// 确认后端已经初始化。
        /// </summary>
        private void EnsureInitialized()
        {
            if (!_isInitialized)
                throw new InvalidOperationException("YooAssetUpdateBackend 尚未初始化。");
        }

        /// <summary>
        /// 记录内容包更新失败。
        /// </summary>
        /// <param name="plan">失败的更新计划。</param>
        /// <param name="errorCode">失败错误码。</param>
        /// <param name="error">错误说明。</param>
        private void LogFailure(UpdatePlan plan, UpdateErrorCode errorCode, string error)
        {
            if (_log != null && _log.IsEnabled(LogLevel.Error))
            {
                _log.Error(
                    Tag,
                    $"Package update failed: package={plan.PackageName}, local={plan.LocalVersion}, " +
                    $"remote={plan.RemoteVersion}, code={errorCode}, error={error}");
            }
        }

        /// <summary>
        /// 将检查错误转换为结构化更新异常。
        /// </summary>
        /// <param name="errorCode">原始错误码。</param>
        /// <param name="error">错误说明。</param>
        /// <returns>带有重试信息的更新异常。</returns>
        private static UpdateException CreateCheckException(UpdateErrorCode errorCode, string error)
        {
            UpdateErrorCode mappedCode = MapNetworkError(errorCode, error);
            bool canRetry = mappedCode != UpdateErrorCode.InvalidRequest &&
                            mappedCode != UpdateErrorCode.HostApplicationIncompatible;
            return new UpdateException(
                mappedCode,
                $"Update check failed: code={mappedCode}, error={error}",
                canRetry);
        }

        /// <summary>
        /// 推断下载错误码。
        /// </summary>
        /// <param name="error">下载错误说明。</param>
        /// <returns>映射后的错误码。</returns>
        private static UpdateErrorCode MapDownloadError(string error)
        {
            UpdateErrorCode networkError = MapNetworkError(UpdateErrorCode.DownloadFailed, error);
            if (networkError != UpdateErrorCode.DownloadFailed) return networkError;
            if (string.IsNullOrEmpty(error)) return UpdateErrorCode.DownloadFailed;
            string normalized = error.ToLowerInvariant();
            if (normalized.Contains("space") || normalized.Contains("disk full"))
                return UpdateErrorCode.InsufficientStorage;
            if (normalized.Contains("hash") || normalized.Contains("crc") || normalized.Contains("verify"))
                return UpdateErrorCode.VerificationFailed;
            return UpdateErrorCode.DownloadFailed;
        }

        /// <summary>
        /// 推断网络错误码。
        /// </summary>
        /// <param name="fallback">无法识别时采用的错误码。</param>
        /// <param name="error">错误说明。</param>
        /// <returns>映射后的错误码。</returns>
        private static UpdateErrorCode MapNetworkError(UpdateErrorCode fallback, string error)
        {
            if (string.IsNullOrEmpty(error)) return fallback;
            string normalized = error.ToLowerInvariant();
            if (normalized.Contains("timeout")) return UpdateErrorCode.Timeout;
            if (normalized.Contains("network") || normalized.Contains("connect") ||
                normalized.Contains("resolve host") || normalized.Contains("resolve destination"))
            {
                return UpdateErrorCode.NetworkUnavailable;
            }
            return fallback;
        }
    }
}
