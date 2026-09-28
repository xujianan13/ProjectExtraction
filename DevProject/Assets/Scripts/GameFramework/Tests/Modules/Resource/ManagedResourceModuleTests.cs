using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using YooAsset;

namespace GameFramework.Tests.Modules.Resource
{
    /// <summary>验证业务资源租约、SingleFlight、LRU、常驻和资源组回滚。</summary>
    [TestFixture]
    public sealed class ManagedResourceModuleTests
    {
        private FakeResourceBackend _backend;
        private ResourceModule _module;
        private readonly List<UnityEngine.Object> _createdAssets = new();

        /// <summary>创建带测试资源包的资源模块。</summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _backend = new FakeResourceBackend();
            _module = new ResourceModule(
                _backend,
                new ResourceCacheSettings(
                    maxIdleEntries: 8,
                    maxEstimatedIdleBytes: -1,
                    maxIdleSeconds: 60f,
                    maintenanceIntervalSeconds: 1f,
                    maxEvictionsPerMaintenance: 8),
                FakeResourceBackend.PackageName);
            _module.OnInit();
        }

        /// <summary>释放缓存句柄和测试创建的 Unity 对象。</summary>
        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            for (int index = _createdAssets.Count - 1; index >= 0; index--)
                UnityEngine.Object.DestroyImmediate(_createdAssets[index]);
            _createdAssets.Clear();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>同一资源的并发请求只加载一次，并为每个调用方返回独立租约。</summary>
        [Test]
        public void AcquireAsync_DuplicateInflight_LoadsOnceAndReturnsIndependentLeases()
        {
            UniTask<ResourceLease<TextAsset>> firstTask = _module.AcquireAsync<TextAsset>("shared");
            UniTask<ResourceLease<TextAsset>> secondTask = _module.AcquireAsync<TextAsset>("shared");
            Assert.AreEqual(1, _backend.LoadCount);

            TextAsset asset = CreateTextAsset("shared");
            _backend.GetLatest("shared").Succeed(asset);
            ResourceLease<TextAsset> first = firstTask.GetAwaiter().GetResult();
            ResourceLease<TextAsset> second = secondTask.GetAwaiter().GetResult();

            Assert.AreSame(asset, first.Asset);
            Assert.AreSame(asset, second.Asset);
            first.Dispose();
            Assert.IsTrue(second.IsValid, "释放一个调用方租约不得使另一个租约失效。");
            second.Dispose();

            Assert.IsTrue(_module.TryAcquire<TextAsset>("shared", out ResourceLease<TextAsset> cached));
            Assert.AreEqual(1, _backend.LoadCount, "内存命中不得再次触发底层加载。");
            cached.Dispose();
        }

        /// <summary>
        /// 显式指定已就绪的包时，无默认包也可以加载并释放资源。
        /// </summary>
        [Test]
        public void AcquireAsync_ExplicitPackageWithoutDefault_LoadsAndReleases()
        {
            _module.OnShutdown();
            _module = new ResourceModule(_backend, new ResourceCacheSettings(), null);
            _module.OnInit();
            UniTask<ResourceLease<TextAsset>> task = _module.AcquireAsync<TextAsset>(FakeResourceBackend.PackageName, "explicit");
            TextAsset asset = CreateTextAsset("explicit");
            _backend.GetLatest("explicit").Succeed(asset);
            ResourceLease<TextAsset> lease = task.GetAwaiter().GetResult();
            Assert.AreSame(asset, lease.Asset);
            Assert.IsNull(_module.DefaultPackageName);
            lease.Dispose();
            _module.TrimAsync(true).GetAwaiter().GetResult();
            Assert.IsTrue(_backend.GetLatest("explicit").Released);
            Assert.Throws<InvalidOperationException>(() => _module.AcquireAsync<TextAsset>("default").GetAwaiter().GetResult());
        }

        /// <summary>一个等待者取消不会取消仍在等待同一资源的其他调用方。</summary>
        [Test]
        public void AcquireAsync_OneWaiterCanceled_DoesNotCancelSharedLoad()
        {
            using var canceled = new CancellationTokenSource();
            UniTask<ResourceLease<TextAsset>> canceledTask =
                _module.AcquireAsync<TextAsset>("cancel-shared", ct: canceled.Token);
            UniTask<ResourceLease<TextAsset>> survivorTask =
                _module.AcquireAsync<TextAsset>("cancel-shared");

            canceled.Cancel();
            TextAsset asset = CreateTextAsset("survivor");
            _backend.GetLatest("cancel-shared").Succeed(asset);

            Assert.Throws<OperationCanceledException>(() => canceledTask.GetAwaiter().GetResult());
            ResourceLease<TextAsset> survivor = survivorTask.GetAwaiter().GetResult();
            Assert.AreSame(asset, survivor.Asset);
            Assert.AreEqual(1, _backend.LoadCount);
            survivor.Dispose();
        }

