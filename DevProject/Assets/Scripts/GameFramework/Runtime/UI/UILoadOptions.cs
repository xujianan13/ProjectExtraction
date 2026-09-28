namespace GameFramework
{
    /// <summary>
    /// 配置异步 UI 操作的等待层和资源失败反馈。
    /// </summary>
    public sealed class UILoadOptions
    {
        /// <summary>
        /// 是否在延迟后显示等待层。
        /// </summary>
        public bool ShowBusy { get; set; } = true;

        /// <summary>
        /// 是否对可恢复的资源失败显示重试对话框。
        /// </summary>
        public bool ShowRetry { get; set; } = true;

        /// <summary>
        /// 等待层显示文字。
        /// </summary>
        public string BusyMessage { get; set; } = "正在加载…";

        /// <summary>
        /// 资源失败对话框标题。
        /// </summary>
        public string RetryTitle { get; set; } = "加载失败";

        /// <summary>
        /// 资源失败对话框正文。
        /// </summary>
        public string RetryMessage { get; set; } = "资源加载失败，请检查网络后重试。";

        /// <summary>
        /// 重试按钮文字。
        /// </summary>
        public string RetryText { get; set; } = "重试";

        /// <summary>
        /// 取消按钮文字。
        /// </summary>
        public string CancelText { get; set; } = "取消";

        /// <summary>
        /// 显示等待层前的延迟毫秒数；零表示立即显示。
        /// </summary>
        public int BusyDelayMilliseconds { get; set; } = 150;
    }
}
