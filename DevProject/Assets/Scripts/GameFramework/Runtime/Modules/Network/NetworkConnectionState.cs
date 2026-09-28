namespace GameFramework
{
    /// <summary>协议无关网络连接的生命周期状态。</summary>
    public enum NetworkConnectionState
    {
        /// <summary>尚未连接或已经关闭。</summary>
        Disconnected = 0,

        /// <summary>正在建立底层连接。</summary>
        Connecting = 1,

        /// <summary>底层连接可用。</summary>
        Connected = 2,

        /// <summary>正在主动关闭。</summary>
        Closing = 3,

        /// <summary>连接、收发或队列发生错误。</summary>
        Failed = 4,
    }
}
