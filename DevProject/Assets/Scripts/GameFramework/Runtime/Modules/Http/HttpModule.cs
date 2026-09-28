using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.Networking;

namespace GameFramework
{
    /// <summary>
    /// HTTP 模块默认实现。基于 UnityWebRequest + UniTask；微信小游戏环境由 com.qq.weixin.minigame SDK 透明代理。
    /// 错误一律封装进 HttpResponse 返回，不抛异常（CancellationToken 触发时除外）。
    /// </summary>
    public sealed class HttpModule : ModuleBase, IHttpModule
    {
        private const string Tag = "Http";
        private const string JsonContentType = "application/json; charset=utf-8";

        /// <inheritdoc />
        public override int Priority => 10;

        private ILogModule _log;
        private CancellationTokenSource _lifecycleCts;

        /// <inheritdoc />
        public override void OnInit()
        {
            _lifecycleCts?.Cancel();
            _lifecycleCts?.Dispose();
            _lifecycleCts = new CancellationTokenSource();
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            CancellationTokenSource lifecycleCts = _lifecycleCts;
            _lifecycleCts = null;
            lifecycleCts?.Cancel();
            lifecycleCts?.Dispose();
            _log = null;
        }

        /// <inheritdoc />
        public UniTask<HttpResponse> GetAsync(string url, CancellationToken ct = default)
            => SendAsync(new HttpRequest(HttpMethod.Get, url), ct);

        /// <inheritdoc />
        public UniTask<HttpResponse> PostJsonAsync(string url, string json, CancellationToken ct = default)
        {
            var req = new HttpRequest(HttpMethod.Post, url)
            {
                Body = json != null ? Encoding.UTF8.GetBytes(json) : Array.Empty<byte>(),
                Headers = new Dictionary<string, string> { { "Content-Type", JsonContentType } },
            };
            return SendAsync(req, ct);
        }

        /// <inheritdoc />
        public void Send(HttpRequest request, Action<HttpResponse> onComplete, CancellationToken ct = default)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            RunWithCallback(() => SendAsync(request, ct), onComplete);
        }

        /// <inheritdoc />
        public void Get(string url, Action<HttpResponse> onComplete, CancellationToken ct = default)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            RunWithCallback(() => GetAsync(url, ct), onComplete);
        }

        /// <inheritdoc />
        public void PostJson(string url, string json, Action<HttpResponse> onComplete, CancellationToken ct = default)
        {
            if (onComplete == null) throw new ArgumentNullException(nameof(onComplete));
            RunWithCallback(() => PostJsonAsync(url, json, ct), onComplete);
        }

        /// <inheritdoc />
        public async UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct = default)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (string.IsNullOrEmpty(request.Url)) throw new ArgumentException("HttpRequest.Url is null or empty", nameof(request));
            if (_lifecycleCts == null)
                throw new InvalidOperationException("HttpModule 尚未初始化或已经关闭。");

            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(
                ct,
                _lifecycleCts.Token);
            CancellationToken requestToken = linkedCts.Token;
            requestToken.ThrowIfCancellationRequested();
            using var uwr = BuildUwr(request);

            try
            {
                await uwr.SendWebRequest().ToUniTask(cancellationToken: requestToken);
            }
            catch (OperationCanceledException)
            {
                // 调用方取消和模块关闭使用同一语义，异步调用方都应结束当前生命周期。
                throw;
            }
            catch (UnityWebRequestException)
            {
                // UniTask 把非成功结果包成异常；统一在下方按 uwr.result 构造 Response
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"Unexpected error: {request.Method} {request.Url} -> {ex}");
                return new HttpResponse(0, null, null, ex.Message);
            }

            return BuildResponse(uwr);
        }

        private void RunWithCallback(Func<UniTask<HttpResponse>> taskFactory, Action<HttpResponse> onComplete)
        {
            RunWithCallbackAsync(taskFactory, onComplete).Forget();
        }

        private async UniTaskVoid RunWithCallbackAsync(Func<UniTask<HttpResponse>> taskFactory, Action<HttpResponse> onComplete)
        {
            HttpResponse response;
            try
            {
                response = await taskFactory();
            }
            catch (OperationCanceledException)
            {
                return;
            }

            try
            {
                onComplete(response);
            }
            catch (Exception ex)
            {
                _log?.Error(Tag, $"HTTP callback threw: {ex}");
            }
        }

        private static UnityWebRequest BuildUwr(HttpRequest request)
        {
            UnityWebRequest uwr;
            switch (request.Method)
            {
                case HttpMethod.Get:
                    uwr = UnityWebRequest.Get(request.Url);
                    break;
                case HttpMethod.Delete:
                    uwr = UnityWebRequest.Delete(request.Url);
                    break;
                case HttpMethod.Post:
                case HttpMethod.Put:
                    uwr = new UnityWebRequest(request.Url, request.Method == HttpMethod.Post ? "POST" : "PUT");
                    if (request.Body != null && request.Body.Length > 0)
                        uwr.uploadHandler = new UploadHandlerRaw(request.Body);
                    uwr.downloadHandler = new DownloadHandlerBuffer();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Method), request.Method, "Unsupported HttpMethod");
            }

            if (request.Headers != null)
            {
                foreach (var kv in request.Headers)
                    uwr.SetRequestHeader(kv.Key, kv.Value);
            }

            if (request.TimeoutSeconds > 0f)
                uwr.timeout = Math.Max(1, (int)Math.Ceiling(request.TimeoutSeconds));

            if (request.LoopbackCertificatePin != null)
            {
                if (!Uri.TryCreate(request.Url, UriKind.Absolute, out Uri endpoint))
                    throw new ArgumentException("HTTP 请求 URL 不是绝对地址。", nameof(request));
                request.LoopbackCertificatePin.ValidateEndpoint(endpoint);
                uwr.certificateHandler = new PinnedCertificateHandler(
                    request.LoopbackCertificatePin);
                uwr.disposeCertificateHandlerOnDispose = true;
            }

            return uwr;
        }

        private sealed class PinnedCertificateHandler : CertificateHandler
        {
            private readonly LoopbackTlsCertificatePin _pin;

            internal PinnedCertificateHandler(LoopbackTlsCertificatePin pin)
            {
                _pin = pin ?? throw new ArgumentNullException(nameof(pin));
            }

            protected override bool ValidateCertificate(byte[] certificateData)
            {
                return _pin.Matches(certificateData);
            }
        }

        private static HttpResponse BuildResponse(UnityWebRequest uwr)
        {
            var status = (int)uwr.responseCode;
            var body = uwr.downloadHandler != null ? uwr.downloadHandler.data : null;
            var headers = uwr.GetResponseHeaders();
            string error = null;
            if (uwr.result == UnityWebRequest.Result.ConnectionError
                || uwr.result == UnityWebRequest.Result.ProtocolError
                || uwr.result == UnityWebRequest.Result.DataProcessingError)
            {
                error = string.IsNullOrEmpty(uwr.error) ? uwr.result.ToString() : uwr.error;
            }
            return new HttpResponse(status, body, headers, error);
        }
    }
}
