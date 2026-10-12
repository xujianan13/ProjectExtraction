namespace GameLogic.Advertising
{
    /// <summary>
    /// 激励广告展示状态；默认状态为失败，避免未初始化结果授予奖励。
    /// </summary>
    public enum RewardedAdStatus
    {
        /// <summary>
        /// 请求或展示失败。
        /// </summary>
        Failed,

        /// <summary>
        /// 广告已完成，可以发放对应业务奖励。
        /// </summary>
        Completed,

        /// <summary>
        /// 请求取消，不能发放奖励。
        /// </summary>
        Cancelled,

        /// <summary>
        /// 其他广告请求尚未结束，当前请求被拒绝。
        /// </summary>
        Busy
    }

    /// <summary>
    /// 不依赖广告平台的激励广告结果。
    /// </summary>
    public readonly struct RewardedAdResult
    {
        /// <summary>
        /// 本次展示状态。
        /// </summary>
        public RewardedAdStatus Status { get; }

        /// <summary>
        /// 失败或拒绝原因；无错误时为空。
        /// </summary>
        public string Error { get; }

        /// <summary>
        /// 是否完成广告并允许调用方发放奖励。
        /// </summary>
        public bool IsCompleted => Status == RewardedAdStatus.Completed;

        /// <summary>
        /// 创建广告展示结果。
        /// </summary>
        /// <param name="status">广告展示状态。</param>
        /// <param name="error">失败或拒绝原因。</param>
        public RewardedAdResult(RewardedAdStatus status, string error = "")
        {
            Status = status;
            Error = error;
        }
    }
}
