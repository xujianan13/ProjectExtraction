using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.InputSystem.Utilities;
using UnityEngine.UI;
using TMPro;

namespace GameFramework
{
    /// <summary>
    /// 基于 Unity Input System 的通用输入模块。
    /// </summary>
    public sealed partial class InputModule : ModuleBase, IInputModule
    {
        private const string BackId = "System/Back";
        private const string StorageSpaceName = "InputBindings";
        private const string FrameworkStorageKey = "Framework/v1";
        private const string GameplayStorageKey = "Gameplay/v1";
        private static readonly InternedString OnScreenUsage = new("OnScreen");
        private readonly Dictionary<string, InputAction> _actions = new(StringComparer.Ordinal);
        private readonly List<KeyValuePair<string, InputAction>> _iterationActions = new();
        private readonly Dictionary<string, string> _ids = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ActionFrame> _frames = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<Action<GameActionEvent>>> _subscribers = new(StringComparer.Ordinal);
        private readonly List<InputContext> _contexts = new();
        private readonly List<BackHandler> _backHandlers = new();
        private readonly HashSet<string> _consumed = new(StringComparer.Ordinal);
        private readonly HashSet<string> _suppressUntilRelease = new(StringComparer.Ordinal);
        private readonly List<GamePointer> _pointers = new();
        private readonly Dictionary<int, PointerTrack> _pointerTracks = new();
        private readonly List<RaycastResult> _uiHits = new();
        private int _nextContextOrder;
        private int _blockCount;
        private int _nextBlockId, _blockGeneration;
        private readonly Dictionary<int, string> _blockReasons = new();
        private int _loadGeneration;
        private bool _loadingGameplay;
        private bool _background;
        private bool _suppressBackOnce;
        private InputActionAsset _frameworkActions;
        private InputActionAsset _gameplayActions;
        private ResourceLease<InputActionAsset> _gameplayLease;
        private InputActionRebindingExtensions.RebindingOperation _rebind;
        private UniTaskCompletionSource<bool> _rebindCompletion;
        private string _activeMap;
        private InputDeviceKind _activeDevice;
        private bool _hasGesturePair;
        private float _previousPinchDistance;
        private float _previousRotation;

        /// <inheritdoc />
        public override int Priority => -30;

        /// <inheritdoc />
        public InputActionAsset FrameworkActions => _frameworkActions;

        /// <inheritdoc />
        public InputDeviceKind ActiveDevice => _activeDevice;

        /// <inheritdoc />
        public IReadOnlyList<GamePointer> Pointers => _pointers;

        /// <inheritdoc />
        public event Action<InputDeviceKind> DeviceChanged;

        /// <inheritdoc />
        public event Action<GamePointerEvent> PointerEvent;

        /// <inheritdoc />
        public event Action<GameGestureEvent> GestureEvent;

