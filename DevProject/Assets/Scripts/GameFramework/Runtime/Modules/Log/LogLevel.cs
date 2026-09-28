namespace GameFramework
{
    /// <summary>
    /// 日志级别。数值越大越严重；Off 用于关闭日志。
    /// </summary>
    public enum LogLevel
    {
        /// <summary>调试细节，仅开发期使用。</summary>
        Debug = 0,
        /// <summary>常规运行信息。</summary>
        Info = 1,
        /// <summary>警告，可恢复的异常情况。</summary>
        Warn = 2,
        /// <summary>错误，影响功能但不致命。</summary>
        Error = 3,
        /// <summary>致命错误，无法继续运行。</summary>
        Fatal = 4,
        /// <summary>关闭所有日志输出。</summary>
        Off = 99,
    }
}
