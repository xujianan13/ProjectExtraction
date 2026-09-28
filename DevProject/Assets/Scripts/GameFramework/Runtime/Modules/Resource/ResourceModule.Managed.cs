using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using YooAsset;

namespace GameFramework
{
    /// <summary>ResourceModule 的业务租约、SingleFlight、LRU 与资源组实现。</summary>
    public sealed partial class ResourceModule
    {
        private readonly IResourceAssetBackend _assetBackend;
        private readonly Dictionary<ResourceCacheKey, ResourceCacheEntry> _resourceEntries = new(128);
        private readonly List<ResourceCacheEntry> _loadingEntries = new(16);
        private readonly LinkedList<ResourceCacheEntry> _idleLru = new();
        private readonly ConcurrentQueue<LeaseReleaseRequest> _pendingLeaseReleases = new();
        private readonly HashSet<string> _pendingCleanupPackages = new(StringComparer.Ordinal);

        private CancellationTokenSource _managedShutdown;
        private int _mainThreadId;
        private int _packageActivationEpoch;
        private int _idleEntryCount;
        private long _estimatedIdleBytes;
        private float _elapsedSeconds;
        private float _maintenanceElapsed;
        private bool _managedInitialized;
        private bool _managedShuttingDown;
        private bool _cleanupRunning;

        /// <summary>创建使用默认 LRU 设置的资源模块。</summary>
        public ResourceModule()
            : this(new YooAssetResourceBackend(), new ResourceCacheSettings(), null)
        {
        }

        /// <summary>创建使用指定 LRU 设置的资源模块。</summary>
        /// <param name="cacheSettings">不可为空的资源缓存设置。</param>
        public ResourceModule(ResourceCacheSettings cacheSettings)
            : this(new YooAssetResourceBackend(), cacheSettings, null)
        {
        }

        /// <summary>
        /// 创建可注入资源和下载后端的模块实例。
        /// </summary>
        /// <param name="assetBackend">资源加载后端。</param>
        /// <param name="cacheSettings">资源缓存设置。</param>
        /// <param name="defaultPackageName">可选默认包名。</param>
        /// <param name="downloadBackend">可选预下载后端。</param>
        internal ResourceModule(
            IResourceAssetBackend assetBackend,
            ResourceCacheSettings cacheSettings,
            string defaultPackageName, IResourceDownloadBackend downloadBackend = null)
        {
            _assetBackend = assetBackend ?? throw new ArgumentNullException(nameof(assetBackend));
            _downloadBackend = downloadBackend ?? new YooAssetDownloadBackend();
            CacheSettings = cacheSettings ?? throw new ArgumentNullException(nameof(cacheSettings));
            DefaultPackageName = defaultPackageName;
            _packageActivationEpoch = string.IsNullOrEmpty(defaultPackageName) ? 0 : 1;
        }

        /// <inheritdoc />
        public ResourceCacheSettings CacheSettings { get; }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            if (!_managedInitialized || _managedShuttingDown)
                return;

            DrainPendingLeaseReleases();
            if (deltaTime > 0f && !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime))
            {
                _elapsedSeconds += deltaTime;
                _maintenanceElapsed += deltaTime;
            }

            PublishLoadingProgress();
            if (_maintenanceElapsed < CacheSettings.MaintenanceIntervalSeconds)
                return;