        /// <inheritdoc />
        public override void OnInit()
        {
            InputActionAsset source = Resources.Load<InputActionAsset>("FrameworkInputActions");
            if (source == null)
            {
                LogError("固定动作资产 Resources/FrameworkInputActions 未找到。");
                return;
            }
            _frameworkActions = UnityEngine.Object.Instantiate(source);
            _frameworkActions.Enable();
            RegisterActions(_frameworkActions);
            RestoreOverrides(_frameworkActions, FrameworkStorageKey);
            EnhancedTouchSupport.Enable();
            GameEntry.ApplicationBackgroundChanged += OnApplicationBackgroundChanged;
            _background = GameEntry.IsInBackground;
            if (_background) SuppressHeldGameplayActions();
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            _consumed.Clear();
            _pointers.Clear();
            if (_background) return;
            RefreshActiveMap();
            bool blocked = GameplayBlocked;
            UpdatePointers(blocked);
            bool textFocus = HasTextFocus();
            _iterationActions.Clear();
            foreach (KeyValuePair<string, InputAction> entry in _actions) _iterationActions.Add(entry);
            for (int i = 0; i < _iterationActions.Count; i++)
            {
                KeyValuePair<string, InputAction> entry = _iterationActions[i];
                if (!_actions.TryGetValue(entry.Key, out InputAction live) || !ReferenceEquals(live, entry.Value)) continue;
                string id = entry.Key;
                InputAction action = entry.Value;
                if (action.actionMap.asset == _gameplayActions && action.WasPressedThisFrame() && IsUIOwnedControl(action))
                    _suppressUntilRelease.Add(id);
                if (action.actionMap.asset == _gameplayActions && action.activeControl?.device is Mouse &&
                    UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0 && action.IsPressed())
                    _suppressUntilRelease.Add(id);
                ActionFrame prior = _frames.TryGetValue(id, out ActionFrame previous) ? previous : default;
                bool suppressed = _suppressUntilRelease.Contains(id);
                if (suppressed && !action.IsPressed()) _suppressUntilRelease.Remove(id);
                bool allowed = !suppressed && IsAllowed(action, blocked, textFocus);
                bool pressed = allowed && action.IsPressed();
                bool started = allowed && action.WasPressedThisFrame();
                bool released = allowed && action.WasReleasedThisFrame();
                bool performed = allowed && action.triggered;
                if (!allowed && prior.Pressed) released = true;
                _frames[id] = new ActionFrame(pressed, started, released, performed);
                if (id == BackId && started && _suppressBackOnce)
                {
                    _suppressBackOnce = false;
                    _consumed.Add(id);
                    continue;
                }
                if (id == BackId && started && HandleBack())
                {
                    _consumed.Add(id);
                    continue;
                }
                if (allowed && started) Dispatch(id, GameActionPhase.Started);
                if (!_actions.TryGetValue(id, out live) || !ReferenceEquals(live, action)) continue;
                if (allowed && performed && IsAllowed(action, GameplayBlocked, HasTextFocus())) Dispatch(id, GameActionPhase.Performed);
                if (!_actions.TryGetValue(id, out live) || !ReferenceEquals(live, action)) continue;
                if (released) Dispatch(id, GameActionPhase.Canceled);
                if (allowed && (started || performed)) DetectDevice(action.activeControl?.device);
            }
            _suppressBackOnce = false;
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            GameEntry.ApplicationBackgroundChanged -= OnApplicationBackgroundChanged;
            CancelRebind();
            UnloadGameplayActions();
            _frameworkActions?.Disable();
            if (_frameworkActions != null) DestroyAsset(_frameworkActions);
            _frameworkActions = null;
            EnhancedTouchSupport.Disable();
            _actions.Clear();
            _iterationActions.Clear();
            _ids.Clear();
            _frames.Clear();
            _subscribers.Clear();
            _contexts.Clear();
            _blockGeneration++;
            _blockCount = 0;
            _blockReasons.Clear();
            _backHandlers.Clear();
            _consumed.Clear();
            _suppressUntilRelease.Clear();
            _suppressBackOnce = false;
            _pointers.Clear();
            _pointerTracks.Clear();
            _lastTapTimes.Clear();
            _lastTapPositions.Clear();
            DeviceChanged = null;
            PointerEvent = null;
            GestureEvent = null;
        }

        /// <inheritdoc />
        public async UniTask<bool> LoadGameplayActionsAsync(string location, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(location) || _gameplayActions != null || _loadingGameplay) return false;
            IResourceModule resources = GameEntry.Get<IResourceModule>();
            if (resources == null || !resources.IsInitialized) return false;

            int generation = ++_loadGeneration;
            _loadingGameplay = true;
            ResourceLease<InputActionAsset> lease = null;
            InputActionAsset clone = null;
            try
            {
                lease = await resources.AcquireAsync<InputActionAsset>(location, ResourceLoadOptions.Resident, ct: ct);
                if (generation != _loadGeneration || ct.IsCancellationRequested) return false;
                if (lease?.IsValid != true || lease.Asset == null)
                {
                    LogError($"玩法动作资产加载失败：{location}");
                    return false;
                }

                clone = UnityEngine.Object.Instantiate(lease.Asset);
                if (!ValidateGameplayActions(clone)) return false;
                RestoreOverrides(clone, GameplayStorageKey);
                clone.Enable();
                if (generation != _loadGeneration || ct.IsCancellationRequested) return false;
                RegisterActions(clone);
                _gameplayActions = clone;
                _gameplayLease = lease;
                clone = null;
                lease = null;
                if (_background) SuppressHeldGameplayActions();
                RefreshActiveMap();
                return true;
            }
            finally
            {
                if (clone != null)
                {
                    clone.Disable();
                    DestroyAsset(clone);
                }
                lease?.Dispose();
                if (generation == _loadGeneration) _loadingGameplay = false;
            }
        }

