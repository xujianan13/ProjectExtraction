namespace GameFramework
{
    /// <summary>
    /// 日志输出端：负责把一条日志写到具体目的地（Unity Console / 文件 / 远端等）。
    /// 由 LogModule 管理一组 Sink，按注册顺序广播。
    /// </summary>
    public interface ILogSink
    {
        /// <summary>
        /// 写入一条日志。实现需保证线程安全（若可能被非主线程调用）。
        /// </summary>
        /// <param name="level">日志级别。</param>
        /// <param name="tag">日志标签，通常是模块或系统名。</param>
        /// <param name="message">日志消息体。</param>
        void Write(LogLevel level, string tag, string message);

        /// <summary>
        /// 强制刷新缓冲（如文件 Sink 落盘）。无缓冲的 Sink 可空实现。
        /// </summary>
        void Flush();
    }
}
