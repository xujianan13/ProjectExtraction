using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using YooAsset;

namespace GameFramework.Tests.Core
{
    /// <summary>
    /// 验证作用域资源扩展在迟到结果和进度回调上的所有权边界。
    /// </summary>
    [TestFixture]
    public sealed class LifetimeResourceTests
    {
        private const string PackageName = "LifetimeResourceTest";
        private ResourceModule _resources;
        private ImmediateBackend _backend;
        private TextAsset _asset;

        /// <summary>
        /// 启动真实资源模块，用于产生可观察释放状态的独立租约。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _asset = new TextAsset("lifetime");
            _backend = new ImmediateBackend(_asset);
            _resources = new ResourceModule(_backend, new ResourceCacheSettings(), PackageName);
            _resources.OnInit();
        }

        /// <summary>
        /// 释放资源模块和测试资产。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            _resources.OnShutdown();
            UnityEngine.Object.DestroyImmediate(_asset);
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>
        /// 已停止作用域不得调用资源模块开始加载。
        /// </summary>
        [Test]
        public void StoppedScope_DoesNotStartLoad()
        {
            using var scope = new LifetimeScope("stopped");
            IResourceModule module = CreateDelayedModule(out DelayedResourceProxy proxy);
            scope.Stop();
            Assert.Throws<OperationCanceledException>(() => scope.AcquireAsync<TextAsset>(module, "asset").GetAwaiter().GetResult());
            Assert.AreEqual(0, proxy.LoadCount);
        }

        /// <summary>
        /// 加载中停止作用域后，迟到的非空租约必须立即释放，不能交付给调用方。
        /// </summary>
        [Test]
        public void StoppedDuringLoad_ReleasesLateLease()
        {
            ResourceLease<TextAsset> lease = CreateLease();
            using var scope = new LifetimeScope("late");
            IResourceModule module = CreateDelayedModule(out DelayedResourceProxy proxy);
            UniTask<ResourceLease<TextAsset>> task = scope.AcquireAsync<TextAsset>(module, "asset");
            Assert.AreEqual(1, proxy.LoadCount);
            scope.Stop();
            proxy.Complete(lease);
            Assert.Throws<OperationCanceledException>(() => task.GetAwaiter().GetResult());
            Assert.IsFalse(lease.IsValid);
            Assert.IsTrue(_backend.Operation.Released);
            Assert.AreEqual(0, scope.RegistrationCount);
        }

        /// <summary>
        /// 调用方取消后，即使作用域仍有效，也不得返回非空或空的成功结果。
        /// </summary>
        [Test]
        public void CallerCancellation_RejectsLateLeaseAndNull()
        {
            ResourceLease<TextAsset> lease = CreateLease();
            using var scope = new LifetimeScope("caller");
            using var firstCancellation = new CancellationTokenSource();
            IResourceModule firstModule = CreateDelayedModule(out DelayedResourceProxy firstProxy);
            UniTask<ResourceLease<TextAsset>> first = scope.AcquireAsync<TextAsset>(firstModule, "asset", ct: firstCancellation.Token);
            firstCancellation.Cancel();
            firstProxy.Complete(lease);
            Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
            Assert.IsTrue(scope.IsActive);
            Assert.IsFalse(lease.IsValid);
            Assert.IsTrue(_backend.Operation.Released);

            using var secondCancellation = new CancellationTokenSource();
            IResourceModule secondModule = CreateDelayedModule(out DelayedResourceProxy secondProxy);
            UniTask<ResourceLease<TextAsset>> second = scope.AcquireAsync<TextAsset>(secondModule, "missing", ct: secondCancellation.Token);
            secondCancellation.Cancel();
            secondProxy.Complete(null);
            Assert.Throws<OperationCanceledException>(() => second.GetAwaiter().GetResult());
            Assert.IsTrue(scope.IsActive);
            Assert.AreEqual(0, scope.RegistrationCount);
        }

        /// <summary>
        /// 作用域停止或调用方取消后，底层迟到进度不再通知业务。
        /// </summary>
        [Test]
        public void Progress_AfterStopOrCallerCancelIsSuppressed()
        {
            using var stoppedScope = new LifetimeScope("progress-stop");
            IResourceModule firstModule = CreateDelayedModule(out DelayedResourceProxy firstProxy);
            var firstValues = new List<float>();
            UniTask<ResourceLease<TextAsset>> first = stoppedScope.AcquireAsync<TextAsset>(firstModule, "asset",
                progress: new InlineProgress(firstValues.Add));
            firstProxy.Report(0.25f);
            stoppedScope.Stop();
            firstProxy.Report(0.75f);
            firstProxy.Complete(null);
            Assert.Throws<OperationCanceledException>(() => first.GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { 0.25f }, firstValues);

            using var activeScope = new LifetimeScope("progress-caller");
            using var cancellation = new CancellationTokenSource();
            IResourceModule secondModule = CreateDelayedModule(out DelayedResourceProxy secondProxy);
            var secondValues = new List<float>();
            UniTask<ResourceLease<TextAsset>> second = activeScope.AcquireAsync<TextAsset>(secondModule, "asset",
                progress: new InlineProgress(secondValues.Add), ct: cancellation.Token);
            secondProxy.Report(0.4f);
            cancellation.Cancel();
            secondProxy.Report(0.9f);
            secondProxy.Complete(null);
            Assert.Throws<OperationCanceledException>(() => second.GetAwaiter().GetResult());
            CollectionAssert.AreEqual(new[] { 0.4f }, secondValues);
            Assert.IsTrue(activeScope.IsActive);
        }

