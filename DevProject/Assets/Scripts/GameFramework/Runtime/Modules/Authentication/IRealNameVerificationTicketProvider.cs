using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 仓库外合规适配层向 CP 客户端提供实名验证票据的稳定端口。
    /// 本端口不得返回姓名、证件号或其他原始身份信息。
    /// </summary>
    public interface IRealNameVerificationTicketProvider
    {
        /// <summary>请求新的单次实名验证票据。</summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>可提交给 Game Auth 的不透明验证票据。</returns>
        UniTask<RealNameVerificationTicket> GetVerificationTicketAsync(CancellationToken ct);
    }
}
