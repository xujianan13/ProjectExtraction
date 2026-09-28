using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 日志模块默认实现。维护一组 Sink，按级别过滤后广播。
    /// 优先级最高（-1000），确保其它模块 OnInit 时已可写日志。
    /// </summary>
    public sealed class LogModule : ModuleBase, ILogModule
    {
        /// <inheritdoc />
        public override int Priority => -1000;

        /// <inheritdoc />
        public LogLevel MinLevel { get; set; } = LogLevel.Debug;

        private readonly List<ILogSink> _sinks = new(4);

        /// <inheritdoc />
        public bool IsEnabled(LogLevel level) => level >= MinLevel && level != LogLevel.Off;

        /// <inheritdoc />
        public void AddSink(ILogSink sink)
        {
            if (sink != null && !_sinks.Contains(sink)) _sinks.Add(sink);
        }

        /// <inheritdoc />
        public void RemoveSink(ILogSink sink) => _sinks.Remove(sink);

        /// <inheritdoc />
        public void Debug(string tag, string message) => Write(LogLevel.Debug, tag, message);

        /// <inheritdoc />
        public void Info(string tag, string message) => Write(LogLevel.Info, tag, message);

        /// <inheritdoc />
        public void Warn(string tag, string message) => Write(LogLevel.Warn, tag, message);

        /// <inheritdoc />
        public void Error(string tag, string message) => Write(LogLevel.Error, tag, message);

        /// <inheritdoc />
        public void Fatal(string tag, string message) => Write(LogLevel.Fatal, tag, message);

        /// <inheritdoc />
        public void Flush()
        {
            for (int i = 0; i < _sinks.Count; i++) _sinks[i].Flush();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            Flush();
            _sinks.Clear();
        }

        // 热路径：用 for + 索引避免 foreach 迭代器分配
        private void Write(LogLevel level, string tag, string message)
        {
            if (level < MinLevel) return;
            for (int i = 0; i < _sinks.Count; i++) _sinks[i].Write(level, tag, message);
        }
    }
}
