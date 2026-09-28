namespace GameFramework
{
    /// <summary>
    /// 存档自动保存策略；打开会话时复制，外部修改不影响在途会话。
    /// </summary>
    public sealed class SavePolicy
    {
        public bool AutoSave { get; set; } = true;
        public double DebounceSeconds { get; set; } = 1;
        public double MaxWaitSeconds { get; set; } = 10;
        public double OperationTimeoutSeconds { get; set; } = 10;
        public bool FlushOnBackground { get; set; } = true;
        public int MaxAttempts { get; set; } = 3;
        public double RetryBaseDelaySeconds { get; set; } = 1;
        public int MaxRecordBytes { get; set; } = 262144;

        /// <summary>
        /// 校验策略参数。
        /// </summary>
        /// <returns>策略是否可用。</returns>
        internal bool IsValid() => Finite(DebounceSeconds) && DebounceSeconds >= 0 && Finite(MaxWaitSeconds) && MaxWaitSeconds > 0 &&
            Finite(OperationTimeoutSeconds) && OperationTimeoutSeconds > 0 && MaxAttempts > 0 && MaxRecordBytes > 0 &&
            Finite(RetryBaseDelaySeconds) && RetryBaseDelaySeconds >= 0;

        /// <summary>
        /// 判断时间参数是否有限。
        /// </summary>
        /// <param name="value">待校验数值。</param>
        /// <returns>是否有限。</returns>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>
        /// 复制配置。
        /// </summary>
        /// <returns>独立策略副本。</returns>
        internal SavePolicy Copy() => (SavePolicy)MemberwiseClone();
    }
}
