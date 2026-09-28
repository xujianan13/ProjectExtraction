using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using YooAsset;

namespace GameFramework
{
    /// <summary>
    /// YooAsset 句柄 / 异步操作到 UniTask 的桥接。
    /// 使用 Completed 回调完成，避免 WaitUntil 每帧轮询。
    /// </summary>
    internal static class YooAssetAwaitExtensions
    {
        /// <summary>等待资源句柄加载完成；取消时不自动 Release，由调用方处理。</summary>
        public static UniTask AwaitAsync(AssetHandle handle, CancellationToken ct = default)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);
            if (handle.IsDone) return UniTask.CompletedTask;

            return AwaitCoreAsync(handle, ct);
        }

        /// <summary>等待场景句柄加载完成。</summary>
        public static UniTask AwaitAsync(SceneHandle handle, CancellationToken ct = default)
        {
            if (handle == null) throw new ArgumentNullException(nameof(handle));
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);
            if (handle.IsDone) return UniTask.CompletedTask;

            return AwaitCoreAsync(handle, ct);
        }

        /// <summary>等待 YooAsset 异步操作完成。</summary>
        public static UniTask AwaitAsync(AsyncOperationBase operation, CancellationToken ct = default)
        {
            if (operation == null) throw new ArgumentNullException(nameof(operation));
            if (ct.IsCancellationRequested) return UniTask.FromCanceled(ct);
            if (operation.IsDone) return UniTask.CompletedTask;

            return AwaitCoreAsync(operation, ct);
        }

        private static async UniTask AwaitCoreAsync(AssetHandle handle, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            Action<AssetHandle> onCompleted = _ => tcs.TrySetResult();
            CancellationTokenRegistration registration = default;

            if (ct.CanBeCanceled)
            {
                registration = ct.RegisterWithoutCaptureExecutionContext(
                    state => ((UniTaskCompletionSource)state).TrySetCanceled(),
                    tcs);
            }

            handle.Completed += onCompleted;
            try
            {
                await tcs.Task;
            }
            finally
            {
                registration.Dispose();
                handle.Completed -= onCompleted;
            }
        }

        private static async UniTask AwaitCoreAsync(SceneHandle handle, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            Action<SceneHandle> onCompleted = _ => tcs.TrySetResult();
            CancellationTokenRegistration registration = default;

            if (ct.CanBeCanceled)
            {
                registration = ct.RegisterWithoutCaptureExecutionContext(
                    state => ((UniTaskCompletionSource)state).TrySetCanceled(),
                    tcs);
            }

            handle.Completed += onCompleted;
            try
            {
                await tcs.Task;
            }
            finally
            {
                registration.Dispose();
                handle.Completed -= onCompleted;
            }
        }

        private static async UniTask AwaitCoreAsync(AsyncOperationBase operation, CancellationToken ct)
        {
            var tcs = new UniTaskCompletionSource();
            Action<AsyncOperationBase> onCompleted = _ => tcs.TrySetResult();
            CancellationTokenRegistration registration = default;

            if (ct.CanBeCanceled)
            {
                registration = ct.RegisterWithoutCaptureExecutionContext(
                    state => ((UniTaskCompletionSource)state).TrySetCanceled(),
                    tcs);
            }

            operation.Completed += onCompleted;
            try
            {
                await tcs.Task;
            }
            finally
            {
                registration.Dispose();
                operation.Completed -= onCompleted;
            }
        }
    }
}
