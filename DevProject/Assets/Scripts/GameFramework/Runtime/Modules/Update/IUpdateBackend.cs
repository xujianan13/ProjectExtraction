using System;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 更新模块后端接口。平台或资源系统适配层实现具体的检查、下载和版本激活操作。
    /// </summary>
    public interface IUpdateBackend
    {
        /// <summary>后端更新进度发生变化时触发。</summary>
        event Action<UpdateProgress> ProgressChanged;

        /// <summary>初始化后端依赖。</summary>
        void Initialize();

        /// <summary>释放后端资源并取消仍在运行的下载。</summary>
        void Shutdown();

        /// <summary>检查指定内容包并生成尚未分配模块计划 ID 的更新计划。</summary>
        /// <param name="request">内容包更新请求。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>后端检查得到的更新计划。</returns>
        UniTask<UpdatePlan> CheckAsync(UpdatePackageRequest request, CancellationToken ct = default);

        /// <summary>执行已经由 ContentUpdateService 确认有效的更新计划。</summary>
        /// <param name="plan">待执行的更新计划。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>结构化执行结果。</returns>
        UniTask<UpdateExecutionResult> ExecuteAsync(UpdatePlan plan, CancellationToken ct = default);
    }
}
