using System;
using System.Security.Cryptography;
using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>验证本地开发证书信任不会扩展到非 TLS 或非回环地址。</summary>
    public sealed class LoopbackTlsCertificatePinTests
    {
        /// <summary>仅 DER 证书的精确 SHA-256 指纹可以通过。</summary>
        [Test]
        public void MatchesOnlyExactCertificateBytes()
        {
            byte[] certificate = { 1, 3, 3, 7 };
            string fingerprint;
            using (SHA256 sha = SHA256.Create())
                fingerprint = BitConverter.ToString(sha.ComputeHash(certificate)).Replace("-", string.Empty);
            var pin = new LoopbackTlsCertificatePin(fingerprint);

            Assert.That(pin.Matches(certificate), Is.True);
            Assert.That(pin.Matches(new byte[] { 1, 3, 3, 8 }), Is.False);
        }

        /// <summary>接受使用 TLS 且不携带凭据的回环端点。</summary>
        [TestCase("https://127.0.0.1:18443/api/public/v1/bootstrap")]
        [TestCase("wss://localhost:18444/game/v1/connect")]
        public void AcceptsOnlyTlsLoopbackEndpoints(string endpoint)
        {
            var pin = CreatePin();
            Assert.DoesNotThrow(() => pin.ValidateEndpoint(new Uri(endpoint)));
        }

        /// <summary>拒绝明文、非回环及在 URI 中携带凭据的端点。</summary>
        [TestCase("http://127.0.0.1:18443/api/public/v1/bootstrap")]
        [TestCase("ws://127.0.0.1:18444/game/v1/connect")]
        [TestCase("https://example.com/api/public/v1/bootstrap")]
        [TestCase("https://user@127.0.0.1:18443/api/public/v1/bootstrap")]
        public void RejectsTrustBroadeningEndpoints(string endpoint)
        {
            var pin = CreatePin();
            Assert.Throws<ArgumentException>(() => pin.ValidateEndpoint(new Uri(endpoint)));
        }

        private static LoopbackTlsCertificatePin CreatePin() =>
            new LoopbackTlsCertificatePin(new string('0', 64));
    }
}
