using System;
using System.Linq;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Diagnostics
{
    /// <summary>
    /// 输入阻断来源与句柄生命周期测试。
    /// </summary>
    [TestFixture]
    public sealed class InputDiagnosticsTests
    {
        /// <summary>
        /// 两个相同理由的阻断句柄应独立释放。
        /// </summary>
        [Test]
        public void DuplicateReasons_ReleaseIndependently()
        {
            var input = new InputModule();
            IDisposable first = input.BlockGameplay("menu");
            IDisposable second = input.BlockGameplay("menu");
            var section = new DiagnosticSection();
            input.CollectDiagnostics(section);
            Assert.AreEqual("2", section.Values.Find(value => value.Name == "ExplicitBlocks").Value);
            Assert.AreEqual(2, section.Values.Count(value => value.Name == "Block0" || value.Name == "Block1"));

            first.Dispose();
            section = new DiagnosticSection();
            input.CollectDiagnostics(section);
            Assert.AreEqual("1", section.Values.Find(value => value.Name == "ExplicitBlocks").Value);
            second.Dispose();
            section = new DiagnosticSection();
            input.CollectDiagnostics(section);
            Assert.AreEqual("0", section.Values.Find(value => value.Name == "ExplicitBlocks").Value);
        }

        /// <summary>
        /// 关闭前的旧句柄不得减少新生命周期中的阻断。
        /// </summary>
        [Test]
        public void OldHandle_AfterShutdown_DoesNotReleaseNewBlock()
        {
            var input = new InputModule();
            IDisposable old = input.BlockGameplay("old");
            input.OnShutdown();
            IDisposable current = input.BlockGameplay("new");
            old.Dispose();
            var section = new DiagnosticSection();
            input.CollectDiagnostics(section);
            Assert.AreEqual("1", section.Values.Find(value => value.Name == "ExplicitBlocks").Value);
            Assert.IsTrue(section.Values.Any(value => value.Name == "Block0" && value.Value == "new"));
            current.Dispose();
        }
    }
}
