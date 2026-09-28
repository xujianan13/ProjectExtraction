using System;

namespace GameFramework
{
    /// <summary>在异步资源获取失败时携带结构化错误。</summary>
    public sealed class ResourceLoadException : Exception
    {
        /// <summary>创建资源加载异常。</summary>
        /// <param name="error">结构化资源错误。</param>
        public ResourceLoadException(ResourceLoadError error)
            : base(error?.Message, error?.Exception)
        {
            Error = error ?? throw new ArgumentNullException(nameof(error));
        }

        /// <summary>结构化资源错误。</summary>
        public ResourceLoadError Error { get; }
    }
}
