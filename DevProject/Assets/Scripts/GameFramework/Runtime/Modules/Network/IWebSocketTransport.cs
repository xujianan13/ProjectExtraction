using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>由 NetworkModule 按平台创建的 WebSocket 传输层抽象。</summary>
    internal interface IWebSocketTransport
    {
        /// <summary>当前传输层状态。</summary>
        NetworkConnectionState State { get; }

        /// <summary>建立连接。</summary>
        UniTask ConnectAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            LoopbackTlsCertificatePin loopbackCertificatePin,
            CancellationToken ct);

        /// <summary>关闭连接。</summary>
        UniTask CloseAsync(CancellationToken ct);

        /// <summary>发送一条完整二进制消息。</summary>
        UniTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct);

        /// <summary>
        /// 注册主线程回调。消息缓冲来自 ArrayPool，消息回调取得所有权并负责归还。
        /// </summary>
        void SetCallbacks(
            Action<byte[], int> onMessage,
            Action<string> onError,
            Action onClosed);
    }
}
