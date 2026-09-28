namespace GameFramework
{
    /// <summary>BootKernel 提供给 UpdateLogic 的稳定运行上下文。</summary>
    public interface IUpdateRuntimeContext
    {
        /// <summary>当前宿主程序版本。</summary>
        string HostApplicationVersion { get; }

        /// <summary>当前发布渠道。</summary>
        string Channel { get; }

        /// <summary>当前平台的内容包初始化参数提供器。</summary>
        IUpdatePackageOptionsProvider PackageOptionsProvider { get; }

        /// <summary>与当前 UpdateLogic 来自同一 UpdaterPackage Manifest 的包级配置。</summary>
        PackageRuntimeSettings PackageSettings { get; }

        /// <summary>通知 BootLogic，当前 UpdateLogic 已完成最低限度初始化。</summary>
        void ReportUpdaterReady();

        /// <summary>记录不阻断 GamePackage 更新的启动告警。</summary>
        void ReportWarning(string message);

    }
}
