using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 在候选清单激活前提供与该版本一同发布的包级配置原文。
    /// </summary>
    public interface IUpdatePackageSettingsSource
    {
        /// <summary>
        /// 读取指定版本的包级配置旁路文件。
        /// </summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="packageVersion">候选内容包版本。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>候选版本包级配置的 JSON 原文。</returns>
        UniTask<string> LoadSettingsAsync(UpdatePackageRequest request, string packageVersion, CancellationToken ct);
    }
}
