using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// HTTP 模块接口。底层走 UnityWebRequest（在微信小游戏环境由 com.qq.weixin.minigame SDK 接管为 wx.request）。
    /// 不内置重试 / 拦截器 / 序列化策略，业务自行装饰。
    /// </summary>
    public interface IHttpModule : IModule
    {
        /// <summary>发送任意 HTTP 请求。请求超时 / 网络失败都通过 <see cref="HttpResponse.IsSuccess"/> 与 <see cref="HttpResponse.Error"/> 表达；调用方取消或模块关闭时抛出取消异常。</summary>
        UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct = default);

        /// <summary>便捷：GET。</summary>
        UniTask<HttpResponse> GetAsync(string url, CancellationToken ct = default);

        /// <summary>便捷：POST 一段 JSON 字符串（自动设置 Content-Type: application/json）。</summary>
        UniTask<HttpResponse> PostJsonAsync(string url, string json, CancellationToken ct = default);

        /// <summary>
        /// 发送任意 HTTP 请求（回调版）。完成时在主线程调用 <paramref name="onComplete"/>；
        /// 调用方取消或模块关闭时不触发回调。
        /// </summary>
        void Send(HttpRequest request, Action<HttpResponse> onComplete, CancellationToken ct = default);

        /// <summary>便捷：GET（回调版）。</summary>
        void Get(string url, Action<HttpResponse> onComplete, CancellationToken ct = default);

        /// <summary>便捷：POST JSON（回调版）。</summary>
        void PostJson(string url, string json, Action<HttpResponse> onComplete, CancellationToken ct = default);
    }
}