            _maintenanceElapsed = 0f;
            int evicted = TrimToBudget(CacheSettings.MaxEvictionsPerMaintenance);
            if (evicted > 0)
                StartPendingCleanup();
        }

        /// <inheritdoc />
        public UniTask<ResourceLease<T>> AcquireAsync<T>(
            string location,
            ResourceLoadOptions options = default,
            IProgress<float> progress = null,
            CancellationToken ct = default) where T : UnityEngine.Object
        {
            EnsureManagedAvailable();
            return AcquireAsync<T>(DefaultPackageName, location, options, progress, ct);
        }

        /// <inheritdoc />
        public async UniTask<ResourceLease<T>> AcquireAsync<T>(
            string packageName,
            string location,
            ResourceLoadOptions options = default,
            IProgress<float> progress = null,
            CancellationToken ct = default) where T : UnityEngine.Object
        {
            EnsureManagedAvailable(requireDefaultPackage: false);
            ValidateLoadRequest(packageName, location, options);
            ct.ThrowIfCancellationRequested();

            if (!_assetBackend.IsPackageReady(packageName))
            {
                throw CreateLoadException(
                    ResourceLoadError.FailureKind.PackageNotReady,
                    packageName,
                    location,
                    $"资源包 '{packageName}' 尚未激活或已经失效。");
            }

            ResourceCacheKey key = CreateKey<T>(packageName, location);
            bool startLoad = false;
            if (!_resourceEntries.TryGetValue(key, out ResourceCacheEntry entry))
            {
                entry = new ResourceCacheEntry(key);
                ApplyLoadOptions(entry, options);
                _resourceEntries.Add(key, entry);
                _loadingEntries.Add(entry);
                startLoad = true;
            }
            else
            {
                ApplyLoadOptions(entry, options);
            }

            if (entry.State == ResourceCacheEntryState.Ready)
            {
                SafeReport(progress, 1f);
                return CreateLease<T>(entry, options.Retention);
            }

            if (entry.State != ResourceCacheEntryState.Loading)
                throw new ResourceLoadException(entry.Failure);

            ResourceProgressObserver observer = null;
            if (progress != null)
            {
                observer = new ResourceProgressObserver(progress);
                entry.ProgressObservers.Add(observer);
                if (entry.LastReportedProgress >= 0f)
                    SafeReport(progress, entry.LastReportedProgress);
            }

            entry.WaiterCount++;
            entry.AbandonWhenLoaded = false;
            if (startLoad)
                LoadEntryAsync<T>(entry).Forget(LogBackgroundFailure);
            try
            {
                ResourceCacheEntry completed = ct.CanBeCanceled
                    ? await entry.Completion.Task.AttachExternalCancellation(ct)
                    : await entry.Completion.Task;

                ct.ThrowIfCancellationRequested();
                if (completed.State != ResourceCacheEntryState.Ready)
                    throw new ResourceLoadException(completed.Failure);
                return CreateLease<T>(completed, options.Retention);
            }
            finally
            {
                if (observer != null)
                    observer.IsActive = false;
                entry.WaiterCount--;
                if (entry.State == ResourceCacheEntryState.Loading && entry.WaiterCount == 0)
                    entry.AbandonWhenLoaded = true;
            }
        }

        /// <inheritdoc />
        public bool TryAcquire<T>(string location, out ResourceLease<T> lease) where T : UnityEngine.Object
        {
            lease = null;
            if (!_managedInitialized || _managedShuttingDown || string.IsNullOrEmpty(DefaultPackageName))
                return false;
            if (!IsValidLocation(location))
                return false;

            ResourceCacheKey key = CreateKey<T>(DefaultPackageName, location);
            if (!_resourceEntries.TryGetValue(key, out ResourceCacheEntry entry) ||
                entry.State != ResourceCacheEntryState.Ready ||
                entry.Operation == null ||
                !entry.Operation.IsValid ||
                entry.Asset is not T)
            {
                return false;
            }

            lease = CreateLease<T>(entry, ResourceLoadOptions.RetentionMode.Cacheable);
            return true;
        }

        /// <inheritdoc />
        public ResourceLoadTicket<T> Load<T>(
            string location,
            Action<ResourceLease<T>> onCompleted,
            Action<ResourceLoadError> onFailed,
            Action<float> onProgress = null,
            ResourceLoadOptions options = default,
            CancellationToken ct = default) where T : UnityEngine.Object
        {
            if (onCompleted == null) throw new ArgumentNullException(nameof(onCompleted));
            if (onFailed == null) throw new ArgumentNullException(nameof(onFailed));

            var ticket = new ResourceLoadTicket<T>(ct);
            CancellationToken ticketToken = ticket.Token;
            RunCallbackLoadAsync(
                    location,
                    options,
                    onCompleted,
                    onFailed,
                    onProgress,
                    ticket,
                    ticketToken)
                .Forget(LogBackgroundFailure);
            return ticket;
        }

        /// <inheritdoc />
        public async UniTask<ResourceGroupLease> AcquireGroupAsync(
            ResourceGroupDefinition definition,
            IProgress<float> progress = null,
            CancellationToken ct = default)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            EnsureManagedAvailable();
            ct.ThrowIfCancellationRequested();

            IReadOnlyList<ResourceGroupDefinition.Item> items = definition.Items;
            if (items.Count == 0)
            {
                SafeReport(progress, 1f);
                return new ResourceGroupLease(
                    definition.Name,
                    new Dictionary<string, IResourceLease>(StringComparer.Ordinal),
                    Array.Empty<ResourceLoadError>());
            }

            var leases = new IResourceLease[items.Count];
            var failures = new ResourceLoadError[items.Count];
            var itemProgress = new float[items.Count];
            int nextIndex = -1;
            int workerCount = Math.Min(definition.MaxConcurrency, items.Count);
            var workers = new UniTask[workerCount];
            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                workers[workerIndex] = LoadGroupWorkerAsync(
                    definition,
                    leases,
                    failures,
                    itemProgress,
                    progress,
                    () => Interlocked.Increment(ref nextIndex),
                    ct);
            }

            try
            {
                await UniTask.WhenAll(workers);
            }
            catch
            {
                DisposeGroupLeases(leases);
                throw;
            }

            var successful = new Dictionary<string, IResourceLease>(items.Count, StringComparer.Ordinal);
            var failureList = new List<ResourceLoadError>();
            for (int index = 0; index < items.Count; index++)
            {
                if (leases[index] != null)
                    successful.Add(items[index].Id, leases[index]);
                if (failures[index] != null)
                    failureList.Add(failures[index]);
            }

            SafeReport(progress, 1f);
            if (failureList.Count > 0 && definition.Policy == ResourceGroupDefinition.FailurePolicy.AllOrNothing)
            {
                DisposeGroupLeases(leases);
                throw new ResourceGroupLoadException(definition.Name, failureList);
            }

            return new ResourceGroupLease(definition.Name, successful, failureList);
        }

        /// <inheritdoc />
        public bool TryEvict<T>(string location) where T : UnityEngine.Object
        {
            if (!_managedInitialized || _managedShuttingDown || !IsValidLocation(location) ||
                string.IsNullOrEmpty(DefaultPackageName))
            {
                return false;
            }

            ResourceCacheKey key = CreateKey<T>(DefaultPackageName, location);
            if (!_resourceEntries.TryGetValue(key, out ResourceCacheEntry entry) || !CanEvict(entry))
                return false;

            string packageName = entry.Key.PackageName;
            EvictEntry(entry);
            RequestUnusedAssetsCleanup(packageName);
            StartPendingCleanup();
            return true;
        }

        /// <inheritdoc />
        public async UniTask<int> TrimAsync(bool evictAllEligible, CancellationToken ct = default)
        {
            EnsureManagedAvailable(requireDefaultPackage: false);
            ct.ThrowIfCancellationRequested();

            int evicted = 0;
            var packages = new HashSet<string>(StringComparer.Ordinal);
            LinkedListNode<ResourceCacheEntry> node = _idleLru.Last;
            while (node != null)
            {
                LinkedListNode<ResourceCacheEntry> previous = node.Previous;
                ResourceCacheEntry entry = node.Value;
                if (!evictAllEligible && !NeedsBudgetEviction(entry))
                    break;
                if (CanEvict(entry))
                {
                    packages.Add(entry.Key.PackageName);
                    EvictEntry(entry);
                    evicted++;
                }
                node = previous;
            }

            foreach (string packageName in packages)
                await _assetBackend.UnloadUnusedAssetsAsync(packageName, ct);
            return evicted;
        }

        internal bool IsLeaseValid(ResourceCacheEntry entry)
        {
            return entry != null &&
                   entry.State == ResourceCacheEntryState.Ready &&
                   entry.Operation != null &&
                   entry.Operation.IsValid &&
                   entry.Asset != null;
        }

        internal void ReleaseLease(ResourceCacheEntry entry, bool pinned)
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThreadId && _mainThreadId != 0)
            {
                _pendingLeaseReleases.Enqueue(new LeaseReleaseRequest(entry, pinned));
                return;
            }

            ApplyLeaseRelease(entry, pinned);
        }

        private void InitializeManagedResources()
        {
            if (_managedInitialized)
                return;

            _mainThreadId = Thread.CurrentThread.ManagedThreadId;
            _managedShutdown = new CancellationTokenSource();
            _managedInitialized = true;
            _managedShuttingDown = false;
            Application.lowMemory += OnLowMemory;
        }

        private void ShutdownManagedResources()
        {
            if (!_managedInitialized || _managedShuttingDown)
                return;

            _managedShuttingDown = true;
            Application.lowMemory -= OnLowMemory;
            DrainPendingLeaseReleases();

            var snapshot = new List<ResourceCacheEntry>(_resourceEntries.Values);
            _managedShutdown.Cancel();
            for (int index = 0; index < snapshot.Count; index++)
            {
                ResourceCacheEntry entry = snapshot[index];
                if (entry.State != ResourceCacheEntryState.Ready)
                    continue;
                if (entry.ActiveLeaseCount > 0 || entry.PinCount > 0)
                {
                    _log?.Warn(
                        Tag,
                        $"Resource lease leaked during shutdown: {entry.Key.PackageName}/{entry.Key.Location}, " +
                        $"leases={entry.ActiveLeaseCount}, pins={entry.PinCount}.");
                }
                EvictEntry(entry, true);
            }

            _idleLru.Clear();
            _idleEntryCount = 0;
            _estimatedIdleBytes = 0;
            _pendingCleanupPackages.Clear();
            _managedInitialized = false;
        }

        private bool TrySetDefaultPackage(string packageName)
        {
            if (!string.IsNullOrEmpty(DefaultPackageName))
            {
                foreach (ResourceCacheEntry entry in _resourceEntries.Values)
                {
                    if (string.Equals(entry.Key.PackageName, DefaultPackageName, StringComparison.Ordinal))
                    {
                        _log?.Error(
                            Tag,
                            $"Cannot rebind default package while managed resource entries exist: '{DefaultPackageName}'.");
                        return false;
                    }
                }
            }

            DefaultPackageName = packageName;
            _packageActivationEpoch++;
            return true;
        }

        private async UniTask LoadEntryAsync<T>(ResourceCacheEntry entry) where T : UnityEngine.Object
        {
            try
            {
                entry.Operation = _assetBackend.LoadAsync<T>(entry.Key.PackageName, entry.Key.Location);
                await entry.Operation.WaitAsync(_managedShutdown.Token);

                if (_managedShuttingDown)
                    throw new OperationCanceledException(_managedShutdown.Token);
                if (entry.Operation.Status != EOperationStatus.Succeeded)
                {
                    FailEntry(
                        entry,
                        new ResourceLoadError(
                            ResourceLoadError.FailureKind.LoadFailed,
                            entry.Key.PackageName,
                            entry.Key.Location,
                            string.IsNullOrEmpty(entry.Operation.Error)
                                ? $"资源 '{entry.Key.Location}' 加载失败。"
                                : entry.Operation.Error));
                    return;
                }

                if (entry.Operation.Asset is not T asset)
                {
                    FailEntry(
                        entry,
                        new ResourceLoadError(
                            ResourceLoadError.FailureKind.LoadFailed,
                            entry.Key.PackageName,
                            entry.Key.Location,
                            $"资源 '{entry.Key.Location}' 为空或不是 {typeof(T).Name}。"));
                    return;
                }

                entry.Asset = asset;
                entry.State = ResourceCacheEntryState.Ready;
                ReportEntryProgress(entry, 1f);
                entry.Completion.TrySetResult(entry);
                entry.ProgressObservers.Clear();

                if (entry.WaiterCount == 0 && entry.ActiveLeaseCount == 0 && entry.PinCount == 0)
                {
                    if (entry.AbandonWhenLoaded || !entry.CacheRequested)
                    {
                        string packageName = entry.Key.PackageName;
                        EvictEntry(entry);
                        RequestUnusedAssetsCleanup(packageName);
                        StartPendingCleanup();
                    }
                    else
                    {
                        AddToLru(entry);
                    }
                }
            }
            catch (OperationCanceledException ex)
            {
                FailEntry(
                    entry,
                    new ResourceLoadError(
                        ResourceLoadError.FailureKind.ModuleShutdown,
                        entry.Key.PackageName,
                        entry.Key.Location,
                        "资源模块关闭时取消了底层加载。",
                        ex));
            }
            catch (Exception ex)
            {
                FailEntry(
                    entry,
                    new ResourceLoadError(
                        ResourceLoadError.FailureKind.LoadFailed,
                        entry.Key.PackageName,
                        entry.Key.Location,
                        $"资源 '{entry.Key.Location}' 加载异常：{ex.Message}",
                        ex));
            }
            finally
            {
                _loadingEntries.Remove(entry);
            }
        }

        private void FailEntry(ResourceCacheEntry entry, ResourceLoadError failure)
        {
            if (entry.State == ResourceCacheEntryState.Evicted || entry.State == ResourceCacheEntryState.Failed)
                return;

            entry.Failure = failure;
            entry.State = ResourceCacheEntryState.Failed;
            RemoveEntry(entry);
            entry.Operation?.Release();
            entry.Completion.TrySetResult(entry);
            entry.ProgressObservers.Clear();
            _log?.Error(Tag, failure.Message);
        }

        private ResourceLease<T> CreateLease<T>(
            ResourceCacheEntry entry,
            ResourceLoadOptions.RetentionMode retention) where T : UnityEngine.Object
        {
            if (!IsLeaseValid(entry) || entry.Asset is not T)
                throw new InvalidOperationException($"资源缓存项已经失效：{entry.Key.Location}");

            RemoveFromLru(entry);
            entry.ActiveLeaseCount++;
            bool pinned = retention == ResourceLoadOptions.RetentionMode.Resident;
            if (pinned)
                entry.PinCount++;
            if (retention != ResourceLoadOptions.RetentionMode.Transient)
                entry.CacheRequested = true;
            return new ResourceLease<T>(this, entry, pinned);
        }

        private void ApplyLeaseRelease(ResourceCacheEntry entry, bool pinned)
        {
            if (entry == null || entry.State != ResourceCacheEntryState.Ready)
                return;

            if (entry.ActiveLeaseCount <= 0)
            {
                _log?.Error(Tag, $"Resource lease count underflow: {entry.Key.PackageName}/{entry.Key.Location}.");
                return;
            }

            entry.ActiveLeaseCount--;
            if (pinned)
            {
                if (entry.PinCount <= 0)
                    _log?.Error(Tag, $"Resource pin count underflow: {entry.Key.PackageName}/{entry.Key.Location}.");
                else
                    entry.PinCount--;
            }

            if (entry.ActiveLeaseCount != 0 || entry.PinCount != 0)
                return;

            if (entry.CacheRequested)
            {
                AddToLru(entry);
                return;
            }

            string packageName = entry.Key.PackageName;
            EvictEntry(entry);
            RequestUnusedAssetsCleanup(packageName);
            StartPendingCleanup();
        }

        private void AddToLru(ResourceCacheEntry entry)
        {
            if (entry.IsInLru || !CanEvict(entry))
                return;

            entry.IdleSince = _elapsedSeconds;
            entry.IsInLru = true;
            _idleLru.AddFirst(entry.LruNode);
            _idleEntryCount++;
            _estimatedIdleBytes += entry.EstimatedMemoryBytes;
        }

        private void RemoveFromLru(ResourceCacheEntry entry)
        {
            if (!entry.IsInLru)
                return;

            _idleLru.Remove(entry.LruNode);
            entry.IsInLru = false;
            _idleEntryCount--;
            _estimatedIdleBytes -= entry.EstimatedMemoryBytes;
            if (_estimatedIdleBytes < 0)
                _estimatedIdleBytes = 0;
        }

        private bool CanEvict(ResourceCacheEntry entry)
        {
            return entry != null &&
                   entry.State == ResourceCacheEntryState.Ready &&
                   entry.ActiveLeaseCount == 0 &&
                   entry.PinCount == 0;
        }

        private void EvictEntry(ResourceCacheEntry entry, bool force = false)
        {
            if (entry == null || entry.State == ResourceCacheEntryState.Evicted)
                return;
            if (!force && !CanEvict(entry))
                return;

            RemoveFromLru(entry);
            RemoveEntry(entry);
            entry.State = ResourceCacheEntryState.Evicted;
            entry.Asset = null;
            entry.Operation?.Release();
            entry.Operation = null;
        }

        private void RemoveEntry(ResourceCacheEntry entry)
        {
            if (_resourceEntries.TryGetValue(entry.Key, out ResourceCacheEntry current) && ReferenceEquals(current, entry))
                _resourceEntries.Remove(entry.Key);
            RemoveFromLru(entry);
        }

        private int TrimToBudget(int maxEvictions)
        {
            int evicted = 0;
            while (evicted < maxEvictions && _idleLru.Last != null)
            {
                ResourceCacheEntry entry = _idleLru.Last.Value;
                if (!NeedsBudgetEviction(entry))
                    break;

                RequestUnusedAssetsCleanup(entry.Key.PackageName);
                EvictEntry(entry);
                evicted++;
            }
            return evicted;
        }

        private bool NeedsBudgetEviction(ResourceCacheEntry entry)
        {
            if (_idleEntryCount > CacheSettings.MaxIdleEntries)
                return true;
            if (CacheSettings.MaxEstimatedIdleBytes >= 0 &&
                _estimatedIdleBytes > CacheSettings.MaxEstimatedIdleBytes)
            {
                return true;
            }
            return CacheSettings.MaxIdleSeconds >= 0f &&
                   _elapsedSeconds - entry.IdleSince >= CacheSettings.MaxIdleSeconds;
        }

        private void PublishLoadingProgress()
        {
            int count = _loadingEntries.Count;
            for (int index = 0; index < count && index < _loadingEntries.Count; index++)
            {
                ResourceCacheEntry entry = _loadingEntries[index];
                if (entry.State != ResourceCacheEntryState.Loading || entry.Operation == null)
                    continue;

                float progress = Mathf.Clamp01(entry.Operation.Progress);
                if (Mathf.Abs(progress - entry.LastReportedProgress) < 0.001f)
                    continue;
                ReportEntryProgress(entry, progress);
            }
        }

        private void ReportEntryProgress(ResourceCacheEntry entry, float value)
        {
            value = Mathf.Clamp01(value);
            entry.LastReportedProgress = value;
            int count = entry.ProgressObservers.Count;
            for (int index = 0; index < count && index < entry.ProgressObservers.Count; index++)
            {
                ResourceProgressObserver observer = entry.ProgressObservers[index];
                if (observer.IsActive)
                    SafeReport(observer.Progress, value);
            }
        }

        private async UniTask RunCallbackLoadAsync<T>(
            string location,
            ResourceLoadOptions options,
            Action<ResourceLease<T>> onCompleted,
            Action<ResourceLoadError> onFailed,
            Action<float> onProgress,
            ResourceLoadTicket<T> ticket,
            CancellationToken ct) where T : UnityEngine.Object
        {
            ResourceLease<T> lease = null;
            try
            {
                // 始终至少让出一帧，保证缓存命中也不会在 Load 返回前重入调用方。
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
                IProgress<float> progress = onProgress == null ? null : new DelegateProgress(onProgress);
                lease = await AcquireAsync<T>(location, options, progress, ct);
                try
                {
                    onCompleted(lease);
                    lease = null;
                }
                catch (Exception callbackError)
                {
                    _log?.Error(Tag, $"Resource completion callback failed: {callbackError}");
                }
            }
            catch (OperationCanceledException ex)
            {
                SafeInvokeFailure(
                    onFailed,
                    new ResourceLoadError(
                        ResourceLoadError.FailureKind.Canceled,
                        DefaultPackageName,
                        location,
                        $"资源请求已取消：{location}",
                        ex));
            }
            catch (ResourceLoadException ex)
            {
                SafeInvokeFailure(onFailed, ex.Error);
            }
            catch (ObjectDisposedException ex)
            {
                SafeInvokeFailure(
                    onFailed,
                    new ResourceLoadError(
                        ResourceLoadError.FailureKind.ModuleShutdown,
                        DefaultPackageName,
                        location,
                        "资源模块已经关闭。",
                        ex));
            }
            catch (ArgumentException ex)
            {
                SafeInvokeFailure(
                    onFailed,
                    new ResourceLoadError(
                        ResourceLoadError.FailureKind.InvalidRequest,
                        DefaultPackageName,
                        location,
                        $"资源请求参数无效：{ex.Message}",
                        ex));
            }

            catch (Exception ex)
            {
                SafeInvokeFailure(
                    onFailed,
                    new ResourceLoadError(
                        ResourceLoadError.FailureKind.LoadFailed,
                        DefaultPackageName,
                        location,
                        $"资源请求异常：{ex.Message}",
                        ex));
            }
            finally
            {
                lease?.Dispose();
                ticket.MarkDone();
            }
        }

        private async UniTask LoadGroupWorkerAsync(
            ResourceGroupDefinition definition,
            IResourceLease[] leases,
            ResourceLoadError[] failures,
            float[] itemProgress,
            IProgress<float> groupProgress,
            Func<int> takeNextIndex,
            CancellationToken ct)
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                int index = takeNextIndex();
                if (index >= definition.Items.Count)
                    return;

                ResourceGroupDefinition.Item item = definition.Items[index];
                var progress = new GroupItemProgress(
                    value =>
                    {
                        itemProgress[index] = Mathf.Clamp01(value);
                        ReportGroupProgress(definition, itemProgress, groupProgress);
                    });
                try
                {
                    leases[index] = await AcquireGroupItemAsync(item, progress, ct);
                    itemProgress[index] = 1f;
                    ReportGroupProgress(definition, itemProgress, groupProgress);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (ResourceLoadException ex)
                {
                    failures[index] = ex.Error;
                    itemProgress[index] = 1f;
                    ReportGroupProgress(definition, itemProgress, groupProgress);
                }
                catch (Exception ex)
                {
                    failures[index] = new ResourceLoadError(
                        ResourceLoadError.FailureKind.LoadFailed,
                        DefaultPackageName,
                        item.Location,
                        $"资源组条目 '{item.Id}' 加载异常：{ex.Message}",
                        ex);
                    itemProgress[index] = 1f;
                    ReportGroupProgress(definition, itemProgress, groupProgress);
                }
            }
        }

        private UniTask<IResourceLease> AcquireGroupItemAsync(
            ResourceGroupDefinition.Item item,
            IProgress<float> progress,
            CancellationToken ct)
        {
            var options = new ResourceLoadOptions(item.Retention, item.EstimatedMemoryBytes);
            return item.Kind switch
            {
                ResourceGroupDefinition.AssetKind.Object => AcquireGroupItemTypedAsync<UnityEngine.Object>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.GameObject => AcquireGroupItemTypedAsync<GameObject>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.Sprite => AcquireGroupItemTypedAsync<Sprite>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.Texture2D => AcquireGroupItemTypedAsync<Texture2D>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.Material => AcquireGroupItemTypedAsync<Material>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.AudioClip => AcquireGroupItemTypedAsync<AudioClip>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.TextAsset => AcquireGroupItemTypedAsync<TextAsset>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.AnimationClip => AcquireGroupItemTypedAsync<AnimationClip>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.RuntimeAnimatorController => AcquireGroupItemTypedAsync<RuntimeAnimatorController>(item.Location, options, progress, ct),
                ResourceGroupDefinition.AssetKind.ScriptableObject => AcquireGroupItemTypedAsync<ScriptableObject>(item.Location, options, progress, ct),
                _ => UniTask.FromException<IResourceLease>(
                    new ArgumentOutOfRangeException(nameof(item.Kind), item.Kind, "未知资源类型。"))
            };
        }

        private async UniTask<IResourceLease> AcquireGroupItemTypedAsync<T>(
            string location,
            ResourceLoadOptions options,
            IProgress<float> progress,
            CancellationToken ct) where T : UnityEngine.Object
        {
            return await AcquireAsync<T>(location, options, progress, ct);
        }

        private static void DisposeGroupLeases(IResourceLease[] leases)
        {
            for (int index = leases.Length - 1; index >= 0; index--)
            {
                leases[index]?.Dispose();
                leases[index] = null;
            }
        }

        private void ReportGroupProgress(
            ResourceGroupDefinition definition,
            float[] itemProgress,
            IProgress<float> progress)
        {
            if (progress == null)
                return;

            float weighted = 0f;
            float totalWeight = 0f;
            for (int index = 0; index < definition.Items.Count; index++)
            {
                float weight = definition.Items[index].ProgressWeight;
                weighted += itemProgress[index] * weight;
                totalWeight += weight;
            }
            SafeReport(progress, totalWeight <= 0f ? 1f : weighted / totalWeight);
        }

        private void ApplyLoadOptions(ResourceCacheEntry entry, ResourceLoadOptions options)
        {
            if (options.Retention != ResourceLoadOptions.RetentionMode.Transient)
                entry.CacheRequested = true;
            if (options.EstimatedMemoryBytes > entry.EstimatedMemoryBytes)
            {
                long delta = options.EstimatedMemoryBytes - entry.EstimatedMemoryBytes;
                entry.EstimatedMemoryBytes = options.EstimatedMemoryBytes;
                if (entry.IsInLru)
                    _estimatedIdleBytes += delta;
            }
        }

        private ResourceCacheKey CreateKey<T>(string packageName, string location) where T : UnityEngine.Object
        {
            int epoch = string.Equals(packageName, DefaultPackageName, StringComparison.Ordinal)
                ? _packageActivationEpoch
                : 0;
            return new ResourceCacheKey(packageName, epoch, location, typeof(T));
        }

        private static void ValidateLoadRequest(
            string packageName,
            string location,
            ResourceLoadOptions options)
        {
            if (string.IsNullOrWhiteSpace(packageName) || packageName != packageName.Trim())
                throw new ArgumentException("资源包名称无效。", nameof(packageName));
            if (!IsValidLocation(location))
                throw new ArgumentException("资源地址不能为空、不能包含首尾空白。", nameof(location));
            if (options.Retention < ResourceLoadOptions.RetentionMode.Cacheable ||
                options.Retention > ResourceLoadOptions.RetentionMode.Resident)
            {
                throw new ArgumentOutOfRangeException(nameof(options));
            }
        }

        private static bool IsValidLocation(string location)
        {
            return !string.IsNullOrWhiteSpace(location) && location == location.Trim();
        }

        /// <summary>
        /// 检查模块生命周期，并按调用入口决定是否要求默认资源包。
        /// </summary>
        /// <param name="requireDefaultPackage">是否需要已绑定的默认包。</param>
        private void EnsureManagedAvailable(bool requireDefaultPackage = true)
        {
            if (!_managedInitialized || _managedShuttingDown)
                throw new ObjectDisposedException(nameof(ResourceModule), "资源模块尚未初始化或已经关闭。");
            if (requireDefaultPackage && string.IsNullOrEmpty(DefaultPackageName))
                throw new InvalidOperationException("Resource module has no default package; bind an active package first.");
        }

        private void DrainPendingLeaseReleases()
        {
            while (_pendingLeaseReleases.TryDequeue(out LeaseReleaseRequest request))
                ApplyLeaseRelease(request.Entry, request.Pinned);
        }

        private void OnLowMemory()
        {
            if (_managedShuttingDown)
                return;
            TrimAsync(true, _managedShutdown.Token).Forget(LogBackgroundFailure);
        }

        private void RequestUnusedAssetsCleanup(string packageName)
        {
            if (_managedShuttingDown || string.IsNullOrEmpty(packageName))
                return;
            _pendingCleanupPackages.Add(packageName);
        }

        private void StartPendingCleanup()
        {
            if (_cleanupRunning || _pendingCleanupPackages.Count == 0 || _managedShuttingDown)
                return;
            CleanupUnusedAssetsAsync().Forget(LogBackgroundFailure);
        }

        private async UniTask CleanupUnusedAssetsAsync()
        {
            _cleanupRunning = true;
            try
            {
                while (_pendingCleanupPackages.Count > 0 && !_managedShuttingDown)
                {
                    string packageName = null;
                    foreach (string pending in _pendingCleanupPackages)
                    {
                        packageName = pending;
                        break;
                    }
                    if (packageName == null)
                        break;

                    _pendingCleanupPackages.Remove(packageName);
                    await _assetBackend.UnloadUnusedAssetsAsync(packageName, _managedShutdown.Token);
                }
            }
            catch (OperationCanceledException) when (_managedShuttingDown)
            {
                // 模块关闭是缓存清理任务的正常终止路径。
            }
            finally
            {
                _cleanupRunning = false;
            }
        }

        private void SafeReport(IProgress<float> progress, float value)
        {
            if (progress == null)
                return;
            try
            {
                progress.Report(Mathf.Clamp01(value));
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"Resource progress callback failed: {ex}");
            }
        }

        private void SafeInvokeFailure(Action<ResourceLoadError> callback, ResourceLoadError error)
        {
            try
            {
                callback(error);
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"Resource failure callback failed: {ex}");
            }
        }

        private void LogBackgroundFailure(Exception ex)
        {
            if (ex is OperationCanceledException && _managedShuttingDown)
                return;
            _log?.Error(Tag, $"Resource background operation failed: {ex}");
        }

        private static ResourceLoadException CreateLoadException(
            ResourceLoadError.FailureKind kind,
            string packageName,
            string location,
            string message)
        {
            return new ResourceLoadException(new ResourceLoadError(kind, packageName, location, message));
        }

        private readonly struct LeaseReleaseRequest
        {
            public LeaseReleaseRequest(ResourceCacheEntry entry, bool pinned)
            {
                Entry = entry;
                Pinned = pinned;
            }

            public ResourceCacheEntry Entry { get; }
            public bool Pinned { get; }
        }

        private sealed class DelegateProgress : IProgress<float>
        {
            private readonly Action<float> _callback;

            public DelegateProgress(Action<float> callback)
            {
                _callback = callback;
            }

            public void Report(float value)
            {
                _callback(value);
            }
        }

        private sealed class GroupItemProgress : IProgress<float>
        {
            private readonly Action<float> _callback;

            public GroupItemProgress(Action<float> callback)
            {
                _callback = callback;
            }

            public void Report(float value)
            {
                _callback(value);
            }
        }
    }
}
