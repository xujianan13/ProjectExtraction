namespace GameFramework
{
    /// <summary>持久化 Current、LastGood 与 Pending 版本事务。</summary>
    public interface IVersionStateStore
    {
        /// <summary>读取指定内容包版本状态。</summary>
        PackageVersionState Load(string packageName);
        /// <summary>首次接入事务时，把当前活动版本保存为 Current 和 LastGood。</summary>
        PackageVersionState InitializeCurrent(string packageName, string version);
        /// <summary>写入等待 Ready 确认的目标版本。</summary>
        void StagePending(string packageName, string version);
        /// <summary>增加 Pending 版本启动尝试次数。</summary>
        PackageVersionState IncrementPendingAttempt(string packageName);
        /// <summary>收到 Ready 后原子提交 Pending 版本。</summary>
        PackageVersionState CommitReady(string packageName, string version);
        /// <summary>
        /// 记录失败并清除 Pending，保留 LastGood。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="errorCode">候选版本失败原因。</param>
        /// <param name="rejectPending">是否禁止本次失败的候选版本再次激活。</param>
        /// <returns>回退后的版本状态。</returns>
        PackageVersionState Rollback(string packageName, UpdateErrorCode errorCode, bool rejectPending = false);
    }
}
