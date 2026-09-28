namespace GameFramework
{
    /// <summary>
    /// 流程状态。
    /// </summary>
    public enum FlowState
    {
        /// <summary>尚未启动。</summary>
        Idle = 0,

        /// <summary>运行中。</summary>
        Running = 1,

        /// <summary>已暂停。</summary>
        Paused = 2,

        /// <summary>已成功完成。</summary>
        Succeeded = 3,

        /// <summary>已失败。</summary>
        Failed = 4,

        /// <summary>已取消。</summary>
        Canceled = 5,
    }
}