        /// <inheritdoc />
        public void UnloadGameplayActions()
        {
            _loadGeneration++;
            _loadingGameplay = false;
            CancelRebind();
            CancelGameplayPointers();
            _contexts.Clear();
            _activeMap = null;
            if (_gameplayActions != null)
            {
                _gameplayActions.Disable();
                foreach (InputActionMap map in _gameplayActions.actionMaps)
                    foreach (InputAction action in map.actions)
                    {
                        string id = map.name + "/" + action.name;
                        _ids.Remove(action.id.ToString());
                        _actions.Remove(id);
                        _frames.Remove(id);
                        _subscribers.Remove(id);
                        _suppressUntilRelease.Remove(id);
                    }
                DestroyAsset(_gameplayActions);
                _gameplayActions = null;
            }
            _gameplayLease?.Dispose();
            _gameplayLease = null;
        }

        /// <inheritdoc />
        public IDisposable PushContext(string map, int priority = 0)
        {
            if (string.IsNullOrWhiteSpace(map) || _gameplayActions?.FindActionMap(map, false) == null)
            {
                LogError($"输入上下文不存在：{map}");
                return new Handle(null);
            }
            var context = new InputContext(map, priority, ++_nextContextOrder);
            _contexts.Add(context);
            RefreshActiveMap();
            return new Handle(() => { _contexts.Remove(context); RefreshActiveMap(); });
        }

        /// <inheritdoc />
        public IDisposable BlockGameplay(string reason)
        {
            int id = ++_nextBlockId;
            int generation = _blockGeneration;
            _blockReasons.Add(id, string.IsNullOrWhiteSpace(reason) ? "Unspecified" : reason);
            _blockCount++;
            SuppressHeldGameplayActions();
            CancelGameplayPointers();
            return new Handle(() =>
            {
                if (generation != _blockGeneration || !_blockReasons.Remove(id)) return;
                _blockCount--;
            });
        }

        /// <inheritdoc />
        public InputButtonState GetButton(string id)
        {
            id = ResolveId(id);
            if (!CanRead(id) || !_frames.TryGetValue(id, out ActionFrame frame)) return default;
            return new InputButtonState(frame.Started, frame.Pressed, frame.Released);
        }

        /// <inheritdoc />
        public float GetAxis(string id)
        {
            id = ResolveId(id);
            if (!CanRead(id) || !_actions.TryGetValue(id, out InputAction action)) return 0f;
            return action.expectedControlType != "Vector2" && action.activeControl?.valueType != typeof(Vector2) ? action.ReadValue<float>() : 0f;
        }

        /// <inheritdoc />
        public Vector2 GetVector2(string id)
        {
            id = ResolveId(id);
            if (!CanRead(id) || !_actions.TryGetValue(id, out InputAction action)) return Vector2.zero;
            return action.expectedControlType == "Vector2" || action.activeControl?.valueType == typeof(Vector2) ? action.ReadValue<Vector2>() : Vector2.zero;
        }

        /// <inheritdoc />
        public IDisposable Subscribe(string id, Action<GameActionEvent> handler)
        {
            id = ResolveId(id);
            if (string.IsNullOrWhiteSpace(id) || handler == null) return new Handle(null);
            if (!_subscribers.TryGetValue(id, out List<Action<GameActionEvent>> handlers))
                _subscribers[id] = handlers = new List<Action<GameActionEvent>>();
            handlers.Add(handler);
            return new Handle(() => handlers.Remove(handler));
        }

