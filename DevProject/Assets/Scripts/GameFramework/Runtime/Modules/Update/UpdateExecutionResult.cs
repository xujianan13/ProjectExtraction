namespace GameFramework
{
    /// <summary>
    /// 内容包更新执行结果。正常、无更新、取消、等待重启和失败均使用结果值表达。
    /// </summary>
    public readonly struct UpdateExecutionResult
    {
        /// <summary>最终执行状态。</summary>
        public UpdateExecutionStatus Status { get; }

        /// <summary>当前进程已经激活的版本；未激活新版本时可为空。</summary>
        public string ActivatedVersion { get; }

        /// <summary>已经准备完成、等待后续激活的版本；没有待激活版本时为空。</summary>
        public string PendingVersion { get; }

        /// <summary>稳定错误码。</summary>
        public UpdateErrorCode ErrorCode { get; }

        /// <summary>用于诊断的底层错误描述，不应由 UI 解析业务状态。</summary>
        public string ErrorMessage { get; }

        /// <summary>调用方是否可以在不改变配置的情况下重试。</summary>
        public bool CanRetry { get; }

        /// <summary>结果是否允许启动流程继续加载当前或新内容。</summary>
        public bool IsSuccess =>
            Status == UpdateExecutionStatus.NoUpdate ||
            Status == UpdateExecutionStatus.Succeeded ||
            Status == UpdateExecutionStatus.Prepared;

        /// <summary>是否需要停止当前启动流程并进入重启等待。</summary>
        public bool RequiresRestart => Status == UpdateExecutionStatus.RestartRequired;

        /// <summary>是否需要由 BootKernel 在当前启动中激活已准备版本。</summary>
        public bool RequiresActivation => Status == UpdateExecutionStatus.Prepared;

        /// <summary>创建内容包更新执行结果。</summary>
        /// <param name="status">最终执行状态。</param>
        /// <param name="activatedVersion">当前进程已经激活的版本。</param>
        /// <param name="pendingVersion">等待后续激活的版本。</param>
        /// <param name="errorCode">稳定错误码。</param>
        /// <param name="errorMessage">底层错误描述。</param>
        /// <param name="canRetry">是否可以重试。</param>
        private UpdateExecutionResult(
            UpdateExecutionStatus status,
            string activatedVersion,
            string pendingVersion,
            UpdateErrorCode errorCode,
            string errorMessage,
            bool canRetry)
        {
            Status = status;
            ActivatedVersion = activatedVersion ?? string.Empty;
            PendingVersion = pendingVersion ?? string.Empty;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
            CanRetry = canRetry;
        }

        /// <summary>创建无更新结果。</summary>
        /// <param name="activeVersion">当前有效版本。</param>
        /// <returns>状态为 <see cref="UpdateExecutionStatus.NoUpdate"/> 的结果。</returns>
        public static UpdateExecutionResult NoUpdate(string activeVersion)
            => new(UpdateExecutionStatus.NoUpdate, activeVersion, null, UpdateErrorCode.None, null, false);

        /// <summary>创建已在当前进程激活的成功结果。</summary>
        /// <param name="activatedVersion">已经激活的版本。</param>
        /// <returns>状态为 <see cref="UpdateExecutionStatus.Succeeded"/> 的结果。</returns>
        public static UpdateExecutionResult Success(string activatedVersion)
            => new(UpdateExecutionStatus.Succeeded, activatedVersion, null, UpdateErrorCode.None, null, false);

        /// <summary>创建等待重启激活的结果。</summary>
        /// <param name="activeVersion">当前进程仍在使用的版本。</param>
        /// <param name="pendingVersion">等待后续激活的版本。</param>
        /// <returns>状态为 <see cref="UpdateExecutionStatus.RestartRequired"/> 的结果。</returns>
        public static UpdateExecutionResult RestartRequired(string activeVersion, string pendingVersion)
            => new(UpdateExecutionStatus.RestartRequired, activeVersion, pendingVersion, UpdateErrorCode.None, null, false);

        /// <summary>创建等待 BootKernel 在当前启动中激活的结果。</summary>
        /// <param name="activeVersion">下载前的活动版本。</param>
        /// <param name="pendingVersion">已经准备完成的目标版本。</param>
        /// <returns>状态为 <see cref="UpdateExecutionStatus.Prepared"/> 的结果。</returns>
        public static UpdateExecutionResult Prepared(string activeVersion, string pendingVersion)
            => new(UpdateExecutionStatus.Prepared, activeVersion, pendingVersion, UpdateErrorCode.None, null, false);

        /// <summary>创建已取消结果。</summary>
        /// <param name="activeVersion">取消后仍然有效的版本。</param>
        /// <returns>状态为 <see cref="UpdateExecutionStatus.Cancelled"/> 的结果。</returns>
        public static UpdateExecutionResult Cancelled(string activeVersion)
            => new(UpdateExecutionStatus.Cancelled, activeVersion, null, UpdateErrorCode.Cancelled, null, false);

        /// <summary>创建失败结果。</summary>
        /// <param name="activeVersion">失败后仍然有效的版本。</param>
        /// <param name="errorCode">稳定错误码。</param>
        /// <param name="errorMessage">底层错误描述。</param>
        /// <param name="canRetry">是否可以重试。</param>
        /// <returns>状态为 <see cref="UpdateExecutionStatus.Failed"/> 的结果。</returns>
        public static UpdateExecutionResult Failure(
            string activeVersion,
            UpdateErrorCode errorCode,
            string errorMessage,
            bool canRetry)
        {
            if (errorCode == UpdateErrorCode.None)
                throw new System.ArgumentOutOfRangeException(nameof(errorCode), "失败结果错误码不能为 None。");
            return new UpdateExecutionResult(
                UpdateExecutionStatus.Failed,
                activeVersion,
                null,
                errorCode,
                errorMessage,
                canRetry);
        }
    }
}
