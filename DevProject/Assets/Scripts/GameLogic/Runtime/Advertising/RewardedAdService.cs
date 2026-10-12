using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameLogic.Advertising
{
    /// <summary>
    /// 在 Unity 主线程中串行展示激励广告，并阻止取消或释放后的结果授予奖励。
    /// </summary>
    public sealed class RewardedAdService : IDisposable
    {
        private readonly IRewardedAdProvider provider;
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private bool busy;
        private bool disposed;

        /// <summary>
        /// 使用显式指定的广告提供者创建应用广告服务。
        /// </summary>
        /// <param name="provider">应用启动时提供的非空广告展示实现。</param>
        public RewardedAdService(IRewardedAdProvider provider)
        {
            this.provider = provider;
        }

        /// <summary>
        /// 展示广告并返回结果；业务方只应在完成状态下发放奖励。
        /// </summary>
        /// <param name="placementId">非空的业务广告位标识。</param>
        /// <param name="ct">调用方的取消令牌。</param>
        /// <returns>完成、取消、失败或忙碌结果。</returns>
        public async UniTask<RewardedAdResult> ShowRewardedAsync(string placementId, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return new RewardedAdResult(RewardedAdStatus.Cancelled);
            if (disposed) return new RewardedAdResult(RewardedAdStatus.Failed, "广告服务已经释放。");
            if (string.IsNullOrWhiteSpace(placementId)) return new RewardedAdResult(RewardedAdStatus.Failed, "广告位标识不能为空。");
            if (busy) return new RewardedAdResult(RewardedAdStatus.Busy, "其他广告请求尚未结束。");

            busy = true;
            using var request = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
            try
            {
                (bool cancelled, RewardedAdResult result) = await provider.ShowRewardedAsync(placementId, request.Token).SuppressCancellationThrow();
                if (cancelled || request.IsCancellationRequested || disposed) return new RewardedAdResult(RewardedAdStatus.Cancelled);
                return result;
            }
            finally
            {
                busy = false;
            }
        }

        /// <summary>
        /// 取消当前展示，并永久拒绝新请求。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            lifetime.Dispose();
        }
    }
}
