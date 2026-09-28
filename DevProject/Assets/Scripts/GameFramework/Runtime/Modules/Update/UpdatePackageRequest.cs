using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 内容包更新检查请求。请求在创建后不可变，可安全跨异步阶段传递。
    /// </summary>
    public sealed class UpdatePackageRequest
    {
        /// <summary>
        /// 默认渠道名称。
        /// </summary>
        public const string DefaultChannel = "default";

        /// <summary>
        /// 默认最大并发下载数。
        /// </summary>
        public const int DefaultDownloadMaxConcurrency = 8;

        /// <summary>
        /// 默认单文件失败重试次数。
        /// </summary>
        public const int DefaultRetryCount = 3;

        /// <summary>
        /// 默认网络请求超时秒数。
        /// </summary>
        public const int DefaultTimeoutSeconds = 60;

        /// <summary>
        /// YooAsset 内容包名称。
        /// </summary>
        public string PackageName { get; }

        /// <summary>
        /// 发布渠道，用于选择对应的远端版本目录。
        /// </summary>
        public string Channel { get; }

        /// <summary>
        /// 当前宿主程序版本，用于远端兼容性判断。
        /// </summary>
        public string HostApplicationVersion { get; }

        /// <summary>
        /// 指定目标版本；为空时请求远端最新版本。
        /// </summary>
        public string TargetVersion { get; }

        /// <summary>
        /// 最大并发下载数。
        /// </summary>
        public int DownloadMaxConcurrency { get; }

        /// <summary>
        /// 单文件失败重试次数。
        /// </summary>
        public int RetryCount { get; }

        /// <summary>
        /// 网络请求超时秒数。
        /// </summary>
        public int TimeoutSeconds { get; }

        /// <summary>
        /// 下载完成后的版本激活时机。
        /// </summary>
        public UpdateActivationMode ActivationMode { get; }

        /// <summary>
        /// 激活前预下载的标签；为空时下载整个内容包的缺失资源，未标记的公共依赖始终包含。
        /// </summary>
        public IReadOnlyList<string> DownloadTags { get; }

        /// <summary>
        /// 保留已有热更程序集使用的构造函数签名，按整包差量方式创建请求。
        /// </summary>
        /// <param name="packageName">YooAsset 内容包名称。</param>
        /// <param name="channel">发布渠道。</param>
        /// <param name="hostApplicationVersion">当前宿主程序版本。</param>
        /// <param name="targetVersion">指定目标版本。</param>
        /// <param name="downloadMaxConcurrency">最大并发下载数。</param>
        /// <param name="retryCount">单文件失败重试次数。</param>
        /// <param name="timeoutSeconds">网络请求超时秒数。</param>
        /// <param name="activationMode">下载完成后的版本激活时机。</param>
        public UpdatePackageRequest(string packageName, string channel, string hostApplicationVersion, string targetVersion,
            int downloadMaxConcurrency, int retryCount, int timeoutSeconds, UpdateActivationMode activationMode)
            : this(packageName, channel, hostApplicationVersion, targetVersion, downloadMaxConcurrency, retryCount, timeoutSeconds,
                activationMode, null)
        {
        }

        /// <summary>
        /// 创建内容包更新检查请求。
        /// </summary>
        /// <param name="packageName">YooAsset 内容包名称。</param>
        /// <param name="channel">发布渠道。</param>
        /// <param name="hostApplicationVersion">当前宿主程序版本。</param>
        /// <param name="targetVersion">指定目标版本；为空时请求最新版本。</param>
        /// <param name="downloadMaxConcurrency">最大并发下载数。</param>
        /// <param name="retryCount">单文件失败重试次数。</param>
        /// <param name="timeoutSeconds">网络请求超时秒数。</param>
        /// <param name="activationMode">下载完成后的版本激活时机。</param>
        /// <param name="downloadTags">激活前需要的资源标签；为空时保留整包差量预下载，指定时至少包含一个有效标签。</param>
        public UpdatePackageRequest(string packageName, string channel = DefaultChannel, string hostApplicationVersion = null,
            string targetVersion = null, int downloadMaxConcurrency = DefaultDownloadMaxConcurrency, int retryCount = DefaultRetryCount,
            int timeoutSeconds = DefaultTimeoutSeconds, UpdateActivationMode activationMode = UpdateActivationMode.Immediate,
            IReadOnlyList<string> downloadTags = null)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("内容包名称不能为空。", nameof(packageName));
            if (string.IsNullOrWhiteSpace(channel))
                throw new ArgumentException("发布渠道不能为空。", nameof(channel));
            if (downloadMaxConcurrency <= 0)
                throw new ArgumentOutOfRangeException(nameof(downloadMaxConcurrency), "最大并发下载数必须大于零。");
            if (retryCount < 0)
                throw new ArgumentOutOfRangeException(nameof(retryCount), "失败重试次数不能小于零。");
            if (timeoutSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "网络请求超时秒数必须大于零。");

            PackageName = packageName;
            Channel = channel;
            HostApplicationVersion = hostApplicationVersion ?? string.Empty;
            TargetVersion = targetVersion;
            DownloadMaxConcurrency = downloadMaxConcurrency;
            RetryCount = retryCount;
            TimeoutSeconds = timeoutSeconds;
            ActivationMode = activationMode;
            if (downloadTags != null)
            {
                if (downloadTags.Count == 0) throw new ArgumentException("预下载标签不能是空集合。", nameof(downloadTags));
                var copied = new List<string>(downloadTags.Count);
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < downloadTags.Count; i++)
                {
                    string tag = downloadTags[i];
                    if (string.IsNullOrWhiteSpace(tag) || tag != tag.Trim())
                        throw new ArgumentException("预下载标签不能为空或包含首尾空白。", nameof(downloadTags));
                    if (unique.Add(tag)) copied.Add(tag);
                }
                DownloadTags = copied.AsReadOnly();
            }
        }
    }
}
