using System;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>Package Settings JSON 解析与兼容性校验测试。</summary>
    [TestFixture]
    public sealed class PackageRuntimeSettingsTests
    {
        /// <summary>有效配置应保留入口、下载策略和首场景。</summary>
        [Test]
        public void ValidJson_PreservesAtomicPackageSettings()
        {
            const string json = "{\"_schemaVersion\":1," +
                                "\"_assemblyName\":\"GameLogic\"," +
                                "\"_entryTypeName\":\"GameLogic.GameMain\"," +
                                "\"_entryMethodName\":\"StartAsync\"," +
                                "\"_startupSceneLocation\":\"StartupFlowTest\"," +
                                "\"_downloadMaxConcurrency\":4," +
                                "\"_retryCount\":2," +
                                "\"_timeoutSeconds\":30}";
            PackageRuntimeSettings settings = PackageRuntimeSettings.FromJson(json);

            bool valid = settings.TryValidate(1, 1, "1.0.0", out string error);

            Assert.That(valid, Is.True, error);
            Assert.That(settings.AssemblyName, Is.EqualTo("GameLogic"));
            Assert.That(settings.StartupSceneLocation, Is.EqualTo("StartupFlowTest"));
            Assert.That(settings.DownloadMaxConcurrency, Is.EqualTo(4));
        }

        /// <summary>高于 BootKernel 支持范围的 Schema 必须被拒绝。</summary>
        [Test]
        public void FutureSchema_IsRejectedByOlderBootKernel()
        {
            const string json = "{\"_schemaVersion\":2," +
                                "\"_assemblyName\":\"UpdateLogic\"," +
                                "\"_entryTypeName\":\"UpdateLogic.UpdateMain\"," +
                                "\"_entryMethodName\":\"StartAsync\"," +
                                "\"_downloadMaxConcurrency\":8," +
                                "\"_retryCount\":3," +
                                "\"_timeoutSeconds\":60}";
            PackageRuntimeSettings settings = PackageRuntimeSettings.FromJson(json);

            bool valid = settings.TryValidate(1, 1, "1.0.0", out string error);

            Assert.That(valid, Is.False);
            Assert.That(error, Does.Contain("Schema"));
        }

        /// <summary>JSON 缺少可选下载参数时应保留 AOT DTO 的明确默认值。</summary>
        [Test]
        public void MissingOptionalDownloadFields_UsesExplicitDefaults()
        {
            const string json = "{\"_schemaVersion\":1," +
                                "\"_assemblyName\":\"UpdateLogic\"," +
                                "\"_entryTypeName\":\"UpdateLogic.UpdateMain\"," +
                                "\"_entryMethodName\":\"StartAsync\"}";

            PackageRuntimeSettings settings = PackageRuntimeSettings.FromJson(json);

            Assert.That(
                settings.DownloadMaxConcurrency,
                Is.EqualTo(UpdatePackageRequest.DefaultDownloadMaxConcurrency));
            Assert.That(settings.RetryCount, Is.EqualTo(UpdatePackageRequest.DefaultRetryCount));
            Assert.That(settings.TimeoutSeconds, Is.EqualTo(UpdatePackageRequest.DefaultTimeoutSeconds));
            Assert.That(settings.AllowOfflineGameFallback, Is.True);
        }

        /// <summary>完整固定字段集合应由严格入口精确解析。</summary>
        [Test]
        public void StrictJson_CompleteSchemaPasses()
        {
            PackageRuntimeSettings settings =
                PackageRuntimeSettings.FromJsonStrict(CreateStrictJson());

            Assert.That(settings.AssemblyName, Is.EqualTo("GameLogic"));
            Assert.That(settings.RequiredLocations.Count, Is.EqualTo(1));
            Assert.That(settings.AllowOfflineGameFallback, Is.True);
        }

        /// <summary>严格入口不得接受同名字段的解析器优先级歧义。</summary>
        [Test]
        public void StrictJson_DuplicateFieldIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_schemaVersion\":1,",
                "\"_schemaVersion\":1,\"_schemaVersion\":1,");

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("字段重复", exception.Message);
            StringAssert.Contains("_schemaVersion", exception.Message);
        }

        /// <summary>协议外字段不得被 JsonUtility 静默忽略。</summary>
        [Test]
        public void StrictJson_UnknownFieldIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_schemaVersion\":1,",
                "\"_schemaVersion\":1,\"unexpected\":true,");

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("未知字段", exception.Message);
            StringAssert.Contains("unexpected", exception.Message);
        }

        /// <summary>严格发布配置必须显式给出全部字段，不能依赖 DTO 默认值。</summary>
        [Test]
        public void StrictJson_MissingFieldIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_maximumHostVersion\":\"\",",
                string.Empty);

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("缺少必需字段", exception.Message);
            StringAssert.Contains("_maximumHostVersion", exception.Message);
        }

        /// <summary>数值字段不得接受字符串等替代类型。</summary>
        [Test]
        public void StrictJson_WrongValueTypeIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_timeoutSeconds\":60",
                "\"_timeoutSeconds\":\"60\"");

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("必须是整数", exception.Message);
            StringAssert.Contains("_timeoutSeconds", exception.Message);
        }

        /// <summary>整数配置不得接受小数或指数表示。</summary>
        [Test]
        public void StrictJson_FractionalIntegerIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_retryCount\":3",
                "\"_retryCount\":3.0");

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("不能使用小数或指数", exception.Message);
        }

        /// <summary>字段名不得以 Unicode 转义伪装成受信字段。</summary>
        [Test]
        public void StrictJson_EscapedFieldNameIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_schemaVersion\"",
                "\"_schema\\u0056ersion\"");

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("字段名不得使用转义", exception.Message);
        }

        /// <summary>根对象后的第二段 JSON 或其他尾随内容必须被拒绝。</summary>
        [Test]
        public void StrictJson_TrailingContentIsRejected()
        {
            string json = CreateStrictJson() + "{}";

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("尾随内容", exception.Message);
        }

        /// <summary>地址集合只能包含字符串，不能混入 null、数字或对象。</summary>
        [Test]
        public void StrictJson_NonStringArrayItemIsRejected()
        {
            string json = CreateStrictJson().Replace(
                "\"_requiredLocations\":[\"StartupFlowTest\"]",
                "\"_requiredLocations\":[null]");

            FormatException exception = Assert.Throws<FormatException>(
                () => PackageRuntimeSettings.FromJsonStrict(json));

            StringAssert.Contains("字符串数组", exception.Message);
        }

        private static string CreateStrictJson()
        {
            return "{" +
                   "\"_schemaVersion\":1," +
                   "\"_assemblyName\":\"GameLogic\"," +
                   "\"_entryTypeName\":\"GameLogic.GameMain\"," +
                   "\"_entryMethodName\":\"StartAsync\"," +
                   "\"_aotMetadataLocations\":[]," +
                   "\"_requiredLocations\":[\"StartupFlowTest\"]," +
                   "\"_startupSceneLocation\":\"StartupFlowTest\"," +
                   "\"_minimumHostVersion\":\"\"," +
                   "\"_maximumHostVersion\":\"\"," +
                   "\"_defaultHostServer\":\"\"," +
                   "\"_fallbackHostServer\":\"\"," +
                   "\"_downloadMaxConcurrency\":8," +
                   "\"_retryCount\":3," +
                   "\"_timeoutSeconds\":60," +
                   "\"_allowOfflineGameFallback\":true" +
                   "}";
        }
    }
}
