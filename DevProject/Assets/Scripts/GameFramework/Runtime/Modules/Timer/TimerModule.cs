using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 定时器模块默认实现。基于 OnUpdate 累加 deltaTime，无线程、兼容微信小游戏。
    /// 内部存储：List&lt;Entry&gt; + 单调递增 ID；取消用线性扫描标记 IsActive=false，OnUpdate 末尾统一 compact。
    /// </summary>
    public sealed class TimerModule : ModuleBase, ITimerModule
    {
        private const string Tag = "Timer";
        // interval 累减时的浮点容差，避免 1.0f - 10×0.1f 略小于 0 导致漏触发
        private const float IntervalEpsilon = 1e-5f;

        /// <inheritdoc />
        public override int Priority => -400;

        // 单调递增 ID，永不复用，避免句柄回收后误命中新定时器。从 1 开始，0 留作 TimerHandle.IsValid 的无效标志。
        private int _nextId = 1;

        private readonly List<Entry> _entries = new(32);
        private readonly Dictionary<int, Waiter> _waiters = new();
        private bool _shutdown;
        private ILogModule _log;

        /// <summary>
        /// 当前尚未取消或完成的定时器数量。
        /// </summary>
        public int ActiveCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _entries.Count; i++) if (_entries[i].IsActive) count++;
                return count;
            }
        }

        /// <inheritdoc />
        public override void OnInit()
        {
            _shutdown = false;
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            _shutdown = true;
            CancelAll();
            // Shutdown 后不会再进入 OnUpdate，立即释放回调引用，避免失效 Entry 滞留到模块实例回收。
            _entries.Clear();
            _log = null;
        }

        /// <inheritdoc />
        public TimerHandle Once(float delay, Action callback) => Once(delay, callback, false);

        /// <inheritdoc />
        public TimerHandle Once(float delay, Action callback, bool unscaledTime)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return Schedule(new Entry
            {
                Id = _nextId++,
                Interval = delay > 0f ? delay : 0f,
                Elapsed = 0f,
                RemainingCount = 1,
                Callback = callback,
                IsActive = true,
                UnscaledTime = unscaledTime,
            });
        }

        /// <inheritdoc />
        public TimerHandle Loop(float interval, Action callback, int repeatCount = -1) => Loop(interval, callback, repeatCount, false);

        /// <inheritdoc />
        public TimerHandle Loop(float interval, Action callback, int repeatCount, bool unscaledTime)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            if (interval <= 0f) throw new ArgumentOutOfRangeException(nameof(interval), "interval must be > 0");
            if (repeatCount == 0) throw new ArgumentOutOfRangeException(nameof(repeatCount), "repeatCount must be -1 or > 0");
            return Schedule(new Entry
            {
                Id = _nextId++,
                Interval = interval,
                Elapsed = 0f,
                RemainingCount = repeatCount, // -1 = forever
                Callback = callback,
                IsActive = true,
                UnscaledTime = unscaledTime,
            });
        }

        /// <inheritdoc />
        public TimerHandle NextFrame(Action callback) => NextFrame(callback, false);

        /// <inheritdoc />
        public TimerHandle NextFrame(Action callback, bool unscaledTime)
        {
            if (callback == null) throw new ArgumentNullException(nameof(callback));
            return Schedule(new Entry
            {
                Id = _nextId++,
                Interval = 0f,
                Elapsed = 0f,
                RemainingCount = 1,
                Callback = callback,
                IsActive = true,
                UnscaledTime = unscaledTime,
            });
        }

        /// <inheritdoc />
        public UniTask DelayAsync(float delay, CancellationToken ct = default) => DelayAsync(delay, ct, false);

        /// <inheritdoc />
        public UniTask DelayAsync(float delay, CancellationToken ct, bool unscaledTime)
            => WaitAsyncCore(delay, nextFrame: false, ct, unscaledTime);

        /// <inheritdoc />
        public UniTask NextFrameAsync(CancellationToken ct = default) => NextFrameAsync(ct, false);

        /// <inheritdoc />
        public UniTask NextFrameAsync(CancellationToken ct, bool unscaledTime)
            => WaitAsyncCore(0f, nextFrame: true, ct, unscaledTime);

        /// <inheritdoc />
        public bool Cancel(TimerHandle handle)
        {
            if (!handle.IsValid) return false;
            // 线性扫描：定时器规模通常较小，O(n) 足够；找到后只标记 IsActive=false，避免迭代期间结构修改。
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                if (e.Id != handle.Id) continue;
                if (!e.IsActive) return false;
                e.IsActive = false;
                _entries[i] = e;
                CancelWaiter(handle.Id);
                return true;
            }
            return false;
        }

        /// <inheritdoc />
        public void CancelAll()
        {
            // 整体清空。如果当前在 OnUpdate 派发中（理论上同步调用），后续的 compact 会处理空列表。
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                e.IsActive = false;
                _entries[i] = e;
            }
            var waiters = new List<Waiter>(_waiters.Values);
            _waiters.Clear();
            foreach (Waiter waiter in waiters) waiter.Completion.TrySetCanceled(waiter.Token);
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            // 快照 count：本帧期间通过回调新加入的 Entry 仅参与下一帧
            int count = _entries.Count;
            for (int i = 0; i < count; i++)
            {
                if (_shutdown || i >= _entries.Count) return;
                if (!_entries[i].IsActive) continue;

                var e = _entries[i];
                if ((GameEntry.IsGameplayPaused || Time.timeScale == 0f) && !e.UnscaledTime) continue;
                e.Elapsed += e.UnscaledTime ? Time.unscaledDeltaTime : deltaTime;

                // while 处理"一帧追多次"的边界（dt 大、interval 小时）；每次触发可能在回调里 Cancel 自己，所以触发后重读
                while (e.IsActive && e.Elapsed + IntervalEpsilon * e.Interval >= e.Interval)
                {
                    if (e.Interval > 0f)
                    {
                        e.Elapsed -= e.Interval;
                        if (e.Elapsed < 0f) e.Elapsed = 0f;
                    }
                    else e.Elapsed = 0f; // delay 为 0 的 Once：避免无限循环

                    if (e.RemainingCount > 0)
                    {
                        e.RemainingCount--;
                        if (e.RemainingCount == 0) e.IsActive = false;
                    }

                    // 写回再 invoke：让回调里读到的状态与外部一致；同时 Cancel 直接修改 _entries[i] 也能生效
                    _entries[i] = e;
                    var callback = e.Callback;
                    try { callback(); }
                    catch (Exception ex) { _log?.Error(Tag, $"Timer callback threw: {ex}"); }

                    // 回调内可能关闭模块或改变当前 Entry，重新读取前检查生命周期。
                    if (_shutdown) return;
                    e = _entries[i];
                }
                _entries[i] = e;
            }

            CompactInactive();
        }

        /// <summary>
        /// 将定时器加入当前模块生命周期。
        /// </summary>
        /// <param name="entry">待调度定时器。</param>
        /// <returns>可取消的定时器句柄。</returns>
        private TimerHandle Schedule(Entry entry)
        {
            if (_shutdown) throw new InvalidOperationException("TimerModule 已关闭。");
            _entries.Add(entry);
            return new TimerHandle(entry.Id);
        }

        /// <summary>
        /// 创建可随模块生命周期取消的异步等待。
        /// </summary>
        /// <param name="delay">等待秒数。</param>
        /// <param name="nextFrame">是否只等待下一帧。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <param name="unscaledTime">是否使用非缩放时间。</param>
        /// <returns>等待任务。</returns>
        private UniTask WaitAsyncCore(float delay, bool nextFrame, CancellationToken ct, bool unscaledTime)
        {
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);

            var waiter = new Waiter(ct);
            TimerHandle handle = default;
            Action complete = () => CompleteWaiter(handle.Id);
            handle = nextFrame ? NextFrame(complete, unscaledTime) : Once(delay, complete, unscaledTime);
            _waiters.Add(handle.Id, waiter);
            if (!ct.CanBeCanceled) return waiter.Completion.Task;

            CancellationTokenRegistration registration = ct.Register(() => Cancel(handle));
            return AwaitAndDisposeRegistrationAsync(waiter.Completion.Task, registration);
        }

        /// <summary>
        /// 完成指定定时器的等待任务。
        /// </summary>
        /// <param name="id">定时器标识。</param>
        private void CompleteWaiter(int id)
        {
            if (!_waiters.TryGetValue(id, out Waiter waiter)) return;
            _waiters.Remove(id);
            waiter.Completion.TrySetResult();
        }

        /// <summary>
        /// 取消指定定时器的等待任务。
        /// </summary>
        /// <param name="id">定时器标识。</param>
        private void CancelWaiter(int id)
        {
            if (!_waiters.TryGetValue(id, out Waiter waiter)) return;
            _waiters.Remove(id);
            waiter.Completion.TrySetCanceled(waiter.Token);
        }

        /// <summary>
        /// 等待定时任务结束，并释放调用方令牌的取消注册。
        /// </summary>
        /// <param name="task">定时器的等待任务。</param>
        /// <param name="registration">调用方令牌的取消注册。</param>
        /// <returns>完成或取消后已释放注册的等待任务。</returns>
        private static async UniTask AwaitAndDisposeRegistrationAsync(UniTask task, CancellationTokenRegistration registration)
        {
            try
            {
                await task;
            }
            finally
            {
                registration.Dispose();
            }
        }

        // 紧凑列表：把 IsActive 的 entry 顺序前移，丢弃尾部已失效项。仅在 OnUpdate 末尾调用，避免迭代期间结构修改。
        private void CompactInactive()
        {
            int write = 0;
            for (int read = 0; read < _entries.Count; read++)
            {
                if (!_entries[read].IsActive) continue;
                if (write != read) _entries[write] = _entries[read];
                write++;
            }
            if (write < _entries.Count)
                _entries.RemoveRange(write, _entries.Count - write);
        }

        // 用 struct 存储以减少 GC；调用回调时通过局部变量读出 Action，不引入额外装箱。
        private struct Entry
        {
            public int Id;
            public float Interval;
            public float Elapsed;
            public int RemainingCount; // -1 = forever, >0 = remaining fires, 0 = drained (then deactivated)
            public Action Callback;
            public bool IsActive;
            public bool UnscaledTime;
        }

        private sealed class Waiter
        {
            internal readonly UniTaskCompletionSource Completion = new();
            internal readonly CancellationToken Token;

            /// <summary>
            /// 创建等待记录。
            /// </summary>
            /// <param name="token">调用方取消令牌。</param>
            internal Waiter(CancellationToken token)
            {
                Token = token;
            }
        }
    }
}
