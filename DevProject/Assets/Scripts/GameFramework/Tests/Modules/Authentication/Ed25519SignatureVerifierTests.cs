using System;
using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>验证托管 Ed25519 实现遵循 RFC 8032 并拒绝非规范输入。</summary>
    public sealed class Ed25519SignatureVerifierTests
    {
        /// <summary>标准向量应通过，任一签名字节变化后应拒绝。</summary>
        [Test]
        public void VerifyAcceptsRfc8032VectorAndRejectsMutation()
        {
            byte[] publicKey = Hex(
                "d75a980182b10ab7d54bfed3c964073a" +
                "0ee172f3daa62325af021a68f707511a");
            byte[] signature = Hex(
                "e5564300c360ac729086e2cc806e828a" +
                "84877f1eb8e5d974d873e06522490155" +
                "5fb8821590a33bacc61e39701cf9b46b" +
                "d25bf5f0595bbe24655141438e7a100b");
            var verifier = new Ed25519SignatureVerifier();

            Assert.That(verifier.Verify(publicKey, Array.Empty<byte>(), signature), Is.True);
            signature[0] ^= 0x01;
            Assert.That(verifier.Verify(publicKey, Array.Empty<byte>(), signature), Is.False);
        }

        /// <summary>拒绝不能作为生产信任锚的小阶单位元公钥。</summary>
        [Test]
        public void VerifyRejectsIdentityPublicKey()
        {
            var identity = new byte[32];
            identity[0] = 1;
            Assert.That(
                new Ed25519SignatureVerifier().Verify(identity, Array.Empty<byte>(), new byte[64]),
                Is.False);
        }

        private static byte[] Hex(string value)
        {
            var bytes = new byte[value.Length / 2];
            for (int index = 0; index < bytes.Length; index++)
                bytes[index] = Convert.ToByte(value.Substring(index * 2, 2), 16);
            return bytes;
        }
    }
}
