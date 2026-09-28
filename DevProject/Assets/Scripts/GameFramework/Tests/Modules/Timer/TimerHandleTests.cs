using NUnit.Framework;

namespace GameFramework.Tests.Modules.Timer
{
    /// <summary>TimerHandle 结构体有效性与相等性测试。</summary>
    [TestFixture]
    public sealed class TimerHandleTests
    {
        /// <summary>默认句柄 Id=0，应视为无效。</summary>
        [Test]
        public void Default_IsInvalid()
        {
            var handle = default(TimerHandle);
            Assert.IsFalse(handle.IsValid);
        }

        /// <summary>非零 Id 的句柄应有效。</summary>
        [Test]
        public void NonZero_IsValid()
        {
            Assert.IsTrue(new TimerHandle(1).IsValid);
        }

        /// <summary>相同 Id 的两个句柄应相等。</summary>
        [Test]
        public void Equals_SameId_ReturnsTrue()
        {
            var a = new TimerHandle(5);
            var b = new TimerHandle(5);
            Assert.IsTrue(a == b);
        }

        /// <summary>ToString 应包含 Id 值。</summary>
        [Test]
        public void ToString_ContainsId()
        {
            Assert.AreEqual("TimerHandle(3)", new TimerHandle(3).ToString());
        }
    }
}
