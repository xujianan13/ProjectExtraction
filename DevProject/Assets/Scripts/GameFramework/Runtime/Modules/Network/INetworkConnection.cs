using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 单条协议无关的二进制网络连接。由 GameLogic 负责连接时机和消息内容。
    /// </summary>
    public interface INetworkConnection
    {
        /// <summary>连接句柄。</summary>
        NetworkConnectionId Id { get; }

        /// <summary>调试名称，例如 game 或 chat。</summary>
        string Name { get; }

        /// <summary>当前连接状态。</summary>
        NetworkConnectionState State { get; }

        /// <summary>等待底层写出的消息数，不包含当前正在写出的消息。</summary>
        int PendingSendCount { get; }

        /// <summary>等待主线程派发的消息数。</summary>
        int PendingReceiveCount { get; }

        /// <summary>注入消息处理器；应在 <see cref="ConnectAsync"/> 前调用。</summary>
        void SetMessageHandler(INetworkMessageHandler handler);

        /// <summary>
        /// 连接 WebSocket。网络失败时写入 <see cref="NetworkConnectionState.Failed"/> 并通过处理器通知。
        /// 仅取消令牌取消时向上抛 <see cref="OperationCanceledException"/>。
        /// </summary>
        UniTask ConnectAsync(
            string url,
            IReadOnlyDictionary<string, string> headers = null,
            CancellationToken ct = default);

        /// <summary>使用显式证书固定连接开发回环 WSS。</summary>
        UniTask ConnectAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            LoopbackTlsCertificatePin loopbackCertificatePin,
            CancellationToken ct = default);

        /// <summary>关闭连接并清理所有待处理消息。</summary>
        UniTask CloseAsync(CancellationToken ct = default);

        /// <summary>
        /// 将原始二进制复制到有界发送队列，由单一发送泵按顺序写出。
        /// 队列已满时返回 false 并通过处理器通知错误。
        /// </summary>
        bool Send(ReadOnlySpan<byte> data);

        /// <summary>将原始二进制入队并等待底层写出。</summary>
        UniTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);
    }
}
