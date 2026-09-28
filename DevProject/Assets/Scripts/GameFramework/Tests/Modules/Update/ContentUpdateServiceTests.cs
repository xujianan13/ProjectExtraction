using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;

namespace GameFramework.Tests.Modules.Update
{
    /// <summary>ContentUpdateService 生命周期、互斥、计划校验、取消和进度转发测试。</summary>
    [TestFixture]
    public sealed class ContentUpdateServiceTests
    {
        private FakeUpdateBackend _backend;
        private ContentUpdateService _service;

        [SetUp]
        public void SetUp()
        {
            _backend = new FakeUpdateBackend();
            _service = new ContentUpdateService(_backend);
            _service.Initialize();
        }

        [TearDown]
        public void TearDown()
        {
            _service.Dispose();
        }

        /// <summary>初始化和关闭应转发到后端且保持幂等。</summary>
        [Test]
        public void Lifecycle_ForwardsToBackendOnce()
        {
            _service.Initialize();
            Assert.AreEqual(1, _backend.InitializeCount);

            _service.Dispose();
            _service.Dispose();
            Assert.AreEqual(1, _backend.ShutdownCount);
        }

        /// <summary>检查成功后模块应分配有效计划 ID。</summary>
        [Test]
        public void CheckAsync_ValidBackendPlan_AssignsPlanId()
        {
            var plan = _service.CheckAsync(new UpdatePackageRequest("UpdaterPackage"))
                .GetAwaiter().GetResult();

            Assert.IsTrue(plan.IsValid);
            Assert.Greater(plan.Id, 0);
            Assert.AreEqual("UpdaterPackage", plan.PackageName);
        }

        /// <summary>有效计划应只执行一次，第二次执行返回 InvalidPlan。</summary>
        [Test]
        public void ExecuteAsync_SamePlanTwice_RejectsSecondExecution()
        {
            var plan = _service.CheckAsync(new UpdatePackageRequest("UpdaterPackage"))
                .GetAwaiter().GetResult();

            var first = _service.ExecuteAsync(plan).GetAwaiter().GetResult();
            var second = _service.ExecuteAsync(plan).GetAwaiter().GetResult();

            Assert.AreEqual(UpdateExecutionStatus.Succeeded, first.Status);
            Assert.AreEqual(UpdateExecutionStatus.Failed, second.Status);
            Assert.AreEqual(UpdateErrorCode.InvalidPlan, second.ErrorCode);
            Assert.AreEqual(1, _backend.ExecuteCount);
        }

        /// <summary>后端抛出取消异常时模块应返回结构化取消结果。</summary>
        [Test]
        public void ExecuteAsync_BackendCancelled_ReturnsCancelledResult()
        {
            var plan = _service.CheckAsync(new UpdatePackageRequest("UpdaterPackage"))
                .GetAwaiter().GetResult();
            _backend.ExecuteHandler = (_, _) =>
                UniTask.FromException<UpdateExecutionResult>(new OperationCanceledException());

            var result = _service.ExecuteAsync(plan).GetAwaiter().GetResult();

            Assert.AreEqual(UpdateExecutionStatus.Cancelled, result.Status);
            Assert.AreEqual(UpdateErrorCode.Cancelled, result.ErrorCode);
        }

        /// <summary>检查运行期间再次发起操作应被互斥保护拒绝。</summary>
        [Test]
        public void CheckAsync_ConcurrentCheck_Throws()
        {
            var completion = new UniTaskCompletionSource<UpdatePlan>();
            _backend.CheckHandler = (_, _) => completion.Task;

            UniTask<UpdatePlan> first = _service.CheckAsync(new UpdatePackageRequest("UpdaterPackage"));
            Assert.Throws<InvalidOperationException>(() =>
                _service.CheckAsync(new UpdatePackageRequest("GamePackage")).GetAwaiter().GetResult());

            completion.TrySetResult(FakeUpdateBackend.CreatePlan("UpdaterPackage"));
            Assert.DoesNotThrow(() => first.GetAwaiter().GetResult());
        }

        /// <summary>后端进度事件应原样转发给模块订阅者。</summary>
        [Test]
        public void ProgressChanged_BackendReports_ForwardsProgress()
        {
            UpdateProgress received = default;
            _service.ProgressChanged += progress => received = progress;

            var expected = new UpdateProgress(
                UpdateProgressPhase.Download,
                50,
                100,
                1,
                2,
                25,
                "bundle.hash",
                0);
            _backend.Report(expected);

            Assert.AreEqual(expected.Phase, received.Phase);
            Assert.AreEqual(expected.DownloadedBytes, received.DownloadedBytes);
            Assert.AreEqual(expected.CurrentFile, received.CurrentFile);
        }

        private sealed class FakeUpdateBackend : IUpdateBackend
        {
            public event Action<UpdateProgress> ProgressChanged;

            public Func<UpdatePackageRequest, CancellationToken, UniTask<UpdatePlan>> CheckHandler { get; set; }

            public Func<UpdatePlan, CancellationToken, UniTask<UpdateExecutionResult>> ExecuteHandler { get; set; }

            public int InitializeCount { get; private set; }

            public int ShutdownCount { get; private set; }

            public int ExecuteCount { get; private set; }

            public void Initialize()
            {
                InitializeCount++;
            }

            public void Shutdown()
            {
                ShutdownCount++;
            }

            public UniTask<UpdatePlan> CheckAsync(
                UpdatePackageRequest request,
                CancellationToken ct = default)
            {
                return CheckHandler != null
                    ? CheckHandler(request, ct)
                    : UniTask.FromResult(CreatePlan(request.PackageName));
            }

            public UniTask<UpdateExecutionResult> ExecuteAsync(
                UpdatePlan plan,
                CancellationToken ct = default)
            {
                ExecuteCount++;
                return ExecuteHandler != null
                    ? ExecuteHandler(plan, ct)
                    : UniTask.FromResult(UpdateExecutionResult.Success(plan.RemoteVersion));
            }

            public void Report(UpdateProgress progress)
            {
                ProgressChanged?.Invoke(progress);
            }

            public static UpdatePlan CreatePlan(string packageName)
            {
                return new UpdatePlan(
                    packageName,
                    "1.0.0",
                    "1.1.0",
                    2,
                    100,
                    false,
                    false,
                    true);
            }
        }
    }
}
