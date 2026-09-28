using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;
using TMPro;

namespace GameFramework
{
    /// <summary>
    /// 统一管理业务窗口、模态反馈、提示、等待层和 UGUI 输入宿主。
    /// </summary>
    public sealed class UIModule : ModuleBase, IUIModule
    {
        private const int FeedbackSortingOrder = 32750;
        private GameWindowConfigCatalog _catalog;
        private GameWindowRegistry _registry;
        private GameWindowManager _windows;
        private UIFeedbackPresenter _feedback;
        private GameObject _root;
        private EventSystem _eventSystem;
        private InputSystemUIInputModule _inputSystemUI;
        private StandaloneInputModule _legacyInput;
        private InputActionAsset _previousUIAsset;
        private InputActionReference[] _previousUIActions;
        private readonly List<InputActionReference> _uiActionReferences = new(10);
        private IDisposable _backHandler;
        private bool _ownsEventSystem;
        private bool _ownsInputSystemUI;
        private bool _legacyInputWasEnabled;
        private CancellationTokenSource _loadLifetime;

        /// <inheritdoc />
        public bool IsReady => _root != null && _feedback != null;

        /// <inheritdoc />
        public bool BlocksGameplayInput
        {
            get
            {
                if (_feedback != null && _feedback.IsBlockingGameplay) return true;
                if (_windows == null) return false;
                IReadOnlyList<GameWindow> windows = _windows.OpenedWindows;
                for (int i = 0; i < windows.Count; i++)
                {
                    GameWindow window = windows[i];
                    if (window.State == GameWindowState.Opened && window.Config?.UseMask == true) return true;
                }
                return false;
            }
        }

        /// <inheritdoc />
        public IReadOnlyList<GameWindow> OpenedWindows => _windows?.OpenedWindows ?? Array.Empty<GameWindow>();

        /// <inheritdoc />
        public override void OnInit()
        {
            _root = new GameObject("[GameUI]", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            UnityEngine.Object.DontDestroyOnLoad(_root);
            Canvas canvas = _root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = FeedbackSortingOrder;
            CanvasScaler scaler = _root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            EnsureEventSystem();
            _feedback = new UIFeedbackPresenter(_root.GetComponent<RectTransform>());
            _loadLifetime = new CancellationTokenSource();
            _catalog = new GameWindowConfigCatalog(Array.Empty<GameWindowConfig>());
            _registry = new GameWindowRegistry();
            _windows = new GameWindowManager(_catalog, _registry, GameEntry.Get<IResourceModule>(), GameEntry.Get<IAudioModule>(),
                GameEntry.Get<ILogModule>());
            _windows.Initialize();
            _backHandler = GameEntry.Get<IInputModule>()?.RegisterBackHandler(HandleBack, 100);
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            if (!IsReady) return;
            if (_eventSystem == null) EnsureEventSystem();
            _windows?.Update();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            _backHandler?.Dispose();
            _backHandler = null;
            _loadLifetime?.Cancel();
            _feedback?.Dispose();
            _feedback = null;
            _loadLifetime?.Dispose();
            _loadLifetime = null;
            _windows?.Shutdown();
            _windows = null;
            _catalog = null;
            _registry = null;
            ReleaseEventSystem();
            if (_root != null) DestroyUnityObject(_root);
            _root = null;
        }

        /// <inheritdoc />
        public void ConfigureWindows(IEnumerable<GameWindowConfig> configs, Action<GameWindowRegistry> register)
        {
            EnsureReady();
            if (configs == null) throw new ArgumentNullException(nameof(configs));
            if (register == null) throw new ArgumentNullException(nameof(register));
            foreach (GameWindowConfig config in configs) _catalog.Add(config);
            register(_registry);
        }

        /// <inheritdoc />
        /// <param name="id">窗口配置 ID。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="options">加载反馈设置。</param>
        /// <returns>已打开的窗口。</returns>
        public UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args = null, CancellationToken ct = default, UILoadOptions options = null)
        {
            EnsureReady();
            GameWindowManager windows = _windows;
            return RunWithLoadingAsync(token => windows.OpenAsync(id, args, token), options, ct);
        }

        /// <inheritdoc />
        /// <param name="id">窗口配置 ID。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已打开的窗口。</returns>
        public UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args, CancellationToken ct)
        {
            return OpenWindowAsync(id, args, ct, null);
        }

