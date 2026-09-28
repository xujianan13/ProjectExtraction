namespace GameFramework
{
    /// <summary>
    /// 并行流程完成策略。
    /// </summary>
    public enum FlowParallelPolicy
    {
        /// <summary>全部子流程成功后整体成功；任一失败或取消会使整体失败或取消。</summary>
        All = 0,

        /// <summary>任一子流程成功后整体成功，并取消其他未完成子流程。</summary>
        Any = 1,

        /// <summary>任一子流程失败或取消时立即结束，并取消其他未完成子流程。</summary>
        FailFast = 2,
    }
}
