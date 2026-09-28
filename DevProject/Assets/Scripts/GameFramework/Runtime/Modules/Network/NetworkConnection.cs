using System;
using System.Buffers;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>单条协议无关二进制网络连接实现。</summary>
    internal sealed class NetworkConnection : INetworkConnection
    {
        private readonly NetworkModule _owner;
        private readonly IWebSocketTransport _transport;
        private readonly Queue<PendingSend> _sendQueue;
        private readonly Queue<PendingReceive> _receiveQueue;
        private readonly int _maxSendQueueSize;
        private readonly int _maxReceiveQueueSize;
        private readonly int _maxReceiveMessagesPerUpdate;

        private INetworkMessageHandler _handler;
        private NetworkConnectionState _state =
            NetworkConnectionState.Disconnected;
        private bool _isSending;
        private bool _shutdown;

        internal NetworkConnection(
            NetworkModule owner,
            NetworkConnectionId id,
            string name,
            IWebSocketTransport transport,
            int maxSendQueueSize,
            int maxReceiveQueueSize,
            int maxReceiveMessagesPerUpdate)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            Id = id;
            Name = name ?? throw new ArgumentNullException(nameof(name));
            _transport = transport ??
                         throw new ArgumentNullException(nameof(transport));
            if (maxSendQueueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxSendQueueSize));
            if (maxReceiveQueueSize <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxReceiveQueueSize));
            if (maxReceiveMessagesPerUpdate <= 0)
                throw new ArgumentOutOfRangeException(
                    nameof(maxReceiveMessagesPerUpdate));

            _maxSendQueueSize = maxSendQueueSize;
            _maxReceiveQueueSize = maxReceiveQueueSize;
            _maxReceiveMessagesPerUpdate = maxReceiveMessagesPerUpdate;
            _sendQueue = new Queue<PendingSend>(maxSendQueueSize);
            _receiveQueue = new Queue<PendingReceive>(maxReceiveQueueSize);
            _transport.SetCallbacks(
                OnTransportMessage,
                OnTransportError,
                OnTransportClosed);
        }

        /// <inheritdoc />
        public NetworkConnectionId Id { get; }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public NetworkConnectionState State => _state;

        /// <inheritdoc />
        public int PendingSendCount => _sendQueue.Count;

        /// <inheritdoc />
        public int PendingReceiveCount => _receiveQueue.Count;

        /// <inheritdoc />
        public void SetMessageHandler(INetworkMessageHandler handler)
        {
            ThrowIfShutdown();
            _handler = handler;
        }

        /// <inheritdoc />
        public UniTask ConnectAsync(
            string url,
            IReadOnlyDictionary<string, string> headers = null,
            CancellationToken ct = default)
        {
            return ConnectAsync(url, headers, null, ct);
        }

        /// <inheritdoc />
        public async UniTask ConnectAsync(
            string url,
            IReadOnlyDictionary<string, string> headers,
            LoopbackTlsCertificatePin loopbackCertificatePin,
            CancellationToken ct = default)
        {
            ThrowIfShutdown();
            if (string.IsNullOrEmpty(url))
                throw new ArgumentException("url 不能为空。", nameof(url));
            if (loopbackCertificatePin != null)
            {
                if (!Uri.TryCreate(url, UriKind.Absolute, out Uri endpoint))
                    throw new ArgumentException("url 不是绝对地址。", nameof(url));
                loopbackCertificatePin.ValidateEndpoint(endpoint);
            }
            if (_state == NetworkConnectionState.Connected ||
                _state == NetworkConnectionState.Connecting)
                throw new InvalidOperationException(
                    $"网络连接 '{Name}' 已经连接或正在连接。");

            SetState(NetworkConnectionState.Connecting);
            try
            {
                await _transport.ConnectAsync(
                    url,
                    headers,
                    loopbackCertificatePin,
                    ct);
            }
            catch (OperationCanceledException)
            {
                ClearSendQueue(
                    new OperationCanceledException("连接建立已取消。"));
                SetState(NetworkConnectionState.Disconnected);
                throw;
            }

            if (_transport.State == NetworkConnectionState.Connected)
            {
                SetState(NetworkConnectionState.Connected);
                StartSendPump();
                return;
            }
            if (_state != NetworkConnectionState.Failed)
            {
                SetState(NetworkConnectionState.Failed);
                ClearSendQueue(
                    new InvalidOperationException("底层网络连接建立失败。"));
            }
        }

        /// <inheritdoc />
        public async UniTask CloseAsync(CancellationToken ct = default)
        {
            if (_state == NetworkConnectionState.Closing) return;
            ClearSendQueue(new InvalidOperationException("连接已经关闭。"));
            ClearReceiveQueue();
            if (_state == NetworkConnectionState.Disconnected) return;

            SetState(NetworkConnectionState.Closing);
            try
            {
                await _transport.CloseAsync(ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                _owner.LogError(Name, $"Close failed: {exception.Message}");
            }
            SetState(NetworkConnectionState.Disconnected);
        }

        /// <inheritdoc />
        public bool Send(ReadOnlySpan<byte> data)
        {
            ThrowIfShutdown();
            if (data.IsEmpty) return true;
            if (!CanQueueSend())
            {
                NotifyError("Send rejected: connection is not connected or connecting");
                return false;
            }
            if (!TryEnqueueSend(data, null)) return false;
            StartSendPump();
            return true;
        }

        /// <inheritdoc />
        public async UniTask SendAsync(
            ReadOnlyMemory<byte> data,
            CancellationToken ct = default)
        {
            ThrowIfShutdown();
            ct.ThrowIfCancellationRequested();
            if (data.IsEmpty) return;
            if (!CanQueueSend())
                throw new InvalidOperationException(
                    "Send rejected: connection is not connected or connecting");

            var completion = new UniTaskCompletionSource();
            if (!TryEnqueueSend(data.Span, completion))
                throw new InvalidOperationException("Send queue is full");
            StartSendPump();
            await completion.Task.AttachExternalCancellation(ct);
        }

        internal void OnUpdate()
        {
            StartSendPump();
            int remaining = _maxReceiveMessagesPerUpdate;
            while (remaining-- > 0 && _receiveQueue.Count > 0)
            {
                PendingReceive pending = _receiveQueue.Dequeue();
                try
                {
                    _handler?.OnMessage(this, pending.AsMemory());
                }
                catch (Exception exception)
                {
                    _owner.LogError(
                        Name,
                        $"Message handler failed: {exception}");
                }
                finally
                {
                    pending.Return();
                }
            }
        }

        internal async UniTask ShutdownAsync(CancellationToken ct)
        {
            if (_shutdown) return;
            _shutdown = true;
            try
            {
                await CloseAsync(ct);
            }
            catch (OperationCanceledException)
            {
                SetState(NetworkConnectionState.Disconnected);
            }
            finally
            {
                ClearSendQueue(new ObjectDisposedException(Name));
                ClearReceiveQueue();
                _handler = null;
            }
        }

        private bool CanQueueSend()
        {
            return _state == NetworkConnectionState.Connected ||
                   _state == NetworkConnectionState.Connecting;
        }

        private bool TryEnqueueSend(
            ReadOnlySpan<byte> data,
            UniTaskCompletionSource completion)
        {
            if (_sendQueue.Count >= _maxSendQueueSize)
            {
                NotifyError(
                    $"Send queue is full (max {_maxSendQueueSize})");
                return false;
            }
            _sendQueue.Enqueue(PendingSend.Copy(data, completion));
            return true;
        }

        private void StartSendPump()
        {
            if (_state != NetworkConnectionState.Connected ||
                _isSending ||
                _sendQueue.Count == 0)
                return;
            SendPumpAsync().Forget();
        }

        private async UniTaskVoid SendPumpAsync()
        {
            _isSending = true;
            try
            {
                while (_sendQueue.Count > 0 &&
                       _state == NetworkConnectionState.Connected)
                {
                    PendingSend pending = _sendQueue.Dequeue();
                    try
                    {
                        await _transport.SendAsync(
                            pending.AsMemory(),
                            CancellationToken.None);
                        pending.TrySetResult();
                    }
                    catch (Exception exception)
                    {
                        pending.TrySetException(exception);
                        NotifyError($"Send failed: {exception.Message}");
                        SetState(NetworkConnectionState.Failed);
                        ClearSendQueue(exception);
                        StopTransportAfterFailure();
                        break;
                    }
                    finally
                    {
                        pending.Return();
                    }
                }
            }
            finally
            {
                _isSending = false;
            }
        }

        private void ClearSendQueue(Exception exception)
        {
            while (_sendQueue.Count > 0)
            {
                PendingSend pending = _sendQueue.Dequeue();
                if (exception != null)
                    pending.TrySetException(exception);
                pending.Return();
            }
        }

        private void ClearReceiveQueue()
        {
            while (_receiveQueue.Count > 0)
            {
                PendingReceive pending = _receiveQueue.Dequeue();
                pending.Return();
            }
        }

        private void OnTransportMessage(byte[] buffer, int length)
        {
            if (buffer == null) return;
            if (_shutdown || length <= 0 || length > buffer.Length)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                return;
            }
            if (_receiveQueue.Count >= _maxReceiveQueueSize)
            {
                ArrayPool<byte>.Shared.Return(buffer);
                string error =
                    $"Receive queue is full (max {_maxReceiveQueueSize})";
                SetState(NetworkConnectionState.Failed);
                ClearSendQueue(new InvalidOperationException(error));
                StopTransportAfterFailure();
                NotifyError(error);
                return;
            }
            _receiveQueue.Enqueue(new PendingReceive(buffer, length));
        }

        private void OnTransportError(string error)
        {
            if (_state == NetworkConnectionState.Closing ||
                _state == NetworkConnectionState.Disconnected)
                return;
            SetState(NetworkConnectionState.Failed);
            ClearSendQueue(new InvalidOperationException(error));
            StopTransportAfterFailure();
            NotifyError(error);
        }

        private void StopTransportAfterFailure()
        {
            ClearReceiveQueue();
            _transport.CloseAsync(CancellationToken.None).Forget();
        }

        private void OnTransportClosed()
        {
            if (_state == NetworkConnectionState.Closing)
            {
                SetState(NetworkConnectionState.Disconnected);
                return;
            }
            if (_state == NetworkConnectionState.Disconnected) return;
            SetState(NetworkConnectionState.Failed);
            ClearReceiveQueue();
            ClearSendQueue(
                new InvalidOperationException("底层网络连接已经关闭。"));
        }

        private void SetState(NetworkConnectionState state)
        {
            if (_state == state) return;
            _state = state;
            try
            {
                _handler?.OnStateChanged(this, state);
            }
            catch (Exception exception)
            {
                _owner.LogError(
                    Name,
                    $"State handler failed: {exception}");
            }
        }

        private void NotifyError(string error)
        {
            try
            {
                _handler?.OnError(this, error);
            }
            catch (Exception exception)
            {
                _owner.LogError(
                    Name,
                    $"Error handler failed: {exception}");
            }
        }

        private void ThrowIfShutdown()
        {
            if (_shutdown)
                throw new ObjectDisposedException(Name);
        }

        private struct PendingSend
        {
            private byte[] _buffer;
            private readonly int _length;
            private readonly UniTaskCompletionSource _completion;

            private PendingSend(
                byte[] buffer,
                int length,
                UniTaskCompletionSource completion)
            {
                _buffer = buffer;
                _length = length;
                _completion = completion;
            }

            internal static PendingSend Copy(
                ReadOnlySpan<byte> data,
                UniTaskCompletionSource completion)
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(data.Length);
                data.CopyTo(buffer);
                return new PendingSend(buffer, data.Length, completion);
            }

            internal ReadOnlyMemory<byte> AsMemory()
            {
                return new ReadOnlyMemory<byte>(_buffer, 0, _length);
            }

            internal void TrySetResult()
            {
                _completion?.TrySetResult();
            }

            internal void TrySetException(Exception exception)
            {
                _completion?.TrySetException(exception);
            }

            internal void Return()
            {
                if (_buffer == null) return;
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }

        private struct PendingReceive
        {
            private byte[] _buffer;
            private readonly int _length;

            internal PendingReceive(byte[] buffer, int length)
            {
                _buffer = buffer;
                _length = length;
            }

            internal ReadOnlyMemory<byte> AsMemory()
            {
                return new ReadOnlyMemory<byte>(_buffer, 0, _length);
            }

            internal void Return()
            {
                if (_buffer == null) return;
                ArrayPool<byte>.Shared.Return(_buffer);
                _buffer = null;
            }
        }
    }
}
