using System.Collections.Generic;

namespace GameFramework.Tests.Mocks
{
    /// <summary>
    /// 记录日志写入的 mock Sink，供 LogModule 测试使用。
    /// </summary>
    internal sealed class MockLogSink : ILogSink
    {
        public readonly struct LogEntry
        {
            public LogLevel Level { get; }
            public string Tag { get; }
            public string Message { get; }

            public LogEntry(LogLevel level, string tag, string message)
            {
                Level = level;
                Tag = tag;
                Message = message;
            }
        }

        public readonly List<LogEntry> Entries = new(8);
        public int FlushCount { get; private set; }

        public void Write(LogLevel level, string tag, string message)
        {
            Entries.Add(new LogEntry(level, tag, message));
        }

        public void Flush() => FlushCount++;
    }
}
