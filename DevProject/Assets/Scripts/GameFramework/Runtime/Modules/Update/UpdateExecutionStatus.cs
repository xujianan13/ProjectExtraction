namespace GameFramework
{
    /// <summary>内容包更新执行的最终状态。</summary>
    public enum UpdateExecutionStatus
    {
        /// <summary>尚未产生有效结果。</summary>
        None = 0,

        /// <summary>内容包已经是目标版本，无需下载。</summary>
        NoUpdate = 1,

        /// <summary>目标版本已下载、校验并在当前进程激活。</summary>
        Succeeded = 2,

        /// <summary>目标版本已准备完成，需要重启或等待下次冷启动激活。</summary>
        RestartRequired = 3,

        /// <summary>操作被调用方取消，当前有效版本保持不变。</summary>
        Cancelled = 4,

        /// <summary>操作失败，当前有效版本保持不变。</summary>
        Failed = 5,

        /// <summary>目标版本已经准备完成，等待 BootKernel 通用激活器切换。</summary>
        Prepared = 6
    }
}
