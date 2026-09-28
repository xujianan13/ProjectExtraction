using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>AllOrNothing 资源组未能完整加载时返回全部已知错误。</summary>
    public sealed class ResourceGroupLoadException : Exception
    {
        /// <summary>创建资源组加载异常。</summary>
        public ResourceGroupLoadException(string groupName, IReadOnlyList<ResourceLoadError> failures)
            : base($"资源组 '{groupName}' 加载失败，失败资源数：{failures?.Count ?? 0}。")
        {
            GroupName = groupName ?? string.Empty;
            Failures = failures ?? Array.Empty<ResourceLoadError>();
        }

        /// <summary>失败资源组名称。</summary>
        public string GroupName { get; }

        /// <summary>全部已知失败项。</summary>
        public IReadOnlyList<ResourceLoadError> Failures { get; }
    }
}
