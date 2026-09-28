using System;
using System.Collections;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameFramework.Tests.UI
{
    /// <summary>
    /// 验证统一异步 UI 加载反馈的等待、重试和取消行为。
    /// </summary>
    public sealed class GameUILoadingTests
    {
        private UIModule _ui;

        /// <summary>
        /// 清理测试创建的 UI 模块。
        /// </summary>
        /// <returns>等待运行时对象销毁的协程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _ui?.OnShutdown();
            _ui = null;
            yield return null;
        }

        /// <summary>
        /// 快速完成的操作不显示等待层。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator FastOperation_DoesNotFlashBusy()
        {
            _ui = new UIModule();
            _ui.OnInit();
            var result = _ui.RunWithLoadingAsync<int>(_ => UniTask.FromResult(7)).AsTask();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.AreEqual(7, result.Result);
            Assert.IsFalse(BusyLayer().activeSelf);
        }

        /// <summary>
        /// 长操作到达延迟后显示等待层，完成后立即释放。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator SlowOperation_ShowsBusyAfterDelay()
        {
            _ui = new UIModule();
            _ui.OnInit();
            var completion = new UniTaskCompletionSource<int>();
            var result = _ui.RunWithLoadingAsync<int>(_ => completion.Task,
                new UILoadOptions { BusyDelayMilliseconds = 40 }).AsTask();
            Assert.IsFalse(BusyLayer().activeSelf);
            yield return new WaitForSecondsRealtime(0.08f);
            Assert.IsTrue(BusyLayer().activeSelf);
            completion.TrySetResult(5);
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.AreEqual(5, result.Result);
            Assert.IsFalse(BusyLayer().activeSelf);
        }

        /// <summary>
        /// 实际资源失败释放等待层后显示可点击的重试对话框。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator LoadFailure_ReleasesBusyBeforeRetryDialog()
        {
            _ui = new UIModule();
            _ui.OnInit();
            int attempts = 0;
            var options = new UILoadOptions { BusyDelayMilliseconds = 0 };
            var result = _ui.RunWithLoadingAsync<int>(_ =>
            {
                attempts++;
                return attempts == 1
                    ? UniTask.FromException<int>(new ResourceLoadException(CreateError(ResourceLoadError.FailureKind.LoadFailed)))
                    : UniTask.FromResult(11);
            }, options).AsTask();

            yield return new WaitUntil(() => Dialog() != null);
            Assert.IsFalse(BusyLayer().activeSelf);
            Dialog().Find("Panel/Confirm").GetComponent<Button>().onClick.Invoke();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.AreEqual(11, result.Result);
            Assert.AreEqual(2, attempts);
            Assert.IsFalse(BusyLayer().activeSelf);
        }

        /// <summary>
        /// 全失败资源组可重试，而包含不可恢复错误的资源组直接返回异常。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator ResourceGroup_RetriesOnlyRecoverableFailures()
        {
            _ui = new UIModule();
            _ui.OnInit();
            int attempts = 0;
            var result = _ui.RunWithLoadingAsync<int>(_ =>
            {
                attempts++;
                return attempts == 1
                    ? UniTask.FromException<int>(new ResourceGroupLoadException("Test",
                        new[] { CreateError(ResourceLoadError.FailureKind.LoadFailed) }))
                    : UniTask.FromResult(3);
            }).AsTask();
            yield return new WaitUntil(() => Dialog() != null);
            Dialog().Find("Panel/Confirm").GetComponent<Button>().onClick.Invoke();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.AreEqual(3, result.Result);
            Assert.AreEqual(2, attempts);
            yield return null;

            var invalid = _ui.RunWithLoadingAsync<int>(_ =>
                UniTask.FromException<int>(new ResourceGroupLoadException("Invalid",
                    new[] { CreateError(ResourceLoadError.FailureKind.PackageNotReady) }))).AsTask();
            yield return new WaitUntil(() => invalid.IsCompleted);
            Assert.IsTrue(invalid.IsFaulted);
            Assert.IsNull(Dialog());
        }

        /// <summary>
        /// 业务异常不进入资源失败重试流程。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator BusinessException_DoesNotRetry()
        {
            _ui = new UIModule();
            _ui.OnInit();
            int attempts = 0;
            var result = _ui.RunWithLoadingAsync<int>(_ =>
            {
                attempts++;
                return UniTask.FromException<int>(new InvalidOperationException("业务初始化失败"));
            }).AsTask();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.IsTrue(result.IsFaulted);
            Assert.IsInstanceOf<InvalidOperationException>(result.Exception.InnerException);
            Assert.AreEqual(1, attempts);
            Assert.IsNull(Dialog());
            Assert.IsFalse(BusyLayer().activeSelf);
        }

        /// <summary>
        /// 窗口业务初始化抛出的资源异常不会被误判为 Prefab 加载失败。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        public IEnumerator WindowInitializationResourceException_DoesNotRetry()
        {
            _ui = new UIModule();
            _ui.OnInit();
            BusinessFailingWindow.Attempts = 0;
            var config = new GameWindowConfig(1, nameof(BusinessFailingWindow), nameof(BusinessFailingWindow), null, 10,
                GameWindowScope.Global, GameWindowCacheMode.Immediate, 0f, false, false, Color.clear);
            _ui.ConfigureWindows(new[] { config }, registry => registry.Register<BusinessFailingWindow>(1));
            var result = _ui.OpenWindowAsync(1).AsTask();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.IsTrue(result.IsFaulted);
            Assert.IsInstanceOf<ResourceLoadException>(result.Exception.InnerException);
            Assert.AreEqual(1, BusinessFailingWindow.Attempts);
            Assert.IsNull(Dialog());
        }

        /// <summary>
        /// 调用方取消时关闭等待层且不弹出错误框。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator CallerCancellation_ClearsBusyWithoutDialog()
        {
            _ui = new UIModule();
            _ui.OnInit();
            using var cancellation = new CancellationTokenSource();
            var options = new UILoadOptions { BusyDelayMilliseconds = 0 };
            var result = _ui.RunWithLoadingAsync<int>(async token =>
            {
                await UniTask.Delay(10000, cancellationToken: token);
                return 1;
            }, options, cancellation.Token).AsTask();
            Assert.IsTrue(BusyLayer().activeSelf);
            cancellation.Cancel();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.That(() => { result.GetAwaiter().GetResult(); }, Throws.InstanceOf<OperationCanceledException>());
            Assert.IsFalse(BusyLayer().activeSelf);
            Assert.IsNull(Dialog());
        }

        /// <summary>
        /// 取消后仍等待操作处理晚到结果，避免资源所有权被通用层丢弃。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator Cancellation_WaitsForLateResultCleanup()
        {
            _ui = new UIModule();
            _ui.OnInit();
            using var cancellation = new CancellationTokenSource();
            var completion = new UniTaskCompletionSource();
            LateResult lateResult = null;
            var result = _ui.RunWithLoadingAsync<LateResult>(async token =>
            {
                await completion.Task;
                lateResult = new LateResult();
                if (token.IsCancellationRequested)
                {
                    lateResult.Dispose();
                    throw new OperationCanceledException(token);
                }
                return lateResult;
            }, new UILoadOptions { BusyDelayMilliseconds = 0 }, cancellation.Token).AsTask();

            cancellation.Cancel();
            yield return null;
            Assert.IsFalse(result.IsCompleted);
            completion.TrySetResult();
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.That(() => { result.GetAwaiter().GetResult(); }, Throws.InstanceOf<OperationCanceledException>());
            Assert.IsNotNull(lateResult);
            Assert.IsTrue(lateResult.Disposed);
            Assert.IsFalse(BusyLayer().activeSelf);
        }

        /// <summary>
        /// 模块关闭会结束等待中的操作并释放等待层。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator Shutdown_CancelsPendingOperation()
        {
            _ui = new UIModule();
            _ui.OnInit();
            var result = _ui.RunWithLoadingAsync<int>(async token =>
            {
                await UniTask.Delay(10000, cancellationToken: token);
                return 1;
            }, new UILoadOptions { BusyDelayMilliseconds = 0 }).AsTask();
            _ui.OnShutdown();
            _ui = null;
            yield return new WaitUntil(() => result.IsCompleted);
            Assert.That(() => { result.GetAwaiter().GetResult(); }, Throws.InstanceOf<OperationCanceledException>());
        }

        /// <summary>
        /// 并发等待层不会盖住资源失败重试按钮。
        /// </summary>
        /// <returns>测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator ConcurrentBusy_DoesNotCoverRetryDialog()
        {
            _ui = new UIModule();
            _ui.OnInit();
            using var cancellation = new CancellationTokenSource();
            var options = new UILoadOptions { BusyDelayMilliseconds = 0 };
            var pending = _ui.RunWithLoadingAsync<int>(async token =>
            {
                await UniTask.Delay(10000, cancellationToken: token);
                return 1;
            }, options, cancellation.Token).AsTask();
            var failed = _ui.RunWithLoadingAsync<int>(_ =>
                UniTask.FromException<int>(new ResourceLoadException(CreateError(ResourceLoadError.FailureKind.LoadFailed))),
                options).AsTask();

            yield return new WaitUntil(() => Dialog() != null);
            Assert.IsTrue(BusyLayer().activeSelf);
            Transform dialog = Dialog();
            Assert.Greater(dialog.parent.GetSiblingIndex(), BusyLayer().transform.GetSiblingIndex());
            dialog.Find("Panel/Cancel").GetComponent<Button>().onClick.Invoke();
            yield return new WaitUntil(() => failed.IsCompleted);
            Assert.That(() => { failed.GetAwaiter().GetResult(); }, Throws.InstanceOf<OperationCanceledException>());
            cancellation.Cancel();
            yield return new WaitUntil(() => pending.IsCompleted);
            Assert.That(() => { pending.GetAwaiter().GetResult(); }, Throws.InstanceOf<OperationCanceledException>());
            Assert.IsFalse(BusyLayer().activeSelf);
        }

        /// <summary>
        /// 模拟需要由操作自身释放的晚到资源。
        /// </summary>
        private sealed class LateResult : IDisposable
        {
            internal bool Disposed { get; private set; }

            /// <summary>
            /// 标记模拟资源已释放。
            /// </summary>
            public void Dispose()
            {
                Disposed = true;
            }
        }

        /// <summary>
        /// 在业务组件绑定阶段模拟资源异常的窗口。
        /// </summary>
        private sealed class BusinessFailingWindow : GameWindow
        {
            internal static int Attempts;

            /// <summary>
            /// 创建测试窗口。
            /// </summary>
            public BusinessFailingWindow()
            {
            }

            /// <summary>
            /// 模拟业务初始化资源异常。
            /// </summary>
            protected override void BindComponents()
            {
                Attempts++;
                throw new ResourceLoadException(CreateError(ResourceLoadError.FailureKind.LoadFailed));
            }
        }

        /// <summary>
        /// 通过测试反射创建具有指定类别的结构化资源错误。
        /// </summary>
        /// <param name="kind">失败类别。</param>
        /// <returns>结构化资源错误。</returns>
        private static ResourceLoadError CreateError(ResourceLoadError.FailureKind kind)
        {
            return (ResourceLoadError)Activator.CreateInstance(typeof(ResourceLoadError),
                BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { kind, "GamePackage", "Test/Asset", "测试资源加载失败", null }, null);
        }

        /// <summary>
        /// 获取当前运行时等待层。
        /// </summary>
        /// <returns>等待层对象。</returns>
        private static GameObject BusyLayer()
        {
            return GameObject.Find("[GameUI]").transform.Find("Busy Layer").gameObject;
        }

        /// <summary>
        /// 获取当前模态对话框。
        /// </summary>
        /// <returns>模态对话框或空引用。</returns>
        private static Transform Dialog()
        {
            GameObject root = GameObject.Find("[GameUI]");
            return root != null ? root.transform.Find("Dialog Layer/Modal Dialog") : null;
        }
    }
}
