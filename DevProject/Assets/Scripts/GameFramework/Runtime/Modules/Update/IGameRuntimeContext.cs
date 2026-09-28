namespace GameFramework
{
    /// <summary>BootKernel 提供给 GameLogic 的最低稳定运行上下文。</summary>
    public interface IGameRuntimeContext
    {
        /// <summary>与当前 GameLogic 来自同一 GamePackage Manifest 的包级配置。</summary>
        PackageRuntimeSettings PackageSettings { get; }

        /// <summary>已验签的公开启动上下文；尚未接入远端 Bootstrap 时为空。</summary>
        ClientBootstrapContext BootstrapContext { get; }

        /// <summary>当前实际激活的客户端版本身份，不表示 Platform 已完成验签。</summary>
        ClientRuntimeIdentity ClientIdentity { get; }

        /// <summary>仅供显式开发构建使用的 Debug 直连前端选项。</summary>
        DevelopmentDirectLoginOptions DevelopmentDirectLogin { get; }

        /// <summary>通知 BootKernel，GameLogic 已完成最低限度初始化。</summary>
        void ReportReady();
    }
}
