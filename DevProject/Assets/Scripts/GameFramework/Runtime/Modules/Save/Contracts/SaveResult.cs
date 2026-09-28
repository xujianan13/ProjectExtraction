namespace GameFramework
{
    /// <summary>
    /// 正式存档操作的可预期错误。
    /// </summary>
    public enum SaveError
    {
        None, InvalidArgument, Unsupported, ReadFailed, WriteFailed, QuotaExceeded, TooLarge, Uncertain, Cancelled,
        Conflict, Corrupt, FormatUnsupported, VersionTooNew, MigrationFailed, ValidationFailed, NotActive, RecoveryRequired, Closed
    }

    /// <summary>
    /// 存档操作结果；失败不会被伪装成默认存档。
    /// </summary>
    public readonly struct SaveResult
    {
        public bool Success => Error == SaveError.None;
        public SaveError Error { get; }
        public string Message { get; }
        public bool CanRetry { get; }

        /// <summary>
        /// 构造操作结果。
        /// </summary>
        /// <param name="error">错误码。</param>
        /// <param name="message">错误说明。</param>
        /// <param name="canRetry">是否允许重试。</param>
        private SaveResult(SaveError error, string message, bool canRetry)
        {
            Error = error;
            Message = message ?? string.Empty;
            CanRetry = canRetry;
        }

        /// <summary>
        /// 创建成功结果。
        /// </summary>
        /// <returns>成功结果。</returns>
        public static SaveResult Ok() => new(SaveError.None, string.Empty, false);

        /// <summary>
        /// 创建失败结果。
        /// </summary>
        /// <param name="error">错误码。</param>
        /// <param name="message">错误说明。</param>
        /// <param name="canRetry">是否允许重试。</param>
        /// <returns>失败结果。</returns>
        public static SaveResult Fail(SaveError error, string message, bool canRetry = false)
            => new(error == SaveError.None ? SaveError.InvalidArgument : error, message, canRetry);
    }

    /// <summary>
    /// 后端读取状态。
    /// </summary>
    public enum SaveReadStatus { Found, Missing, Failed }

    /// <summary>
    /// 后端读取记录的结果。
    /// </summary>
    public readonly struct SaveReadResult
    {
        public SaveReadStatus Status { get; }
        public string Record { get; }
        public SaveResult Result { get; }

        /// <summary>
        /// 创建读取结果。
        /// </summary>
        /// <param name="status">读取状态。</param>
        /// <param name="record">完整记录。</param>
        /// <param name="result">错误信息。</param>
        private SaveReadResult(SaveReadStatus status, string record, SaveResult result)
        {
            Status = status;
            Record = record;
            Result = result;
        }

        /// <summary>
        /// 返回找到的完整记录。
        /// </summary>
        /// <param name="record">记录内容。</param>
        /// <returns>找到的记录。</returns>
        public static SaveReadResult Found(string record) => new(SaveReadStatus.Found, record, SaveResult.Ok());

        /// <summary>
        /// 返回明确不存在的记录。
        /// </summary>
        /// <returns>不存在结果。</returns>
        public static SaveReadResult Missing() => new(SaveReadStatus.Missing, null, SaveResult.Ok());

        /// <summary>
        /// 返回读取失败。
        /// </summary>
        /// <param name="result">失败信息。</param>
        /// <returns>失败结果。</returns>
        public static SaveReadResult Failed(SaveResult result) => new(SaveReadStatus.Failed, null, result);
    }
}
