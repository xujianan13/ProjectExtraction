using System;
using System.Threading;

namespace GameFramework
{
    /// <summary>控制一次回调式资源请求的独立取消与完成状态。</summary>
    /// <typeparam name="T">Unity 资源类型。</typeparam>
    public sealed class ResourceLoadTicket<T> : IDisposable where T : UnityEngine.Object
    {
        private readonly CancellationTokenSource _cancellation;
        private int _isDone;
        private int _isDisposed;

        internal ResourceLoadTicket(CancellationToken externalToken)
        {
            _cancellation = externalToken.CanBeCanceled
                ? CancellationTokenSource.CreateLinkedTokenSource(externalToken)
                : new CancellationTokenSource();
        }

        internal CancellationToken Token => _cancellation.Token;

        /// <summary>完成、失败或取消回调是否已经结束。</summary>
        public bool IsDone => Volatile.Read(ref _isDone) != 0;

        /// <summary>仅取消本调用方等待，不会取消其他调用方共享的底层加载。</summary>
        public void Cancel()
        {
            if (Volatile.Read(ref _isDisposed) != 0 || IsDone)
                return;
            try
            {
                _cancellation.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Dispose 与跨线程取消竞争时，已释放票据等价于已经取消。
            }
        }

        /// <summary>取消仍在等待的请求并释放票据资源。</summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _isDisposed, 1) != 0)
                return;

            if (!IsDone)
                _cancellation.Cancel();
            _cancellation.Dispose();
        }

        internal void MarkDone()
        {
            Interlocked.Exchange(ref _isDone, 1);
        }
    }
}
