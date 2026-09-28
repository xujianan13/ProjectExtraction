using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 协议无关网络模块。管理多条二进制连接，并统一收发队列、主线程派发和平台传输实现。
    /// </summary>
    public sealed class NetworkModule : ModuleBase, INetworkModule
    {
        private const string Tag = "Network";
        private const int DefaultSendQueueSize = 256;
        private const int DefaultReceiveQueueSize = 256;
        private const int DefaultReceiveMessagesPerUpdate = 32;

        private readonly Dictionary<int, NetworkConnection> _connectionsById =
            new Dictionary<int, NetworkConnection>(8);
        private readonly Dictionary<string, NetworkConnection> _connectionsByName =
            new Dictionary<string, NetworkConnection>(8);
        private readonly List<INetworkConnection> _connectionSnapshot =
            new List<INetworkConnection>(8);
        private readonly Func<IWebSocketTransport> _transportFactory;

        private int _nextId = 1;
        private ILogModule _log;

        /// <summary>使用默认 WebSocket 传输实现创建模块。</summary>
        public NetworkModule()
            : this(CreateTransport)
        {
        }

        internal NetworkModule(Func<IWebSocketTransport> transportFactory)
        {
            _transportFactory = transportFactory ??
                                throw new ArgumentNullException(
                                    nameof(transportFactory));
        }

        /// <inheritdoc />
        public override int Priority => 20;

        /// <inheritdoc />
        public int DefaultMaxSendQueueSize { get; set; } =
            DefaultSendQueueSize;

        /// <inheritdoc />
        public int DefaultMaxReceiveQueueSize { get; set; } =
            DefaultReceiveQueueSize;

        /// <inheritdoc />
        public int DefaultMaxReceiveMessagesPerUpdate { get; set; } =
            DefaultReceiveMessagesPerUpdate;

        /// <inheritdoc />
        public IReadOnlyList<INetworkConnection> Connections =>
            _connectionSnapshot;

        /// <inheritdoc />
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            for (int index = 0; index < _connectionSnapshot.Count; index++)
            {
                if (_connectionSnapshot[index] is NetworkConnection connection)
                    connection.OnUpdate();
            }
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            for (int index = _connectionSnapshot.Count - 1; index >= 0; index--)
            {
                if (_connectionSnapshot[index] is NetworkConnection connection)
                    connection.ShutdownAsync(CancellationToken.None).Forget();
            }

            _connectionsById.Clear();
            _connectionsByName.Clear();
            _connectionSnapshot.Clear();
            _log = null;
        }

        /// <inheritdoc />
        public INetworkConnection Create(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException(
                    "连接名称不能为空。",
                    nameof(name));
            if (_connectionsByName.ContainsKey(name))
                throw new InvalidOperationException(
                    $"网络连接名称重复：{name}。");
            ValidateQueueSettings();

            var id = new NetworkConnectionId(_nextId++);
            IWebSocketTransport transport = _transportFactory() ??
                                             throw new InvalidOperationException(
                                                 "网络传输工厂返回了空实现。");
            var connection = new NetworkConnection(
                this,
                id,
                name,
                transport,
                DefaultMaxSendQueueSize,
                DefaultMaxReceiveQueueSize,
                DefaultMaxReceiveMessagesPerUpdate);

            _connectionsById.Add(id.Value, connection);
            _connectionsByName.Add(name, connection);
            _connectionSnapshot.Add(connection);
            _log?.Info(Tag, $"Created connection '{name}' ({id}).");
            return connection;
        }

        /// <inheritdoc />
        public INetworkConnection Get(NetworkConnectionId id)
        {
            return _connectionsById.TryGetValue(
                id.Value,
                out NetworkConnection connection)
                ? connection
                : null;
        }

        /// <inheritdoc />
        public INetworkConnection GetByName(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            return _connectionsByName.TryGetValue(
                name,
                out NetworkConnection connection)
                ? connection
                : null;
        }

        /// <inheritdoc />
        public bool Destroy(NetworkConnectionId id)
        {
            if (!_connectionsById.TryGetValue(
                    id.Value,
                    out NetworkConnection connection))
                return false;

            connection.ShutdownAsync(CancellationToken.None).Forget();
            _connectionsById.Remove(id.Value);
            _connectionsByName.Remove(connection.Name);
            _connectionSnapshot.Remove(connection);
            _log?.Info(Tag, $"Destroyed connection '{connection.Name}' ({id}).");
            return true;
        }

        internal void LogError(string connectionName, string message)
        {
            _log?.Error(Tag, $"[{connectionName}] {message}");
        }

        private void ValidateQueueSettings()
        {
            if (DefaultMaxSendQueueSize <= 0)
                throw new InvalidOperationException(
                    "DefaultMaxSendQueueSize 必须大于零。");
            if (DefaultMaxReceiveQueueSize <= 0)
                throw new InvalidOperationException(
                    "DefaultMaxReceiveQueueSize 必须大于零。");
            if (DefaultMaxReceiveMessagesPerUpdate <= 0)
                throw new InvalidOperationException(
                    "DefaultMaxReceiveMessagesPerUpdate 必须大于零。");
        }

        private static IWebSocketTransport CreateTransport()
        {
            return new ClientWebSocketTransport();
        }
    }
}