        /// <inheritdoc />
        /// <typeparam name="TWindow">操作结果类型。</typeparam>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="options">加载反馈设置。</param>
        /// <returns>已打开的窗口。</returns>
        public UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args = null, CancellationToken ct = default, UILoadOptions options = null)
            where TWindow : GameWindow
        {
            EnsureReady();
            GameWindowManager windows = _windows;
            return RunWithLoadingAsync(token => windows.OpenAsync<TWindow>(args, token), options, ct);
        }

        /// <inheritdoc />
        /// <typeparam name="TWindow">操作结果类型。</typeparam>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已打开的窗口。</returns>
        public UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args, CancellationToken ct) where TWindow : GameWindow
        {
            return OpenWindowAsync<TWindow>(args, ct, null);
        }

        /// <inheritdoc />
        /// <typeparam name="T">操作结果类型。</typeparam>
        /// <param name="operation">响应取消并负责自身资源清理的异步操作。</param>
        /// <param name="options">加载反馈设置。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>由调用方负责生命周期的操作结果。</returns>
        public async UniTask<T> RunWithLoadingAsync<T>(Func<CancellationToken, UniTask<T>> operation, UILoadOptions options = null,
            CancellationToken ct = default)
        {
            EnsureReady();
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            options ??= new UILoadOptions();
            if (options.BusyDelayMilliseconds < 0) throw new ArgumentOutOfRangeException(nameof(options), "等待层延迟不能为负数。");
            UIFeedbackPresenter feedback = _feedback;
            CancellationToken moduleToken = _loadLifetime.Token;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, moduleToken);
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    return await RunLoadingAttemptAsync(operation, options, feedback, linked.Token);
                }
                catch (ResourceLoadException error) when (options.ShowRetry && IsRecoverable(error))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    bool retry = await feedback.ConfirmAsync(options.RetryTitle, options.RetryMessage, options.RetryText,
                        options.CancelText, linked.Token);
                    if (!retry) throw new OperationCanceledException("用户取消资源加载重试。", linked.Token);
                }
                catch (ResourceGroupLoadException error) when (options.ShowRetry && IsRecoverable(error))
                {
                    linked.Token.ThrowIfCancellationRequested();
                    bool retry = await feedback.ConfirmAsync(options.RetryTitle, options.RetryMessage, options.RetryText,
                        options.CancelText, linked.Token);
                    if (!retry) throw new OperationCanceledException("用户取消资源组加载重试。", linked.Token);
                }
            }
        }

        /// <summary>
        /// 执行单次异步操作，并在操作结束前清理该次等待句柄。
        /// </summary>
        /// <typeparam name="T">操作结果类型。</typeparam>
        /// <param name="operation">应响应取消并自行清理晚到结果的异步操作。</param>
        /// <param name="options">加载反馈选项。</param>
        /// <param name="feedback">本次调用所属的反馈界面。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>异步操作结果。</returns>
        private async UniTask<T> RunLoadingAttemptAsync<T>(Func<CancellationToken, UniTask<T>> operation, UILoadOptions options,
            UIFeedbackPresenter feedback, CancellationToken ct)
        {
            IUIBusyHandle busy = null;
            using var pendingCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
            UniTask pending = ShowPendingBusyAsync(options, feedback, handle => busy = handle, pendingCancellation.Token);
            try
            {
                return await operation(ct);
            }
            finally
            {
                pendingCancellation.Cancel();
                await pending;
                busy?.Dispose();
            }
        }

        /// <summary>
        /// 仅当异步操作仍在进行时显示等待层。
        /// </summary>
        /// <param name="options">加载反馈选项。</param>
        /// <param name="feedback">本次调用所属的反馈界面。</param>
        /// <param name="setHandle">接收等待句柄的回调。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>等待层显示操作。</returns>
        private async UniTask ShowPendingBusyAsync(UILoadOptions options, UIFeedbackPresenter feedback,
            Action<IUIBusyHandle> setHandle, CancellationToken ct)
        {
            if (!options.ShowBusy) return;
            if (options.BusyDelayMilliseconds > 0)
            {
                bool cancelled = await UniTask.Delay(options.BusyDelayMilliseconds, ignoreTimeScale: true,
                    cancellationToken: ct).SuppressCancellationThrow();
                if (cancelled) return;
            }
            if (!ct.IsCancellationRequested) setHandle(feedback.BeginBusy(options.BusyMessage));
        }

        /// <summary>
        /// 判断单个资源失败是否允许用户重试。
        /// </summary>
        /// <param name="error">资源加载异常。</param>
        /// <returns>是否为实际加载失败。</returns>
        private static bool IsRecoverable(ResourceLoadException error)
        {
            return !error.Data.Contains(GameWindowManager.BusinessFailureMarker) &&
                error.Error?.Kind == ResourceLoadError.FailureKind.LoadFailed;
        }

        /// <summary>
        /// 判断完整失败的资源组是否只包含实际加载失败。
        /// </summary>
        /// <param name="error">资源组加载异常。</param>
        /// <returns>全部失败项是否可重试。</returns>
        private static bool IsRecoverable(ResourceGroupLoadException error)
        {
            if (error.Data.Contains(GameWindowManager.BusinessFailureMarker) || error.Failures.Count == 0) return false;
            for (int i = 0; i < error.Failures.Count; i++)
                if (error.Failures[i]?.Kind != ResourceLoadError.FailureKind.LoadFailed) return false;
            return true;
        }

        /// <inheritdoc />
        public UniTask<bool> CloseWindowAsync(int id, GameWindowCloseReason reason = GameWindowCloseReason.User, CancellationToken ct = default)
        {
            EnsureReady();
            return _windows.CloseAsync(id, reason, ct);
        }

        /// <inheritdoc />
        public UniTask<bool> CloseTopWindowAsync(CancellationToken ct = default)
        {
            EnsureReady();
            return _windows.CloseTopAsync(ct: ct);
        }

        /// <inheritdoc />
        public UniTask CloseAllWindowsAsync(CancellationToken ct = default)
        {
            EnsureReady();
            return _windows.CloseAllAsync(ct);
        }

        /// <inheritdoc />
        public UniTask DestroyWindowScopeAsync(GameWindowScope scope, CancellationToken ct = default)
        {
            EnsureReady();
            return _windows.DestroyScopeAsync(scope, ct);
        }

        /// <inheritdoc />
        public void ShowToast(string message, float durationSeconds = 2.5f)
        {
            EnsureReady();
            _feedback.ShowToast(message, durationSeconds);
        }

        /// <inheritdoc />
        public UniTask<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消", CancellationToken ct = default)
        {
            EnsureReady();
            return _feedback.ConfirmAsync(title, message, confirmText, cancelText, ct);
        }

        /// <inheritdoc />
        public UniTask AlertAsync(string title, string message, string confirmText = "确定", CancellationToken ct = default)
        {
            EnsureReady();
            return _feedback.AlertAsync(title, message, confirmText, ct);
        }

        /// <inheritdoc />
        public IUIBusyHandle BeginBusy(string message = "请稍候…")
        {
            EnsureReady();
            return _feedback.BeginBusy(message);
        }

        /// <summary>
        /// 确保当前宿主拥有可用的 UGUI 事件系统。
        /// </summary>
        private void EnsureEventSystem()
        {
            if (_eventSystem != null && _inputSystemUI != null) return;
            _eventSystem = EventSystem.current;
            if (_eventSystem == null)
            {
                var host = new GameObject("[GameUIEventSystem]", typeof(EventSystem));
                host.transform.SetParent(_root.transform, false);
                _eventSystem = host.GetComponent<EventSystem>();
                _ownsEventSystem = true;
            }

            _legacyInput = _eventSystem.GetComponent<StandaloneInputModule>();
            if (_legacyInput != null)
            {
                _legacyInputWasEnabled = _legacyInput.enabled;
                _legacyInput.enabled = false;
            }

            _inputSystemUI = _eventSystem.GetComponent<InputSystemUIInputModule>();
            _ownsInputSystemUI = _inputSystemUI == null;
            if (_ownsInputSystemUI) _inputSystemUI = _eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();
            else SavePreviousUIActions();
            ConfigureUIActions();
        }

        /// <summary>
        /// 使用框架固定动作资产配置 UGUI 的唯一输入模块。
        /// </summary>
        private void ConfigureUIActions()
        {
            InputActionAsset asset = GameEntry.Get<IInputModule>()?.FrameworkActions ?? Resources.Load<InputActionAsset>("FrameworkInputActions");
            if (asset == null) throw new InvalidOperationException("框架固定输入动作资产尚未就绪。");

            _inputSystemUI.UnassignActions();
            _inputSystemUI.actionsAsset = asset;
            _inputSystemUI.point = CreateUIActionReference(asset, "Point");
            _inputSystemUI.leftClick = CreateUIActionReference(asset, "Click");
            _inputSystemUI.rightClick = CreateUIActionReference(asset, "RightClick");
            _inputSystemUI.middleClick = CreateUIActionReference(asset, "MiddleClick");
            _inputSystemUI.scrollWheel = CreateUIActionReference(asset, "ScrollWheel");
            _inputSystemUI.move = CreateUIActionReference(asset, "Move");
            _inputSystemUI.submit = CreateUIActionReference(asset, "Submit");
            _inputSystemUI.cancel = CreateUIActionReference(asset, "Cancel");
            _inputSystemUI.trackedDevicePosition = CreateUIActionReference(asset, "TrackedDevicePosition");
            _inputSystemUI.trackedDeviceOrientation = CreateUIActionReference(asset, "TrackedDeviceOrientation");
        }

        /// <summary>
        /// 为框架 UI 动作创建可赋给 EventSystem 的引用。
        /// </summary>
        /// <param name="asset">固定动作资产。</param>
        /// <param name="actionName">UI 动作名称。</param>
        /// <returns>需要在关闭时释放的动作引用。</returns>
        private InputActionReference CreateUIActionReference(InputActionAsset asset, string actionName)
        {
            InputAction action = asset.FindAction("UI/" + actionName, false);
            if (action == null) throw new InvalidOperationException("缺少框架 UI 动作：" + actionName);
            InputActionReference reference = InputActionReference.Create(action);
            _uiActionReferences.Add(reference);
            return reference;
        }

        /// <summary>
        /// 保存既有 UGUI 输入模块的配置，以便框架卸载时恢复。
        /// </summary>
        private void SavePreviousUIActions()
        {
            _previousUIAsset = _inputSystemUI.actionsAsset;
            _previousUIActions = new[]
            {
                _inputSystemUI.point, _inputSystemUI.leftClick, _inputSystemUI.rightClick, _inputSystemUI.middleClick,
                _inputSystemUI.scrollWheel, _inputSystemUI.move, _inputSystemUI.submit, _inputSystemUI.cancel,
                _inputSystemUI.trackedDevicePosition, _inputSystemUI.trackedDeviceOrientation
            };
        }

        /// <summary>
        /// 恢复场景自带事件系统并释放框架创建的动作引用。
        /// </summary>
        private void ReleaseEventSystem()
        {
            if (_inputSystemUI != null)
            {
                _inputSystemUI.UnassignActions();
                if (!_ownsInputSystemUI && _previousUIActions != null)
                {
                    _inputSystemUI.actionsAsset = _previousUIAsset;
                    _inputSystemUI.point = _previousUIActions[0];
                    _inputSystemUI.leftClick = _previousUIActions[1];
                    _inputSystemUI.rightClick = _previousUIActions[2];
                    _inputSystemUI.middleClick = _previousUIActions[3];
                    _inputSystemUI.scrollWheel = _previousUIActions[4];
                    _inputSystemUI.move = _previousUIActions[5];
                    _inputSystemUI.submit = _previousUIActions[6];
                    _inputSystemUI.cancel = _previousUIActions[7];
                    _inputSystemUI.trackedDevicePosition = _previousUIActions[8];
                    _inputSystemUI.trackedDeviceOrientation = _previousUIActions[9];
                }
                if (_ownsInputSystemUI && !_ownsEventSystem) DestroyUnityObject(_inputSystemUI);
            }
            for (int i = 0; i < _uiActionReferences.Count; i++) DestroyUnityObject(_uiActionReferences[i]);
            _uiActionReferences.Clear();
            if (_legacyInput != null && !_ownsEventSystem) _legacyInput.enabled = _legacyInputWasEnabled;
            if (_ownsEventSystem && _eventSystem != null) DestroyUnityObject(_eventSystem.gameObject);

            _eventSystem = null;
            _inputSystemUI = null;
            _legacyInput = null;
            _previousUIAsset = null;
            _previousUIActions = null;
            _ownsEventSystem = false;
            _ownsInputSystemUI = false;
            _legacyInputWasEnabled = false;
        }

        /// <summary>
        /// 按 UI 优先级处理系统返回动作。
        /// </summary>
        /// <returns>是否由文本输入、等待层、弹窗或窗口消费。</returns>
        private bool HandleBack()
        {
            GameObject selected = _eventSystem != null ? _eventSystem.currentSelectedGameObject : null;
            if (selected != null)
            {
                InputField field = selected.GetComponentInParent<InputField>();
                TMP_InputField tmpField = selected.GetComponentInParent<TMP_InputField>();
                if ((field != null && field.isFocused) || (tmpField != null && tmpField.isFocused))
                {
                    field?.DeactivateInputField();
                    tmpField?.DeactivateInputField();
                    _eventSystem.SetSelectedGameObject(null);
                    return true;
                }
            }
            if (_feedback?.TryHandleBack() == true) return true;
            if (_windows == null || _windows.OpenedWindows.Count == 0) return false;
            _windows.CloseTopAsync().Forget(LogAsyncFailure);
            return true;
        }
        /// <summary>
        /// 校验 UI 模块已经完成初始化。
        /// </summary>
        private void EnsureReady()
        {
            if (!IsReady) throw new InvalidOperationException("UI 模块尚未初始化。");
        }

        /// <summary>
        /// 将异步返回键操作的错误写入现有日志模块。
        /// </summary>
        /// <param name="exception">异步操作异常。</param>
        private static void LogAsyncFailure(Exception exception)
        {
            if (exception is OperationCanceledException) return;
            GameEntry.Get<ILogModule>()?.Error("UI", exception.ToString());
        }

        /// <summary>
        /// 在运行时与编辑器测试中正确销毁 UI 对象。
        /// </summary>
        /// <param name="target">需要销毁的对象。</param>
        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }
    }
}
