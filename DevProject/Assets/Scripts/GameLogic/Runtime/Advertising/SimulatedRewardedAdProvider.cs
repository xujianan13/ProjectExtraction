using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameLogic.Advertising
{
    /// <summary>
    /// 联调广告提供者；合法请求立即模拟完成，不连接平台或等待回调。
    /// </summary>
    public sealed class SimulatedRewardedAdProvider : IRewardedAdProvider
    {
        /// <summary>
        /// 立即返回模拟广告结果。
        /// </summary>
        /// <param name="placementId">非空的业务广告位标识。</param>
        /// <param name="ct">本次展示的取消令牌。</param>
        /// <returns>合法且未取消的请求立即完成，其他请求返回取消或失败。</returns>
        public UniTask<RewardedAdResult> ShowRewardedAsync(string placementId, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested) return UniTask.FromResult(new RewardedAdResult(RewardedAdStatus.Cancelled));
            if (string.IsNullOrWhiteSpace(placementId))
                return UniTask.FromResult(new RewardedAdResult(RewardedAdStatus.Failed, "广告位标识不能为空。"));
            return UniTask.FromResult(new RewardedAdResult(RewardedAdStatus.Completed));
        }
    }
}
