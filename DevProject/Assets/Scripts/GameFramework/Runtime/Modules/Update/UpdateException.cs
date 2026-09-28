using System;

namespace GameFramework
{
    /// <summary>
    /// 更新检查阶段的基础设施异常。正常的无更新、取消和执行失败仍使用计划或执行结果表达。
    /// </summary>
    public sealed class UpdateException : Exception
    {
        /// <summary>稳定错误码。</summary>
        public UpdateErrorCode ErrorCode { get; }

        /// <summary>调用方是否可以重新发起完整检查流程。</summary>
        public bool CanRetry { get; }

        /// <summary>创建更新检查基础设施异常。</summary>
        /// <param name="errorCode">稳定错误码。</param>
        /// <param name="message">诊断信息。</param>
        /// <param name="canRetry">是否可以重新检查。</param>
        /// <param name="innerException">底层异常。</param>
        public UpdateException(
            UpdateErrorCode errorCode,
            string message,
            bool canRetry,
            Exception innerException = null)
            : base(message, innerException)
        {
            if (errorCode == UpdateErrorCode.None)
                throw new ArgumentOutOfRangeException(nameof(errorCode), "异常错误码不能为 None。");

            ErrorCode = errorCode;
            CanRetry = canRetry;
        }
    }
}
