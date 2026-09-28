using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 框架入口：唯一的 MonoBehaviour 宿主，承载模块管理器的生命周期。
    /// 通过静态 API 暴露模块注册 / 获取，业务无需持有实例引用。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GameEntry : MonoBehaviour
    {
        private static GameEntry _instance;
        // 静态持有：保证 ModuleManager 在场景切换时不会被销毁
        private static readonly ModuleManager _manager = new();
        private static bool _ownsTimeScale;
        private static float _resumeTimeScale = 1f;
        private static bool _isStartingFramework;
        private static bool _isStoppingFramework;
        private static bool _stopRequested;
        private bool _isApplicationPaused;
        private bool _hasApplicationFocus = true;
        private bool _isInBackground;

        /// <summary>
        /// 应用是否因失去焦点或系统挂起而处于后台。
        /// </summary>
        public static bool IsInBackground => _instance != null && _instance._isInBackground;

        /// <summary>
        /// 应用前后台状态发生变化时通知模块；true 表示进入后台。
        /// </summary>
        public static event Action<bool> ApplicationBackgroundChanged;

        /// <summary>
        /// 框架模块是否已全部初始化并且尚未停止。
        /// </summary>
        public static bool IsRunning => _manager.IsInitialized;

        /// <summary>
        /// 本轮框架运行的应用作用域，在模块初始化前建立并在模块关闭前释放。
        /// </summary>
        public static LifetimeScope ApplicationScope { get; private set; }

        /// <summary>
        /// Unity 生命周期宿主是否已经创建。
        /// </summary>
        public static bool HasHost => _instance != null;

        /// <summary>
        /// 玩法是否因业务主动暂停或应用进入后台而停止推进。
        /// </summary>
        public static bool IsGameplayPaused => IsRunning && (IsInBackground ||
            Get<IGameplayModule>()?.IsPaused == true || Get<IGameplayModule>()?.IsApplicationPaused == true);

        /// <summary>
        /// 创建并标记 DontDestroyOnLoad 的 GameEntry 宿主。幂等：重复调用返回同一实例。
        /// </summary>
        /// <returns>唯一的框架宿主。</returns>
        public static GameEntry Bootstrap()
        {
            if (_instance != null) return _instance;
            var go = new GameObject("[GameEntry]");
            DontDestroyOnLoad(go);
            _instance = go.AddComponent<GameEntry>();
            return _instance;
        }

        /// <summary>
        /// 注册框架默认模块（显式 new，禁止反射）。已注册的接口类型不会被覆盖。幂等。
        /// 内容更新服务不属于游戏模块图，由 UpdateLogic 独立持有。
        /// </summary>
        public static void RegisterDefaults()
        {
            if (Get<ILogModule>() == null) Register<ILogModule>(new LogModule());
            if (Get<IDiagnosticsModule>() == null) Register<IDiagnosticsModule>(new DiagnosticsModule());
            if (Get<IEventModule>() == null) Register<IEventModule>(new EventModule());
            if (Get<ITimerModule>() == null) Register<ITimerModule>(new TimerModule());
            if (Get<ITweenModule>() == null) Register<ITweenModule>(new TweenModule());
            if (Get<IStorageModule>() == null) Register<IStorageModule>(new StorageModule());
            if (Get<ISaveModule>() == null) Register<ISaveModule>(new SaveModule());
            if (Get<IPoolModule>() == null) Register<IPoolModule>(new PoolModule());
            if (Get<IResourceModule>() == null) Register<IResourceModule>(new ResourceModule());
            if (Get<IPrefabPoolModule>() == null) Register<IPrefabPoolModule>(new PrefabPoolModule());
            if (Get<IInputModule>() == null) Register<IInputModule>(new InputModule());
            if (Get<IFlowModule>() == null) Register<IFlowModule>(new FlowModule());
            if (Get<IHttpModule>() == null) Register<IHttpModule>(new HttpModule());
            if (Get<INetworkModule>() == null) Register<INetworkModule>(new NetworkModule());
            if (Get<IAudioModule>() == null) Register<IAudioModule>(new AudioModule());
            if (Get<IModelModule>() == null) Register<IModelModule>(new ModelModule());
            if (Get<ISceneModule>() == null) Register<ISceneModule>(new SceneModule());
            if (Get<IGameplayModule>() == null) Register<IGameplayModule>(new GameplayModule());
            if (Get<IServiceModule>() == null) Register<IServiceModule>(new ServiceModule());
            if (Get<IUIModule>() == null) Register<IUIModule>(new UIModule());
        }

        /// <summary>
        /// 注册模块。须在 <see cref="StartFramework"/> 之前调用；同接口已注册则替换。
        /// 推荐显式指定模块接口（如 <c>Register&lt;ILogModule&gt;(new LogModule())</c>）；
        /// 省略泛型传入具体实例时，会自动解析唯一的 IModule 子接口作为 Key。
        /// </summary>
        public static T Register<T>(T module) where T : class, IModule => _manager.Register(module);

        /// <summary>
        /// 获取已注册的模块；未注册返回 null。
        /// </summary>
        public static T Get<T>() where T : class, IModule => _manager.Get<T>();

        /// <summary>
        /// 启动框架：按优先级初始化所有已注册模块。注册完成后调用一次。
        /// </summary>
        public static void StartFramework()
        {
            if (IsRunning || _isStartingFramework || _isStoppingFramework) return;
            _isStartingFramework = true;
            var scope = new LifetimeScope("Application");
            ApplicationScope = scope;
            scope.Defer(CompleteFrameworkStop);
            try
            {
                _manager.InitAll(scope.Dispose);
                Get<IGameplayModule>()?.SetApplicationPaused(IsInBackground);
                Get<IAudioModule>()?.SetApplicationPaused(IsInBackground);
                RefreshGameplayPause();
            }
            finally
            {
                if (!_manager.IsInitialized) scope.Dispose();
                _isStartingFramework = false;
                if (_stopRequested) StopFramework();
            }
        }

        /// <summary>
        /// 停止框架并按依赖反序卸载全部模块，恢复框架持有的时间缩放。
        /// </summary>
        public static void StopFramework()
        {
            if (_isStoppingFramework) return;
            if (_isStartingFramework)
            {
                _stopRequested = true;
                ApplicationScope?.Stop();
                return;
            }
            _isStoppingFramework = true;
            ApplicationScope?.Dispose();
            if (ApplicationScope == null || ApplicationScope.State == LifetimeScopeState.Disposed) CompleteFrameworkStop();
        }

        /// <summary>
        /// 作为应用作用域最后一项清理卸载模块，确保子级回调重入关闭时也先完成业务资源释放。
        /// </summary>
        private static void CompleteFrameworkStop()
        {
            if (!_isStoppingFramework) return;
            try
            {
                _manager.ShutdownAll();
                RefreshGameplayPause();
                ApplicationBackgroundChanged = null;
            }
            finally
            {
                _stopRequested = false;
                _isStoppingFramework = false;
            }
        }

        /// <summary>
        /// 将暂停原因应用到 Unity 缩放时间；首次暂停保存原速度，全部原因解除后恢复。
        /// </summary>
        internal static void RefreshGameplayPause()
        {
            if (IsGameplayPaused)
            {
                if (!_ownsTimeScale) _resumeTimeScale = Time.timeScale;
                _ownsTimeScale = true;
                Time.timeScale = 0f;
            }
            else if (_ownsTimeScale)
            {
                Time.timeScale = _resumeTimeScale;
                _ownsTimeScale = false;
            }
        }

        /// <summary>
        /// 驱动模块更新，系统模块继续运行，玩法模块自行遵守统一暂停状态。
        /// </summary>
        private void Update()
        {
            RefreshGameplayPause();
            _manager.UpdateAll(IsGameplayPaused ? 0f : Time.deltaTime);
        }

        /// <summary>
        /// 驱动固定帧模块；玩法模块自行检查暂停状态。
        /// </summary>
        private void FixedUpdate()
        {
            RefreshGameplayPause();
            _manager.FixedUpdateAll(IsGameplayPaused ? 0f : Time.fixedDeltaTime);
        }

        /// <summary>
        /// 驱动帧末模块；玩法模块自行检查暂停状态。
        /// </summary>
        private void LateUpdate()
        {
            RefreshGameplayPause();
            _manager.LateUpdateAll(IsGameplayPaused ? 0f : Time.deltaTime);
        }

        /// <summary>
        /// 接收 Unity 的系统挂起通知。
        /// </summary>
        /// <param name="paused">应用是否被挂起。</param>
        private void OnApplicationPause(bool paused)
        {
            _isApplicationPaused = paused;
            UpdateBackgroundState();
        }

        /// <summary>
        /// 接收 Unity 的窗口焦点通知。
        /// </summary>
        /// <param name="hasFocus">应用是否具有焦点。</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            _hasApplicationFocus = hasFocus;
            UpdateBackgroundState();
        }

        /// <summary>
        /// 合并系统挂起和焦点状态，且只在实际变化时通知模块。
        /// </summary>
        private void UpdateBackgroundState()
        {
            bool inBackground = _isApplicationPaused || !_hasApplicationFocus;
            if (_isInBackground == inBackground) return;
            _isInBackground = inBackground;
            Get<IGameplayModule>()?.SetApplicationPaused(inBackground);
            Get<IAudioModule>()?.SetApplicationPaused(inBackground);
            RefreshGameplayPause();
            ApplicationBackgroundChanged?.Invoke(inBackground);
        }

        /// <summary>
        /// 应用退出时释放宿主持有的框架。
        /// </summary>
        private void OnApplicationQuit() => OnDestroy();

        /// <summary>
        /// 宿主被直接销毁时也关闭模块，避免留下静态注册状态。
        /// </summary>
        private void OnDestroy()
        {
            if (_instance != this) return;
            StopFramework();
            _instance = null;
        }
    }
}
