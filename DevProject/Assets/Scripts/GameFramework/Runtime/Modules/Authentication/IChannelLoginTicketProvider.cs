using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 外部发行层向 CP 客户端提供渠道登录票据的稳定端口。
    /// 项目内不实现具体渠道 SDK，也不通过本端口提供游客身份。
    /// </summary>
    public interface IChannelLoginTicketProvider
    {
        /// <summary>请求新的单次渠道登录票据。</summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>可提交给 Game Auth API 的不透明票据。</returns>
        UniTask<ChannelLoginTicket> GetLoginTicketAsync(CancellationToken ct);
    }
}
