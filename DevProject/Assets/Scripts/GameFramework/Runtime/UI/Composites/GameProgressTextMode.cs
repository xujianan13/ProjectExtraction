namespace GameFramework
{
    /// <summary>
    /// 进度条文本显示模式。
    /// </summary>
    public enum GameProgressTextMode
    {
        /// <summary>
        /// 不显示文本。
        /// </summary>
        None = 0,

        /// <summary>
        /// 显示百分比。
        /// </summary>
        Percentage = 1,

        /// <summary>
        /// 显示当前值。
        /// </summary>
        Value = 2,

        /// <summary>
        /// 显示当前值和最大值。
        /// </summary>
        ValueAndMaximum = 3
    }
}
