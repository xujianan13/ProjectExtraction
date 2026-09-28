using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>宿主程序检查、跳转更新与重启的平台边界。</summary>
    public interface IPlatformUpdateService
    {
        /// <summary>当前平台是否支持安全程序化重启。</summary>
        bool CanRestart { get; }
        /// <summary>检查宿主程序更新。</summary>
        UniTask<HostApplicationUpdateResult> CheckHostApplicationAsync(CancellationToken ct = default);
        /// <summary>打开商店、安装器或平台更新入口。</summary>
        UniTask<bool> OpenHostApplicationUpdateAsync(CancellationToken ct = default);
        /// <summary>请求平台安全重启。</summary>
        UniTask<bool> RestartAsync(CancellationToken ct = default);
    }
}