        /// <summary>底层同步完成时也必须先把结果交给当前等待者，再按 Transient 策略释放。</summary>
        [Test]
        public void AcquireAsync_SynchronouslyCompletedTransient_ReturnsValidLease()
        {
            TextAsset asset = CreateTextAsset("synchronous");
            _backend.CompleteOnLoadAsset = asset;

            ResourceLease<TextAsset> lease = _module
                .AcquireAsync<TextAsset>("synchronous", ResourceLoadOptions.Transient)
                .GetAwaiter()
                .GetResult();
            FakeResourceOperation operation = _backend.GetLatest("synchronous");

            Assert.AreSame(asset, lease.Asset);
            Assert.IsFalse(operation.Released);
            lease.Dispose();
            Assert.IsTrue(operation.Released);
        }


        /// <summary>失败缓存项会被删除，后续请求可以重新发起底层加载。</summary>
        [Test]
        public void AcquireAsync_FailedLoad_CanRetry()
        {
            UniTask<ResourceLease<TextAsset>> failedTask = _module.AcquireAsync<TextAsset>("retry");
            _backend.GetLatest("retry").Fail("expected failure");
            ResourceLoadException failure = Assert.Throws<ResourceLoadException>(
                () => failedTask.GetAwaiter().GetResult());
            Assert.AreEqual(ResourceLoadError.FailureKind.LoadFailed, failure.Error.Kind);

            UniTask<ResourceLease<TextAsset>> retryTask = _module.AcquireAsync<TextAsset>("retry");
            TextAsset asset = CreateTextAsset("retry-success");
            _backend.GetLatest("retry").Succeed(asset);
            ResourceLease<TextAsset> retry = retryTask.GetAwaiter().GetResult();

            Assert.AreEqual(2, _backend.LoadCount);
            Assert.AreSame(asset, retry.Asset);
            retry.Dispose();
        }

        /// <summary>LRU 超预算时淘汰最早进入空闲状态的资源。</summary>
        [Test]
        public void OnUpdate_OverLruBudget_EvictsLeastRecentlyUsedEntry()
        {
            _module.OnShutdown();
            _module = new ResourceModule(
                _backend,
                new ResourceCacheSettings(
                    maxIdleEntries: 1,
                    maxEstimatedIdleBytes: -1,
                    maxIdleSeconds: 60f,
                    maintenanceIntervalSeconds: 1f,
                    maxEvictionsPerMaintenance: 8),
                FakeResourceBackend.PackageName);
            _module.OnInit();

            ResourceLease<TextAsset> first = CompleteAcquire("lru-first", "first");
            first.Dispose();
            ResourceLease<TextAsset> second = CompleteAcquire("lru-second", "second");
            second.Dispose();
            _module.OnUpdate(1.1f);

            Assert.IsFalse(_module.TryAcquire<TextAsset>("lru-first", out _));
            Assert.IsTrue(_module.TryAcquire<TextAsset>("lru-second", out ResourceLease<TextAsset> retained));
            retained.Dispose();
            Assert.GreaterOrEqual(_backend.UnloadCount, 1);
        }

        /// <summary>常驻租约存续期间即使空闲预算为零也不能被淘汰。</summary>
        [Test]
        public void ResidentLease_PreventsEvictionUntilDisposed()
        {
            _module.OnShutdown();
            _module = new ResourceModule(
                _backend,
                new ResourceCacheSettings(
                    maxIdleEntries: 0,
                    maxEstimatedIdleBytes: -1,
                    maxIdleSeconds: 0f,
                    maintenanceIntervalSeconds: 1f,
                    maxEvictionsPerMaintenance: 8),
                FakeResourceBackend.PackageName);
            _module.OnInit();

            UniTask<ResourceLease<TextAsset>> task = _module.AcquireAsync<TextAsset>(
                "resident",
                ResourceLoadOptions.Resident);
            _backend.GetLatest("resident").Succeed(CreateTextAsset("resident"));
            ResourceLease<TextAsset> resident = task.GetAwaiter().GetResult();

            _module.OnUpdate(1.1f);
            Assert.IsTrue(resident.IsValid);
            resident.Dispose();
            _module.OnUpdate(1.1f);
            Assert.IsFalse(_module.TryAcquire<TextAsset>("resident", out _));
        }

        /// <summary>AllOrNothing 资源组失败时释放本组已经取得的成功租约。</summary>
        [Test]
        public void AcquireGroupAsync_AllOrNothingFailure_RollsBackSuccessfulLeases()
        {
            var definition = new ResourceGroupDefinition(
                "atomic",
                new[]
                {
                    new ResourceGroupDefinition.Item(
                        "first",
                        "group-first",
                        ResourceGroupDefinition.AssetKind.TextAsset,
                        ResourceLoadOptions.RetentionMode.Transient),
                    new ResourceGroupDefinition.Item(
                        "second",
                        "group-second",
                        ResourceGroupDefinition.AssetKind.TextAsset,
                        ResourceLoadOptions.RetentionMode.Transient)
                },
                ResourceGroupDefinition.FailurePolicy.AllOrNothing,
                maxConcurrency: 2);

            UniTask<ResourceGroupLease> groupTask = _module.AcquireGroupAsync(definition);
            FakeResourceOperation first = _backend.GetLatest("group-first");
            FakeResourceOperation second = _backend.GetLatest("group-second");
            first.Succeed(CreateTextAsset("group-first"));
            second.Fail("group failure");

            ResourceGroupLoadException failure = Assert.Throws<ResourceGroupLoadException>(
                () => groupTask.GetAwaiter().GetResult());
            Assert.AreEqual(1, failure.Failures.Count);
            Assert.IsTrue(first.Released, "原子组失败后必须回滚已经成功的临时租约。");
            Assert.IsTrue(second.Released, "失败的底层句柄必须释放。");
        }

