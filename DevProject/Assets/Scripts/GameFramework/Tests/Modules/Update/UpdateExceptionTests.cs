using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>UpdateException 稳定错误码和重试语义测试。</summary>
    [TestFixture]
    public sealed class UpdateExceptionTests
    {
        /// <summary>基础设施异常应保留稳定错误码和重试标志。</summary>
        [Test]
        public void Constructor_NetworkError_PreservesStructuredData()
        {
            var exception = new UpdateException(
                UpdateErrorCode.NetworkUnavailable,
                "offline",
                true);

            Assert.AreEqual(UpdateErrorCode.NetworkUnavailable, exception.ErrorCode);
            Assert.IsTrue(exception.CanRetry);
            Assert.AreEqual("offline", exception.Message);
        }
    }
}
