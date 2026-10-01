using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 以打开顺序和关闭 LRU 管理窗口实例，统一负责资源租约、层级、蒙层、外部点击与关联行为。
    /// 所有改变窗口集合的异步 API 会串行化，避免并发打开产生重复实例。
    /// </summary>
    public sealed class GameWindowManager
    {
        private const string Tag = "GameWindow";
        internal const string BusinessFailureMarker = "GameFramework.UI.WindowBusinessFailure";

        private readonly IGameWindowConfigCatalog _catalog;
        private readonly GameWindowRegistry _registry;
        private readonly IResourceModule _resource;
        private readonly IAudioModule _audio;
        private readonly ILogModule _log;
        private readonly GameWindowManagerSettings _settings;
        private readonly Func<double> _clock;
        private readonly Dictionary<int, WindowEntry> _entries = new();
        private readonly List<GameWindow> _openedWindows = new();
        private readonly List<GameWindow> _closedWindows = new();
        private readonly SemaphoreSlim _operationGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();

        private GameWindowPresentation _presentation;
        private bool _initialized;
        private bool _shutdown;

        /// <summary>
        /// 创建窗口管理器；包含 Prefab 的配置要求资源模块不为空。
        /// </summary>
        /// <param name="catalog">窗口配置目录。</param>
        /// <param name="registry">窗口类型注册表。</param>
        /// <param name="resource">资源模块。</param>
        /// <param name="audio">音频模块。</param>
        /// <param name="log">日志模块。</param>
        /// <param name="settings">窗口管理设置。</param>
        /// <param name="clock">未缩放时钟。</param>
        public GameWindowManager(
            IGameWindowConfigCatalog catalog,
            GameWindowRegistry registry,
            IResourceModule resource = null,
            IAudioModule audio = null,
            ILogModule log = null,
            GameWindowManagerSettings settings = null,
            Func<double> clock = null)
        {
            _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _resource = resource;
            _audio = audio;
            _log = log;
            _settings = settings ?? new GameWindowManagerSettings();
            _clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);
        }

        /// <summary>
        /// 是否已经完成配置校验并创建窗口根节点。
        /// </summary>
        public bool IsInitialized => _initialized && !_shutdown;

        /// <summary>
        /// 当前已打开窗口，按最近打开顺序排列。
        /// </summary>
        public IReadOnlyList<GameWindow> OpenedWindows => _openedWindows;

        /// <summary>
        /// 当前已关闭窗口，按 LRU 从最早关闭到最近关闭排列。
        /// </summary>
        public IReadOnlyList<GameWindow> ClosedWindows => _closedWindows;

        /// <summary>
        /// 当前提供蒙层的最高优先级窗口；无蒙层时为 null。
        /// </summary>
        public GameWindow MaskWindow { get; private set; }

        /// <summary>
        /// 当前接收窗口外点击的最上层窗口；未启用时为 null。
        /// </summary>
        public GameWindow OutsideClickWindow { get; private set; }

        /// <summary>
        /// 当前显示在最上层的窗口。
        /// </summary>
        public GameWindow TopWindow => FindTopmost(static _ => true);

        /// <summary>
        /// 校验配置、注册关系和关联环，并创建常驻 Overlay Canvas。
        /// </summary>
        public void Initialize()
        {
            if (_shutdown) throw new ObjectDisposedException(nameof(GameWindowManager));
            if (_initialized) return;
            ValidateConfiguration();
            _presentation = new GameWindowPresentation(_settings.SortingOrder);
            _presentation.OutsideClicked += OnOutsideClicked;
            _initialized = true;
        }

        /// <summary>
        /// 更新已创建的窗口根 Canvas 缩放设置。
        /// </summary>
        /// <param name="referenceResolution">参考分辨率。</param>
        /// <param name="matchWidthOrHeight">宽高匹配权重。</param>
        /// <returns>窗口根存在且已配置时为真。</returns>
        internal bool TryConfigureCanvas(Vector2 referenceResolution, float matchWidthOrHeight)
        {
            if (!IsInitialized || _presentation == null) return false;
            return _presentation.TryConfigureCanvas(referenceResolution, matchWidthOrHeight);
        }

        /// <summary>
        /// 按窗口 ID 打开；缓存命中时复用实例并再次调用 Show。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>打开的窗口实例。</returns>
        public async UniTask<GameWindow> OpenAsync(
            int id,
            GameWindowOpenArgs args = null,
            CancellationToken ct = default)
        {
            EnsureInitialized();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _operationGate.WaitAsync(linked.Token);
            try
            {
                return await OpenInternalAsync(
                    id,
                    args ?? new GameWindowOpenArgs(),
                    new HashSet<int>(),
                    linked.Token);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 按显式注册的窗口类型打开。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <typeparam name="TWindow">窗口类型。</typeparam>
        /// <returns>打开的窗口实例。</returns>
        public async UniTask<TWindow> OpenAsync<TWindow>(
            GameWindowOpenArgs args = null,
            CancellationToken ct = default)
            where TWindow : GameWindow
        {
            if (!_registry.TryGetId(typeof(TWindow), out int id))
                throw new KeyNotFoundException($"窗口类型未显式注册：{typeof(TWindow).FullName}");
            return (TWindow)await OpenAsync(id, args, ct);
        }

        /// <summary>
        /// 关闭指定 ID 的已打开窗口；已关闭或不存在时返回 false。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="reason">窗口关闭原因。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否关闭了目标窗口。</returns>
        public async UniTask<bool> CloseAsync(
            int id,
            GameWindowCloseReason reason = GameWindowCloseReason.User,
            CancellationToken ct = default)
        {
            EnsureInitialized();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _operationGate.WaitAsync(linked.Token);
            try
            {
                return CloseInternal(id, reason, new HashSet<int>(), true);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 按类型关闭窗口。
        /// </summary>
        /// <param name="reason">窗口关闭原因。</param>
        /// <param name="ct">取消令牌。</param>
        /// <typeparam name="TWindow">窗口类型。</typeparam>
        /// <returns>是否关闭了目标窗口。</returns>
        public UniTask<bool> CloseAsync<TWindow>(
            GameWindowCloseReason reason = GameWindowCloseReason.User,
            CancellationToken ct = default)
            where TWindow : GameWindow
        {
            if (!_registry.TryGetId(typeof(TWindow), out int id))
                return UniTask.FromResult(false);
            return CloseAsync(id, reason, ct);
        }

        /// <summary>
        /// 关闭当前最上层窗口。
        /// </summary>
        /// <param name="reason">窗口关闭原因。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否关闭了窗口。</returns>
        public async UniTask<bool> CloseTopAsync(GameWindowCloseReason reason = GameWindowCloseReason.User, CancellationToken ct = default)
        {
            EnsureInitialized();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _operationGate.WaitAsync(linked.Token);
            try
            {
                GameWindow top = FindTopmost(static _ => true);
                return top != null && CloseInternal(top.Id, reason, new HashSet<int>(), true);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 关闭当前全部已打开窗口，关闭后的缓存规则仍由各窗口配置决定。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>窗口全部关闭后的异步操作。</returns>
        public async UniTask CloseAllAsync(CancellationToken ct = default)
        {
            EnsureInitialized();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _operationGate.WaitAsync(linked.Token);
            try
            {
                var ids = new int[_openedWindows.Count];
                for (int index = 0; index < ids.Length; index++)
                    ids[index] = _openedWindows[_openedWindows.Count - 1 - index].Id;
                var visited = new HashSet<int>();
                for (int index = 0; index < ids.Length; index++)
                    CloseInternal(ids[index], GameWindowCloseReason.CloseAll, visited, true);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 关闭并真正销毁指定作用域的全部窗口，包括 Resident 窗口。
        /// </summary>
        /// <param name="scope">窗口作用域。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>作用域销毁完成后的异步操作。</returns>
        public async UniTask DestroyScopeAsync(
            GameWindowScope scope,
            CancellationToken ct = default)
        {
            EnsureInitialized();
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _operationGate.WaitAsync(linked.Token);
            try
            {
                DestroyScopeInternal(scope, GameWindowCloseReason.ScopeExit);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 强制同步销毁作用域窗口，供逻辑场景 ForceExit 使用。
        /// </summary>
        /// <param name="scope">窗口作用域。</param>
        public void DestroyScopeImmediate(GameWindowScope scope)
        {
            if (!IsInitialized) return;
            if (!_operationGate.Wait(0))
            {
                foreach (WindowEntry entry in new List<WindowEntry>(_entries.Values))
                {
                    if (entry.Config.Scope == scope) entry.Window.StopScopesInternal();
                }
                _log?.Warn(Tag, $"窗口操作进行中，延迟销毁作用域：{scope}");
                DestroyScopeDeferredAsync(scope).Forget();
                return;
            }
            try
            {
                DestroyScopeInternal(scope, GameWindowCloseReason.ScopeExit);
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 查询当前缓存或打开中的窗口实例。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="window">窗口实例。</param>
        /// <returns>是否找到窗口配置或实例。</returns>
        public bool TryGet(int id, out GameWindow window)
        {
            if (_entries.TryGetValue(id, out WindowEntry entry))
            {
                window = entry.Window;
                return true;
            }
            window = null;
            return false;
        }

        /// <summary>
        /// 处理 Timed 过期窗口；使用未缩放实时时钟，不受 Time.timeScale 影响。
        /// </summary>
        public void Update()
        {
            if (!IsInitialized || !_operationGate.Wait(0)) return;
            try
            {
                double now = _clock();
                for (int index = _closedWindows.Count - 1; index >= 0; index--)
                {
                    GameWindow window = _closedWindows[index];
                    WindowEntry entry = _entries[window.Id];
                    if (entry.Config.CacheMode != GameWindowCacheMode.Timed) continue;
                    if (now - entry.ClosedAt < entry.Config.DestroyDelaySeconds) continue;
                    DestroyEntry(entry, GameWindowCloseReason.User, false);
                }
                RefreshInteractionLayers();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 停止接收新请求，并销毁全部窗口、交互层和资源租约。
        /// </summary>
        public void Shutdown()
        {
            if (_shutdown) return;
            _shutdown = true;
            _lifetime.Cancel();
            foreach (WindowEntry entry in new List<WindowEntry>(_entries.Values))
                entry.Window.StopScopesInternal();
            if (!_operationGate.Wait(0))
            {
                CompleteShutdownAsync().Forget();
                return;
            }
            try
            {
                CompleteShutdownInternal();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 递归处理关联规则并打开目标窗口。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="openingPath">打开依赖访问路径。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>打开的窗口实例。</returns>
        private async UniTask<GameWindow> OpenInternalAsync(
            int id,
            GameWindowOpenArgs args,
            HashSet<int> openingPath,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_catalog.TryGet(id, out GameWindowConfig config))
                throw new KeyNotFoundException($"窗口配置不存在：{id}");
            ValidateWindowConfiguration(config);
            if (!openingPath.Add(id))
                throw new InvalidOperationException($"窗口关联打开形成运行时环：{id}");

            try
            {
                for (int index = 0; index < config.OpenBeforeIds.Count; index++)
                {
                    await OpenInternalAsync(
                        config.OpenBeforeIds[index],
                        GameWindowOpenArgs.Association,
                        openingPath,
                        ct);
                }

                ct.ThrowIfCancellationRequested();
                if (_entries.TryGetValue(id, out WindowEntry cached))
                {
                    try
                    {
                        CloseOnOpen(config);
                        ShowCached(cached, args);
                        return cached.Window;
                    }
                    catch (ResourceLoadException error)
                    {
                        error.Data[BusinessFailureMarker] = true;
                        throw;
                    }
                    catch (ResourceGroupLoadException error)
                    {
                        error.Data[BusinessFailureMarker] = true;
                        throw;
                    }
                }
                return await CreateAndShowAsync(config, args, ct);
            }
            finally
            {
                openingPath.Remove(id);
            }
        }

        /// <summary>
        /// 加载 Prefab、实例化窗口并显示。
        /// </summary>
        /// <param name="config">窗口配置。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>新创建并打开的窗口实例。</returns>
        private async UniTask<GameWindow> CreateAndShowAsync(
            GameWindowConfig config,
            GameWindowOpenArgs args,
            CancellationToken ct)
        {
            ResourceLease<GameObject> lease = null;
            GameObject root = null;
            GameWindow window = null;
            WindowEntry entry = null;
            bool resourceAcquired = false;
            try
            {
                if (string.IsNullOrEmpty(config.PrefabAddress))
                {
                    root = new GameObject(config.Name, typeof(RectTransform));
                }
                else
                {
                    if (_resource == null)
                        throw new InvalidOperationException(
                            $"窗口 {config.Id} 配置了 Prefab，但窗口管理器没有 IResourceModule。");
                    lease = await _resource.AcquireAsync<GameObject>(
                        config.PrefabAddress,
                        ResourceLoadOptions.Transient,
                        ct: ct);
                    if (lease?.Asset == null)
                        throw new InvalidOperationException(
                            $"窗口 Prefab 加载失败：{config.PrefabAddress}");
                    root = UnityEngine.Object.Instantiate(lease.Asset);
                    root.name = config.Name;
                }

                resourceAcquired = true;
                root.SetActive(false);
                root.transform.SetParent(_presentation.GetLayer(config.Layer), false);
                window = _registry.Create(config.Id);
                window.InitializeInternal(this, config, root);
                ct.ThrowIfCancellationRequested();
                CloseOnOpen(config);
                entry = new WindowEntry(config, window, lease);
                _entries.Add(config.Id, entry);
                root.SetActive(true);
                root.transform.SetAsLastSibling();
                window.ShowInternal(args);
                _openedWindows.Add(window);
                RefreshInteractionLayers();
                if (args.PlaySound) PlaySound(config.OpenSoundAddress);
                return window;
            }
            catch (Exception error)
            {
                if (resourceAcquired && (error is ResourceLoadException || error is ResourceGroupLoadException))
                    error.Data[BusinessFailureMarker] = true;
                if (entry != null)
                {
                    DestroyEntry(entry, GameWindowCloseReason.User, false);
                }
                else
                {
                    try
                    {
                        window?.DestroyInternal();
                    }
                    catch (Exception ex)
                    {
                        _log?.Error(Tag, $"窗口初始化失败后的 Destroy 异常：{ex}");
                    }
                    DestroyUnityObject(root);
                    lease?.Dispose();
                }
                throw;
            }
        }

        /// <summary>
        /// 在目标窗口完成加载和初始化后关闭其关联窗口。
        /// </summary>
        /// <param name="config">目标窗口配置。</param>
        private void CloseOnOpen(GameWindowConfig config)
        {
            var visited = new HashSet<int>();
            for (int index = 0; index < config.CloseOnOpenIds.Count; index++)
                CloseInternal(config.CloseOnOpenIds[index], GameWindowCloseReason.Association, visited, false);
        }

        /// <summary>
        /// 重新显示缓存中的窗口并更新打开顺序。
        /// </summary>
        /// <param name="entry">窗口缓存条目。</param>
        /// <param name="args">窗口打开参数。</param>
        private void ShowCached(WindowEntry entry, GameWindowOpenArgs args)
        {
            GameWindow window = entry.Window;
            bool wasClosed = window.State == GameWindowState.Closed;
            if (wasClosed)
            {
                window.Root.transform.SetParent(_presentation.GetLayer(entry.Config.Layer), false);
                window.Root.SetActive(true);
            }
            window.Root.transform.SetAsLastSibling();
            try
            {
                window.ShowInternal(args);
            }
            catch
            {
                if (wasClosed) window.Root.SetActive(false);
                throw;
            }

            if (wasClosed)
                _closedWindows.Remove(window);
            else
                _openedWindows.Remove(window);
            _openedWindows.Add(window);
            RefreshInteractionLayers();
            if (args.PlaySound) PlaySound(entry.Config.OpenSoundAddress);
        }

        /// <summary>
        /// 关闭窗口并处理缓存和关联关闭。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="reason">窗口关闭原因。</param>
        /// <param name="visited">已访问窗口集合。</param>
        /// <param name="playSound">是否播放音效。</param>
        /// <returns>窗口是否从打开状态成功关闭。</returns>
        private bool CloseInternal(
            int id,
            GameWindowCloseReason reason,
            HashSet<int> visited,
            bool playSound)
        {
            if (!visited.Add(id)) return false;
            if (!_entries.TryGetValue(id, out WindowEntry entry) ||
                entry.Window.State != GameWindowState.Opened)
            {
                return false;
            }

            try
            {
                entry.Window.CloseInternal(reason);
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"窗口 {id} Close 异常，仍继续关闭和回收：{ex}");
            }
            _openedWindows.Remove(entry.Window);
            bool hasRoot = entry.Window.Root != null;
            if (hasRoot) entry.Window.Root.SetActive(false);
            if (playSound) PlaySound(entry.Config.CloseSoundAddress);

            if (!hasRoot || entry.Config.CacheMode == GameWindowCacheMode.Immediate)
            {
                DestroyEntry(entry, reason, false);
            }
            else
            {
                entry.ClosedAt = _clock();
                _closedWindows.Remove(entry.Window);
                _closedWindows.Add(entry.Window);
            }

            for (int index = 0; index < entry.Config.CloseWithSelfIds.Count; index++)
            {
                CloseInternal(
                    entry.Config.CloseWithSelfIds[index],
                    GameWindowCloseReason.Association,
                    visited,
                    false);
            }
            EvictOverflow();
            RefreshInteractionLayers();
            return true;
        }

        /// <summary>
        /// 淘汰超出容量的非常驻缓存窗口。
        /// </summary>
        private void EvictOverflow()
        {
            int evictableCount = 0;
            for (int index = 0; index < _closedWindows.Count; index++)
            {
                if (_entries[_closedWindows[index].Id].Config.CacheMode !=
                    GameWindowCacheMode.Resident)
                {
                    evictableCount++;
                }
            }

            while (evictableCount > _settings.MaxClosedWindows)
            {
                WindowEntry victim = null;
                for (int index = 0; index < _closedWindows.Count; index++)
                {
                    WindowEntry candidate = _entries[_closedWindows[index].Id];
                    if (candidate.Config.CacheMode == GameWindowCacheMode.Resident) continue;
                    victim = candidate;
                    break;
                }
                if (victim == null) return;
                DestroyEntry(victim, GameWindowCloseReason.User, false);
                evictableCount--;
            }
        }

        /// <summary>
        /// 销毁指定作用域中的全部窗口。
        /// </summary>
        /// <param name="scope">窗口作用域。</param>
        /// <param name="reason">窗口关闭原因。</param>
        private void DestroyScopeInternal(GameWindowScope scope, GameWindowCloseReason reason)
        {
            var victims = new List<WindowEntry>();
            foreach (WindowEntry entry in _entries.Values)
            {
                if (entry.Config.Scope == scope) victims.Add(entry);
            }
            for (int index = 0; index < victims.Count; index++)
            {
                DestroyEntry(
                    victims[index],
                    reason,
                    victims[index].Window.State == GameWindowState.Opened);
            }
            RefreshInteractionLayers();
        }

        /// <summary>
        /// 销毁窗口实例并释放 Prefab 资源租约。
        /// </summary>
        /// <param name="entry">窗口缓存条目。</param>
        /// <param name="reason">窗口关闭原因。</param>
        /// <param name="invokeClose">销毁前是否执行关闭回调。</param>
        private void DestroyEntry(
            WindowEntry entry,
            GameWindowCloseReason reason,
            bool invokeClose)
        {
            GameWindow window = entry.Window;
            GameObject root = window.Root;
            _openedWindows.Remove(window);
            _closedWindows.Remove(window);
            _entries.Remove(entry.Config.Id);
            if (invokeClose && window.State == GameWindowState.Opened)
            {
                try
                {
                    window.CloseInternal(reason);
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"窗口 {entry.Config.Id} 销毁前 Close 异常：{ex}");
                }
            }
            try
            {
                window.DestroyInternal();
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"窗口 {entry.Config.Id} Destroy 异常：{ex}");
            }
            DestroyUnityObject(root);
            entry.PrefabLease?.Dispose();
        }

        /// <summary>
        /// 更新最上层模态遮罩与窗口外点击区域。
        /// </summary>
        private void RefreshInteractionLayers()
        {
            if (_presentation == null) return;
            GameWindow top = FindTopmost(static _ => true);
            MaskWindow = FindTopmost(static window => window.Config.UseMask);
            OutsideClickWindow = top != null && top.Config.CloseOnOutsideClick ? top : null;
            _presentation.Refresh(MaskWindow, OutsideClickWindow);
        }

        /// <summary>
        /// 按渲染层级和打开顺序查找最上层窗口。
        /// </summary>
        /// <param name="predicate">筛选窗口的条件。</param>
        /// <returns>符合条件的最上层窗口；不存在时为 null。</returns>
        private GameWindow FindTopmost(Predicate<GameWindow> predicate)
        {
            GameWindow result = null;
            int resultLayer = int.MinValue;
            int resultOpenIndex = -1;
            for (int index = 0; index < _openedWindows.Count; index++)
            {
                GameWindow candidate = _openedWindows[index];
                if (!predicate(candidate)) continue;
                int layer = candidate.Config.Layer;
                if (result != null &&
                    (layer < resultLayer || layer == resultLayer && index < resultOpenIndex))
                {
                    continue;
                }
                result = candidate;
                resultLayer = layer;
                resultOpenIndex = index;
            }
            return result;
        }

        /// <summary>
        /// 响应窗口外点击事件。
        /// </summary>
        private void OnOutsideClicked()
        {
            CloseOutsideWindowAsync().Forget();
        }

        /// <summary>
        /// 关闭允许点击外部关闭的最上层窗口。
        /// </summary>
        /// <returns>外部点击关闭操作。</returns>
        private async UniTask CloseOutsideWindowAsync()
        {
            try
            {
                await _operationGate.WaitAsync(_lifetime.Token);
                try
                {
                    GameWindow top = FindTopmost(static _ => true);
                    if (top != null && top.Config.CloseOnOutsideClick)
                    {
                        CloseInternal(
                            top.Id,
                            GameWindowCloseReason.OutsideClick,
                            new HashSet<int>(),
                            true);
                    }
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// 等待当前操作结束后销毁作用域窗口。
        /// </summary>
        /// <param name="scope">窗口作用域。</param>
        /// <returns>延后销毁操作。</returns>
        private async UniTask DestroyScopeDeferredAsync(GameWindowScope scope)
        {
            try
            {
                await _operationGate.WaitAsync(_lifetime.Token);
                try
                {
                    DestroyScopeInternal(scope, GameWindowCloseReason.ScopeExit);
                }
                finally
                {
                    _operationGate.Release();
                }
            }
            catch (OperationCanceledException)
            {
            }
        }

        /// <summary>
        /// 校验全部窗口配置、注册关系和打开依赖环。
        /// </summary>
        private void ValidateConfiguration()
        {
            IReadOnlyList<GameWindowConfig> configs = _catalog.Items;
            for (int index = 0; index < configs.Count; index++)
                ValidateWindowConfiguration(configs[index]);

            var states = new Dictionary<int, byte>();
            for (int index = 0; index < configs.Count; index++)
                VisitOpenDependencies(configs[index].Id, states);
        }

        /// <summary>
        /// 校验单个窗口配置及关联引用。
        /// </summary>
        /// <param name="config">窗口配置。</param>
        private void ValidateWindowConfiguration(GameWindowConfig config)
        {
            if (!_registry.TryGetType(config.Id, out Type type))
                throw new InvalidOperationException($"窗口 {config.Id} 未显式注册：{config.ClassName}");
            if (!string.Equals(config.ClassName, type.Name, StringComparison.Ordinal) &&
                !string.Equals(config.ClassName, type.FullName, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"窗口 {config.Id} 配置类名 {config.ClassName} 与注册类型 {type.FullName} 不一致。");
            if (!string.IsNullOrEmpty(config.PrefabAddress) && _resource == null)
                throw new InvalidOperationException($"窗口 {config.Id} 需要 IResourceModule。");
            ValidateReferences(config.Id, config.OpenBeforeIds, nameof(config.OpenBeforeIds));
            ValidateReferences(config.Id, config.CloseOnOpenIds, nameof(config.CloseOnOpenIds));
            ValidateReferences(config.Id, config.CloseWithSelfIds, nameof(config.CloseWithSelfIds));
        }

        /// <summary>
        /// 校验关联窗口 ID 集合。
        /// </summary>
        /// <param name="ownerId">所属窗口 ID。</param>
        /// <param name="ids">关联窗口 ID 集合。</param>
        /// <param name="field">配置字段名称。</param>
        private void ValidateReferences(int ownerId, IReadOnlyList<int> ids, string field)
        {
            var unique = new HashSet<int>();
            for (int index = 0; index < ids.Count; index++)
            {
                int targetId = ids[index];
                if (targetId == ownerId)
                    throw new InvalidOperationException($"窗口 {ownerId} 的 {field} 不能引用自身。");
                if (!unique.Add(targetId))
                    throw new InvalidOperationException($"窗口 {ownerId} 的 {field} 重复引用 {targetId}。");
                if (!_catalog.TryGet(targetId, out _))
                    throw new InvalidOperationException($"窗口 {ownerId} 的 {field} 引用了未知窗口 {targetId}。");
            }
        }

        /// <summary>
        /// 遍历打开依赖并检测环。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="states">依赖遍历状态。</param>
        private void VisitOpenDependencies(int id, Dictionary<int, byte> states)
        {
            if (states.TryGetValue(id, out byte state))
            {
                if (state == 1) throw new InvalidOperationException($"窗口 OpenBeforeIds 存在环：{id}");
                if (state == 2) return;
            }
            states[id] = 1;
            _catalog.TryGet(id, out GameWindowConfig config);
            for (int index = 0; index < config.OpenBeforeIds.Count; index++)
                VisitOpenDependencies(config.OpenBeforeIds[index], states);
            states[id] = 2;
        }

        /// <summary>
        /// 请求播放窗口音效。
        /// </summary>
        /// <param name="address">资源地址。</param>
        private void PlaySound(string address)
        {
            if (_audio == null || string.IsNullOrEmpty(address) || _shutdown) return;
            PlaySoundAsync(address).Forget();
        }

        /// <summary>
        /// 异步播放窗口音效并记录失败。
        /// </summary>
        /// <param name="address">资源地址。</param>
        /// <returns>音效播放操作。</returns>
        private async UniTask PlaySoundAsync(string address)
        {
            try
            {
                await _audio.PlayAsync(address, AudioChannel.Sfx, ct: _lifetime.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                _log?.Warn(Tag, $"窗口音效播放失败 {address}：{ex.Message}");
            }
        }

        /// <summary>
        /// 等待进行中的操作结束后完成销毁。
        /// </summary>
        /// <returns>卸载操作。</returns>
        private async UniTask CompleteShutdownAsync()
        {
            await _operationGate.WaitAsync();
            try
            {
                CompleteShutdownInternal();
            }
            finally
            {
                _operationGate.Release();
            }
        }

        /// <summary>
        /// 销毁所有窗口、交互层和资源租约。
        /// </summary>
        private void CompleteShutdownInternal()
        {
            var victims = new List<WindowEntry>(_entries.Values);
            for (int index = 0; index < victims.Count; index++)
            {
                DestroyEntry(
                    victims[index],
                    GameWindowCloseReason.Shutdown,
                    victims[index].Window.State == GameWindowState.Opened);
            }
            _presentation?.Dispose();
            _presentation = null;
            MaskWindow = null;
            OutsideClickWindow = null;
            _initialized = false;
            _lifetime.Dispose();
        }

        /// <summary>
        /// 确认窗口管理器已初始化且未卸载。
        /// </summary>
        private void EnsureInitialized()
        {
            if (_shutdown) throw new ObjectDisposedException(nameof(GameWindowManager));
            if (!_initialized) throw new InvalidOperationException("窗口管理器尚未初始化。");
        }

        /// <summary>
        /// 按运行状态销毁 Unity 对象。
        /// </summary>
        /// <param name="target">待销毁对象。</param>
        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying)
                UnityEngine.Object.Destroy(target);
            else
                UnityEngine.Object.DestroyImmediate(target);
        }

        private sealed class WindowEntry
        {
            /// <summary>
            /// 保存窗口实例和其 Prefab 资源租约。
            /// </summary>
            /// <param name="config">窗口配置。</param>
            /// <param name="window">窗口实例。</param>
            /// <param name="prefabLease">Prefab 资源租约。</param>
            internal WindowEntry(
                GameWindowConfig config,
                GameWindow window,
                ResourceLease<GameObject> prefabLease)
            {
                Config = config;
                Window = window;
                PrefabLease = prefabLease;
            }

            internal GameWindowConfig Config { get; }
            internal GameWindow Window { get; }
            internal ResourceLease<GameObject> PrefabLease { get; }
            internal double ClosedAt { get; set; }
        }

        private sealed class GameWindowPresentation : IDisposable
        {
            private readonly SortedDictionary<int, RectTransform> _layers = new();
            private readonly GameObject _root;
            private readonly CanvasScaler _scaler;
            private readonly RectTransform _maskTransform;
            private readonly Image _maskImage;
            private readonly RectTransform _outsideTransform;

            /// <summary>
            /// 创建窗口专用 Canvas、模态遮罩和外部点击层。
            /// </summary>
            /// <param name="sortingOrder">Canvas 排序值。</param>
            internal GameWindowPresentation(int sortingOrder)
            {
                _root = new GameObject(
                    "[GameWindowRoot]",
                    typeof(RectTransform),
                    typeof(Canvas),
                    typeof(CanvasScaler),
                    typeof(GraphicRaycaster));
                Canvas canvas = _root.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = sortingOrder;
                _scaler = _root.GetComponent<CanvasScaler>();
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = new Vector2(1920f, 1080f);
                _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                _scaler.matchWidthOrHeight = 0.5f;
                if (Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_root);

                GameObject mask = CreateStretchObject("[WindowMask]", _root.transform);
                _maskTransform = mask.GetComponent<RectTransform>();
                _maskImage = mask.AddComponent<Image>();
                _maskImage.raycastTarget = true;
                mask.SetActive(false);

                GameObject outside = CreateStretchObject("[WindowOutsideClick]", _root.transform);
                _outsideTransform = outside.GetComponent<RectTransform>();
                Image outsideImage = outside.AddComponent<Image>();
                outsideImage.color = Color.clear;
                outsideImage.raycastTarget = true;
                Button outsideButton = outside.AddComponent<Button>();
                outsideButton.transition = Selectable.Transition.None;
                outsideButton.onClick.AddListener(() => OutsideClicked?.Invoke());
                outside.SetActive(false);
            }

            internal event Action OutsideClicked;

            /// <summary>
            /// 将窗口根切换到调用方指定的参考尺寸。
            /// </summary>
            /// <param name="referenceResolution">参考分辨率。</param>
            /// <param name="matchWidthOrHeight">宽高匹配权重。</param>
            /// <returns>根 CanvasScaler 可用时为真。</returns>
            internal bool TryConfigureCanvas(Vector2 referenceResolution, float matchWidthOrHeight)
            {
                if (_scaler == null) return false;
                _scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                _scaler.referenceResolution = referenceResolution;
                _scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
                _scaler.matchWidthOrHeight = matchWidthOrHeight;
                return true;
            }

            /// <summary>
            /// 获取或创建指定窗口层级。
            /// </summary>
            /// <param name="layer">窗口层级。</param>
            /// <returns>指定层级的 RectTransform。</returns>
            internal RectTransform GetLayer(int layer)
            {
                if (_layers.TryGetValue(layer, out RectTransform existing)) return existing;
                GameObject layerObject = CreateStretchObject($"Layer_{layer}", _root.transform);
                var rect = layerObject.GetComponent<RectTransform>();
                _layers.Add(layer, rect);
                int index = 0;
                foreach (RectTransform item in _layers.Values)
                    item.SetSiblingIndex(index++);
                return rect;
            }

            /// <summary>
            /// 将遮罩及外部点击区域放到对应窗口下方。
            /// </summary>
            /// <param name="maskWindow">显示遮罩的窗口。</param>
            /// <param name="outsideWindow">接收外部点击的窗口。</param>
            internal void Refresh(GameWindow maskWindow, GameWindow outsideWindow)
            {
                if (_root == null) return;
                if (maskWindow == null)
                {
                    _maskTransform.gameObject.SetActive(false);
                }
                else
                {
                    _maskImage.color = maskWindow.Config.MaskColor;
                    PlaceBefore(_maskTransform, maskWindow.RootTransform);
                }

                if (outsideWindow == null)
                {
                    _outsideTransform.gameObject.SetActive(false);
                }
                else
                {
                    PlaceBefore(_outsideTransform, outsideWindow.RootTransform);
                }
            }

            /// <inheritdoc />
            public void Dispose()
            {
                OutsideClicked = null;
                DestroyUnityObject(_root);
                _layers.Clear();
            }

            /// <summary>
            /// 将交互层放在目标窗口的前一个同级位置。
            /// </summary>
            /// <param name="overlay">遮罩或点击区域。</param>
            /// <param name="owner">窗口管理器。</param>
            private static void PlaceBefore(RectTransform overlay, RectTransform owner)
            {
                overlay.SetParent(owner.parent, false);
                overlay.gameObject.SetActive(true);
                overlay.SetSiblingIndex(owner.GetSiblingIndex());
            }

            /// <summary>
            /// 创建拉伸填满父节点的 UI 对象。
            /// </summary>
            /// <param name="name">窗口名称。</param>
            /// <param name="parent">父节点。</param>
            /// <returns>新创建的拉伸 UI 对象。</returns>
            private static GameObject CreateStretchObject(string name, Transform parent)
            {
                var result = new GameObject(name, typeof(RectTransform));
                var rect = result.GetComponent<RectTransform>();
                rect.SetParent(parent, false);
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = Vector2.one;
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                rect.localScale = Vector3.one;
                return result;
            }
        }
    }
}
