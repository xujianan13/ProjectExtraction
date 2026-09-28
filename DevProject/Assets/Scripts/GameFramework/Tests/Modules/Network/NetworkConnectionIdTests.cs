using NUnit.Framework;

namespace GameFramework.Tests.Modules.Network
{
    /// <summary>NetworkConnectionId 结构体有效性与相等性测试。</summary>
    [TestFixture]
    public sealed class NetworkConnectionIdTests
    {
        /// <summary>默认 Value=0 应视为无效句柄。</summary>
        [Test]
        public void Default_IsInvalid()
        {
            Assert.IsFalse(default(NetworkConnectionId).IsValid);
        }

        /// <summary>非零 Value 应视为有效句柄。</summary>
        [Test]
        public void NonZero_IsValid()
        {
            Assert.IsTrue(new NetworkConnectionId(1).IsValid);
        }

        /// <summary>相同 Value 的两个 NetworkConnectionId 应相等。</summary>
        [Test]
        public void Equals_SameValue_ReturnsTrue()
        {
            Assert.IsTrue(new NetworkConnectionId(5) == new NetworkConnectionId(5));
        }

        /// <summary>ToString 应包含 Value。</summary>
        [Test]
        public void ToString_ContainsValue()
        {
            Assert.AreEqual("NetworkConnectionId(3)", new NetworkConnectionId(3).ToString());
        }

        /// <summary>整型应能隐式转换为 NetworkConnectionId。</summary>
        [Test]
        public void ImplicitConversion_FromInt_CreatesNetworkConnectionId()
        {
            NetworkConnectionId id = 42;
            Assert.AreEqual(42, id.Value);
            Assert.IsTrue(id == new NetworkConnectionId(42));
        }

        /// <summary>NetworkConnectionId 应能显式转换为整型。</summary>
        [Test]
        public void ExplicitConversion_ToInt_ReturnsValue()
        {
            var id = new NetworkConnectionId(99);
            Assert.AreEqual(99, (int)id);
        }
    }
}
