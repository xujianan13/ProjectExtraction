using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 逻辑场景基类，每次进入建立独立作用域，退出时统一停止任务并释放业务登记。
    /// </summary>
    public abstract class SceneBase : IScene
    {
        private bool _isForceExiting;
        private bool _forceExitCompleted;

        /// <inheritdoc />
        public string Name { get; }
        /// <inheritdoc />
        public bool IsInited { get; protected set; }
        /// <inheritdoc />
        public bool IsPreloaded { get; protected set; }
        /// <inheritdoc />
        public bool IsEntering { get; protected set; }
        /// <inheritdoc />
        public bool IsExiting { get; protected set; }
        /// <inheritdoc />
        public bool IsActive { get; protected set; }

        /// <summary>
        /// 本轮进入的作用域；退出后保留已经结束的对象，下一次进入创建新作用域。
        /// </summary>
        public LifetimeScope SceneScope { get; private set; }

        /// <inheritdoc />
        public SceneState State
        {
            get
            {
                if (IsEntering) return SceneState.Entering;
                if (IsExiting) return SceneState.Exiting;
                if (IsActive) return SceneState.Running;
                return SceneState.Idle;
            }
        }

        /// <summary>
        /// 本轮场景取消令牌；开始退出即取消，退出回调应使用其独立传入的令牌。
        /// </summary>
        protected CancellationToken SceneToken => SceneScope?.Token ?? default;

        /// <summary>
        /// 创建逻辑场景实例。
        /// </summary>
        /// <param name="name">场景唯一名称。</param>
        protected SceneBase(string name) => Name = name;

        /// <summary>
        /// 执行一次性初始化。
        /// </summary>
        /// <param name="ct">初始化取消令牌。</param>
        /// <returns>初始化任务。</returns>
        public async UniTask InitSceneAsync(CancellationToken ct = default)
        {
            if (IsInited) return;
            await OnInitSceneAsync(ct);
            IsInited = true;
        }

        /// <summary>
        /// 初始化后执行一次预加载。
        /// </summary>
        /// <param name="ct">预加载取消令牌。</param>
        /// <returns>预加载任务。</returns>
        public async UniTask PreloadSceneAsync(CancellationToken ct = default)
        {
            await InitSceneAsync(ct);
            if (IsPreloaded) return;
            await OnPreloadSceneAsync(ct);
            IsPreloaded = true;
        }

        /// <summary>
        /// 为本轮进入建立作用域，进入失败时释放已经登记的内容。
        /// </summary>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">进入及本轮使用的取消令牌。</param>
        /// <returns>进入场景任务。</returns>
        public async UniTask EnterSceneAsync(SceneEnterData enterData = null, CancellationToken ct = default)
        {
            if (IsEntering || IsExiting || IsActive || _isForceExiting) return;
            var scope = new LifetimeScope($"Scene:{Name}", ct);
            SceneScope = scope;
            _forceExitCompleted = false;
            IsEntering = true;
            bool entered = false;
            try
            {
                scope.Token.ThrowIfCancellationRequested();
                await OnEnterSceneAsync(enterData, scope.Token);
                scope.Token.ThrowIfCancellationRequested();
                if (!ReferenceEquals(SceneScope, scope)) return;
                IsActive = true;
                entered = true;
            }
            finally
            {
                if (!entered) scope.Dispose();
                if (ReferenceEquals(SceneScope, scope)) IsEntering = false;
            }
        }

        /// <summary>
        /// 停止本轮工作，等待业务退出后释放登记；退出期间不再推进玩法更新。
        /// </summary>
        /// <param name="ct">独立的退出取消令牌。</param>
        /// <returns>场景退出任务。</returns>
        public async UniTask ExitSceneAsync(CancellationToken ct = default)
        {
            if (IsExiting || !IsActive || _isForceExiting) return;
            var scope = SceneScope;
            IsExiting = true;
            IsActive = false;
            bool exited = false;
            try
            {
                scope?.Stop();
                if (!ReferenceEquals(SceneScope, scope) || !IsExiting) return;
                await OnExitSceneAsync(ct);
                exited = true;
            }
            finally
            {
                scope?.Dispose();
                if (ReferenceEquals(SceneScope, scope))
                {
                    IsExiting = false;
                    if (exited) _forceExitCompleted = true;
                }
            }
        }

        /// <summary>
        /// 强制停止任务并清理本轮业务；回调失败仍然释放作用域和重置状态。
        /// </summary>
        public void ForceExit()
        {
            if (_isForceExiting || _forceExitCompleted) return;
            var scope = SceneScope;
            _isForceExiting = true;
            _forceExitCompleted = true;
            IsActive = false;
            try
            {
                scope?.Stop();
                OnForceExit();
            }
            finally
            {
                scope?.Dispose();
                IsEntering = false;
                IsExiting = false;
                _isForceExiting = false;
            }
        }

        /// <summary>
        /// 仅在本轮作用域有效且场景处于运行状态时推进玩法。
        /// </summary>
        /// <param name="deltaTime">本帧经过的游戏时间。</param>
        public void Update(float deltaTime)
        {
            if (!IsActive || IsExiting || SceneScope?.IsActive != true) return;
            OnUpdate(deltaTime);
        }

        /// <summary>
        /// 执行一次性初始化，默认不执行额外操作。
        /// </summary>
        /// <param name="ct">初始化取消令牌。</param>
        /// <returns>初始化任务。</returns>
        protected virtual UniTask OnInitSceneAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 在初始化之后预加载场景内容。
        /// </summary>
        /// <param name="ct">预加载取消令牌。</param>
        /// <returns>预加载任务。</returns>
        protected virtual UniTask OnPreloadSceneAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 创建本轮场景内容；订阅、计时器及租约登记到本轮场景作用域。
        /// </summary>
        /// <param name="enterData">进入场景的业务参数。</param>
        /// <param name="ct">本轮场景取消令牌。</param>
        /// <returns>进入任务。</returns>
        protected virtual UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 执行业务退出，可等待存档或对象池等异步收尾。
        /// </summary>
        /// <param name="ct">独立的退出取消令牌。</param>
        /// <returns>退出任务。</returns>
        protected virtual UniTask OnExitSceneAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 执行强制退出时的同步业务清理。
        /// </summary>
        protected virtual void OnForceExit() { }

        /// <summary>
        /// 推进本轮场景玩法。
        /// </summary>
        /// <param name="deltaTime">本帧经过的游戏时间。</param>
        protected virtual void OnUpdate(float deltaTime) { }
    }
}
