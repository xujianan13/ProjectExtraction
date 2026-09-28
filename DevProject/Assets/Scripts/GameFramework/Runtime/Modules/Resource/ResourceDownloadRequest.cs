using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 资源预下载目标及下载设置。地址和标签只能选一种。
    /// </summary>
    public sealed class ResourceDownloadRequest
    {
        private readonly string[] _locations;
        private readonly string[] _tags;

        /// <summary>
        /// 保存预下载目标和并发设置。
        /// </summary>
        /// <param name="packageName">可选包名。</param>
        /// <param name="locations">地址列表。</param>
        /// <param name="tags">标签列表。</param>
        /// <param name="maximumConcurrency">最大并发数。</param>
        /// <param name="retryCount">失败重试次数。</param>
        private ResourceDownloadRequest(string packageName, string[] locations, string[] tags, int maximumConcurrency, int retryCount)
        {
            PackageName = packageName;
            _locations = Deduplicate(locations);
            _tags = Deduplicate(tags);
            MaximumConcurrency = maximumConcurrency;
            RetryCount = retryCount;
        }

        /// <summary>
        /// 指定包名；为空时使用默认包。
        /// </summary>
        public string PackageName { get; }
        /// <summary>
        /// 需下载的资源地址。每次读取返回副本。
        /// </summary>
        public string[] Locations => _locations == null ? null : (string[])_locations.Clone();
        /// <summary>
        /// 需下载的资源标签。每次读取返回副本。
        /// </summary>
        public string[] Tags => _tags == null ? null : (string[])_tags.Clone();
        /// <summary>
        /// 最大并发数。
        /// </summary>
        public int MaximumConcurrency { get; }
        /// <summary>
        /// 失败重试次数。
        /// </summary>
        public int RetryCount { get; }

        /// <summary>
        /// 创建按资源地址下载的请求，包含该资源的 Bundle 依赖。
        /// </summary>
        /// <param name="locations">非空资源地址列表。</param>
        /// <param name="packageName">可选包名。</param>
        /// <param name="maximumConcurrency">最大并发数。</param>
        /// <param name="retryCount">失败重试次数。</param>
        /// <returns>预下载请求。</returns>
        public static ResourceDownloadRequest ForLocations(string[] locations, string packageName = null, int maximumConcurrency = 8, int retryCount = 2)
            => new(packageName, locations, null, maximumConcurrency, retryCount);

        /// <summary>
        /// 创建按标签下载的请求。
        /// </summary>
        /// <param name="tags">非空标签列表。</param>
        /// <param name="packageName">可选包名。</param>
        /// <param name="maximumConcurrency">最大并发数。</param>
        /// <param name="retryCount">失败重试次数。</param>
        /// <returns>预下载请求。</returns>
        public static ResourceDownloadRequest ForTags(string[] tags, string packageName = null, int maximumConcurrency = 8, int retryCount = 2)
            => new(packageName, null, tags, maximumConcurrency, retryCount);

        /// <summary>
        /// 复制并去除重复目标。
        /// </summary>
        /// <param name="values">输入目标。</param>
        /// <returns>独立的去重数组。</returns>
        private static string[] Deduplicate(string[] values)
        {
            if (values == null) return null;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new List<string>(values.Length);
            foreach (string value in values)
                if (seen.Add(value)) result.Add(value);
            return result.ToArray();
        }
    }

    /// <summary>
    /// 资源预下载的最终状态。
    /// </summary>
    public enum ResourceDownloadStatus { Success, Failure, Cancelled, Unsupported }

    /// <summary>
    /// 资源预下载的进度快照。
    /// </summary>
    public readonly struct ResourceDownloadProgress
    {
        /// <summary>
        /// 创建进度快照。
        /// </summary>
        /// <param name="currentCount">已完成文件数。</param>
        /// <param name="totalCount">文件总数。</param>
        /// <param name="currentBytes">已下载字节数。</param>
        /// <param name="totalBytes">字节总数。</param>
        public ResourceDownloadProgress(int currentCount, int totalCount, long currentBytes, long totalBytes)
        {
            CurrentCount = currentCount;
            TotalCount = totalCount;
            CurrentBytes = currentBytes;
            TotalBytes = totalBytes;
        }

        /// <summary>
        /// 已完成文件数。
        /// </summary>
        public int CurrentCount { get; }
        /// <summary>
        /// 文件总数。
        /// </summary>
        public int TotalCount { get; }
        /// <summary>
        /// 已下载字节数。
        /// </summary>
        public long CurrentBytes { get; }
        /// <summary>
        /// 字节总数。
        /// </summary>
        public long TotalBytes { get; }
    }

    /// <summary>
    /// 资源预下载结果。
    /// </summary>
    public readonly struct ResourceDownloadResult
    {
        /// <summary>
        /// 创建预下载结果。
        /// </summary>
        /// <param name="status">最终状态。</param>
        /// <param name="error">失败原因；成功时为空。</param>
        /// <param name="totalCount">计划下载文件数，零表示已在本地。</param>
        /// <param name="canRetry">底层下载失败后是否可以重试。</param>
        public ResourceDownloadResult(ResourceDownloadStatus status, string error, int totalCount, bool canRetry = false)
        {
            Status = status;
            Error = error;
            TotalCount = totalCount;
            CanRetry = canRetry;
        }

        /// <summary>
        /// 最终状态。
        /// </summary>
        public ResourceDownloadStatus Status { get; }
        /// <summary>
        /// 失败原因。
        /// </summary>
        public string Error { get; }
        /// <summary>
        /// 计划下载文件数。
        /// </summary>
        public int TotalCount { get; }
        /// <summary>
        /// 底层下载失败后是否值得重试。
        /// </summary>
        public bool CanRetry { get; }
        /// <summary>
        /// 是否成功。
        /// </summary>
        public bool IsSuccess => Status == ResourceDownloadStatus.Success;
    }
}
