using System;
using System.Buffers;
using System.Collections.Generic;
using System.Net.Security;
using System.Net.WebSockets;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 基于 System.Net.WebSockets.ClientWebSocket 的传输实现。
    /// Editor / Standalone 直接使用。
    /// WebGL / 微信小游戏依赖构建链路中的 weapp-adapter（或等价 polyfill）将
    /// <c>System.Net.WebSockets</c> 映射到 <c>wx.connectSocket</c>；框架侧不再维护平行 Transport。
    /// 若真机联调失败，优先排查 polyfill / 合法域名 / WSS，而非在业务层改用原生 Socket API。
    /// 接收路径复用 scratch 缓冲与 ArrayPool，避免每帧 new List / byte[]。
    /// </summary>
    internal sealed class ClientWebSocketTransport : IWebSocketTransport
    {
        private const int ReceiveBufferSize = 8192;

        private readonly byte[] _receiveBuffer = new byte[ReceiveBufferSize];
        private readonly List<byte[]> _fragmentChunks = new(4);
        private readonly List<int> _fragmentLengths = new(4);

        private ClientWebSocket _socket;
        private CancellationTokenSource _receiveCts;
        private Action<byte[], int> _onMessage;
        private Action<string> _onError;
        private Action _onClosed;
        private NetworkConnectionState _state = NetworkConnectionState.Disconnected;
        private bool _closeNotified;

        /// <inheritdoc />
        public NetworkConnectionState State => _state;

        /// <inheritdoc />
        public void SetCallbacks(
            Action<byte[], int> onMessage,
            Action<string> onError,
            Action onClosed)
        {
            _onMessage = onMessage;
            _onError = onError;
            _onClosed = onClosed;
        }

        /// <inheritdoc />
        public async UniTask ConnectAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            LoopbackTlsCertificatePin loopbackCertificatePin,
            CancellationToken ct)
        {
            if (_state == NetworkConnectionState.Connected || _state == NetworkConnectionState.Connecting)
                throw new InvalidOperationException("WebSocket transport is already connected or connecting");

            _closeNotified = false;
            _state = NetworkConnectionState.Connecting;

            _socket?.Dispose();
            _socket = new ClientWebSocket();

            if (loopbackCertificatePin != null)
            {
                loopbackCertificatePin.ValidateEndpoint(new Uri(url));
                _socket.Options.RemoteCertificateValidationCallback =
                    (_, certificate, _, _) => MatchesCertificate(
                        loopbackCertificatePin,
                        certificate);
            }

            if (headers != null)
            {
                foreach (var kv in headers)
                    _socket.Options.SetRequestHeader(kv.Key, kv.Value);
            }

            try
            {
                await _socket.ConnectAsync(new Uri(url), ct);
            }
            catch (OperationCanceledException)
            {
                CleanupSocket();
                _state = NetworkConnectionState.Disconnected;
                throw;
            }
            catch (Exception ex)
            {
                CleanupSocket();
                _state = NetworkConnectionState.Failed;
                NotifyError(ex.Message);
                return;
            }

            if (_socket.State != WebSocketState.Open)
            {
                _state = NetworkConnectionState.Failed;
                NotifyError("WebSocket connect finished but state is not Open");
                CleanupSocket();
                return;
            }

            _state = NetworkConnectionState.Connected;
            _receiveCts?.Cancel();
            _receiveCts?.Dispose();
            _receiveCts = new CancellationTokenSource();
            ReceiveLoopAsync(_receiveCts.Token).Forget();
        }

        /// <inheritdoc />
        public async UniTask CloseAsync(CancellationToken ct)
        {
            if (_state == NetworkConnectionState.Disconnected || _state == NetworkConnectionState.Closing)
            {
                NotifyClosedOnce();
                return;
            }

            _state = NetworkConnectionState.Closing;
            _receiveCts?.Cancel();

            var socket = _socket;
            if (socket != null && (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived))
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                    // 关闭失败仍视为已断开
                }
            }

            CleanupSocket();
            _state = NetworkConnectionState.Disconnected;
            NotifyClosedOnce();
        }

        /// <inheritdoc />
        public async UniTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
                throw new InvalidOperationException("WebSocket is not open");

            await _socket.SendAsync(data, WebSocketMessageType.Binary, true, ct);
        }

        private struct ReceivedMessage
        {
            public byte[] Buffer;
            public int Length;
            public bool HasData => Buffer != null && Length > 0;
        }

        private async UniTaskVoid ReceiveLoopAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested && _socket != null && _socket.State == WebSocketState.Open)
                {
                    var result = await _socket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer), ct);

                    if (result.MessageType == WebSocketMessageType.Close)
                        break;

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        await UniTask.SwitchToMainThread();
                        NotifyError("Received unexpected Text frame; only Binary is supported");
                        continue;
                    }

                    var message = await ReadBinaryMessageAsync(_receiveBuffer, result, ct);
                    if (!message.HasData)
                        continue;

                    await UniTask.SwitchToMainThread();
                    DispatchMessage(message.Buffer, message.Length);
                }
            }
            catch (OperationCanceledException)
            {
                // 主动关闭时正常取消
            }
            catch (Exception ex)
            {
                await UniTask.SwitchToMainThread();
                if (_state == NetworkConnectionState.Connected || _state == NetworkConnectionState.Connecting)
                {
                    _state = NetworkConnectionState.Failed;
                    NotifyError(ex.Message);
                }
            }
            finally
            {
                ReturnFragmentChunks();
            }

            await UniTask.SwitchToMainThread();

            if (_state == NetworkConnectionState.Connected || _state == NetworkConnectionState.Connecting)
            {
                _state = NetworkConnectionState.Failed;
                NotifyClosedOnce();
            }
        }

        /// <summary>
        /// 读取一条完整 Binary 消息。Buffer 来自 ArrayPool，成功派发后由 NetworkConnection 归还。
        /// </summary>
        private async UniTask<ReceivedMessage> ReadBinaryMessageAsync(
            byte[] buffer,
            WebSocketReceiveResult firstResult,
            CancellationToken ct)
        {
            if (firstResult.EndOfMessage)
            {
                if (firstResult.Count == 0)
                    return default;

                return new ReceivedMessage
                {
                    Buffer = RentCopy(buffer, 0, firstResult.Count),
                    Length = firstResult.Count,
                };
            }

            ReturnFragmentChunks();
            AddFragment(buffer, 0, firstResult.Count);

            while (_socket != null && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(new ArraySegment<byte>(_receiveBuffer), ct);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    ReturnFragmentChunks();
                    return default;
                }

                AddFragment(_receiveBuffer, 0, result.Count);

                if (!result.EndOfMessage)
                    continue;

                if (!TryMergeFragments(out var payload, out var length))
                {
                    ReturnFragmentChunks();
                    return default;
                }

                return new ReceivedMessage { Buffer = payload, Length = length };
            }

            ReturnFragmentChunks();
            return default;
        }

        private void AddFragment(byte[] source, int offset, int count)
        {
            if (count <= 0) return;

            var chunk = RentCopy(source, offset, count);
            _fragmentChunks.Add(chunk);
            _fragmentLengths.Add(count);
        }

        private bool TryMergeFragments(out byte[] payload, out int length)
        {
            payload = null;
            length = 0;

            if (_fragmentChunks.Count == 0)
                return false;

            var total = 0;
            for (int i = 0; i < _fragmentLengths.Count; i++)
                total += _fragmentLengths[i];

            if (total == 0)
            {
                ReturnFragmentChunks();
                return false;
            }

            payload = ArrayPool<byte>.Shared.Rent(total);
            var writeOffset = 0;
            for (int i = 0; i < _fragmentChunks.Count; i++)
            {
                var chunk = _fragmentChunks[i];
                var chunkLength = _fragmentLengths[i];
                Buffer.BlockCopy(chunk, 0, payload, writeOffset, chunkLength);
                writeOffset += chunkLength;
                ArrayPool<byte>.Shared.Return(chunk);
            }

            _fragmentChunks.Clear();
            _fragmentLengths.Clear();
            length = total;
            return true;
        }

        private void ReturnFragmentChunks()
        {
            for (int i = 0; i < _fragmentChunks.Count; i++)
                ArrayPool<byte>.Shared.Return(_fragmentChunks[i]);

            _fragmentChunks.Clear();
            _fragmentLengths.Clear();
        }

        private static byte[] RentCopy(byte[] source, int offset, int count)
        {
            var rented = ArrayPool<byte>.Shared.Rent(count);
            Buffer.BlockCopy(source, offset, rented, 0, count);
            return rented;
        }

        private void DispatchMessage(byte[] buffer, int length)
        {
            if (buffer == null) return;
            if (length <= 0)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }

            Action<byte[], int> callback = _onMessage;
            if (callback == null)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }

            try
            {
                callback(buffer, length);
            }
            catch
            {
                ArrayPool<byte>.Shared.Return(buffer);
                throw;
            }
        }

        private void CleanupSocket()
        {
            ReturnFragmentChunks();

            _receiveCts?.Cancel();
            _receiveCts?.Dispose();
            _receiveCts = null;

            _socket?.Dispose();
            _socket = null;
        }

        private void NotifyError(string error)
        {
            _onError?.Invoke(error);
        }

        private void NotifyClosedOnce()
        {
            if (_closeNotified) return;
            _closeNotified = true;
            _onClosed?.Invoke();
        }

        private static bool MatchesCertificate(
            LoopbackTlsCertificatePin pin,
            X509Certificate certificate)
        {
            if (certificate == null) return false;
            try
            {
                return pin.Matches(certificate.GetRawCertData());
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                return false;
            }
        }
    }
}
