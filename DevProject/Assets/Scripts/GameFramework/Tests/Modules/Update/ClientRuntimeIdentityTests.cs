using System;
using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>覆盖实际激活版本身份与 Debug 公开选项的 Fail Closed 校验。</summary>
    public sealed class ClientRuntimeIdentityTests
    {
        [Test]
        public void IdentityPreservesEveryIndependentVersionAxis()
        {
            var identity = new ClientRuntimeIdentity(
                "official.android",
                "android",
                "host-1",
                "updater-2",
                "game-3",
                4,
                5);

            Assert.That(identity.Channel, Is.EqualTo("official.android"));
            Assert.That(identity.ClientPlatform, Is.EqualTo("android"));
            Assert.That(identity.HostVersion, Is.EqualTo("host-1"));
            Assert.That(identity.UpdaterVersion, Is.EqualTo("updater-2"));
            Assert.That(identity.GameVersion, Is.EqualTo("game-3"));
            Assert.That(identity.ProtocolVersion, Is.EqualTo(4));
            Assert.That(identity.RuntimeConfigVersion, Is.EqualTo(5));
        }

        [Test]
        public void IdentityAndDirectOptionsRejectAmbiguousValues()
        {
            Assert.Throws<ArgumentException>(() => new ClientRuntimeIdentity(
                " official",
                "android",
                "host",
                "updater",
                "game",
                1,
                1));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ClientRuntimeIdentity(
                    "official",
                    "android",
                    "host",
                    "updater",
                    "game",
                    0,
                    1));
            Assert.Throws<ArgumentException>(() =>
                new DevelopmentDirectLoginOptions(true, "not-a-sha256"));
        }
    }
}