        /// <summary>共享加载进度由模块集中广播，并保证成功时最终到达 1。</summary>
        [Test]
        public void AcquireAsync_Progress_IsBroadcastAndCompletesAtOne()
        {
            var values = new List<float>();
            var progress = new InlineProgress(values.Add);
            UniTask<ResourceLease<TextAsset>> task = _module.AcquireAsync<TextAsset>("progress", progress: progress);
            FakeResourceOperation operation = _backend.GetLatest("progress");
            operation.SetProgress(0.4f);
            _module.OnUpdate(0.1f);
            operation.Succeed(CreateTextAsset("progress"));
            ResourceLease<TextAsset> lease = task.GetAwaiter().GetResult();

            CollectionAssert.Contains(values, 0.4f);
            Assert.AreEqual(1f, values[values.Count - 1]);
            lease.Dispose();
        }

        private ResourceLease<TextAsset> CompleteAcquire(string location, string value)
        {
            UniTask<ResourceLease<TextAsset>> task = _module.AcquireAsync<TextAsset>(location);
            _backend.GetLatest(location).Succeed(CreateTextAsset(value));
            return task.GetAwaiter().GetResult();
        }

        private TextAsset CreateTextAsset(string value)
        {
            var asset = new TextAsset(value);
            _createdAssets.Add(asset);
            return asset;
        }

        private sealed class InlineProgress : IProgress<float>
        {
            private readonly Action<float> _callback;

            public InlineProgress(Action<float> callback)
            {
                _callback = callback;
            }

            public void Report(float value)
            {
                _callback(value);
            }
        }

        private sealed class FakeResourceBackend : IResourceAssetBackend
        {
            public const string PackageName = "TestPackage";

            private readonly Dictionary<string, List<FakeResourceOperation>> _operations = new(StringComparer.Ordinal);

            public int LoadCount { get; private set; }
            public int UnloadCount { get; private set; }
            public UnityEngine.Object CompleteOnLoadAsset { get; set; }

            public bool IsPackageReady(string packageName)
            {
                return string.Equals(packageName, PackageName, StringComparison.Ordinal);
            }

            public IResourceAssetOperation LoadAsync<T>(string packageName, string location)
                where T : UnityEngine.Object
            {
                var operation = new FakeResourceOperation();
                if (!_operations.TryGetValue(location, out List<FakeResourceOperation> operations))
                {
                    operations = new List<FakeResourceOperation>();
                    _operations.Add(location, operations);
                }
                operations.Add(operation);
                LoadCount++;
                if (CompleteOnLoadAsset != null)
                {
                    UnityEngine.Object asset = CompleteOnLoadAsset;
                    CompleteOnLoadAsset = null;
                    operation.Succeed(asset);
                }
                return operation;
            }

            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                UnloadCount++;
                return UniTask.CompletedTask;
            }

            public FakeResourceOperation GetLatest(string location)
            {
                Assert.IsTrue(_operations.TryGetValue(location, out List<FakeResourceOperation> operations));
                Assert.IsNotEmpty(operations);
                return operations[operations.Count - 1];
            }
        }

        private sealed class FakeResourceOperation : IResourceAssetOperation
        {
            private readonly UniTaskCompletionSource _completion = new();

            public bool IsValid => !Released;
            public bool IsDone { get; private set; }
            public float Progress { get; private set; }
            public EOperationStatus Status { get; private set; }
            public string Error { get; private set; } = string.Empty;
            public UnityEngine.Object Asset { get; private set; }
            public bool Released { get; private set; }

            public UniTask WaitAsync(CancellationToken ct)
            {
                return ct.CanBeCanceled
                    ? _completion.Task.AttachExternalCancellation(ct)
                    : _completion.Task;
            }

            public void Release()
            {
                Released = true;
            }

            public void SetProgress(float value)
            {
                Progress = value;
            }

            public void Succeed(UnityEngine.Object asset)
            {
                Asset = asset;
                Progress = 1f;
                Status = EOperationStatus.Succeeded;
                IsDone = true;
                _completion.TrySetResult();
            }

            public void Fail(string error)
            {
                Error = error;
                Progress = 1f;
                Status = EOperationStatus.Failed;
                IsDone = true;
                _completion.TrySetResult();
            }
        }
    }
}
