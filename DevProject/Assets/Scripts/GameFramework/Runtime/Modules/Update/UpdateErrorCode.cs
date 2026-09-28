namespace GameFramework
{
    /// <summary>内容包检查和执行使用的稳定错误码。</summary>
    public enum UpdateErrorCode
    {
        /// <summary>没有错误。</summary>
        None = 0,

        /// <summary>请求参数无效。</summary>
        InvalidRequest = 1,

        /// <summary>更新计划无效、过期或不属于当前模块。</summary>
        InvalidPlan = 2,

        /// <summary>内容包尚未初始化。</summary>
        PackageNotInitialized = 10,

        /// <summary>内容包初始化失败。</summary>
        PackageInitializationFailed = 11,

        /// <summary>请求远端版本失败。</summary>
        PackageVersionRequestFailed = 20,

        /// <summary>加载目标版本清单失败。</summary>
        PackageManifestLoadFailed = 21,

        /// <summary>宿主程序与目标内容版本不兼容。</summary>
        HostApplicationIncompatible = 22,

        /// <summary>无法创建下载任务。</summary>
        DownloadCreationFailed = 30,

        /// <summary>内容文件下载失败。</summary>
        DownloadFailed = 31,

        /// <summary>网络当前不可用。</summary>
        NetworkUnavailable = 32,

        /// <summary>网络请求超时。</summary>
        Timeout = 33,

        /// <summary>可用存储空间不足。</summary>
        InsufficientStorage = 34,

        /// <summary>下载内容完整性校验失败。</summary>
        VerificationFailed = 40,

        /// <summary>目标版本激活或暂存失败。</summary>
        ActivationFailed = 41,

        /// <summary>操作被调用方取消。</summary>
        Cancelled = 50,

        /// <summary>未能映射为更具体错误码的失败。</summary>
        Unknown = 999
    }
}
