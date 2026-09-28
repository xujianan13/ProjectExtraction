using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// HTTP 请求描述。字段较多且生命周期跨 await，采用 class；按规范使用属性而非公开字段。
    /// </summary>
    public sealed class HttpRequest
    {
        /// <summary>请求 URL（必填）。</summary>
        public string Url { get; set; }

        /// <summary>HTTP 方法，默认 GET。</summary>
        public HttpMethod Method { get; set; } = HttpMethod.Get;

        /// <summary>请求头。null 表示无自定义头。</summary>
        public Dictionary<string, string> Headers { get; set; }

        /// <summary>请求体字节流。编码由业务决定（JSON / form / protobuf 等）。GET 请求会被忽略。</summary>
        public byte[] Body { get; set; }

        /// <summary>请求超时（秒）。0 表示用底层默认（不建议）。</summary>
        public float TimeoutSeconds { get; set; } = 10f;

        /// <summary>
        /// 仅供显式开发构建连接 HTTPS 回环服务的证书固定值；生产请求必须保持为空。
        /// </summary>
        public LoopbackTlsCertificatePin LoopbackCertificatePin { get; set; }

        /// <summary>便捷构造：仅 URL 的 GET 请求。</summary>
        public HttpRequest() { }

        /// <summary>便捷构造：指定方法 + URL。</summary>
        public HttpRequest(HttpMethod method, string url)
        {
            Method = method;
            Url = url;
        }
    }
}
