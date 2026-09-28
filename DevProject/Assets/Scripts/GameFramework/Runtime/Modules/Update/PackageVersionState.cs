namespace GameFramework
{
    /// <summary>
    /// 单个可更新内容包的持久化事务状态。
    /// </summary>
    public readonly struct PackageVersionState
    {
        /// <summary>
        /// 创建版本状态快照。
        /// </summary>
        /// <param name="currentVersion">当前已提交版本。</param>
        /// <param name="lastGoodVersion">最近确认可用版本。</param>
        /// <param name="pendingVersion">等待确认的候选版本。</param>
        /// <param name="pendingAttemptCount">候选版本启动尝试次数。</param>
        /// <param name="lastFailureCode">最近失败错误码。</param>
        /// <param name="rejectedVersion">不应再次激活的失败候选版本。</param>
        public PackageVersionState(
            string currentVersion,
            string lastGoodVersion,
            string pendingVersion,
            int pendingAttemptCount,
            UpdateErrorCode lastFailureCode, string rejectedVersion = null)
        {
            CurrentVersion = currentVersion ?? string.Empty;
            LastGoodVersion = lastGoodVersion ?? string.Empty;
            PendingVersion = pendingVersion ?? string.Empty;
            PendingAttemptCount = pendingAttemptCount < 0 ? 0 : pendingAttemptCount;
            LastFailureCode = lastFailureCode;
            RejectedVersion = rejectedVersion ?? string.Empty;
        }

        /// <summary>当前已提交版本。</summary>
        public string CurrentVersion { get; }
        /// <summary>最近确认可用版本。</summary>
        public string LastGoodVersion { get; }
        /// <summary>等待 Ready 确认的版本。</summary>
        public string PendingVersion { get; }
        /// <summary>Pending 版本启动尝试次数。</summary>
        public int PendingAttemptCount { get; }
        /// <summary>最近失败错误码。</summary>
        public UpdateErrorCode LastFailureCode { get; }
        /// <summary>
        /// 启动失败且不应重复激活的候选版本。
        /// </summary>
        public string RejectedVersion { get; }
        /// <summary>当前是否存在 Pending 事务。</summary>
        public bool HasPending => !string.IsNullOrEmpty(PendingVersion);
    }
}
