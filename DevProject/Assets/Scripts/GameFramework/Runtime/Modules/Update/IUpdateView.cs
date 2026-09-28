using System;

namespace GameFramework
{
    /// <summary>热更更新流程使用的稳定视图接口。</summary>
    public interface IUpdateView
    {
        /// <summary>用户请求重试时触发。</summary>
        event Action RetryRequested;

        /// <summary>显示正在检查的内容包。</summary>
        /// <param name="packageName">内容包名称。</param>
        void ShowChecking(string packageName);

        /// <summary>显示检查生成的更新计划。</summary>
        /// <param name="plan">更新计划。</param>
        void ShowPlan(UpdatePlan plan);

        /// <summary>显示下载与激活进度。</summary>
        /// <param name="progress">更新进度。</param>
        void ShowProgress(UpdateProgress progress);

        /// <summary>显示更新失败信息。</summary>
        /// <param name="errorCode">稳定错误码。</param>
        /// <param name="message">简短错误说明。</param>
        /// <param name="canRetry">是否允许重试。</param>
        void ShowFailure(UpdateErrorCode errorCode, string message, bool canRetry);

        /// <summary>清除失败状态并禁止重复点击。</summary>
        void ClearFailure();

        /// <summary>更新与游戏交接完成后隐藏界面。</summary>
        void Hide();
    }
}
