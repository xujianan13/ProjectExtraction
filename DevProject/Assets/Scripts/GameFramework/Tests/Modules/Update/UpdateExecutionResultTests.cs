using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>UpdateExecutionResult 状态工厂与重试语义测试。</summary>
    [TestFixture]
    public sealed class UpdateExecutionResultTests
    {
        /// <summary>等待重启结果应保留当前版本和待激活版本。</summary>
        [Test]
        public void RestartRequired_WithPendingVersion_PreservesVersions()
        {
            var result = UpdateExecutionResult.RestartRequired("1.0.0", "1.1.0");

            Assert.AreEqual(UpdateExecutionStatus.RestartRequired, result.Status);
            Assert.IsTrue(result.RequiresRestart);
            Assert.AreEqual("1.0.0", result.ActivatedVersion);
            Assert.AreEqual("1.1.0", result.PendingVersion);
        }

        /// <summary>延迟激活结果应成功并要求 BootKernel 在当前启动切换目标版本。</summary>
        [Test]
        public void Prepared_WithPendingVersion_RequiresBootActivation()
        {
            var result = UpdateExecutionResult.Prepared("1.0.0", "1.1.0");

            Assert.AreEqual(UpdateExecutionStatus.Prepared, result.Status);
            Assert.IsTrue(result.IsSuccess);
            Assert.IsTrue(result.RequiresActivation);
            Assert.IsFalse(result.RequiresRestart);
            Assert.AreEqual("1.1.0", result.PendingVersion);
        }

        /// <summary>网络失败应保留稳定错误码和可重试标志。</summary>
        [Test]
        public void Failure_NetworkUnavailable_CanRetry()
        {
            var result = UpdateExecutionResult.Failure(
                "1.0.0",
                UpdateErrorCode.NetworkUnavailable,
                "offline",
                true);

            Assert.AreEqual(UpdateExecutionStatus.Failed, result.Status);
            Assert.AreEqual(UpdateErrorCode.NetworkUnavailable, result.ErrorCode);
            Assert.IsTrue(result.CanRetry);
        }

        /// <summary>取消后的下载器不能复用，因此应重新检查而不是直接重试原计划。</summary>
        [Test]
        public void Cancelled_Default_DoesNotRetrySamePlan()
        {
            var result = UpdateExecutionResult.Cancelled("1.0.0");

            Assert.AreEqual(UpdateExecutionStatus.Cancelled, result.Status);
            Assert.IsFalse(result.CanRetry);
        }
    }
}
