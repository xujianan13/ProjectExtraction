using System;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>UpdateProgress 归一化进度与边界校验测试。</summary>
    [TestFixture]
    public sealed class UpdateProgressTests
    {
        /// <summary>同时存在字节数和文件数时应优先使用字节进度。</summary>
        [Test]
        public void NormalizedProgress_WithBytes_UsesByteRatio()
        {
            var progress = new UpdateProgress(
                UpdateProgressPhase.Download,
                25,
                100,
                2,
                4,
                10,
                null,
                0);

            Assert.AreEqual(0.25f, progress.NormalizedProgress);
        }

        /// <summary>空下载完成阶段应返回完整进度。</summary>
        [Test]
        public void NormalizedProgress_CompletedWithoutFiles_ReturnsOne()
        {
            var progress = new UpdateProgress(
                UpdateProgressPhase.Completed,
                0,
                0,
                0,
                0,
                0,
                null,
                0);

            Assert.AreEqual(1f, progress.NormalizedProgress);
        }

        /// <summary>已下载字节超过总字节数应抛出范围异常。</summary>
        [Test]
        public void Constructor_DownloadedBytesAboveTotal_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new UpdateProgress(
                UpdateProgressPhase.Download,
                101,
                100,
                0,
                0,
                0,
                null,
                0));
        }
    }
}