        /// <inheritdoc />
        public IDisposable RegisterBackHandler(Func<bool> handler, int priority = 0)
        {
            if (handler == null) return new Handle(null);
            var registration = new BackHandler(handler, priority);
            _backHandlers.Add(registration);
            _backHandlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
            return new Handle(() => _backHandlers.Remove(registration));
        }

        /// <inheritdoc />
        public bool TryConsumeAction(string id)
        {
            id = ResolveId(id);
            if (string.IsNullOrWhiteSpace(id) || !CanRead(id)) return false;
            if (!_frames.TryGetValue(id, out ActionFrame frame) || (!frame.Started && !frame.Performed)) return false;
            return _consumed.Add(id);
        }

        /// <inheritdoc />
        public bool TryGetPointer(int pointerId, out GamePointer pointer)
        {
            for (int i = 0; i < _pointers.Count; i++)
                if (_pointers[i].Id == pointerId) { pointer = _pointers[i]; return true; }
            pointer = default;
            return false;
        }

        /// <inheritdoc />
        public bool TryGetWorldRay(Camera camera, int pointerId, out Ray ray)
        {
            if (camera != null && TryGetPointer(pointerId, out GamePointer pointer))
            {
                ray = camera.ScreenPointToRay(pointer.Position);
                return true;
            }
            ray = default;
            return false;
        }

        private bool GameplayBlocked => _background || GameEntry.IsGameplayPaused || _blockCount > 0 || GameEntry.Get<IUIModule>()?.BlocksGameplayInput == true;

        /// <summary>
        /// 登记资产中的路径和稳定 GUID。
        /// </summary>
        /// <param name="asset">动作资产。</param>
        private void RegisterActions(InputActionAsset asset)
        {
            foreach (InputActionMap map in asset.actionMaps)
                foreach (InputAction action in map.actions)
                {
                    string id = map.name + "/" + action.name;
                    _actions[id] = action;
                    _ids[action.id.ToString()] = id;
                }
        }

