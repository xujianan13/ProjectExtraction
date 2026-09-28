namespace GameFramework
{
    /// <summary>内容包检查和执行期间的结构化阶段。</summary>
    public enum UpdateProgressPhase
    {
        /// <summary>尚未开始。</summary>
        None = 0,

        /// <summary>正在初始化内容包。</summary>
        InitializePackage = 1,

        /// <summary>正在请求远端版本。</summary>
        RequestPackageVersion = 2,

        /// <summary>正在加载目标版本清单。</summary>
        LoadPackageManifest = 3,

        /// <summary>正在生成下载清单。</summary>
        PrepareDownload = 4,

        /// <summary>正在下载内容文件。</summary>
        Download = 5,

        /// <summary>正在校验下载内容。</summary>
        Verify = 6,

        /// <summary>正在激活或暂存目标版本。</summary>
        Activate = 7,

        /// <summary>当前更新操作已完成。</summary>
        Completed = 8
    }
}
