using System;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>UpdatePackageRequest 默认值与参数校验测试。</summary>
    [TestFixture]
    public sealed class UpdatePackageRequestTests
    {
        /// <summary>只提供包名时应使用稳定默认参数。</summary>
        [Test]
        public void Constructor_OnlyPackageName_UsesDefaults()
        {
            var request = new UpdatePackageRequest("UpdaterPackage");

            Assert.AreEqual(UpdatePackageRequest.DefaultChannel, request.Channel);
            Assert.AreEqual(UpdatePackageRequest.DefaultDownloadMaxConcurrency, request.DownloadMaxConcurrency);
            Assert.AreEqual(UpdatePackageRequest.DefaultRetryCount, request.RetryCount);
            Assert.AreEqual(UpdatePackageRequest.DefaultTimeoutSeconds, request.TimeoutSeconds);
        }

        /// <summary>空包名应抛出参数异常。</summary>
        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Constructor_EmptyPackageName_Throws(string packageName)
        {
            Assert.Throws<ArgumentException>(() => new UpdatePackageRequest(packageName));
        }

        /// <summary>非正数并发数应抛出范围异常。</summary>
        [Test]
        public void Constructor_NonPositiveConcurrency_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new UpdatePackageRequest("UpdaterPackage", downloadMaxConcurrency: 0));
        }
    }
}
