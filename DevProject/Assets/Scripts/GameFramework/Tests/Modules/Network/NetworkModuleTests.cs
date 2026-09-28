using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace GameFramework.Tests.Modules.Network
{
    /// <summary>NetworkModule 连接管理、接收队列和每帧派发预算测试。</summary>
    [TestFixture]
    public sealed class NetworkModuleTests
    {
        private FakeWebSocketTransport _transport;
        private NetworkModule _module;

        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.SetupWithLog();
            _transport = new FakeWebSocketTransport();
            _module = new NetworkModule(() => _transport);
            _module.OnInit();
        }

        [TearDown]
        public void TearDown()
        {
            _module.OnShutdown();
            FrameworkTestHelper.ResetFramework();
        }

        [Test]
        public void Create_ReturnsConnection()
        {
            INetworkConnection connection = _module.Create("main");
            Assert.IsNotNull(connection);
            Assert.AreEqual("main", connection.Name);
            Assert.AreEqual(1, _module.Connections.Count);
        }

        [Test]
        public void Create_DuplicateName_Throws()
        {
            _module.Create("main");
            Assert.Throws<InvalidOperationException>(() =>
                _module.Create("main"));
        }

        [Test]
        public void Create_EmptyName_Throws()
        {
            Assert.Throws<ArgumentException>(() => _module.Create(""));
            Assert.Throws<ArgumentException>(() => _module.Create(null));
        }

        [Test]
        public void GetAndDestroy_UseStableConnectionId()
        {
            INetworkConnection connection = _module.Create("main");
            Assert.AreSame(connection, _module.GetByName("main"));
            Assert.AreSame(connection, _module.Get(connection.Id));
            Assert.IsTrue(_module.Destroy(connection.Id));
            Assert.IsNull(_module.Get(connection.Id));
            Assert.AreEqual(0, _module.Connections.Count);
            Assert.IsFalse(
                _module.Destroy(new NetworkConnectionId(999)));
        }

        [Test]
        public void QueueDefaults_ArePositive()
        {
            Assert.Greater(_module.DefaultMaxSendQueueSize, 0);
            Assert.Greater(_module.DefaultMaxReceiveQueueSize, 0);
            Assert.Greater(
                _module.DefaultMaxReceiveMessagesPerUpdate,
                0);
        }

        /// <summary>
        /// 发送队列按入队顺序发送消息。
        /// </summary>
        /// <returns>执行异步断言的测试协程。</returns>
        [UnityTest]
        public IEnumerator SendQueue_SerializesMessagesInOrder()
        {
            return UniTask.ToCoroutine(async () =>
            {
                INetworkConnection connection = _module.Create("main");
                await connection.ConnectAsync("wss://example.com/game");
                _transport.BlockNextSend();

                UniTask first = connection.SendAsync(new byte[] { 1 });
                Assert.IsTrue(connection.Send(new byte[] { 2 }));

                CollectionAssert.AreEqual(new byte[] { 1 }, _transport.SentMessages);
                Assert.AreEqual(1, connection.PendingSendCount);

                _transport.ReleaseBlockedSend();
                await first;
                await UniTask.Yield();

                CollectionAssert.AreEqual(new byte[] { 1, 2 }, _transport.SentMessages);
                Assert.AreEqual(0, connection.PendingSendCount);
            });
        }

        /// <summary>
        /// 接收队列按每帧预算派发消息。
        /// </summary>
        /// <returns>执行异步断言的测试协程。</returns>
        [UnityTest]
        public IEnumerator ReceiveQueue_DispatchesWithinPerUpdateBudget()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _module.DefaultMaxReceiveQueueSize = 4;
                _module.DefaultMaxReceiveMessagesPerUpdate = 2;
                INetworkConnection connection = _module.Create("main");
                var handler = new RecordingNetworkHandler();
                connection.SetMessageHandler(handler);
                await connection.ConnectAsync("wss://example.com/game");

                _transport.Deliver(1);
                _transport.Deliver(2);
                _transport.Deliver(3);

                Assert.AreEqual(0, handler.Messages.Count);
                Assert.AreEqual(3, connection.PendingReceiveCount);
                _module.OnUpdate(0f);
                CollectionAssert.AreEqual(new byte[] { 1, 2 }, handler.Messages);
                Assert.AreEqual(1, connection.PendingReceiveCount);
                _module.OnUpdate(0f);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, handler.Messages);
                Assert.AreEqual(0, connection.PendingReceiveCount);
            });
        }

        /// <summary>
        /// 接收队列溢出时将连接标记为失败。
        /// </summary>
        /// <returns>执行异步断言的测试协程。</returns>
        [UnityTest]
        public IEnumerator ReceiveQueue_OverflowFailsConnection()
        {
            return UniTask.ToCoroutine(async () =>
            {
                _module.DefaultMaxReceiveQueueSize = 1;
                INetworkConnection connection = _module.Create("main");
                var handler = new RecordingNetworkHandler();
                connection.SetMessageHandler(handler);
                await connection.ConnectAsync("wss://example.com/game");

                _transport.Deliver(1);
                _transport.Deliver(2);

                Assert.AreEqual(NetworkConnectionState.Failed, connection.State);
                StringAssert.Contains("Receive queue is full", handler.LastError);
                Assert.AreEqual(0, connection.PendingReceiveCount);
            });
        }

        private sealed class RecordingNetworkHandler : INetworkMessageHandler
        {
            internal List<byte> Messages { get; } = new List<byte>();
            internal string LastError { get; private set; }

            public void OnMessage(
                INetworkConnection connection,
                ReadOnlyMemory<byte> data)
            {
                Messages.Add(data.Span[0]);
            }

            public void OnStateChanged(
                INetworkConnection connection,
                NetworkConnectionState state)
            {
            }

            public void OnError(
                INetworkConnection connection,
                string error)
            {
                LastError = error;
            }
        }

        private sealed class FakeWebSocketTransport : IWebSocketTransport
        {
            private Action<byte[], int> _onMessage;
            private Action<string> _onError;
            private Action _onClosed;
            private UniTaskCompletionSource _nextSendBlock;
            private UniTaskCompletionSource _activeSendBlock;

            internal List<byte> SentMessages { get; } = new List<byte>();

            public NetworkConnectionState State { get; private set; } =
                NetworkConnectionState.Disconnected;

            public UniTask ConnectAsync(
                string url,
                IReadOnlyDictionary<string, string> headers,
                LoopbackTlsCertificatePin loopbackCertificatePin,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                State = NetworkConnectionState.Connected;
                return UniTask.CompletedTask;
            }

            public UniTask CloseAsync(CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                State = NetworkConnectionState.Disconnected;
                _onClosed?.Invoke();
                return UniTask.CompletedTask;
            }

            public async UniTask SendAsync(
                ReadOnlyMemory<byte> data,
                CancellationToken ct)
            {
                ct.ThrowIfCancellationRequested();
                SentMessages.Add(data.Span[0]);
                UniTaskCompletionSource blocked = _nextSendBlock;
                _nextSendBlock = null;
                _activeSendBlock = blocked;
                if (blocked != null)
                {
                    await blocked.Task.AttachExternalCancellation(ct);
                }
                _activeSendBlock = null;
            }

            public void SetCallbacks(
                Action<byte[], int> onMessage,
                Action<string> onError,
                Action onClosed)
            {
                _onMessage = onMessage;
                _onError = onError;
                _onClosed = onClosed;
            }

            internal void BlockNextSend()
            {
                _nextSendBlock = new UniTaskCompletionSource();
            }

            internal void ReleaseBlockedSend()
            {
                _activeSendBlock?.TrySetResult();
            }

            internal void Deliver(byte value)
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
                buffer[0] = value;
                _onMessage(buffer, 1);
            }
        }
    }
}
