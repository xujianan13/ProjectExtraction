using System.Collections.Generic;
using System.Text;

namespace GameFramework
{
    /// <summary>
    /// HTTP 响应。属性都是只读，由 HttpModule 内部构造；业务侧只读消费。
    /// </summary>
    public sealed class HttpResponse
    {
        /// <summary>HTTP 状态码。0 表示连接级失败（看 <see cref="Error"/>）。</summary>
        public int StatusCode { get; }

        /// <summary>响应体字节流。可能为 null。</summary>
        public byte[] Body { get; }

        /// <summary>响应头。可能为 null（连接级失败时）。</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>错误描述。成功时为 null 或空串。</summary>
        public string Error { get; }

        /// <summary>是否成功（2xx 且无错误）。</summary>
        public bool IsSuccess => StatusCode >= 200 && StatusCode < 300 && string.IsNullOrEmpty(Error);

        /// <summary>把响应体按 UTF-8 解析成字符串。Body 为 null/空时返回空串。</summary>
        public string GetBodyAsString()
            => Body == null || Body.Length == 0 ? string.Empty : Encoding.UTF8.GetString(Body);

        /// <summary>由 HttpModule 内部构造。</summary>
        public HttpResponse(int statusCode, byte[] body, IReadOnlyDictionary<string, string> headers, string error)
        {
            StatusCode = statusCode;
            Body = body;
            Headers = headers;
            Error = error;
        }
    }
}
