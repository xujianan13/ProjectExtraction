using YooAsset;

namespace GameFramework
{
    /// <summary>为指定内容包提供当前平台的 YooAsset 初始化参数。</summary>
    public interface IUpdatePackageOptionsProvider
    {
        /// <summary>创建内容包初始化参数。</summary>
        InitializePackageOptions CreateOptions(UpdatePackageRequest request);
    }
}
