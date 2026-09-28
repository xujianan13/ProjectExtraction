namespace GameFramework
{
    /// <summary>
    /// 日志模块接口。提供分级日志 API 和 Sink 管理，业务通过 GameEntry.Get&lt;ILogModule&gt;() 访问。
    /// </summary>
    public interface ILogModule : IModule
    {
        /// <summary>
        /// 最低输出级别。低于此级别的日志将被过滤，不会派发到 Sink。
        /// </summary>
        LogLevel MinLevel { get; set; }

        /// <summary>
        /// 指定级别是否会实际输出。调用方应在拼接昂贵字符串前先判断，避免热路径无意义分配。
        /// </summary>
        /// <param name="level">待检查的日志级别。</param>
        bool IsEnabled(LogLevel level);

        /// <summary>添加日志输出端。同一 Sink 重复添加无效。</summary>
        void AddSink(ILogSink sink);

        /// <summary>移除日志输出端。</summary>
        void RemoveSink(ILogSink sink);

        /// <summary>记录 Debug 级日志。</summary>
        void Debug(string tag, string message);

        /// <summary>记录 Info 级日志。</summary>
        void Info(string tag, string message);

        /// <summary>记录 Warn 级日志。</summary>
        void Warn(string tag, string message);

        /// <summary>记录 Error 级日志。</summary>
        void Error(string tag, string message);

        /// <summary>记录 Fatal 级日志。</summary>
        void Fatal(string tag, string message);

        /// <summary>刷新所有 Sink 的缓冲。</summary>
        void Flush();
    }
}
