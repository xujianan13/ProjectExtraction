using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>
    /// 验证跨平台游戏标识的选择规则。
    /// </summary>
    public sealed class ApplicationIdentityTests
    {
        /// <summary>
        /// 平台应用标识存在时保持原值。
        /// </summary>
        [Test]
        public void Resolve_UsesRuntimeIdentifierWhenPresent()
        {
            Assert.AreEqual("com.example.game", ApplicationIdentity.Resolve("com.example.game", "Studio", "Game"));
        }

        /// <summary>
        /// Windows Standalone 未提供标识时使用公司名和产品名。
        /// </summary>
        [Test]
        public void Resolve_FallsBackToCompanyAndProductWhenIdentifierMissing()
        {
            Assert.AreEqual("Studio.Game", ApplicationIdentity.Resolve(string.Empty, "Studio", "Game"));
        }

        /// <summary>
        /// 超长中文名称生成稳定且满足存档身份限制的标识。
        /// </summary>
        [Test]
        public void Resolve_HashesLongCompanyAndProduct()
        {
            string company = new string('公', 80);
            string product = new string('游', 80);
            string result = ApplicationIdentity.Resolve(string.Empty, company, product);
            Assert.AreEqual(result, ApplicationIdentity.Resolve(null, company, product));
            Assert.IsTrue(result.StartsWith("app-"));
            Assert.IsTrue(new SaveIdentity(result, "release", "guest", "main").IsValid);
            Assert.AreNotEqual(result, ApplicationIdentity.Resolve(string.Empty, company, product + "戏"));
        }

        /// <summary>
        /// 超长平台应用标识同样生成稳定且有效的标识。
        /// </summary>
        [Test]
        public void Resolve_HashesLongRuntimeIdentifier()
        {
            string identifier = new string('a', 255);
            string result = ApplicationIdentity.Resolve(identifier, "Studio", "Game");
            Assert.AreEqual(result, ApplicationIdentity.Resolve(identifier, "Other", "Product"));
            Assert.IsTrue(result.StartsWith("app-"));
            Assert.IsTrue(new SaveIdentity(result, "release", "guest", "main").IsValid);
        }
    }
}
