using GameFramework.Tests.Mocks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Log
{
    /// <summary>LogModule 日志级别过滤、Sink 管理与生命周期测试。</summary>
    [TestFixture]
    public sealed class LogModuleTests
    {
        private LogModule _module;
        private MockLogSink _sink;

        [SetUp]
        public void SetUp()
        {
            _module = new LogModule();
            _sink = new MockLogSink();
            _module.AddSink(_sink);
        }

        /// <summary>低于 MinLevel 的级别 IsEnabled 应为 false。</summary>
        [Test]
        public void IsEnabled_RespectsMinLevel()
        {
            _module.MinLevel = LogLevel.Warn;
            Assert.IsFalse(_module.IsEnabled(LogLevel.Debug));
            Assert.IsFalse(_module.IsEnabled(LogLevel.Info));
            Assert.IsTrue(_module.IsEnabled(LogLevel.Warn));
            Assert.IsTrue(_module.IsEnabled(LogLevel.Error));
            Assert.IsFalse(_module.IsEnabled(LogLevel.Off));
        }

        /// <summary>低于 MinLevel 的日志不应写入 Sink。</summary>
        [Test]
        public void MinLevel_FiltersLowerLevelMessages()
        {
            _module.MinLevel = LogLevel.Info;
            _module.Debug("Test", "debug msg");
            _module.Info("Test", "info msg");
            Assert.AreEqual(1, _sink.Entries.Count);
            Assert.AreEqual(LogLevel.Info, _sink.Entries[0].Level);
        }

        /// <summary>一条日志应广播到所有已注册 Sink。</summary>
        [Test]
        public void Write_BroadcastsToAllSinks()
        {
            var sink2 = new MockLogSink();
            _module.AddSink(sink2);
            _module.Warn("Test", "warn msg");
            Assert.AreEqual(1, _sink.Entries.Count);
            Assert.AreEqual(1, sink2.Entries.Count);
        }

        /// <summary>重复 AddSink 同一实例应被忽略。</summary>
        [Test]
        public void AddSink_DuplicateSink_Ignored()
        {
            _module.AddSink(_sink);
            _module.Info("Test", "msg");
            Assert.AreEqual(1, _sink.Entries.Count);
        }

        /// <summary>RemoveSink 后该 Sink 不再接收日志。</summary>
        [Test]
        public void RemoveSink_StopsBroadcasting()
        {
            _module.RemoveSink(_sink);
            _module.Info("Test", "msg");
            Assert.AreEqual(0, _sink.Entries.Count);
        }

        /// <summary>Flush 应调用所有 Sink 的 Flush。</summary>
        [Test]
        public void Flush_CallsAllSinks()
        {
            var sink2 = new MockLogSink();
            _module.AddSink(sink2);
            _module.Flush();
            Assert.AreEqual(1, _sink.FlushCount);
            Assert.AreEqual(1, sink2.FlushCount);
        }

        /// <summary>OnShutdown 应先 Flush 再清空 Sink 列表。</summary>
        [Test]
        public void OnShutdown_FlushesAndClearsSinks()
        {
            _module.OnShutdown();
            _module.Info("Test", "msg");
            Assert.AreEqual(1, _sink.FlushCount);
            Assert.AreEqual(0, _sink.Entries.Count);
        }

        /// <summary>LogLevel 枚举值应按严重程度递增。</summary>
        [Test]
        public void LogLevel_OrderIsAscending()
        {
            Assert.Less(LogLevel.Debug, LogLevel.Info);
            Assert.Less(LogLevel.Info, LogLevel.Warn);
            Assert.Less(LogLevel.Warn, LogLevel.Error);
            Assert.Less(LogLevel.Error, LogLevel.Fatal);
            Assert.Less(LogLevel.Fatal, LogLevel.Off);
        }
    }
}
