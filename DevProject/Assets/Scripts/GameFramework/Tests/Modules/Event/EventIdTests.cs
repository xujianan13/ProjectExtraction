using NUnit.Framework;

namespace GameFramework.Tests.Modules.Event
{
    /// <summary>EventId 结构体相等性与格式化测试。</summary>
    [TestFixture]
    public sealed class EventIdTests
    {
        /// <summary>相同 Value 的两个 EventId 应相等。</summary>
        [Test]
        public void Equals_SameValue_ReturnsTrue()
        {
            var a = new EventId(42);
            var b = new EventId(42);
            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a == b);
        }

        /// <summary>不同 Value 的两个 EventId 应不相等。</summary>
        [Test]
        public void Equals_DifferentValue_ReturnsFalse()
        {
            var a = new EventId(1);
            var b = new EventId(2);
            Assert.IsFalse(a.Equals(b));
            Assert.IsTrue(a != b);
        }

        /// <summary>相同 Value 的 GetHashCode 应一致。</summary>
        [Test]
        public void GetHashCode_SameValue_ReturnsSameHash()
        {
            Assert.AreEqual(new EventId(99).GetHashCode(), new EventId(99).GetHashCode());
        }

        /// <summary>ToString 应包含整型 Value。</summary>
        [Test]
        public void ToString_ContainsValue()
        {
            Assert.AreEqual("EventId(7)", new EventId(7).ToString());
        }

        /// <summary>整型应能隐式转换为 EventId。</summary>
        [Test]
        public void ImplicitConversion_FromInt_CreatesEventId()
        {
            EventId id = 42;
            Assert.AreEqual(42, id.Value);
            Assert.IsTrue(id == new EventId(42));
        }

        /// <summary>EventId 应能显式转换为整型。</summary>
        [Test]
        public void ExplicitConversion_ToInt_ReturnsValue()
        {
            var id = new EventId(99);
            Assert.AreEqual(99, (int)id);
        }
    }
}
