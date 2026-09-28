using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>UpdatePlan 状态与模块计划 ID 测试。</summary>
    [TestFixture]
    public sealed class UpdatePlanTests
    {
        /// <summary>后端直接创建的计划尚未分配 ID，因此不可执行。</summary>
        [Test]
        public void Constructor_BackendPlan_IsNotExecutableYet()
        {
            var plan = new UpdatePlan(
                "GamePackage",
                "1.0.0",
                "1.1.0",
                1,
                100,
                true,
                false,
                true);

            Assert.AreEqual(0, plan.Id);
            Assert.IsFalse(plan.IsValid);
            Assert.IsTrue(plan.HasUpdate);
            Assert.IsTrue(plan.IsMandatory);
        }
    }
}
