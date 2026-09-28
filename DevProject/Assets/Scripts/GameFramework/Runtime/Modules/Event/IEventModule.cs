using System;

namespace GameFramework
{
    /// <summary>
    /// 事件总线模块接口。提供进程内同步事件分发，业务通过 GameEntry.Get&lt;IEventModule&gt;() 访问。
    /// 同一个 EventId 只能绑定一种 payload 类型（含无 payload 形式），重复使用不同类型会抛异常。
    /// </summary>
    public interface IEventModule : IModule
    {
        /// <summary>订阅带 payload 的事件。重复订阅同一 handler 会被记录多次，由业务自行控制。</summary>
        /// <typeparam name="T">事件载荷类型，要求是 struct，避免装箱与 GC。</typeparam>
        void Subscribe<T>(EventId id, Action<T> handler) where T : struct;

        /// <summary>取消订阅。未匹配的 handler 静默忽略，不抛异常。</summary>
        void Unsubscribe<T>(EventId id, Action<T> handler) where T : struct;

        /// <summary>同步派发事件。无订阅者时空操作。Handler 抛异常会被捕获并写日志，不影响其他订阅者。</summary>
        void Publish<T>(EventId id, in T evt) where T : struct;

        /// <summary>订阅无 payload 事件。</summary>
        void Subscribe(EventId id, Action handler);

        /// <summary>取消无 payload 事件订阅。</summary>
        void Unsubscribe(EventId id, Action handler);

        /// <summary>派发无 payload 事件。</summary>
        void Publish(EventId id);

        /// <summary>移除指定 EventId 下的所有订阅者（含 payload / 无 payload 形式）。</summary>
        void Clear(EventId id);

        /// <summary>移除所有 EventId 的订阅者。</summary>
        void ClearAll();
    }
}
