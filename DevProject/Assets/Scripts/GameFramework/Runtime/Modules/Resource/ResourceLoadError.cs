using System;

namespace GameFramework
{
    /// <summary>描述一次资源请求未能取得租约的稳定错误信息。</summary>
    public sealed class ResourceLoadError
    {
        /// <summary>资源加载失败类别。</summary>
        public enum FailureKind : byte
        {
            /// <summary>资源地址或参数无效。</summary>
            InvalidRequest,

            /// <summary>目标资源包尚未激活或已经失效。</summary>
            PackageNotReady,

            /// <summary>底层资源加载失败或返回了错误类型。</summary>
            LoadFailed,

            /// <summary>调用方取消了自己的等待。</summary>
            Canceled,

            /// <summary>资源模块已关闭。</summary>
            ModuleShutdown
        }

        internal ResourceLoadError(
            FailureKind kind,
            string packageName,
            string location,
            string message,
            Exception exception = null)
        {
            Kind = kind;
            PackageName = packageName ?? string.Empty;
            Location = location ?? string.Empty;
            Message = message ?? string.Empty;
            Exception = exception;
        }

        /// <summary>失败类别。</summary>
        public FailureKind Kind { get; }

        /// <summary>请求所属资源包。</summary>
        public string PackageName { get; }

        /// <summary>请求的逻辑资源地址。</summary>
        public string Location { get; }

        /// <summary>可记录但不包含敏感数据的错误说明。</summary>
        public string Message { get; }

        /// <summary>原始异常；正常的底层失败可能为空。</summary>
        public Exception Exception { get; }
    }
}
