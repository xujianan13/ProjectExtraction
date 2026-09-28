using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 将模块操作绑定到生命周期作用域。
    /// </summary>
    public static class LifetimeScopeExtensions
    {
        /// <summary>
        /// 订阅无载荷事件。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="events">事件模块。</param>
        /// <param name="id">事件编号。</param>
        /// <param name="handler">业务回调。</param>
        /// <returns>提前退订句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration Subscribe(this LifetimeScope scope, IEventModule events, EventId id, Action handler)
        {
            RequireActiveArguments(scope, events, handler);
            if (!scope.IsActive) return null;
            Action wrapped = () => { if (scope.IsActive) handler(); };
            events.Subscribe(id, wrapped);
            return scope.Defer(() => events.Unsubscribe(id, wrapped));
        }

        /// <summary>
        /// 订阅带载荷事件。
        /// </summary>
        /// <typeparam name="T">事件载荷类型。</typeparam>
        /// <param name="scope">所有者。</param>
        /// <param name="events">事件模块。</param>
        /// <param name="id">事件编号。</param>
        /// <param name="handler">业务回调。</param>
        /// <returns>提前退订句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration Subscribe<T>(this LifetimeScope scope, IEventModule events, EventId id, Action<T> handler) where T : struct
        {
            RequireActiveArguments(scope, events, handler);
            if (!scope.IsActive) return null;
            Action<T> wrapped = value => { if (scope.IsActive) handler(value); };
            events.Subscribe(id, wrapped);
            return scope.Defer(() => events.Unsubscribe(id, wrapped));
        }

        /// <summary>
        /// 调度一次性计时器。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="timers">计时器模块。</param>
        /// <param name="delay">等待秒数。</param>
        /// <param name="callback">业务回调。</param>
        /// <param name="unscaledTime">是否使用非缩放时间。</param>
        /// <returns>提前取消句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration Once(this LifetimeScope scope, ITimerModule timers, float delay, Action callback, bool unscaledTime = false)
        {
            RequireActiveArguments(scope, timers, callback);
            if (!scope.IsActive) return null;
            ScopeRegistration registration = null;
            TimerHandle handle = timers.Once(delay, () =>
            {
                registration?.Forget();
                if (scope.IsActive) callback();
            }, unscaledTime);
            if (!handle.IsValid) return null;
            registration = scope.Defer(() => timers.Cancel(handle));
            return registration;
        }

        /// <summary>
        /// 调度循环计时器。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="timers">计时器模块。</param>
        /// <param name="interval">间隔秒数。</param>
        /// <param name="callback">业务回调。</param>
        /// <param name="repeatCount">执行次数，负一表示持续。</param>
        /// <param name="unscaledTime">是否使用非缩放时间。</param>
        /// <returns>提前取消句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration Loop(this LifetimeScope scope, ITimerModule timers, float interval, Action callback, int repeatCount = -1, bool unscaledTime = false)
        {
            RequireActiveArguments(scope, timers, callback);
            if (!scope.IsActive) return null;
            ScopeRegistration registration = null;
            int remaining = repeatCount;
            TimerHandle handle = timers.Loop(interval, () =>
            {
                if (remaining > 0 && --remaining == 0) registration?.Forget();
                if (scope.IsActive) callback();
            }, repeatCount, unscaledTime);
            if (!handle.IsValid) return null;
            registration = scope.Defer(() => timers.Cancel(handle));
            return registration;
        }

        /// <summary>
        /// 下一帧调度一次回调。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="timers">计时器模块。</param>
        /// <param name="callback">业务回调。</param>
        /// <param name="unscaledTime">是否在暂停时继续。</param>
        /// <returns>提前取消句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration NextFrame(this LifetimeScope scope, ITimerModule timers, Action callback, bool unscaledTime = false)
        {
            RequireActiveArguments(scope, timers, callback);
            if (!scope.IsActive) return null;
            ScopeRegistration registration = null;
            TimerHandle handle = timers.NextFrame(() =>
            {
                registration?.Forget();
                if (scope.IsActive) callback();
            }, unscaledTime);
            if (!handle.IsValid) return null;
            registration = scope.Defer(() => timers.Cancel(handle));
            return registration;
        }

        /// <summary>
        /// 订阅玩法输入动作。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="id">动作编号。</param>
        /// <param name="handler">业务回调。</param>
        /// <returns>提前退订句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration SubscribeInput(this LifetimeScope scope, IInputModule input, string id, Action<GameActionEvent> handler)
        {
            RequireActiveArguments(scope, input, handler);
            if (!scope.IsActive) return null;
            return OwnLatest(scope, input.Subscribe(id, value => { if (scope.IsActive) handler(value); }));
        }

        /// <summary>
        /// 订阅玩法指针事件。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="handler">业务回调。</param>
        /// <returns>提前退订句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration SubscribePointer(this LifetimeScope scope, IInputModule input, Action<GamePointerEvent> handler)
        {
            RequireActiveArguments(scope, input, handler);
            if (!scope.IsActive) return null;
            Action<GamePointerEvent> wrapped = value => { if (scope.IsActive) handler(value); };
            input.PointerEvent += wrapped;
            return scope.Defer(() => input.PointerEvent -= wrapped);
        }

        /// <summary>
        /// 订阅玩法手势事件。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="handler">业务回调。</param>
        /// <returns>提前退订句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration SubscribeGesture(this LifetimeScope scope, IInputModule input, Action<GameGestureEvent> handler)
        {
            RequireActiveArguments(scope, input, handler);
            if (!scope.IsActive) return null;
            Action<GameGestureEvent> wrapped = value => { if (scope.IsActive) handler(value); };
            input.GestureEvent += wrapped;
            return scope.Defer(() => input.GestureEvent -= wrapped);
        }

        /// <summary>
        /// 注册系统返回键处理。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="handler">业务处理器。</param>
        /// <param name="priority">优先级。</param>
        /// <returns>提前退订句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration RegisterBackHandler(this LifetimeScope scope, IInputModule input, Func<bool> handler, int priority = 0)
        {
            RequireActiveArguments(scope, input, handler);
            if (!scope.IsActive) return null;
            return OwnLatest(scope, input.RegisterBackHandler(() => scope.IsActive && handler(), priority));
        }

        /// <summary>
        /// 压入玩法输入上下文。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="map">动作映射。</param>
        /// <param name="priority">优先级。</param>
        /// <returns>提前退出句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration PushContext(this LifetimeScope scope, IInputModule input, string map, int priority = 0)
        {
            RequireActiveArguments(scope, input, map);
            return scope.IsActive ? OwnLatest(scope, input.PushContext(map, priority)) : null;
        }

        /// <summary>
        /// 临时阻断玩法输入。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="input">输入模块。</param>
        /// <param name="reason">阻断原因。</param>
        /// <returns>提前解除句柄，作用域失活时返回 null。</returns>
        public static ScopeRegistration BlockGameplay(this LifetimeScope scope, IInputModule input, string reason)
        {
            RequireActiveArguments(scope, input, reason);
            return scope.IsActive ? OwnLatest(scope, input.BlockGameplay(reason)) : null;
        }

        /// <summary>
        /// 加载并接管默认包中的资源租约。
        /// </summary>
        /// <typeparam name="T">Unity 资源类型。</typeparam>
        /// <param name="scope">所有者。</param>
        /// <param name="resources">资源模块。</param>
        /// <param name="location">资源地址。</param>
        /// <param name="options">加载设置。</param>
        /// <param name="progress">仍存活时接收进度。</param>
        /// <param name="ct">调用方令牌。</param>
        /// <returns>属于该作用域的资源租约。</returns>
        public static UniTask<ResourceLease<T>> AcquireAsync<T>(this LifetimeScope scope, IResourceModule resources, string location,
            ResourceLoadOptions options = default, IProgress<float> progress = null, CancellationToken ct = default) where T : UnityEngine.Object
        {
            RequireActiveArguments(scope, resources, location);
            return AcquireCore(scope, token => resources.AcquireAsync<T>(location, options, GuardProgress(scope, progress, ct), token), ct);
        }

        /// <summary>
        /// 加载并接管指定包中的资源租约。
        /// </summary>
        /// <typeparam name="T">Unity 资源类型。</typeparam>
        /// <param name="scope">所有者。</param>
        /// <param name="resources">资源模块。</param>
        /// <param name="packageName">资源包名。</param>
        /// <param name="location">资源地址。</param>
        /// <param name="options">加载设置。</param>
        /// <param name="progress">仍存活时接收进度。</param>
        /// <param name="ct">调用方令牌。</param>
        /// <returns>属于该作用域的资源租约。</returns>
        public static UniTask<ResourceLease<T>> AcquireAsync<T>(this LifetimeScope scope, IResourceModule resources, string packageName, string location,
            ResourceLoadOptions options = default, IProgress<float> progress = null, CancellationToken ct = default) where T : UnityEngine.Object
        {
            RequireActiveArguments(scope, resources, packageName);
            if (location == null) throw new ArgumentNullException(nameof(location));
            return AcquireCore(scope, token => resources.AcquireAsync<T>(packageName, location, options, GuardProgress(scope, progress, ct), token), ct);
        }

        /// <summary>
        /// 加载并接管资源组租约。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="resources">资源模块。</param>
        /// <param name="definition">资源组定义。</param>
        /// <param name="progress">仍存活时接收进度。</param>
        /// <param name="ct">调用方令牌。</param>
        /// <returns>属于该作用域的资源组租约。</returns>
        public static UniTask<ResourceGroupLease> AcquireGroupAsync(this LifetimeScope scope, IResourceModule resources, ResourceGroupDefinition definition,
            IProgress<float> progress = null, CancellationToken ct = default)
        {
            RequireActiveArguments(scope, resources, definition);
            return AcquireCore(scope, token => resources.AcquireGroupAsync(definition, GuardProgress(scope, progress, ct), token), ct);
        }

        /// <summary>
        /// 统一执行资源取得、迟到检查与租约登记。
        /// </summary>
        /// <typeparam name="T">可释放租约类型。</typeparam>
        /// <param name="scope">所有者。</param>
        /// <param name="load">资源加载委托。</param>
        /// <param name="ct">调用方令牌。</param>
        /// <returns>成功登记的租约。</returns>
        private static async UniTask<T> AcquireCore<T>(LifetimeScope scope, Func<CancellationToken, UniTask<T>> load, CancellationToken ct) where T : class, IDisposable
        {
            scope.CheckThread();
            if (!scope.IsActive || ct.IsCancellationRequested) throw new OperationCanceledException(ct.IsCancellationRequested ? ct : scope.Token);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(scope.Token, ct);
            T lease = await load(linked.Token);
            if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
            scope.CheckThread();
            if (lease == null)
            {
                if (!scope.IsActive || ct.IsCancellationRequested) throw new OperationCanceledException(ct.IsCancellationRequested ? ct : scope.Token);
                return null;
            }
            if (!scope.IsActive || ct.IsCancellationRequested)
            {
                LifetimeScope.CleanSafely(lease.Dispose);
                throw new OperationCanceledException(ct.IsCancellationRequested ? ct : scope.Token);
            }
            if (!scope.TryOwn(lease)) throw new OperationCanceledException(scope.Token);
            return lease;
        }

        /// <summary>
        /// 屏蔽作用域失活后的业务进度回调。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="progress">业务进度接收方。</param>
        /// <param name="ct">调用方令牌。</param>
        /// <returns>受存活状态约束的进度接收方。</returns>
        private static IProgress<float> GuardProgress(LifetimeScope scope, IProgress<float> progress, CancellationToken ct)
            => progress == null ? null : new InlineProgress(value => { if (PlayerLoopHelper.IsMainThread && scope.IsActive && !ct.IsCancellationRequested) progress.Report(value); });

        /// <summary>
        /// 接管模块返回的释放句柄。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="handle">模块句柄。</param>
        /// <returns>提前释放登记。</returns>
        private static ScopeRegistration OwnLatest(LifetimeScope scope, IDisposable handle)
        {
            scope.TryOwn(handle, out ScopeRegistration registration);
            return registration;
        }

        /// <summary>
        /// 验证扩展调用参数和线程。
        /// </summary>
        /// <param name="scope">所有者。</param>
        /// <param name="module">模块实例。</param>
        /// <param name="callback">业务回调或标识。</param>
        private static void RequireActiveArguments(LifetimeScope scope, object module, object callback)
        {
            if (scope == null) throw new ArgumentNullException(nameof(scope));
            scope.CheckThread();
            if (module == null) throw new ArgumentNullException(nameof(module));
            if (callback == null) throw new ArgumentNullException(nameof(callback));
        }

        private sealed class InlineProgress : IProgress<float>
        {
            private readonly Action<float> _report;

            /// <summary>
            /// 创建同步进度适配器。
            /// </summary>
            /// <param name="report">进度回调。</param>
            public InlineProgress(Action<float> report) => _report = report;

            /// <summary>
            /// 接收进度。
            /// </summary>
            /// <param name="value">进度值。</param>
            public void Report(float value) => _report(value);
        }
    }
}