        /// <summary>
        /// 从真实资源模块取得单独租约。
        /// </summary>
        /// <returns>需要由被测适配器释放的租约。</returns>
        private ResourceLease<TextAsset> CreateLease()
            => _resources.AcquireAsync<TextAsset>("asset", ResourceLoadOptions.Transient).GetAwaiter().GetResult();

        /// <summary>
        /// 创建只拦截异步加载入口的资源模块代理。
        /// </summary>
        /// <param name="proxy">用于控制完成时刻的代理实例。</param>
        /// <returns>供作用域扩展调用的资源模块接口。</returns>
        private static IResourceModule CreateDelayedModule(out DelayedResourceProxy proxy)
        {
            IResourceModule module = DispatchProxy.Create<IResourceModule, DelayedResourceProxy>();
            proxy = (DelayedResourceProxy)module;
            return module;
        }

        /// <summary>
        /// 保留进度通知的同步时序。
        /// </summary>
        private sealed class InlineProgress : IProgress<float>
        {
            private readonly Action<float> _report;

            /// <summary>
            /// 创建同步进度接收器。
            /// </summary>
            /// <param name="report">进度通知动作。</param>
            public InlineProgress(Action<float> report) => _report = report;

            /// <summary>
            /// 转发进度。
            /// </summary>
            /// <param name="value">当前进度。</param>
            public void Report(float value) => _report(value);
        }

        /// <summary>
        /// 立即返回测试资产的资源后端。
        /// </summary>
        private sealed class ImmediateBackend : IResourceAssetBackend
        {
            private readonly TextAsset _asset;

            /// <summary>
            /// 保存测试资产。
            /// </summary>
            /// <param name="asset">供租约使用的资产。</param>
            public ImmediateBackend(TextAsset asset) => _asset = asset;

            /// <summary>
            /// 最近一次底层加载操作。
            /// </summary>
            public ImmediateOperation Operation { get; private set; }

            /// <summary>
            /// 判断测试包是否有效。
            /// </summary>
            /// <param name="packageName">包名。</param>
            /// <returns>是否为测试包。</returns>
            public bool IsPackageReady(string packageName) => packageName == PackageName;

            /// <summary>
            /// 返回已完成的资产操作。
            /// </summary>
            /// <typeparam name="T">资产类型。</typeparam>
            /// <param name="packageName">包名。</param>
            /// <param name="location">资源地址。</param>
            /// <returns>已完成的操作。</returns>
            public IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object
            {
                Operation = new ImmediateOperation(_asset);
                return Operation;
            }

            /// <summary>
            /// 测试后端无需执行卸载。
            /// </summary>
            /// <param name="packageName">包名。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成操作。</returns>
            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct) => UniTask.CompletedTask;
        }

        /// <summary>
        /// 记录底层释放结果的即时资源操作。
        /// </summary>
        private sealed class ImmediateOperation : IResourceAssetOperation
        {
            /// <summary>
            /// 创建已完成操作。
            /// </summary>
            /// <param name="asset">加载的资产。</param>
            public ImmediateOperation(UnityEngine.Object asset) => Asset = asset;

            /// <summary>
            /// 操作是否有效。
            /// </summary>
            public bool IsValid => !Released;

            /// <summary>
            /// 操作是否完成。
            /// </summary>
            public bool IsDone => true;

            /// <summary>
            /// 完成进度。
            /// </summary>
            public float Progress => 1f;

            /// <summary>
            /// 成功状态。
            /// </summary>
            public EOperationStatus Status => EOperationStatus.Succeeded;

            /// <summary>
            /// 错误文本。
            /// </summary>
            public string Error => string.Empty;

            /// <summary>
            /// 资产对象。
            /// </summary>
            public UnityEngine.Object Asset { get; }

            /// <summary>
            /// 是否已经释放。
            /// </summary>
            public bool Released { get; private set; }

            /// <summary>
            /// 等待已经完成的操作。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成任务。</returns>
            public UniTask WaitAsync(CancellationToken ct) => UniTask.CompletedTask;

            /// <summary>
            /// 标记底层句柄释放。
            /// </summary>
            public void Release() => Released = true;
        }
    }

    /// <summary>
    /// 人工控制异步返回时刻的资源模块代理。
    /// </summary>
    public class DelayedResourceProxy : DispatchProxy
    {
        private readonly UniTaskCompletionSource<ResourceLease<TextAsset>> _completion = new();
        private IProgress<float> _progress;

        /// <summary>
        /// 扩展方法发起的加载次数。
        /// </summary>
        public int LoadCount { get; private set; }

        /// <summary>
        /// 仅接收测试使用的 TextAsset 租约请求。
        /// </summary>
        /// <param name="targetMethod">被调用的接口方法。</param>
        /// <param name="args">调用参数。</param>
        /// <returns>由测试控制完成时刻的异步结果。</returns>
        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name != nameof(IResourceModule.AcquireAsync) || targetMethod.GetGenericArguments()[0] != typeof(TextAsset))
                throw new NotSupportedException(targetMethod.Name);
            int offset = args.Length == 5 ? 1 : 0;
            _progress = (IProgress<float>)args[2 + offset];
            LoadCount++;
            return _completion.Task;
        }

        /// <summary>
        /// 触发底层进度回调。
        /// </summary>
        /// <param name="value">报告的进度。</param>
        public void Report(float value) => _progress?.Report(value);

        /// <summary>
        /// 完成异步请求。
        /// </summary>
        /// <param name="lease">返回的租约，允许为空。</param>
        public void Complete(ResourceLease<TextAsset> lease) => _completion.TrySetResult(lease);
    }
}
