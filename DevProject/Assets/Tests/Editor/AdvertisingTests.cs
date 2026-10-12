using System.Threading;
using Cysharp.Threading.Tasks;
using GameLogic.Advertising;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证模拟广告立即完成，以及取消、并发和服务释放对奖励资格的限制。
    /// </summary>
    public sealed class AdvertisingTests
    {
        /// <summary>
        /// 模拟广告不等待 Unity 帧或平台回调。
        /// </summary>
        [Test]
        public void SimulatedProvider_CompletesImmediately()
        {
            var provider = new SimulatedRewardedAdProvider();
            UniTask<RewardedAdResult> result = provider.ShowRewardedAsync("test-placement");
            Assert.That(result.Status, Is.EqualTo(UniTaskStatus.Succeeded));
            Assert.That(result.GetAwaiter().GetResult().IsCompleted, Is.True);
        }

        /// <summary>
        /// 服务保持模拟完成请求的即时性，并可连续展示。
        /// </summary>
        [Test]
        public void SimulatedService_CompletesRepeatedRequestsImmediately()
        {
            using var service = new RewardedAdService(new SimulatedRewardedAdProvider());
            for (int i = 0; i < 3; i++)
            {
                UniTask<RewardedAdResult> result = service.ShowRewardedAsync("test-placement");
                Assert.That(result.Status, Is.EqualTo(UniTaskStatus.Succeeded));
                Assert.That(result.GetAwaiter().GetResult().IsCompleted, Is.True);
            }
        }

        /// <summary>
        /// 空广告位不调用提供者。
        /// </summary>
        /// <param name="placementId">无效广告位标识。</param>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        public void EmptyPlacement_FailsWithoutProviderCall(string placementId)
        {
            var provider = new HeldProvider();
            using var service = new RewardedAdService(provider);
            RewardedAdResult result = service.ShowRewardedAsync(placementId).GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(RewardedAdStatus.Failed));
            Assert.That(result.Error, Is.Not.Empty);
            Assert.That(provider.CallCount, Is.Zero);
        }

        /// <summary>
        /// 已取消的请求不展示广告，也不授予奖励。
        /// </summary>
        [Test]
        public void PreCancelledRequest_DoesNotCallProvider()
        {
            var provider = new HeldProvider();
            using var service = new RewardedAdService(provider);
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            RewardedAdResult result = service.ShowRewardedAsync("test-placement", cancellation.Token).GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(RewardedAdStatus.Cancelled));
            Assert.That(provider.CallCount, Is.Zero);
            Assert.That(new SimulatedRewardedAdProvider().ShowRewardedAsync("test-placement", cancellation.Token)
                .GetAwaiter().GetResult().IsCompleted, Is.False);
        }

        /// <summary>
        /// 待完成的展示阻止并发请求，结束后允许下一次展示。
        /// </summary>
        [Test]
        public void ConcurrentRequest_IsBusyUntilFirstRequestEnds()
        {
            var provider = new HeldProvider();
            using var service = new RewardedAdService(provider);
            UniTask<RewardedAdResult> first = service.ShowRewardedAsync("first");
            Assert.That(first.Status, Is.EqualTo(UniTaskStatus.Pending));
            Assert.That(service.ShowRewardedAsync("second").GetAwaiter().GetResult().Status, Is.EqualTo(RewardedAdStatus.Busy));
            Assert.That(provider.CallCount, Is.EqualTo(1));
            provider.Complete();
            Assert.That(first.GetAwaiter().GetResult().IsCompleted, Is.True);
            UniTask<RewardedAdResult> next = service.ShowRewardedAsync("next");
            provider.Complete();
            Assert.That(next.GetAwaiter().GetResult().IsCompleted, Is.True);
            Assert.That(provider.CallCount, Is.EqualTo(2));
        }

        /// <summary>
        /// 提供者忽略取消且晚报完成时，服务依然拒绝奖励资格。
        /// </summary>
        [Test]
        public void CancelledRequest_LateCompletionCannotGrantReward()
        {
            var provider = new HeldProvider();
            using var service = new RewardedAdService(provider);
            using var cancellation = new CancellationTokenSource();
            UniTask<RewardedAdResult> pending = service.ShowRewardedAsync("test-placement", cancellation.Token);
            cancellation.Cancel();
            Assert.That(provider.RequestToken.IsCancellationRequested, Is.True);
            Assert.That(service.ShowRewardedAsync("other").GetAwaiter().GetResult().Status, Is.EqualTo(RewardedAdStatus.Busy));
            provider.Complete();
            Assert.That(pending.GetAwaiter().GetResult().Status, Is.EqualTo(RewardedAdStatus.Cancelled));
        }

        /// <summary>
        /// 服务释放取消当前展示，并拒绝晚结果与后续请求。
        /// </summary>
        [Test]
        public void DisposedService_RejectsLateCompletionAndNewRequests()
        {
            var provider = new HeldProvider();
            using var service = new RewardedAdService(provider);
            UniTask<RewardedAdResult> pending = service.ShowRewardedAsync("test-placement");
            service.Dispose();
            Assert.That(provider.RequestToken.IsCancellationRequested, Is.True);
            provider.Complete();
            Assert.That(pending.GetAwaiter().GetResult().Status, Is.EqualTo(RewardedAdStatus.Cancelled));
            Assert.That(service.ShowRewardedAsync("next").GetAwaiter().GetResult().Status, Is.EqualTo(RewardedAdStatus.Failed));
            Assert.That(provider.CallCount, Is.EqualTo(1));
        }

        /// <summary>
        /// 提供者失败按原结果返回，并解除展示门禁。
        /// </summary>
        [Test]
        public void FailedRequest_PropagatesReasonAndAllowsRetry()
        {
            var provider = new HeldProvider();
            using var service = new RewardedAdService(provider);
            UniTask<RewardedAdResult> pending = service.ShowRewardedAsync("test-placement");
            provider.Complete(RewardedAdStatus.Failed, "模拟失败");
            RewardedAdResult result = pending.GetAwaiter().GetResult();
            Assert.That(result.Status, Is.EqualTo(RewardedAdStatus.Failed));
            Assert.That(result.Error, Is.EqualTo("模拟失败"));
            UniTask<RewardedAdResult> next = service.ShowRewardedAsync("retry");
            provider.Complete();
            Assert.That(next.GetAwaiter().GetResult().IsCompleted, Is.True);
        }

        /// <summary>
        /// 默认结果不具备发奖励资格。
        /// </summary>
        [Test]
        public void DefaultResult_DoesNotGrantReward()
        {
            Assert.That(default(RewardedAdResult).IsCompleted, Is.False);
        }

        /// <summary>
        /// 可手动返回完成结果且故意忽略取消的提供者。
        /// </summary>
        private sealed class HeldProvider : IRewardedAdProvider
        {
            private UniTaskCompletionSource<RewardedAdResult> receipt;
            public int CallCount { get; private set; }
            public CancellationToken RequestToken { get; private set; }

            /// <summary>
            /// 记录广告请求并等待测试主动返回完成。
            /// </summary>
            /// <param name="placementId">广告位标识。</param>
            /// <param name="ct">广告请求取消令牌。</param>
            /// <returns>手动完成的广告结果。</returns>
            public UniTask<RewardedAdResult> ShowRewardedAsync(string placementId, CancellationToken ct = default)
            {
                CallCount++;
                RequestToken = ct;
                receipt = new UniTaskCompletionSource<RewardedAdResult>();
                return receipt.Task;
            }

            /// <summary>
            /// 返回当前展示结果。
            /// </summary>
            /// <param name="status">需要返回的广告状态。</param>
            /// <param name="error">失败或拒绝原因。</param>
            public void Complete(RewardedAdStatus status = RewardedAdStatus.Completed, string error = "") =>
                receipt.TrySetResult(new RewardedAdResult(status, error));
        }
    }
}