        /// <summary>
        /// 检查热更玩法动作不会覆盖固定系统与 UI 动作。
        /// </summary>
        /// <param name="asset">待启用的玩法动作资产。</param>
        /// <returns>映射和动作 ID 是否全部不冲突。</returns>
        private bool ValidateGameplayActions(InputActionAsset asset)
        {
            var actionIds = new HashSet<Guid>();
            foreach (InputActionMap map in asset.actionMaps)
            {
                if (string.Equals(map.name, "System", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(map.name, "UI", StringComparison.OrdinalIgnoreCase) ||
                    _frameworkActions?.FindActionMap(map.name, false) != null)
                {
                    LogError($"玩法动作映射与固定动作冲突：{map.name}");
                    return false;
                }
                foreach (InputAction action in map.actions)
                {
                    string path = map.name + "/" + action.name;
                    if (_actions.ContainsKey(path) || _ids.ContainsKey(action.id.ToString()) || !actionIds.Add(action.id))
                    {
                        LogError($"玩法动作 ID 与已注册动作冲突：{path}");
                        return false;
                    }
                }
            }
            return true;
        }
        /// <summary>
        /// 按优先级选择当前玩法动作映射。
        /// </summary>
        private void RefreshActiveMap()
        {
            InputContext best = null;
            foreach (InputContext context in _contexts)
                if (best == null || context.Priority > best.Priority ||
                    (context.Priority == best.Priority && context.Order > best.Order)) best = context;
            string next = best?.Map;
            if (_activeMap == next) return;
            CancelGameplayPointers();
            SuppressHeldGameplayActions();
            if (_gameplayActions != null) foreach (InputActionMap map in _gameplayActions.actionMaps)
                foreach (InputAction action in map.actions)
                    _frames.Remove(map.name + "/" + action.name);
            _activeMap = next;
        }

        /// <summary>
        /// 判断动作是否能进入玩法。
        /// </summary>
        /// <param name="action">待检查动作。</param>
        /// <param name="blocked">玩法是否被阻断。</param>
        /// <param name="textFocus">文本输入是否获得焦点。</param>
        /// <returns>动作是否可用。</returns>
        private bool IsAllowed(InputAction action, bool blocked, bool textFocus)
        {
            string map = action.actionMap.name;
            if (map == "System" || map == "UI") return true;
            if (blocked || map != _activeMap) return false;
            InputDevice device = action.activeControl?.device;

            return !textFocus || action.activeControl?.device is not Keyboard;
        }

        /// <summary>
        /// 判断动作当前是否由 UGUI 占用的指针触发。
        /// </summary>
        /// <param name="action">待检查动作。</param>
        /// <returns>动作控制是否归 UI。</returns>
        private bool IsUIOwnedControl(InputAction action)
        {
            InputDevice device = action.activeControl?.device;
            if (device is Mouse)
            {
                if (_pointerTracks.TryGetValue(0, out PointerTrack track)) return track.Captured;
                return Mouse.current != null && IsUIHit(Mouse.current.position.ReadValue());
            }
            if (device is Touchscreen)
            {
                if (UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0)
                {
                    int primaryId = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches[0].touchId + 1;
                    return _pointerTracks.TryGetValue(primaryId, out PointerTrack track) && track.Captured;
                }
            }
            return false;
        }

        /// <summary>
        /// 将动作 GUID 转为路径 ID。
        /// </summary>
        /// <param name="id">路径或 GUID 动作 ID。</param>
        /// <returns>规范化的动作 ID。</returns>
        private string ResolveId(string id) => id != null && _ids.TryGetValue(id, out string path) ? path : id;

        /// <summary>
        /// 判断当前帧是否允许读取动作。
        /// </summary>
        /// <param name="id">规范化动作 ID。</param>
        /// <returns>动作是否可读。</returns>
        private bool CanRead(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (_consumed.Contains(id) || _suppressUntilRelease.Contains(id) || !_actions.TryGetValue(id, out InputAction action)) return false;
            return IsAllowed(action, GameplayBlocked, HasTextFocus());
        }

        /// <summary>
        /// 向玩法订阅者派发动作事件。
        /// </summary>
        /// <param name="id">规范化动作 ID。</param>
        /// <param name="phase">动作阶段。</param>
        private void Dispatch(string id, GameActionPhase phase)
        {
            if (_consumed.Contains(id) || !_subscribers.TryGetValue(id, out List<Action<GameActionEvent>> handlers)) return;
            GameActionEvent message = new(id, phase);
            foreach (Action<GameActionEvent> handler in handlers.ToArray())
            {
                if (!_actions.TryGetValue(id, out InputAction current)) break;
                if (phase != GameActionPhase.Canceled && !IsAllowed(current, GameplayBlocked, HasTextFocus())) break;
                handler(message);
            }
        }

        /// <summary>
        /// 按优先级处理系统返回动作。
        /// </summary>
        /// <returns>返回动作是否已消费。</returns>
        private bool HandleBack()
        {
            if (_rebind != null) { CancelRebind(); return true; }
            foreach (BackHandler item in _backHandlers.ToArray()) if (item.Handler()) return true;
            return false;
        }

        /// <summary>
        /// 判断 UGUI 文本输入是否拥有焦点。
        /// </summary>
        /// <returns>文本输入是否拥有焦点。</returns>
        private static bool HasTextFocus()
        {
            GameObject selected = EventSystem.current?.currentSelectedGameObject;
            return selected != null && (selected.GetComponentInParent<InputField>() != null || selected.GetComponentInParent<TMP_InputField>() != null);
        }

        /// <summary>
        /// 更新最近输入设备类型。
        /// </summary>
        /// <param name="device">产生输入的设备。</param>
        private void DetectDevice(InputDevice device)
        {
            InputDeviceKind kind = device switch
            {
                Gamepad gamepad when HasOnScreenUsage(gamepad) => InputDeviceKind.Touch,
                Gamepad => InputDeviceKind.Gamepad,
                Touchscreen => InputDeviceKind.Touch,
                Mouse or Keyboard => InputDeviceKind.KeyboardMouse,
                _ => InputDeviceKind.Unknown
            };
            if (kind == InputDeviceKind.Unknown || kind == _activeDevice) return;
            _activeDevice = kind;
            DeviceChanged?.Invoke(kind);
        }

        /// <summary>
        /// 判断设备是否由触屏虚拟控件创建。
        /// </summary>
        /// <param name="device">待检查的输入设备。</param>
        /// <returns>设备是否标记为屏幕虚拟设备。</returns>
        private static bool HasOnScreenUsage(InputDevice device)
        {
            var usages = device.usages;
            for (int i = 0; i < usages.Count; i++)
                if (usages[i] == OnScreenUsage) return true;
            return false;
        }

        /// <summary>
        /// 进入或退出后台时取消玩法输入。
        /// </summary>
        /// <param name="isBackground">是否进入后台。</param>
        private void OnApplicationBackgroundChanged(bool isBackground)
        {
            _background = isBackground;
            SuppressHeldGameplayActions();
            _frames.Clear();
            _consumed.Clear();
            CancelGameplayPointers();
            _lastTapTimes.Clear();
            _lastTapPositions.Clear();
        }

        /// <summary>
        /// 要求当前按住的玩法动作先释放再重新按下。
        /// </summary>
        private void SuppressHeldGameplayActions()
        {
            if (_gameplayActions == null) return;
            foreach (InputActionMap map in _gameplayActions.actionMaps)
                foreach (InputAction action in map.actions)
                    if (action.IsPressed()) _suppressUntilRelease.Add(map.name + "/" + action.name);
        }

        /// <summary>
        /// 按编辑器运行状态销毁动作资产克隆。
        /// </summary>
        /// <param name="asset">待销毁资产。</param>
        private static void DestroyAsset(InputActionAsset asset)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(asset);
            else UnityEngine.Object.DestroyImmediate(asset);
        }

