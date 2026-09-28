using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// 将 YooAsset 异步操作转换为可取消等待的 UniTask。
    /// </summary>
    internal static class YooAssetOperationAwaiter
    {
        /// <summary>
        /// 等待 YooAsset 操作结束。
        /// </summary>
        /// <param name="operation">需要等待的操作。</param>
        /// <param name="ct">等待过程的取消令牌。</param>
        /// <returns>操作结束或等待被取消时完成的任务。</returns>
        internal static UniTask AwaitAsync(AsyncOperationBase operation, CancellationToken ct = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);
            if (operation.IsDone) return UniTask.CompletedTask;
            return AwaitCoreAsync(operation, ct);
        }

        /// <summary>
        /// 通过完成事件等待尚未结束的 YooAsset 操作。
        /// </summary>
        /// <param name="operation">需要等待的操作。</param>
        /// <param name="ct">等待过程的取消令牌。</param>
        /// <returns>操作结束或等待被取消时完成的任务。</returns>
        private static async UniTask AwaitCoreAsync(AsyncOperationBase operation, CancellationToken ct)
        {
            var source = new UniTaskCompletionSource();
            Action<AsyncOperationBase> onCompleted = _ => source.TrySetResult();
            CancellationTokenRegistration registration = default;
            if (ct.CanBeCanceled)
            {
                registration = ct.RegisterWithoutCaptureExecutionContext(
                    state => ((UniTaskCompletionSource)state).TrySetCanceled(),
                    source);
            }

            operation.Completed += onCompleted;
            try
            {
                await source.Task;
            }
            finally
            {
                registration.Dispose();
                operation.Completed -= onCompleted;
            }
        }
    }
}
