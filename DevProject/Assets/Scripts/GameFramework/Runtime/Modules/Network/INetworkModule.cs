using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 网络模块接口。管理协议无关的二进制连接、收发队列与主线程消息派发。
    /// 不内置 Protobuf、业务路由、心跳或重连。
    /// </summary>
    public interface INetworkModule : IModule
    {
        /// <summary>默认发送队列上限（条数）。</summary>
        int DefaultMaxSendQueueSize { get; set; }

        /// <summary>默认接收队列上限（条数）。</summary>
        int DefaultMaxReceiveQueueSize { get; set; }

        /// <summary>默认每帧最多派发的接收消息条数。</summary>
        int DefaultMaxReceiveMessagesPerUpdate { get; set; }

        /// <summary>创建一条逻辑连接。同名重复创建抛 <see cref="System.InvalidOperationException"/>。</summary>
        INetworkConnection Create(string name);

        /// <summary>按句柄获取连接；不存在返回 null。</summary>
        INetworkConnection Get(NetworkConnectionId id);

        /// <summary>按名称获取连接；不存在返回 null。</summary>
        INetworkConnection GetByName(string name);

        /// <summary>关闭并移除连接。不存在返回 false。</summary>
        bool Destroy(NetworkConnectionId id);

        /// <summary>当前所有连接（只读快照）。</summary>
        IReadOnlyList<INetworkConnection> Connections { get; }
    }
}
