using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 补间播放状态。
    /// </summary>
    public enum TweenState { Playing, Paused, Completed, Canceled, TargetDestroyed, Faulted }

    /// <summary>
    /// 单次播放的控制和等待句柄；终态保持可查询。
    /// </summary>
    public sealed class TweenHandle : IDisposable
    {
        private TweenModule _owner;
        private TweenClip _clip;
        private readonly bool _unscaledTime;
        private readonly UniTaskCompletionSource<TweenState> _completion = new();
        private ScopeRegistration _scopeRegistration;
        private CancellationTokenRegistration _scopeTokenRegistration;
        private CancellationTokenRegistration _externalTokenRegistration;
        private int _cancelRequested;
        private double _elapsed;
        private bool _evaluated;

        /// <summary>
        /// 创建一轮播放状态。
        /// </summary>
        /// <param name="owner">拥有此句柄的模块。</param><param name="clip">播放片段。</param>
        /// <param name="unscaledTime">是否使用非缩放时间。</param>
        internal TweenHandle(TweenModule owner, TweenClip clip, bool unscaledTime)
        {
            _owner = owner;
            _clip = clip;
            _unscaledTime = unscaledTime;
            State = TweenState.Playing;
        }

        /// <summary>
        /// 当前播放状态。
        /// </summary>
        public TweenState State { get; private set; }

        /// <summary>
        /// 是否仍由模块推进。
        /// </summary>
        public bool IsActive => State == TweenState.Playing || State == TweenState.Paused;

        /// <summary>
        /// 自然完成时调用，状态已变为完成。
        /// </summary>
        public event Action<TweenHandle> Completed;

        /// <summary>
        /// 取消、目标销毁或回调故障时调用，状态已变为终态。
        /// </summary>
        public event Action<TweenHandle> Stopped;

        /// <summary>
        /// 暂停时间推进。
        /// </summary>
        /// <returns>是否从播放状态成功暂停。</returns>
        public bool Pause()
        {
            if (State != TweenState.Playing) return false;
            State = TweenState.Paused;
            return true;
        }

        /// <summary>
        /// 恢复时间推进。
        /// </summary>
        /// <returns>是否从暂停状态成功恢复。</returns>
        public bool Resume()
        {
            if (State != TweenState.Paused) return false;
            State = TweenState.Playing;
            return true;
        }

        /// <summary>
        /// 取消播放，重复调用无效。
        /// </summary>
        /// <returns>是否取消了活动播放。</returns>
        public bool Cancel()
        {
            if (!IsActive) return false;
            Finish(TweenState.Canceled);
            return true;
        }

        /// <summary>
        /// 等待终态并返回完成、取消、目标销毁或故障原因。
        /// </summary>
        /// <returns>终态任务。</returns>
        public UniTask<TweenState> WaitAsync() => _completion.Task;

        /// <summary>
        /// 释放句柄时取消活动播放。
        /// </summary>
        public void Dispose() => Cancel();

        /// <summary>
        /// 关联作用域和外部取消令牌。
        /// </summary>
        /// <param name="scope">可选作用域。</param><param name="cancellationToken">外部取消令牌。</param>
        internal void Bind(LifetimeScope scope, CancellationToken cancellationToken)
        {
            if (scope != null)
            {
                if (!scope.TryOwn(this, out _scopeRegistration)) return;
                _scopeTokenRegistration = scope.Token.Register(RequestCancel);
                if (!IsActive) { _scopeTokenRegistration.Dispose(); return; }
                if (!scope.IsActive) RequestCancel();
            }
            if (!IsActive) return;
            if (cancellationToken.CanBeCanceled)
            {
                _externalTokenRegistration = cancellationToken.Register(RequestCancel);
                if (!IsActive) _externalTokenRegistration.Dispose();
            }
        }

        /// <summary>
        /// 推进一帧并检查绑定目标。
        /// </summary>
        /// <param name="scaledDelta">缩放时间增量。</param><param name="unscaledDelta">非缩放时间增量。</param>
        internal void Tick(float scaledDelta, float unscaledDelta)
        {
            if (!IsActive) return;
            if (Volatile.Read(ref _cancelRequested) != 0) { Cancel(); return; }
            if (!_clip.TargetsAlive()) { Finish(TweenState.TargetDestroyed); return; }
            if (State == TweenState.Paused) return;
            float delta = _unscaledTime ? unscaledDelta : scaledDelta;
            if (delta <= 0f && _evaluated && _clip.Duration > 0f) return;
            float previousTime = _evaluated ? (float)Math.Min(_elapsed, _clip.Duration) : -1f;
            _elapsed += delta;
            float time = (float)Math.Min(_elapsed, _clip.Duration);
            _evaluated = true;
            try { _clip.Evaluate(time, previousTime, this); }
            catch (Exception error)
            {
                _owner?.LogError(error);
                Finish(TweenState.Faulted);
                return;
            }
            if (CanWrite() && _elapsed >= _clip.Duration) Finish(TweenState.Completed);
        }

        /// <summary>
        /// 每次写入前重新检查取消状态和所有绑定目标。
        /// </summary>
        /// <returns>本轮是否仍允许写入。</returns>
        internal bool CanWrite()
        {
            if (!IsActive) return false;
            if (Volatile.Read(ref _cancelRequested) != 0) { Cancel(); return false; }
            if (!_clip.TargetsAlive()) { Finish(TweenState.TargetDestroyed); return false; }
            return true;
        }

        /// <summary>
        /// 从取消令牌请求停止，后台请求只标记并切回主线程。
        /// </summary>
        private void RequestCancel()
        {
            Interlocked.Exchange(ref _cancelRequested, 1);
            if (_owner == null) return;
            if (_owner.IsOwnerThread) Cancel();
            else UniTask.Post(() => { if (IsActive) Cancel(); });
        }

        /// <summary>
        /// 原子完成所有权清理并通知外部订阅者。
        /// </summary>
        /// <param name="state">完成原因。</param>
        private void Finish(TweenState state)
        {
            if (!IsActive) return;
            State = state;
            TweenModule owner = _owner;
            _owner = null;
            _clip = null;
            _scopeTokenRegistration.Dispose();
            _externalTokenRegistration.Dispose();
            _scopeRegistration?.Forget();
            _scopeRegistration = null;
            owner?.Finished();
            Action<TweenHandle> callback = state == TweenState.Completed ? Completed : Stopped;
            Completed = null;
            Stopped = null;
            if (callback != null)
            {
                foreach (Delegate subscriber in callback.GetInvocationList())
                {
                    try { ((Action<TweenHandle>)subscriber)(this); }
                    catch (Exception error) { owner?.LogError(error); }
                }
            }
            _completion.TrySetResult(state);
        }
    }
}
