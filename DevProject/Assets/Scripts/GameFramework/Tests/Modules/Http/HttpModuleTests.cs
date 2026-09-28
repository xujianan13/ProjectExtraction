using System;
using System.Collections;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Modules.Http
{
    /// <summary>HttpModule 请求参数校验、回调行为与 HttpRequest 默认值测试。</summary>
    [TestFixture]
    public sealed class HttpModuleTests
    {
        // 连接失败测试用 HTTP：Player Settings 禁用非安全连接时也会立即失败，仍可验证错误回调路径。
        private const string UnreachableUrl = "http://127.0.0.1:1";
        // 取消测试用占位 URL；SendAsync 在发起网络请求前会检查 ct，不依赖实际连通性。
        private const string PlaceholderUrl = "https://example.com";

        private HttpModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _module = new HttpModule();
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        /// <summary>SendAsync 传入 null request 应抛 ArgumentNullException。</summary>
        [Test]
        public void SendAsync_NullRequest_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _module.SendAsync(null).GetAwaiter().GetResult());
        }

        /// <summary>SendAsync 传入空 Url 应抛 ArgumentException。</summary>
        [Test]
        public void SendAsync_EmptyUrl_Throws()
        {
            Assert.Throws<System.ArgumentException>(() =>
                _module.SendAsync(new HttpRequest()).GetAwaiter().GetResult());
        }

        /// <summary>SendAsync 传入不支持的 Method 应抛 ArgumentOutOfRangeException。</summary>
        [Test]
        public void SendAsync_UnsupportedMethod_Throws()
        {
            var request = new HttpRequest { Url = "http://example.com", Method = (HttpMethod)99 };
            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                _module.SendAsync(request).GetAwaiter().GetResult());
        }

        /// <summary>SendAsync 取消时应抛 OperationCanceledException。</summary>
        [Test]
        public void SendAsync_Cancelled_ThrowsOperationCanceledException()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();
            Assert.Throws<OperationCanceledException>(() =>
                _module.SendAsync(new HttpRequest { Url = PlaceholderUrl }, cts.Token)
                    .GetAwaiter().GetResult());
        }

        /// <summary>SendAsync 连接失败应返回带 Error 的 HttpResponse，不抛异常。</summary>
        [UnityTest]
        public IEnumerator SendAsync_ConnectionError_ReturnsFailureResponse()
        {
            HttpResponse response = null;
            yield return CaptureResponse().ToCoroutine();

            async UniTask CaptureResponse()
            {
                response = await _module.SendAsync(new HttpRequest { Url = UnreachableUrl, TimeoutSeconds = 1f });
            }

            Assert.IsFalse(response.IsSuccess);
            Assert.AreEqual(0, response.StatusCode);
            Assert.IsFalse(string.IsNullOrEmpty(response.Error));
        }

        /// <summary>HttpRequest 默认 Method 应为 GET。</summary>
        [Test]
        public void HttpRequest_DefaultMethod_IsGet()
        {
            var request = new HttpRequest { Url = "http://example.com" };
            Assert.AreEqual(HttpMethod.Get, request.Method);
        }

        /// <summary>HttpRequest 默认 TimeoutSeconds 应为 10。</summary>
        [Test]
        public void HttpRequest_DefaultTimeout_IsTenSeconds()
        {
            var request = new HttpRequest();
            Assert.AreEqual(10f, request.TimeoutSeconds);
        }

        /// <summary>HttpRequest 构造函数应正确设置 Method 与 Url。</summary>
        [Test]
        public void HttpRequest_Constructor_SetsMethodAndUrl()
        {
            var request = new HttpRequest(HttpMethod.Post, "http://example.com");
            Assert.AreEqual(HttpMethod.Post, request.Method);
            Assert.AreEqual("http://example.com", request.Url);
        }

        /// <summary>Send 传入 null onComplete 应抛 ArgumentNullException。</summary>
        [Test]
        public void Send_NullCallback_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _module.Send(new HttpRequest { Url = "http://example.com" }, null));
        }

        /// <summary>Get 传入 null onComplete 应抛 ArgumentNullException。</summary>
        [Test]
        public void Get_NullCallback_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _module.Get("http://example.com", null));
        }

        /// <summary>PostJson 传入 null onComplete 应抛 ArgumentNullException。</summary>
        [Test]
        public void PostJson_NullCallback_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() =>
                _module.PostJson("http://example.com", "{}", null));
        }

        /// <summary>Send 连接失败应在回调中返回带 Error 的 HttpResponse。</summary>
        [UnityTest]
        public IEnumerator Send_ConnectionError_InvokesCallbackWithFailureResponse()
        {
            HttpResponse result = null;
            _module.Send(
                new HttpRequest { Url = UnreachableUrl, TimeoutSeconds = 1f },
                response => result = response);

            yield return WaitForCallback(() => result != null);
            Assert.IsFalse(result.IsSuccess);
            Assert.AreEqual(0, result.StatusCode);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error));
        }

        /// <summary>Get 连接失败应在回调中返回带 Error 的 HttpResponse。</summary>
        [UnityTest]
        public IEnumerator Get_ConnectionError_InvokesCallbackWithFailureResponse()
        {
            HttpResponse result = null;
            _module.Get(UnreachableUrl, response => result = response);

            yield return WaitForCallback(() => result != null);
            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error));
        }

        /// <summary>PostJson 连接失败应在回调中返回带 Error 的 HttpResponse。</summary>
        [UnityTest]
        public IEnumerator PostJson_ConnectionError_InvokesCallbackWithFailureResponse()
        {
            HttpResponse result = null;
            _module.PostJson(UnreachableUrl, "{}", response => result = response);

            yield return WaitForCallback(() => result != null);
            Assert.IsFalse(result.IsSuccess);
            Assert.IsFalse(string.IsNullOrEmpty(result.Error));
        }

        /// <summary>Send 取消时不应触发回调。</summary>
        [UnityTest]
        public IEnumerator Send_Cancelled_DoesNotInvokeCallback()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var invoked = false;
            _module.Send(
                new HttpRequest { Url = PlaceholderUrl },
                _ => invoked = true,
                cts.Token);

            yield return WaitForCallback(() => true, settleSeconds: 0.5f);
            Assert.IsFalse(invoked);
        }

        /// <summary>Get 取消时不应触发回调。</summary>
        [UnityTest]
        public IEnumerator Get_Cancelled_DoesNotInvokeCallback()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var invoked = false;
            _module.Get(PlaceholderUrl, _ => invoked = true, cts.Token);

            yield return WaitForCallback(() => true, settleSeconds: 0.5f);
            Assert.IsFalse(invoked);
        }

        /// <summary>PostJson 取消时不应触发回调。</summary>
        [UnityTest]
        public IEnumerator PostJson_Cancelled_DoesNotInvokeCallback()
        {
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            var invoked = false;
            _module.PostJson(PlaceholderUrl, "{}", _ => invoked = true, cts.Token);

            yield return WaitForCallback(() => true, settleSeconds: 0.5f);
            Assert.IsFalse(invoked);
        }

        /// <summary>模块关闭时应取消仍在执行的异步请求。</summary>
        [UnityTest]
        public IEnumerator SendAsync_Shutdown_CancelsActiveRequest()
        {
            var cancelled = false;
            UniTask observation = ObserveCancellationAsync();

            _module.OnShutdown();

            yield return observation.ToCoroutine();
            Assert.IsTrue(cancelled);

            async UniTask ObserveCancellationAsync()
            {
                try
                {
                    await _module.SendAsync(
                        new HttpRequest { Url = PlaceholderUrl, TimeoutSeconds = 30f });
                }
                catch (OperationCanceledException)
                {
                    cancelled = true;
                }
            }
        }

        /// <summary>模块关闭取消回调请求时不应触发完成回调。</summary>
        [UnityTest]
        public IEnumerator Send_Shutdown_DoesNotInvokeCallback()
        {
            var invoked = false;
            _module.Send(
                new HttpRequest { Url = PlaceholderUrl, TimeoutSeconds = 30f },
                _ => invoked = true);

            _module.OnShutdown();

            yield return WaitForCallback(() => true, settleSeconds: 0.5f);
            Assert.IsFalse(invoked);
        }

        /// <summary>Send 回调抛异常时应被吞掉，不影响框架继续运行。</summary>
        [UnityTest]
        public IEnumerator Send_CallbackThrows_IsSwallowed()
        {
            HttpResponse result = null;
            _module.Send(
                new HttpRequest { Url = UnreachableUrl, TimeoutSeconds = 1f },
                response =>
                {
                    result = response;
                    throw new System.InvalidOperationException("callback boom");
                });

            yield return WaitForCallback(() => result != null);
            Assert.IsNotNull(result);
            Assert.IsFalse(result.IsSuccess);
        }

        private static IEnumerator WaitForCallback(System.Func<bool> predicate, float timeoutSeconds = 5f, float settleSeconds = 0f)
        {
            var elapsed = 0f;
            while (!predicate() && elapsed < timeoutSeconds)
            {
                elapsed += 0.05f;
                yield return new UnityEngine.WaitForSeconds(0.05f);
            }

            if (settleSeconds > 0f)
                yield return new UnityEngine.WaitForSeconds(settleSeconds);
        }
    }
}
