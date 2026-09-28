using System;

namespace GameFramework
{
    /// <summary>
    /// 内容包更新进度快照。只包含可计算数据，不包含最终界面文案。
    /// </summary>
    public readonly struct UpdateProgress
    {
        /// <summary>当前更新阶段。</summary>
        public UpdateProgressPhase Phase { get; }

        /// <summary>已经下载的字节数。</summary>
        public long DownloadedBytes { get; }

        /// <summary>需要下载的总字节数。</summary>
        public long TotalBytes { get; }

        /// <summary>已经完成的文件数量。</summary>
        public int DownloadedCount { get; }

        /// <summary>需要下载的文件总数。</summary>
        public int TotalCount { get; }

        /// <summary>最近统计区间内的每秒下载字节数。</summary>
        public long BytesPerSecond { get; }

        /// <summary>当前开始处理的文件标识；底层无法提供时为空。</summary>
        public string CurrentFile { get; }

        /// <summary>当前文件已经发生的重试次数。</summary>
        public int RetryCount { get; }

        /// <summary>按字节优先、文件数兜底计算的归一化进度。</summary>
        public float NormalizedProgress
        {
            get
            {
                if (TotalBytes > 0) return (float)DownloadedBytes / TotalBytes;
                if (TotalCount > 0) return (float)DownloadedCount / TotalCount;
                return Phase == UpdateProgressPhase.Completed ? 1f : 0f;
            }
        }

        /// <summary>创建更新进度快照。</summary>
        /// <param name="phase">当前更新阶段。</param>
        /// <param name="downloadedBytes">已经下载的字节数。</param>
        /// <param name="totalBytes">需要下载的总字节数。</param>
        /// <param name="downloadedCount">已经完成的文件数量。</param>
        /// <param name="totalCount">需要下载的文件总数。</param>
        /// <param name="bytesPerSecond">每秒下载字节数。</param>
        /// <param name="currentFile">当前文件标识。</param>
        /// <param name="retryCount">当前文件重试次数。</param>
        public UpdateProgress(
            UpdateProgressPhase phase,
            long downloadedBytes,
            long totalBytes,
            int downloadedCount,
            int totalCount,
            long bytesPerSecond,
            string currentFile,
            int retryCount)
        {
            if (downloadedBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(downloadedBytes), "已下载字节数不能小于零。");
            if (totalBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(totalBytes), "总字节数不能小于零。");
            if (downloadedBytes > totalBytes)
                throw new ArgumentOutOfRangeException(nameof(downloadedBytes), "已下载字节数不能大于总字节数。");
            if (downloadedCount < 0)
                throw new ArgumentOutOfRangeException(nameof(downloadedCount), "已下载文件数不能小于零。");
            if (totalCount < 0)
                throw new ArgumentOutOfRangeException(nameof(totalCount), "总文件数不能小于零。");
            if (downloadedCount > totalCount)
                throw new ArgumentOutOfRangeException(nameof(downloadedCount), "已下载文件数不能大于总文件数。");
            if (bytesPerSecond < 0)
                throw new ArgumentOutOfRangeException(nameof(bytesPerSecond), "每秒下载字节数不能小于零。");
            if (retryCount < 0)
                throw new ArgumentOutOfRangeException(nameof(retryCount), "重试次数不能小于零。");

            Phase = phase;
            DownloadedBytes = downloadedBytes;
            TotalBytes = totalBytes;
            DownloadedCount = downloadedCount;
            TotalCount = totalCount;
            BytesPerSecond = bytesPerSecond;
            CurrentFile = currentFile ?? string.Empty;
            RetryCount = retryCount;
        }
    }
}
