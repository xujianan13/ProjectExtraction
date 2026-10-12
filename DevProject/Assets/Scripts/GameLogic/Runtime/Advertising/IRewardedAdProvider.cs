using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameLogic.Advertising
{
    /// <summary>
    /// 激励广告展示提供者；平台适配负责将预期失败和取消转换为结果，不直接发放业务奖励。
    /// </summary>
    public interface IRewardedAdProvider
    {
        /// <summary>
        /// 展示指定广告位并返回是否已完成激励广告。
        /// </summary>
        /// <param name="placementId">非空的业务广告位标识。</param>
        /// <param name="ct">本次展示的取消令牌。</param>
        /// <returns>广告展示结果；只有完成状态允许调用方发放奖励。</returns>
        UniTask<RewardedAdResult> ShowRewardedAsync(string placementId, CancellationToken ct = default);
    }
}
