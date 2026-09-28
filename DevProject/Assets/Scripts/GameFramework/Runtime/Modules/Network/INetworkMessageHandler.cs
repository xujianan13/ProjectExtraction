using System;

namespace GameFramework
{
    /// <summary>
    /// 单连接的消息与状态回调。由 GameLogic 实现，负责协议解析与业务分发。
    /// 框架保证回调在主线程触发。
    /// </summary>
    public interface INetworkMessageHandler
    {
        /// <summary>
        /// 收到一条完整二进制消息。
        /// <paramref name="data"/> 仅在回调期间有效；异步持有或跨帧保存时必须自行拷贝。
        /// </summary>
        void OnMessage(INetworkConnection connection, ReadOnlyMemory<byte> data);

        /// <summary>连接状态变化。</summary>
        void OnStateChanged(
            INetworkConnection connection,
            NetworkConnectionState state);

        /// <summary>底层连接或队列错误。</summary>
        void OnError(INetworkConnection connection, string error);
    }
}
