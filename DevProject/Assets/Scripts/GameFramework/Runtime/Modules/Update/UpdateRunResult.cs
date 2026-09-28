namespace GameFramework
{
    /// <summary>UpdateLogic 完成一次更新流程后返回给 BootLogic 的结果。</summary>
    public readonly struct UpdateRunResult
    {
        private UpdateRunResult(
            bool isSuccess,
            string gameTargetVersion,
            string pendingUpdaterVersion,
            UpdateErrorCode errorCode,
            string errorMessage,
            bool canRetry)
        {
            IsSuccess = isSuccess;
            GameTargetVersion = gameTargetVersion ?? string.Empty;
            PendingUpdaterVersion = pendingUpdaterVersion ?? string.Empty;
            ErrorCode = errorCode;
            ErrorMessage = errorMessage ?? string.Empty;
            CanRetry = canRetry;
        }

        /// <summary>是否允许 BootLogic 继续启动 GameLogic。</summary>
        public bool IsSuccess { get; }

        /// <summary>已经准备完成、需要由 BootKernel 通用激活器选择的 GamePackage 目标版本。</summary>
        public string GameTargetVersion { get; }

        /// <summary>等待下次冷启动激活的 UpdaterPackage 版本。</summary>
        public string PendingUpdaterVersion { get; }

        /// <summary>失败错误码。</summary>
        public UpdateErrorCode ErrorCode { get; }

        /// <summary>失败诊断信息。</summary>
        public string ErrorMessage { get; }

        /// <summary>是否可以在不改变配置的情况下重试。</summary>
        public bool CanRetry { get; }

        /// <summary>创建成功结果。</summary>
        public static UpdateRunResult Success(string gameTargetVersion, string pendingUpdaterVersion = null)
            => new(true, gameTargetVersion, pendingUpdaterVersion, UpdateErrorCode.None, null, false);

        /// <summary>创建失败结果。</summary>
        public static UpdateRunResult Failure(
            UpdateErrorCode errorCode,
            string errorMessage,
            bool canRetry,
            string pendingUpdaterVersion = null)
            => new(false, null, pendingUpdaterVersion, errorCode, errorMessage, canRetry);
    }
}
