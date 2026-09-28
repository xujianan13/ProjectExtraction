namespace GameFramework
{
    /// <summary>
    /// 通道声源达到上限时处理新播放请求的策略。
    /// </summary>
    public enum AudioVoiceOverflowPolicy
    {
        /// <summary>
        /// 拒绝新播放请求。
        /// </summary>
        RejectNew,

        /// <summary>
        /// 终止最早的活动音频，为新播放请求腾出位置。
        /// </summary>
        StealOldest,
    }
}
