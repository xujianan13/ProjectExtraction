using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 玩法根节点基类。一个 Root 表示一个可独立运行的玩法实例，内部管理局部功能模块和数据模型。
    /// </summary>
    public abstract class GameplayRoot : IGameplay, IFixedUpdateGameplay, ILateUpdateGameplay
    {
        private readonly List<GameplayFeature> _features = new(16);
        private readonly Dictionary<Type, GameplayFeature> _featureDict = new(16);
        private readonly List<GameplayModel> _models = new(16);
        private readonly Dictionary<Type, GameplayModel> _modelDict = new(16);
        private int _lifecycleVersion;
        private bool _isForceExiting;
        private bool _isDestroying;
        private bool _destroyRequested;

        /// <summary>
        /// 创建玩法根节点。
        /// </summary>
        /// <param name="name">玩法唯一名称；为空时使用类型名。</param>
        protected GameplayRoot(string name = null)
        {
            Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public GameplayState State { get; private set; }

        /// <inheritdoc />
        public bool IsInited { get; private set; }

        /// <inheritdoc />
        public bool IsEntering { get; private set; }

        /// <inheritdoc />
        public bool IsExiting { get; private set; }

        /// <inheritdoc />
        public bool IsActive { get; private set; }

        /// <inheritdoc />
        public bool IsDestroyed { get; private set; }

        /// <summary>
        /// 本轮玩法的所有权作用域；初始化阶段为空，退出后保留已释放的作用域，重新进入时创建新作用域。
        /// </summary>
        public LifetimeScope GameplayScope { get; private set; }

        /// <summary>
        /// 本轮玩法取消令牌；开始退出即取消，退出回调应使用其独立传入的令牌。
        /// </summary>
        protected CancellationToken GameplayToken => GameplayScope?.Token ?? default;

        /// <summary>
        /// 添加玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <returns>添加到玩法的功能模块。</returns>
        public T AddFeature<T>() where T : GameplayFeature, new()
        {
            return AddFeature(new T());
        }

        /// <summary>
        /// 添加玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <param name="feature">功能模块实例。</param>
        /// <exception cref="ArgumentNullException">feature 为 null。</exception>
        /// <exception cref="InvalidOperationException">玩法已进入运行期或重复添加同类型功能模块。</exception>
        /// <returns>添加到玩法的功能模块。</returns>
        public T AddFeature<T>(T feature) where T : GameplayFeature
        {
            if (feature == null) throw new ArgumentNullException(nameof(feature));
            EnsureCanAddPart();

            Type type = typeof(T);
            if (_featureDict.ContainsKey(type))
                throw new InvalidOperationException($"Gameplay feature {type.Name} already exists.");

            feature.Attach(this);
            _features.Add(feature);
            _featureDict.Add(type, feature);
            return feature;
        }

        /// <summary>
        /// 获取玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <returns>对应的功能模块；未找到时为空。</returns>
        public T GetFeature<T>() where T : GameplayFeature
        {
            return _featureDict.TryGetValue(typeof(T), out var feature) ? (T)feature : null;
        }

        /// <summary>
        /// 添加玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <returns>添加到玩法的数据模型。</returns>
        public T AddModel<T>() where T : GameplayModel, new()
        {
            return AddModel(new T());
        }

        /// <summary>
        /// 添加玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <param name="model">数据模型实例。</param>
        /// <exception cref="ArgumentNullException">model 为 null。</exception>
        /// <exception cref="InvalidOperationException">玩法已进入运行期或重复添加同类型数据模型。</exception>
        /// <returns>添加到玩法的数据模型。</returns>
        public T AddModel<T>(T model) where T : GameplayModel
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            EnsureCanAddPart();

            Type type = typeof(T);
            if (_modelDict.ContainsKey(type))
                throw new InvalidOperationException($"Gameplay model {type.Name} already exists.");

            model.Attach(this);
            _models.Add(model);
            _modelDict.Add(type, model);
            return model;
        }

        /// <summary>
        /// 获取玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <returns>对应的数据模型；未找到时为空。</returns>
        public T GetModel<T>() where T : GameplayModel
        {
            return _modelDict.TryGetValue(typeof(T), out var model) ? (T)model : null;
        }

        /// <inheritdoc />
        /// <param name="ct">取消令牌。</param>
        /// <returns>初始化玩法任务。</returns>
        public async UniTask InitGameplayAsync(CancellationToken ct = default)
        {
            ThrowIfDestroyed();
            if (IsInited) return;
            if (State != GameplayState.Idle)
                throw new InvalidOperationException($"Gameplay {Name} cannot init from {State}.");

            State = GameplayState.Initializing;
            var version = _lifecycleVersion;
            try
            {
                ThrowIfLifecycleInterrupted(version, ct);
                await OnInitGameplayAsync(ct);
                ThrowIfLifecycleInterrupted(version, ct);
                for (int i = 0; i < _features.Count; i++)
                {
                    await _features[i].InitAsync(ct);
                    ThrowIfLifecycleInterrupted(version, ct);
                }
                for (int i = 0; i < _models.Count; i++)
                {
                    await _models[i].InitAsync(ct);
                    ThrowIfLifecycleInterrupted(version, ct);
                }

                IsInited = true;
                State = GameplayState.Ready;
            }
            catch
            {
                if (!IsDestroyed && version == _lifecycleVersion) State = GameplayState.Idle;
                throw;
            }
        }

        /// <summary>
        /// 初始化后为本轮玩法建立作用域；进入失败或取消时清理本轮所有权。
        /// </summary>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">进入和本轮玩法使用的取消令牌。</param>
        /// <returns>进入玩法任务。</returns>
        public async UniTask EnterGameplayAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
        {
            ThrowIfDestroyed();
            if (IsEntering || IsActive || IsExiting || _isForceExiting || _isDestroying) return;
            await InitGameplayAsync(ct);
            ThrowIfDestroyed();
            if (IsEntering || IsActive || IsExiting || _isForceExiting || _isDestroying) return;
            if (State != GameplayState.Ready)
                throw new InvalidOperationException($"Gameplay {Name} cannot enter from {State}.");
            if (GameplayScope != null && GameplayScope.State != LifetimeScopeState.Disposed) return;

            var scope = new LifetimeScope($"Gameplay:{Name}", ct);
            GameplayScope = scope;
            IsEntering = true;
            State = GameplayState.Entering;
            int version = _lifecycleVersion;
            bool entered = false;
            try
            {
                var token = scope.Token;
                ThrowIfLifecycleInterrupted(version, token);
                for (int i = 0; i < _features.Count; i++)
                {
                    await _features[i].EnterAsync(enterData, token);
                    ThrowIfLifecycleInterrupted(version, token);
                }
                for (int i = 0; i < _models.Count; i++)
                {
                    await _models[i].EnterAsync(enterData, token);
                    ThrowIfLifecycleInterrupted(version, token);
                }

                await OnEnterGameplayAsync(enterData, token);
                ThrowIfLifecycleInterrupted(version, token);
                IsActive = true;
                State = GameplayState.Running;
                entered = true;
            }
            catch
            {
                if (IsCurrentLifecycle(scope, version)) ForceExit();
                throw;
            }
            finally
            {
                if (!entered) scope.Dispose();
                if (IsCurrentLifecycle(scope, version)) IsEntering = false;
            }
        }

        /// <summary>
        /// 先停止本轮任务和更新，等待业务退出后再释放登记；旧退出任务不会影响新一轮玩法。
        /// </summary>
        /// <param name="ct">独立的退出取消令牌。</param>
        /// <returns>退出玩法任务。</returns>
        public async UniTask ExitGameplayAsync(CancellationToken ct = default)
        {
            ThrowIfDestroyed();
            if (IsExiting || !IsActive || _isForceExiting || _isDestroying) return;
            var scope = GameplayScope;
            int version = _lifecycleVersion;
            IsExiting = true;
            IsActive = false;
            State = GameplayState.Exiting;
            try
            {
                scope?.Stop();
                if (!IsCurrentLifecycle(scope, version) || !IsExiting) return;
                await OnExitGameplayAsync(ct);
                if (!IsCurrentLifecycle(scope, version)) return;
                for (int i = _models.Count - 1; i >= 0; i--)
                {
                    await _models[i].ExitAsync(ct);
                    if (!IsCurrentLifecycle(scope, version)) return;
                }
                for (int i = _features.Count - 1; i >= 0; i--)
                {
                    await _features[i].ExitAsync(ct);
                    if (!IsCurrentLifecycle(scope, version)) return;
                }
            }
            catch
            {
                if (IsCurrentLifecycle(scope, version)) ForceExit();
                throw;
            }
            finally
            {
                scope?.Dispose();
                if (IsCurrentLifecycle(scope, version))
                {
                    IsExiting = false;
                    State = GameplayState.Ready;
                }
            }
        }

        /// <summary>
        /// 同步停止本轮工作并逆序清理业务；单个回调失败会记录日志并继续释放其余对象。
        /// </summary>
        public void ForceExit()
        {
            if (IsDestroyed || _isForceExiting) return;
            _lifecycleVersion++;
            bool needsCleanup = IsActive || IsEntering || IsExiting;
            var scope = GameplayScope;
            _isForceExiting = true;
            IsActive = false;
            IsEntering = false;
            IsExiting = false;
            try
            {
                scope?.Stop();
                if (needsCleanup)
                {
                    LifetimeScope.CleanSafely(OnForceExit);
                    for (int i = _models.Count - 1; i >= 0; i--) LifetimeScope.CleanSafely(_models[i].ForceExit);
                    for (int i = _features.Count - 1; i >= 0; i--) LifetimeScope.CleanSafely(_features[i].ForceExit);
                }
            }
            finally
            {
                scope?.Dispose();
                State = IsInited ? GameplayState.Ready : GameplayState.Idle;
                _isForceExiting = false;
                if (_destroyRequested && !_isDestroying) DestroyGameplay();
            }
        }

        /// <summary>
        /// 强制结束本轮使用并销毁局部对象；清理回调中的重复销毁会合并到当前流程。
        /// </summary>
        public void DestroyGameplay()
        {
            if (IsDestroyed || _isDestroying) return;
            if (_isForceExiting)
            {
                _destroyRequested = true;
                return;
            }
            _isDestroying = true;
            try
            {
                ForceExit();
                for (int i = _models.Count - 1; i >= 0; i--) LifetimeScope.CleanSafely(_models[i].Destroy);
                for (int i = _features.Count - 1; i >= 0; i--) LifetimeScope.CleanSafely(_features[i].Destroy);
                LifetimeScope.CleanSafely(OnDestroyGameplay);
            }
            finally
            {
                _models.Clear();
                _modelDict.Clear();
                _features.Clear();
                _featureDict.Clear();
                IsInited = false;
                IsActive = false;
                IsEntering = false;
                IsExiting = false;
                IsDestroyed = true;
                State = GameplayState.Destroyed;
                _destroyRequested = false;
                _isDestroying = false;
            }
        }

        /// <summary>
        /// 仅在本轮作用域活跃时更新；任一局部对象退出玩法后，不再继续派发本轮更新。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public void Update(float deltaTime)
        {
            var scope = GameplayScope;
            int version = _lifecycleVersion;
            if (!CanUpdate(scope, version)) return;
            for (int i = 0; i < _models.Count; i++)
            {
                _models[i].Update(deltaTime);
                if (!CanUpdate(scope, version)) return;
            }
            for (int i = 0; i < _features.Count; i++)
            {
                _features[i].Update(deltaTime);
                if (!CanUpdate(scope, version)) return;
            }
            OnUpdateGameplay(deltaTime);
        }

        /// <summary>
        /// 固定帧按 Model、Feature、Root 顺序更新本轮玩法。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        public void FixedUpdate(float deltaTime)
        {
            var scope = GameplayScope;
            int version = _lifecycleVersion;
            if (!CanUpdate(scope, version)) return;
            for (int i = 0; i < _models.Count; i++)
            {
                _models[i].FixedUpdate(deltaTime);
                if (!CanUpdate(scope, version)) return;
            }
            for (int i = 0; i < _features.Count; i++)
            {
                _features[i].FixedUpdate(deltaTime);
                if (!CanUpdate(scope, version)) return;
            }
            OnFixedUpdateGameplay(deltaTime);
        }

        /// <summary>
        /// 帧末按 Model、Feature、Root 顺序更新本轮玩法。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public void LateUpdate(float deltaTime)
        {
            var scope = GameplayScope;
            int version = _lifecycleVersion;
            if (!CanUpdate(scope, version)) return;
            for (int i = 0; i < _models.Count; i++)
            {
                _models[i].LateUpdate(deltaTime);
                if (!CanUpdate(scope, version)) return;
            }
            for (int i = 0; i < _features.Count; i++)
            {
                _features[i].LateUpdate(deltaTime);
                if (!CanUpdate(scope, version)) return;
            }
            OnLateUpdateGameplay(deltaTime);
        }

        /// <summary>
        /// 初始化玩法，默认空实现。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>初始化任务。</returns>
        protected virtual UniTask OnInitGameplayAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 进入玩法，默认空实现。
        /// </summary>
        /// <param name="enterData">进入参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>进入任务。</returns>
        protected virtual UniTask OnEnterGameplayAsync(GameplayEnterData enterData, CancellationToken ct)
            => UniTask.CompletedTask;

        /// <summary>
        /// 退出玩法，默认空实现。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>退出任务。</returns>
        protected virtual UniTask OnExitGameplayAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 强制退出时的同步清理。
        /// </summary>
        protected virtual void OnForceExit() { }

        /// <summary>
        /// 销毁玩法，默认空实现。
        /// </summary>
        protected virtual void OnDestroyGameplay() { }

        /// <summary>
        /// 每帧更新，默认空实现。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        protected virtual void OnUpdateGameplay(float deltaTime) { }

        /// <summary>
        /// 玩法固定帧更新，默认空实现。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        protected virtual void OnFixedUpdateGameplay(float deltaTime) { }

        /// <summary>
        /// 玩法帧末更新，默认空实现。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        protected virtual void OnLateUpdateGameplay(float deltaTime) { }

        /// <summary>
        /// 限制局部对象只能在构造或初始化阶段添加。
        /// </summary>
        private void EnsureCanAddPart()
        {
            if (!_isDestroying && !_isForceExiting && (State == GameplayState.Idle || State == GameplayState.Initializing)) return;
            throw new InvalidOperationException($"Gameplay {Name} cannot add parts from {State}.");
        }

        /// <summary>
        /// 检查异步生命周期操作是否已被取消或销毁。
        /// </summary>
        /// <param name="version">操作开始时的生命周期版本。</param>
        /// <param name="ct">操作的取消令牌。</param>
        private void ThrowIfLifecycleInterrupted(int version, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (IsDestroyed || version != _lifecycleVersion)
                throw new OperationCanceledException(ct);
        }

        /// <summary>
        /// 拒绝复用已经销毁的玩法实例。
        /// </summary>
        private void ThrowIfDestroyed()
        {
            if (IsDestroyed)
                throw new InvalidOperationException($"Gameplay {Name} has been destroyed.");
        }

        /// <summary>
        /// 判断异步流程是否仍属于当前未销毁的玩法轮次。
        /// </summary>
        /// <param name="scope">发起流程时捕获的作用域。</param>
        /// <param name="version">发起流程时的生命周期版本。</param>
        /// <returns>流程是否仍属于当前轮次。</returns>
        private bool IsCurrentLifecycle(LifetimeScope scope, int version)
            => !IsDestroyed && version == _lifecycleVersion && ReferenceEquals(GameplayScope, scope);

        /// <summary>
        /// 判断本轮玩法是否仍可接收帧更新。
        /// </summary>
        /// <param name="scope">本帧开始时的作用域。</param>
        /// <param name="version">本帧开始时的生命周期版本。</param>
        /// <returns>玩法和作用域是否均处于运行状态。</returns>
        private bool CanUpdate(LifetimeScope scope, int version)
            => IsActive && !IsExiting && !GameEntry.IsGameplayPaused && scope?.IsActive == true && IsCurrentLifecycle(scope, version);

    }

    /// <summary>
    /// 强类型玩法根节点基类。业务玩法继承此类后，Feature / Model 可直接获得具体 Root 类型。
    /// </summary>
    /// <typeparam name="TRoot">具体玩法根节点类型。</typeparam>
    public abstract class GameplayRoot<TRoot> : GameplayRoot where TRoot : GameplayRoot<TRoot>
    {
        /// <summary>
        /// 创建强类型玩法根节点。
        /// </summary>
        /// <param name="name">玩法唯一名称；为空时使用类型名。</param>
        protected GameplayRoot(string name = null) : base(name)
        {
        }

        /// <summary>
        /// 当前具体玩法根节点。
        /// </summary>
        protected TRoot Self => (TRoot)this;

        /// <summary>
        /// 添加强类型玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <returns>添加到玩法的功能模块。</returns>
        public new T AddFeature<T>() where T : GameplayFeature<TRoot>, new()
        {
            return AddFeature(new T());
        }

        /// <summary>
        /// 添加强类型玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <param name="feature">功能模块实例。</param>
        /// <returns>添加到玩法的功能模块。</returns>
        public new T AddFeature<T>(T feature) where T : GameplayFeature<TRoot>
        {
            return base.AddFeature(feature);
        }

        /// <summary>
        /// 获取强类型玩法内功能模块。
        /// </summary>
        /// <typeparam name="T">功能模块类型。</typeparam>
        /// <returns>对应的功能模块；未找到时为空。</returns>
        public new T GetFeature<T>() where T : GameplayFeature<TRoot>
        {
            return base.GetFeature<T>();
        }

        /// <summary>
        /// 添加强类型玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <returns>添加到玩法的数据模型。</returns>
        public new T AddModel<T>() where T : GameplayModel<TRoot>, new()
        {
            return AddModel(new T());
        }

        /// <summary>
        /// 添加强类型玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <param name="model">数据模型实例。</param>
        /// <returns>添加到玩法的数据模型。</returns>
        public new T AddModel<T>(T model) where T : GameplayModel<TRoot>
        {
            return base.AddModel(model);
        }

        /// <summary>
        /// 获取强类型玩法内数据模型。
        /// </summary>
        /// <typeparam name="T">数据模型类型。</typeparam>
        /// <returns>对应的数据模型；未找到时为空。</returns>
        public new T GetModel<T>() where T : GameplayModel<TRoot>
        {
            return base.GetModel<T>();
        }
    }
}
