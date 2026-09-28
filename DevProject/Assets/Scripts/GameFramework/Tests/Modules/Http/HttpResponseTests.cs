using System.Collections.Generic;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Http
{
    /// <summary>HttpResponse 成功判定、Body 解析与 Headers 保留测试。</summary>
    [TestFixture]
    public sealed class HttpResponseTests
    {
        /// <summary>2xx 且无 Error 时 IsSuccess 应为 true。</summary>
        [Test]
        public void IsSuccess_2xxWithoutError_ReturnsTrue()
        {
            var response = new HttpResponse(200, new byte[] { 1 }, null, null);
            Assert.IsTrue(response.IsSuccess);
        }

        /// <summary>非 2xx 状态码时 IsSuccess 应为 false。</summary>
        [Test]
        public void IsSuccess_Non2xx_ReturnsFalse()
        {
            var response = new HttpResponse(404, null, null, null);
            Assert.IsFalse(response.IsSuccess);
        }

        /// <summary>2xx 但带 Error 描述时 IsSuccess 应为 false。</summary>
        [Test]
        public void IsSuccess_WithError_ReturnsFalse()
        {
            var response = new HttpResponse(200, null, null, "connection failed");
            Assert.IsFalse(response.IsSuccess);
        }

        /// <summary>StatusCode=0 且带 Error 表示连接级失败。</summary>
        [Test]
        public void IsSuccess_ZeroStatusWithError_ReturnsFalse()
        {
            var response = new HttpResponse(0, null, null, "timeout");
            Assert.IsFalse(response.IsSuccess);
        }

        /// <summary>Body 为 null 或空时 GetBodyAsString 应返回空串。</summary>
        [Test]
        public void GetBodyAsString_EmptyBody_ReturnsEmpty()
        {
            Assert.AreEqual(string.Empty, new HttpResponse(200, null, null, null).GetBodyAsString());
            Assert.AreEqual(string.Empty, new HttpResponse(200, System.Array.Empty<byte>(), null, null).GetBodyAsString());
        }

        /// <summary>GetBodyAsString 应按 UTF-8 解码 Body。</summary>
        [Test]
        public void GetBodyAsString_Utf8Body_ReturnsString()
        {
            var body = System.Text.Encoding.UTF8.GetBytes("hello");
            Assert.AreEqual("hello", new HttpResponse(200, body, null, null).GetBodyAsString());
        }

        /// <summary>构造时传入的 Headers 应只读保留。</summary>
        [Test]
        public void Headers_ArePreserved()
        {
            var headers = new Dictionary<string, string> { { "Content-Type", "application/json" } };
            var response = new HttpResponse(200, null, headers, null);
            Assert.AreEqual("application/json", response.Headers["Content-Type"]);
        }
    }
}
