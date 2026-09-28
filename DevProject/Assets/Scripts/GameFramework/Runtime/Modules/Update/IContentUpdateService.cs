using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>不依赖 GameFramework 模块生命周期的内容更新服务。</summary>
    public interface IContentUpdateService : IDisposable
    {
        /// <summary>更新阶段或下载数据变化时触发。</summary>
        event Action<UpdateProgress> ProgressChanged;

        /// <summary>初始化服务及其后端依赖。</summary>
        void Initialize();

        /// <summary>检查指定内容包并生成不可变更新计划。</summary>
        UniTask<UpdatePlan> CheckAsync(UpdatePackageRequest request, CancellationToken ct = default);

        /// <summary>执行已经检查完成的更新计划。</summary>
        UniTask<UpdateExecutionResult> ExecuteAsync(UpdatePlan plan, CancellationToken ct = default);
    }
}
