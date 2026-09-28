using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Modules.Resource
{
    /// <summary>
    /// 验证 Bundle 预下载的能力、清单、取消与失败结果。
    /// </summary>
    [TestFixture]
    public sealed class ResourceDownloadTests
    {
        private const string PackageName = "DownloadTest";
        private ResourceModule _module;
        private FakeDownloadBackend _backend;

        /// <summary>
        /// 创建资源模块和可控下载器。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _backend = new FakeDownloadBackend();
            _module = new ResourceModule(new FakeAssetBackend(), new ResourceCacheSettings(), PackageName, _backend);
            _module.OnInit();
        }

        /// <summary>
        /// 关闭模块。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>
        /// 未声明包的落盘能力时明确返回不支持，且不创建下载器。
        /// </summary>
        [Test]
        public void DownloadAsync_UnconfiguredPackage_IsUnsupported()
        {
            ResourceDownloadResult result = _module.DownloadAsync(ResourceDownloadRequest.ForLocations(new[] { "hero" })).GetAwaiter().GetResult();
            Assert.AreEqual(ResourceDownloadStatus.Unsupported, result.Status);
            Assert.AreEqual(0, _backend.CreateCount);
        }

        /// <summary>
        /// 空目标不能退化为整包下载。
        /// </summary>
        [Test]
        public void DownloadAsync_EmptyTargets_FailsBeforeCreatingDownloader()
        {
            _module.ConfigureDownloadSupport(PackageName, true);
            ResourceDownloadResult result = _module.DownloadAsync(ResourceDownloadRequest.ForTags(Array.Empty<string>())).GetAwaiter().GetResult();
            Assert.AreEqual(ResourceDownloadStatus.Failure, result.Status);
            Assert.AreEqual(0, _backend.CreateCount);
        }

        /// <summary>
        /// 底层失败应保留失败状态与错误。
        /// </summary>
        [Test]
        public void DownloadAsync_BackendFailure_ReturnsFailure()
        {
            _module.ConfigureDownloadSupport(PackageName, true);
            _backend.Operation.IsDone = true;
            _backend.Operation.Succeeded = false;
            _backend.Operation.Error = "network error";
            ResourceDownloadResult result = _module.DownloadAsync(ResourceDownloadRequest.ForLocations(new[] { "hero" })).GetAwaiter().GetResult();
            Assert.AreEqual(ResourceDownloadStatus.Failure, result.Status);
            Assert.AreEqual("network error", result.Error);
            Assert.IsTrue(result.CanRetry);
            Assert.AreEqual(1, _backend.CreateCount);
        }

        /// <summary>
        /// 调用方修改输入或返回数组不会改变已建立的请求。
        /// </summary>
        [Test]
        public void DownloadRequest_CopiesAndDeduplicatesTargets()
        {
            string[] locations = { "hero", "hero" };
            ResourceDownloadRequest request = ResourceDownloadRequest.ForLocations(locations);
            locations[0] = "changed";
            request.Locations[0] = "also changed";
            CollectionAssert.AreEqual(new[] { "hero" }, request.Locations);
        }
        /// <summary>
        /// 调用方取消会取消本次底层下载。
        /// </summary>
        /// <returns>等待下载结束的测试协程。</returns>
        [UnityTest]
        public IEnumerator DownloadAsync_Cancel_CancelsDownloader()
        {
            _module.ConfigureDownloadSupport(PackageName, true);
            using var cancellation = new CancellationTokenSource();
            UniTask<ResourceDownloadResult> task = _module.DownloadAsync(ResourceDownloadRequest.ForLocations(new[] { "hero" }), ct: cancellation.Token);
            Assert.IsTrue(_backend.Operation.Started);
            cancellation.Cancel();
            yield return null;
            Assert.AreEqual(ResourceDownloadStatus.Cancelled, task.GetAwaiter().GetResult().Status);
            Assert.IsTrue(_backend.Operation.Canceled);
        }

        /// <summary>
        /// 模块关闭会取消正在进行的下载。
        /// </summary>
        /// <returns>等待下载结束的测试协程。</returns>
        [UnityTest]
        public IEnumerator DownloadAsync_Shutdown_CancelsDownloader()
        {
            _module.ConfigureDownloadSupport(PackageName, true);
            UniTask<ResourceDownloadResult> task = _module.DownloadAsync(ResourceDownloadRequest.ForLocations(new[] { "hero" }));
            _module.OnShutdown();
            yield return null;
            Assert.AreEqual(ResourceDownloadStatus.Cancelled, task.GetAwaiter().GetResult().Status);
            Assert.IsTrue(_backend.Operation.Canceled);
        }

        /// <summary>
        /// 激活清单变化使已启动下载失败。
        /// </summary>
        /// <returns>等待下载结束的测试协程。</returns>
        [UnityTest]
        public IEnumerator DownloadAsync_ManifestChange_CancelsOldDownloader()
        {
            _module.ConfigureDownloadSupport(PackageName, true);
            UniTask<ResourceDownloadResult> task = _module.DownloadAsync(ResourceDownloadRequest.ForLocations(new[] { "hero" }));
            _backend.Version = "v2";
            yield return null;
            Assert.AreEqual(ResourceDownloadStatus.Failure, task.GetAwaiter().GetResult().Status);
            Assert.IsTrue(_backend.Operation.Canceled);
        }

        /// <summary>
        /// 最终进度回调可同步取消、关闭模块或改变清单，不得报告旧请求成功。
        /// </summary>
        /// <param name="change">零表示调用方取消，一表示模块关闭，二表示清单变化。</param>
        [TestCase(0)]
        [TestCase(1)]
        [TestCase(2)]
        public void DownloadAsync_FinalProgressReentrancy_DoesNotReturnSuccess(int change)
        {
            _module.ConfigureDownloadSupport(PackageName, true);
            _backend.Operation.IsDone = true;
            using var cancellation = new CancellationTokenSource();
            var progress = new ImmediateProgress(() =>
            {
                if (change == 0) cancellation.Cancel();
                else if (change == 1) _module.OnShutdown();
                else _backend.Version = "v2";
            });
            ResourceDownloadResult result = _module.DownloadAsync(ResourceDownloadRequest.ForLocations(new[] { "hero" }),
                progress, cancellation.Token).GetAwaiter().GetResult();
            Assert.AreEqual(change == 2 ? ResourceDownloadStatus.Failure : ResourceDownloadStatus.Cancelled, result.Status);
            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(result.CanRetry);
        }

        /// <summary>
        /// 在报告进度的当前调用栈执行回调。
        /// </summary>
        private sealed class ImmediateProgress : IProgress<ResourceDownloadProgress>
        {
            private readonly Action _callback;
            /// <summary>
            /// 保存同步回调。
            /// </summary>
            /// <param name="callback">回调操作。</param>
            public ImmediateProgress(Action callback) => _callback = callback;
            /// <summary>
            /// 执行本次同步回调。
            /// </summary>
            /// <param name="value">当前下载进度。</param>
            public void Report(ResourceDownloadProgress value) => _callback();
        }

        private sealed class FakeAssetBackend : IResourceAssetBackend
        {
            /// <inheritdoc />
            public bool IsPackageReady(string packageName) => true;
            /// <inheritdoc />
            public IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object => throw new NotSupportedException();
            /// <inheritdoc />
            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct) => UniTask.CompletedTask;
        }

        private sealed class FakeDownloadBackend : IResourceDownloadBackend
        {
            public string Version = "v1";
            public int CreateCount;
            public readonly FakeDownloadOperation Operation = new();
            /// <inheritdoc />
            public bool IsPackageReady(string packageName) => packageName == PackageName;
            /// <inheritdoc />
            public string GetPackageVersion(string packageName) => Version;
            /// <inheritdoc />
            public bool TryCreate(string packageName, ResourceDownloadRequest request, out IResourceDownloadOperation operation, out string error)
            {
                CreateCount++;
                operation = Operation;
                error = null;
                return true;
            }
        }

        private sealed class FakeDownloadOperation : IResourceDownloadOperation
        {
            public bool IsDone { get; set; }
            public bool Succeeded { get; set; } = true;
            public string Error { get; set; }
            public bool Started { get; private set; }
            public bool Canceled { get; private set; }
            public int TotalCount => 2;
            public ResourceDownloadProgress Progress => new(0, 2, 0, 100);
            /// <inheritdoc />
            public void Start() => Started = true;
            /// <inheritdoc />
            public void Cancel()
            {
                Canceled = true;
                IsDone = true;
            }
        }
    }
}
