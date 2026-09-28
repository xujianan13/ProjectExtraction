using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 事件总线默认实现。每个 EventId 对应一个通道（有/无 payload 二选一），通道内部用 List 维护订阅者。
    /// 派发期间允许 Subscribe / Unsubscribe / Clear，操作会延后到本通道分发结束后统一应用，避免迭代时结构修改。
    /// </summary>
    public sealed class EventModule : ModuleBase, IEventModule
    {
        private const string Tag = "Event";

        /// <inheritdoc />
        public override int Priority => -500;

        private readonly Dictionary<EventId, IChannel> _channels = new(64);
        private ILogModule _log;

        /// <inheritdoc />
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            ClearAll();
            _log = null;
        }

        /// <inheritdoc />
        public void Subscribe<T>(EventId id, Action<T> handler) where T : struct
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var channel = GetOrCreate<T>(id);
            channel.Subscribe(handler);
        }

        /// <inheritdoc />
        public void Unsubscribe<T>(EventId id, Action<T> handler) where T : struct
        {
            if (handler == null) return;
            if (!_channels.TryGetValue(id, out var ch)) return;
            if (ch is Channel<T> typed) typed.Unsubscribe(handler);
        }

        /// <inheritdoc />
        public void Publish<T>(EventId id, in T evt) where T : struct
        {
            if (!_channels.TryGetValue(id, out var ch)) return;
            if (ch is Channel<T> typed)
            {
                typed.Publish(in evt, this);
                return;
            }
            ThrowPayloadMismatch(id, typeof(T), ch.PayloadType);
        }

        /// <inheritdoc />
        public void Subscribe(EventId id, Action handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var channel = GetOrCreateVoid(id);
            channel.Subscribe(handler);
        }

        /// <inheritdoc />
        public void Unsubscribe(EventId id, Action handler)
        {
            if (handler == null) return;
            if (!_channels.TryGetValue(id, out var ch)) return;
            if (ch is VoidChannel voidCh) voidCh.Unsubscribe(handler);
        }

        /// <inheritdoc />
        public void Publish(EventId id)
        {
            if (!_channels.TryGetValue(id, out var ch)) return;
            if (ch is VoidChannel voidCh)
            {
                voidCh.Publish(this);
                return;
            }
            ThrowPayloadMismatch(id, null, ch.PayloadType);
        }

        /// <inheritdoc />
        public void Clear(EventId id)
        {
            if (_channels.TryGetValue(id, out var ch)) ch.Clear();
        }

        /// <inheritdoc />
        public void ClearAll()
        {
            foreach (var ch in _channels.Values) ch.Clear();
            _channels.Clear();
        }

        // Channel 在派发期间调用，把 handler 抛出的异常导给日志，避免单个 handler 影响整组分发。
        internal void OnHandlerException(Exception ex)
        {
            _log?.Error(Tag, $"Handler threw: {ex}");
        }

        private Channel<T> GetOrCreate<T>(EventId id) where T : struct
        {
            if (_channels.TryGetValue(id, out var ch))
            {
                if (ch is Channel<T> typed) return typed;
                ThrowPayloadMismatch(id, typeof(T), ch.PayloadType);
            }
            var created = new Channel<T>();
            _channels.Add(id, created);
            return created;
        }

        private VoidChannel GetOrCreateVoid(EventId id)
        {
            if (_channels.TryGetValue(id, out var ch))
            {
                if (ch is VoidChannel voidCh) return voidCh;
                ThrowPayloadMismatch(id, null, ch.PayloadType);
            }
            var created = new VoidChannel();
            _channels.Add(id, created);
            return created;
        }

        private static void ThrowPayloadMismatch(EventId id, Type requested, Type existing)
        {
            var req = requested == null ? "void" : requested.Name;
            var exi = existing == null ? "void" : existing.Name;
            throw new InvalidOperationException(
                $"EventId {id.Value} payload type mismatch: expected {exi}, got {req}");
        }

        // 通道基础接口：只暴露非泛型操作，方便外层用 Dictionary<EventId, IChannel> 统一存储。
        private interface IChannel
        {
            Type PayloadType { get; }
            void Clear();
        }

        // 带 payload 的通道。Publish 用 `in T` 传递避免拷贝；订阅者增删在分发期间延后应用。
        private sealed class Channel<T> : IChannel where T : struct
        {
            private readonly List<Action<T>> _handlers = new(4);
            private List<Action<T>> _pendingAdd;
            private List<Action<T>> _pendingRemove;
            private int _dispatching;
            private bool _pendingClear;

            public Type PayloadType => typeof(T);

            public void Subscribe(Action<T> handler)
            {
                if (_dispatching > 0)
                {
                    (_pendingAdd ??= new List<Action<T>>(4)).Add(handler);
                    return;
                }
                _handlers.Add(handler);
            }

            public void Unsubscribe(Action<T> handler)
            {
                if (_dispatching > 0)
                {
                    (_pendingRemove ??= new List<Action<T>>(4)).Add(handler);
                    return;
                }
                _handlers.Remove(handler);
            }

            public void Publish(in T evt, EventModule owner)
            {
                _dispatching++;
                try
                {
                    int count = _handlers.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try { _handlers[i](evt); }
                        catch (Exception ex) { owner.OnHandlerException(ex); }
                    }
                }
                finally
                {
                    _dispatching--;
                    if (_dispatching == 0) ApplyPending();
                }
            }

            public void Clear()
            {
                if (_dispatching > 0) { _pendingClear = true; return; }
                _handlers.Clear();
                _pendingAdd?.Clear();
                _pendingRemove?.Clear();
            }

            private void ApplyPending()
            {
                if (_pendingClear)
                {
                    _handlers.Clear();
                    _pendingAdd?.Clear();
                    _pendingRemove?.Clear();
                    _pendingClear = false;
                    return;
                }
                if (_pendingRemove != null && _pendingRemove.Count > 0)
                {
                    for (int i = 0; i < _pendingRemove.Count; i++) _handlers.Remove(_pendingRemove[i]);
                    _pendingRemove.Clear();
                }
                if (_pendingAdd != null && _pendingAdd.Count > 0)
                {
                    for (int i = 0; i < _pendingAdd.Count; i++) _handlers.Add(_pendingAdd[i]);
                    _pendingAdd.Clear();
                }
            }
        }

        // 无 payload 通道。复用 Channel<T> 的语义，但避免引入一个空 struct 类型。
        private sealed class VoidChannel : IChannel
        {
            private readonly List<Action> _handlers = new(4);
            private List<Action> _pendingAdd;
            private List<Action> _pendingRemove;
            private int _dispatching;
            private bool _pendingClear;

            public Type PayloadType => null;

            public void Subscribe(Action handler)
            {
                if (_dispatching > 0)
                {
                    (_pendingAdd ??= new List<Action>(4)).Add(handler);
                    return;
                }
                _handlers.Add(handler);
            }

            public void Unsubscribe(Action handler)
            {
                if (_dispatching > 0)
                {
                    (_pendingRemove ??= new List<Action>(4)).Add(handler);
                    return;
                }
                _handlers.Remove(handler);
            }

            public void Publish(EventModule owner)
            {
                _dispatching++;
                try
                {
                    int count = _handlers.Count;
                    for (int i = 0; i < count; i++)
                    {
                        try { _handlers[i](); }
                        catch (Exception ex) { owner.OnHandlerException(ex); }
                    }
                }
                finally
                {
                    _dispatching--;
                    if (_dispatching == 0) ApplyPending();
                }
            }

            public void Clear()
            {
                if (_dispatching > 0) { _pendingClear = true; return; }
                _handlers.Clear();
                _pendingAdd?.Clear();
                _pendingRemove?.Clear();
            }

            private void ApplyPending()
            {
                if (_pendingClear)
                {
                    _handlers.Clear();
                    _pendingAdd?.Clear();
                    _pendingRemove?.Clear();
                    _pendingClear = false;
                    return;
                }
                if (_pendingRemove != null && _pendingRemove.Count > 0)
                {
                    for (int i = 0; i < _pendingRemove.Count; i++) _handlers.Remove(_pendingRemove[i]);
                    _pendingRemove.Clear();
                }
                if (_pendingAdd != null && _pendingAdd.Count > 0)
                {
                    for (int i = 0; i < _pendingAdd.Count; i++) _handlers.Add(_pendingAdd[i]);
                    _pendingAdd.Clear();
                }
            }
        }
    }
}
