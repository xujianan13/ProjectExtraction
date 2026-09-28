using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 在框架更新循环内推进补间的默认模块。
    /// </summary>
    public sealed class TweenModule : ModuleBase, ITweenModule
    {
        private readonly List<TweenHandle> _handles = new(32);
        private int _threadId;
        private int _activeCount;
        private bool _shutdown;
        private ILogModule _log;

        /// <inheritdoc />
        public override int Priority => -350;

        /// <inheritdoc />
        public int ActiveCount => _activeCount;

        /// <summary>
        /// 当前调用是否来自模块所属线程。
        /// </summary>
        internal bool IsOwnerThread => Thread.CurrentThread.ManagedThreadId == _threadId;

        /// <inheritdoc />
        public override void OnInit()
        {
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _shutdown = false;
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            _shutdown = true;
            CancelAll();
            _handles.Clear();
            _log = null;
        }

        /// <inheritdoc />
        public TweenHandle Play(TweenClip clip, LifetimeScope scope = null, CancellationToken cancellationToken = default, bool unscaledTime = false)
        {
            if (clip == null) throw new ArgumentNullException(nameof(clip));
            if (_shutdown) throw new InvalidOperationException("TweenModule 已关闭。");
            if (_threadId == 0) _threadId = Thread.CurrentThread.ManagedThreadId;
            if (!IsOwnerThread) throw new InvalidOperationException("TweenModule 只能在所属线程播放补间。");
            var handle = new TweenHandle(this, clip, unscaledTime);
            _handles.Add(handle);
            _activeCount++;
            handle.Bind(scope, cancellationToken);
            return handle;
        }

        /// <inheritdoc />
        public void CancelAll()
        {
            TweenHandle[] snapshot = _handles.ToArray();
            foreach (TweenHandle handle in snapshot) handle.Cancel();
            if (!_updating) Compact();
        }

        private bool _updating;

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            float scaled = GameEntry.IsGameplayPaused || Time.timeScale == 0f ? 0f : deltaTime;
            Advance(scaled, Time.unscaledDeltaTime);
        }

        /// <summary>
        /// 使用可控双时间源推进一帧，供模块测试与诊断使用。
        /// </summary>
        /// <param name="scaledDelta">缩放时间秒数。</param><param name="unscaledDelta">非缩放时间秒数。</param>
        internal void Advance(float scaledDelta, float unscaledDelta)
        {
            if (_shutdown) return;
            if (float.IsNaN(scaledDelta) || float.IsInfinity(scaledDelta) || scaledDelta < 0f) throw new ArgumentOutOfRangeException(nameof(scaledDelta));
            if (float.IsNaN(unscaledDelta) || float.IsInfinity(unscaledDelta) || unscaledDelta < 0f) throw new ArgumentOutOfRangeException(nameof(unscaledDelta));
            int count = _handles.Count;
            _updating = true;
            try
            {
                for (int i = 0; i < count && !_shutdown; i++) _handles[i].Tick(scaledDelta, unscaledDelta);
            }
            finally
            {
                _updating = false;
                Compact();
            }
        }

        /// <summary>
        /// 一个句柄进入终态。
        /// </summary>
        internal void Finished()
        {
            _activeCount--;
            if (!_updating) Compact();
        }

        /// <summary>
        /// 记录用户写入或回调失败。
        /// </summary>
        /// <param name="error">失败异常。</param>
        internal void LogError(Exception error)
        {
            try { _log?.Error("Tween", $"补间回调失败：{error}"); }
            catch { }
        }

        /// <summary>
        /// 移除终态句柄，释放模块侧引用。
        /// </summary>
        private void Compact()
        {
            int write = 0;
            for (int read = 0; read < _handles.Count; read++)
            {
                if (!_handles[read].IsActive) continue;
                if (write != read) _handles[write] = _handles[read];
                write++;
            }
            if (write < _handles.Count) _handles.RemoveRange(write, _handles.Count - write);
        }
    }
}