        /// <summary>
        /// 通过框架日志输出输入错误。
        /// </summary>
        /// <param name="message">错误说明。</param>
        private static void LogError(string message) => GameEntry.Get<ILogModule>()?.Error("Input", message);

        private readonly struct ActionFrame
        {
            internal readonly bool Pressed;
            internal readonly bool Started;
            internal readonly bool Released;
            internal readonly bool Performed;

            /// <summary>
            /// 创建一帧动作状态。
            /// </summary>
            /// <param name="pressed">当前是否按住。</param>
            /// <param name="started">本帧是否按下。</param>
            /// <param name="released">本帧是否松开。</param>
            /// <param name="performed">本帧是否执行。</param>
            internal ActionFrame(bool pressed, bool started, bool released, bool performed)
            {
                Pressed = pressed;
                Started = started;
                Released = released;
                Performed = performed;
            }
        }

        private sealed class InputContext
        {
            internal readonly string Map;
            internal readonly int Priority;
            internal readonly int Order;

            /// <summary>
            /// 创建输入焦点。
            /// </summary>
            /// <param name="map">动作映射名称。</param>
            /// <param name="priority">焦点优先级。</param>
            /// <param name="order">创建顺序。</param>
            internal InputContext(string map, int priority, int order)
            {
                Map = map;
                Priority = priority;
                Order = order;
            }
        }

        private sealed class BackHandler
        {
            internal readonly Func<bool> Handler;
            internal readonly int Priority;

            /// <summary>
            /// 创建返回动作处理器。
            /// </summary>
            /// <param name="handler">处理回调。</param>
            /// <param name="priority">处理优先级。</param>
            internal BackHandler(Func<bool> handler, int priority)
            {
                Handler = handler;
                Priority = priority;
            }
        }

        private sealed class Handle : IDisposable
        {
            private Action _dispose;

            /// <summary>
            /// 创建可释放句柄。
            /// </summary>
            /// <param name="dispose">释放回调。</param>
            internal Handle(Action dispose) { _dispose = dispose; }

            /// <summary>
            /// 释放句柄一次。
            /// </summary>
            public void Dispose()
            {
                Action action = _dispose;
                _dispose = null;
                action?.Invoke();
            }
        }
    }
}