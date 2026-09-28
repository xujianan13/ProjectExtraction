using System;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>
    /// 验证启动标签筛选不会悄然退回整包下载，且异步请求保持不可变。
    /// </summary>
    public sealed class UpdateDownloadSelectionTests
    {
        /// <summary>
        /// 旧调用方未指定标签时保持整包差量预下载行为。
        /// </summary>
        [Test]
        public void UnspecifiedTagsKeepFullDownload()
        {
            var request = new UpdatePackageRequest("GamePackage");
            Assert.IsNull(request.DownloadTags);
            Assert.IsNull(YooAssetUpdateBackend.CreateDownloaderOptions(request).Tags);
        }

        /// <summary>
        /// 请求与 YooAsset 选项各自持有副本，后续修改不会扩大下载范围。
        /// </summary>
        [Test]
        public void TagsAreDeduplicatedAndIsolatedFromCallerAndDownloader()
        {
            var tags = new[] { "Startup", "Level01", "Startup" };
            var request = new UpdatePackageRequest("GamePackage", downloadMaxConcurrency: 2, retryCount: 1, downloadTags: tags);
            tags[0] = "Optional";
            var options = YooAssetUpdateBackend.CreateDownloaderOptions(request);
            CollectionAssert.AreEqual(new[] { "Startup", "Level01" }, options.Tags);
            Assert.AreEqual(2, options.MaximumConcurrency);
            Assert.AreEqual(1, options.RetryCount);
            options.Tags[0] = "Optional";
            CollectionAssert.AreEqual(new[] { "Startup", "Level01" }, request.DownloadTags);
            CollectionAssert.AreEqual(request.DownloadTags, YooAssetUpdateBackend.CreateDownloaderOptions(request).Tags);
        }

        /// <summary>
        /// 空标签集合必须明确报错，防止误将未配置当成有效启动计划。
        /// </summary>
        [Test]
        public void EmptySelectionIsRejected()
        {
            Assert.Throws<ArgumentException>(() => new UpdatePackageRequest("GamePackage", downloadTags: Array.Empty<string>()));
        }

        /// <summary>
        /// 拒绝空白标签，避免配置错误造成预下载遗漏。
        /// </summary>
        /// <param name="tag">无效标签。</param>
        [TestCase(null)]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase(" Startup")]
        public void InvalidTagIsRejected(string tag)
        {
            Assert.Throws<ArgumentException>(() => new UpdatePackageRequest("GamePackage", downloadTags: new[] { tag }));
        }
    }
}
