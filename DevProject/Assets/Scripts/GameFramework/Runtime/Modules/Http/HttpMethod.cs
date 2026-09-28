namespace GameFramework
{
    /// <summary>
    /// HTTP 请求方法。覆盖最常用的 4 种；如需 PATCH / OPTIONS 等再扩展。
    /// </summary>
    public enum HttpMethod
    {
        /// <summary>GET 请求。</summary>
        Get,
        /// <summary>POST 请求。</summary>
        Post,
        /// <summary>PUT 请求。</summary>
        Put,
        /// <summary>DELETE 请求。</summary>
        Delete,
    }
}
