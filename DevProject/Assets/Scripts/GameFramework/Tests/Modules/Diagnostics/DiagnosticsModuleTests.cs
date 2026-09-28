using System;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Diagnostics
{
    /// <summary>
    /// 诊断快照隔离、来源登记和内存日志边界测试。
    /// </summary>
    [TestFixture]
    public sealed class DiagnosticsModuleTests
    {
        private DiagnosticsModule _module;

        /// <summary>
        /// 为每项测试创建独立诊断模块。
        /// </summary>
        [SetUp]
        public void SetUp() => _module = new DiagnosticsModule { IsLogCaptureEnabled = true };

        /// <summary>
        /// 释放来源和内存日志。
        /// </summary>
        [TearDown]
        public void TearDown() => _module.OnShutdown();

        /// <summary>
        /// 缺失模块显示不可用，采集不会启动框架或改变宿主状态。
        /// </summary>
        [Test]
        public void Snapshot_MissingModules_AreUnavailableAndReadOnly()
        {
            bool running = GameEntry.IsRunning;
            bool hasHost = GameEntry.HasHost;
            DiagnosticSnapshot snapshot = _module.CaptureSnapshot();
            Assert.AreEqual(running, GameEntry.IsRunning);
            Assert.AreEqual(hasHost, GameEntry.HasHost);
            Assert.IsTrue(snapshot.Sections.Any(section => section.Name == "Framework"));
            if (GameEntry.Get<ISceneModule>() == null)
                Assert.IsTrue(snapshot.Sections.Find(section => section.Name == "Scene").Values.Any(value => value.Value == "Unavailable"));
            Assert.IsNotEmpty(_module.ExportJson());
        }

        /// <summary>
        /// 来源异常、释放和重入均不影响其他来源或已返回快照。
        /// </summary>
        [Test]
        public void Providers_AreIsolatedAndDisposed()
        {
            var retained = new RetainingProvider();
            var broken = new ThrowingProvider();
            var nested = new ReentrantProvider(_module);
            IDisposable retainedRegistration = _module.RegisterProvider(retained);
            IDisposable brokenRegistration = _module.RegisterProvider(broken);
            IDisposable nestedRegistration = _module.RegisterProvider(nested);
            DiagnosticSnapshot snapshot = _module.CaptureSnapshot();
            Assert.AreEqual("ready", snapshot.Sections.Find(section => section.Name == "Retained").Values[0].Value);
            Assert.AreEqual("采集失败：InvalidOperationException", snapshot.Sections.Find(section => section.Name == "Broken").Error);
            Assert.IsTrue(nested.NestedSnapshot.Sections.Any(section => section.Name == "Diagnostics" && section.Error != null));
            retained.Section.Values[0].Value = "changed";
            Assert.AreEqual("ready", snapshot.Sections.Find(section => section.Name == "Retained").Values[0].Value);
            retainedRegistration.Dispose();
            brokenRegistration.Dispose();
            nestedRegistration.Dispose();
            Assert.IsFalse(_module.CaptureSnapshot().Sections.Any(section => section.Name == "Retained" || section.Name == "Broken"));
        }

        /// <summary>
        /// 日志环仅保留最近条目，线程写入后仍满足容量和长度限制。
        /// </summary>
        [Test]
        public void Logs_AreBoundedAcrossThreadsAndShutdown()
        {
            _module.LogCapacity = 3;
            Parallel.For(0, 100, i => _module.Write(LogLevel.Info, "Test", new string('x', 5000) + i));
            DiagnosticSnapshot snapshot = _module.CaptureSnapshot();
            Assert.AreEqual(3, snapshot.Logs.Count);
            Assert.IsTrue(snapshot.Logs.All(entry => entry.Message.Length <= 4096));
            _module.OnShutdown();
            _module.Write(LogLevel.Info, "Test", "late");
            Assert.IsEmpty(_module.CaptureSnapshot().Logs);
        }

        /// <summary>
        /// 保留填充对象的来源，用于验证快照深拷贝。
        /// </summary>
        private sealed class RetainingProvider : IDiagnosticProvider
        {
            internal DiagnosticSection Section;
            public string Name => "Retained";

            /// <summary>
            /// 填充并保留区域引用。
            /// </summary>
            /// <param name="section">诊断区域。</param>
            public void Collect(DiagnosticSection section) { Section = section; section.Add("State", "ready"); }
        }

        /// <summary>
        /// 始终抛出异常的来源。
        /// </summary>
        private sealed class ThrowingProvider : IDiagnosticProvider
        {
            public string Name => "Broken";

            /// <summary>
            /// 模拟来源采集失败。
            /// </summary>
            /// <param name="section">诊断区域。</param>
            public void Collect(DiagnosticSection section) => throw new InvalidOperationException();
        }

        /// <summary>
        /// 在采集回调中重复采集的来源。
        /// </summary>
        private sealed class ReentrantProvider : IDiagnosticProvider
        {
            private readonly DiagnosticsModule _module;
            internal DiagnosticSnapshot NestedSnapshot;
            public string Name => "Reentrant";

            /// <summary>
            /// 创建重入来源。
            /// </summary>
            /// <param name="module">目标诊断模块。</param>
            internal ReentrantProvider(DiagnosticsModule module) => _module = module;

            /// <summary>
            /// 在回调中读取嵌套快照。
            /// </summary>
            /// <param name="section">诊断区域。</param>
            public void Collect(DiagnosticSection section) => NestedSnapshot = _module.CaptureSnapshot();
        }
    }
}
