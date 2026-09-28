using System;

namespace GameFramework
{
    /// <summary>
    /// 一次内容包检查生成的更新计划。计划仅描述更新事实，不持有底层下载器或清单对象。
    /// </summary>
    public readonly struct UpdatePlan
    {
        /// <summary>ContentUpdateService 分配的计划 ID；零表示尚未由服务确认。</summary>
        public int Id { get; }

        /// <summary>内容包名称。</summary>
        public string PackageName { get; }

        /// <summary>检查时正在使用的本地版本。</summary>
        public string LocalVersion { get; }

        /// <summary>检查得到的远端目标版本。</summary>
        public string RemoteVersion { get; }

        /// <summary>需要下载的文件数量。</summary>
        public int DownloadCount { get; }

        /// <summary>需要下载的总字节数。</summary>
        public long DownloadBytes { get; }

        /// <summary>是否为必须完成的更新。</summary>
        public bool IsMandatory { get; }

        /// <summary>更新提交后是否需要重启或等待下次冷启动。</summary>
        public bool RequiresRestart { get; }

        /// <summary>是否存在需要执行的更新。</summary>
        public bool HasUpdate { get; }

        /// <summary>计划是否已经由 ContentUpdateService 确认并可以执行。</summary>
        public bool IsValid => Id > 0 && !string.IsNullOrEmpty(PackageName);

        /// <summary>创建更新计划。</summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="localVersion">本地版本。</param>
        /// <param name="remoteVersion">远端目标版本。</param>
        /// <param name="downloadCount">需要下载的文件数量。</param>
        /// <param name="downloadBytes">需要下载的总字节数。</param>
        /// <param name="isMandatory">是否为强制更新。</param>
        /// <param name="requiresRestart">更新提交后是否需要重启。</param>
        /// <param name="hasUpdate">是否存在需要执行的更新。</param>
        public UpdatePlan(
            string packageName,
            string localVersion,
            string remoteVersion,
            int downloadCount,
            long downloadBytes,
            bool isMandatory,
            bool requiresRestart,
            bool hasUpdate)
        {
            if (string.IsNullOrWhiteSpace(packageName))
                throw new ArgumentException("内容包名称不能为空。", nameof(packageName));
            if (downloadCount < 0)
                throw new ArgumentOutOfRangeException(nameof(downloadCount), "下载文件数量不能小于零。");
            if (downloadBytes < 0)
                throw new ArgumentOutOfRangeException(nameof(downloadBytes), "下载字节数不能小于零。");

            Id = 0;
            PackageName = packageName;
            LocalVersion = localVersion ?? string.Empty;
            RemoteVersion = remoteVersion ?? string.Empty;
            DownloadCount = downloadCount;
            DownloadBytes = downloadBytes;
            IsMandatory = isMandatory;
            RequiresRestart = requiresRestart;
            HasUpdate = hasUpdate;
        }

        internal UpdatePlan AssignId(int id)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            return new UpdatePlan(
                id,
                PackageName,
                LocalVersion,
                RemoteVersion,
                DownloadCount,
                DownloadBytes,
                IsMandatory,
                RequiresRestart,
                HasUpdate);
        }

        private UpdatePlan(
            int id,
            string packageName,
            string localVersion,
            string remoteVersion,
            int downloadCount,
            long downloadBytes,
            bool isMandatory,
            bool requiresRestart,
            bool hasUpdate)
        {
            Id = id;
            PackageName = packageName;
            LocalVersion = localVersion;
            RemoteVersion = remoteVersion;
            DownloadCount = downloadCount;
            DownloadBytes = downloadBytes;
            IsMandatory = isMandatory;
            RequiresRestart = requiresRestart;
            HasUpdate = hasUpdate;
        }
    }
}
