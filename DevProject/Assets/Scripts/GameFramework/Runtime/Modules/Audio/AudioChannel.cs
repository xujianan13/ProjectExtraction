namespace GameFramework
{
    /// <summary>
    /// 音频通道，每个通道独立管理音量、路由和声源上限。
    /// </summary>
    public enum AudioChannel
    {
        /// <summary>
        /// 背景音乐通道。
        /// </summary>
        Music = 0,

        /// <summary>
        /// 音效通道。
        /// </summary>
        Sfx = 1,

        /// <summary>
        /// 语音通道。
        /// </summary>
        Voice = 2,
    }
}
