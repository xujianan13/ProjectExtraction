using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 生命周期作用域的阶段。
    /// </summary>
    public enum LifetimeScopeState { Active, Stopping, Disposed }

    /// <summary>
    /// 持有一轮使用期间的清理责任。创建和操作须在同一线程执行。
    /// </summary>
    public sealed class LifetimeScope : IDisposable
    {
        private readonly int _threadId;
        private readonly SynchronizationContext _context;
        private readonly CancellationToken _externalToken;
        private readonly CancellationTokenSource _source = new();
        private readonly CancellationToken _token;
        private readonly LinkedList<ScopeRegistration> _registrations = new();
        private readonly LinkedList<LifetimeScope> _children = new();
        private LifetimeScope _parent;
        private LinkedListNode<LifetimeScope> _parentNode;
        private CancellationTokenRegistration _externalRegistration;
        private volatile LifetimeScopeState _state;
        private bool _stopping;
        private bool _disposing;
        private bool _disposeRequested;

        /// <summary>
        /// 创建独立作用域。
        /// </summary>
        /// <param name="name">诊断名称。</param>
        /// <param name="cancellationToken">使作用域停止的外部令牌。</param>
        public LifetimeScope(string name = null, CancellationToken cancellationToken = default)
        {
            if (!PlayerLoopHelper.IsMainThread) throw new InvalidOperationException("LifetimeScope 只能在 Unity 主线程创建。");
            Name = name;
            _threadId = Thread.CurrentThread.ManagedThreadId;
            _context = SynchronizationContext.Current;
            _externalToken = cancellationToken;
            _token = _source.Token;
            _state = LifetimeScopeState.Active;
            if (cancellationToken.CanBeCanceled) _externalRegistration = cancellationToken.Register(OnExternalCanceled);
            if (cancellationToken.IsCancellationRequested) Stop();
        }

        /// <summary>
        /// 创建与父级关联的子作用域。
        /// </summary>
        /// <param name="parent">父作用域。</param>
        /// <param name="name">诊断名称。</param>
        private LifetimeScope(LifetimeScope parent, string name) : this(name)
        {
            _parent = parent;
            _parentNode = parent._children.AddLast(this);
        }

        /// <summary>
        /// 诊断名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 当前阶段。
        /// </summary>
        public LifetimeScopeState State => _state;

        /// <summary>
        /// 是否仍接受新工作。
        /// </summary>
        public bool IsActive => _state == LifetimeScopeState.Active && !_externalToken.IsCancellationRequested && (_parent == null || _parent.IsActive);

        /// <summary>
        /// 结束后保持已取消的令牌。
        /// </summary>
        public CancellationToken Token => _token;

        /// <summary>
        /// 当前清理登记数。
        /// </summary>
        public int RegistrationCount => _registrations.Count;

        /// <summary>
        /// 当前子作用域数。
        /// </summary>
        public int ChildCount => _children.Count;

        /// <summary>
        /// 创建由本作用域持有的子作用域。
        /// </summary>
        /// <param name="name">子作用域诊断名称。</param>
        /// <returns>新建的子作用域；父级已失活时返回 null。</returns>
        public LifetimeScope CreateChild(string name = null)
        {
            CheckThread();
            return IsActive ? new LifetimeScope(this, name) : null;
        }

        /// <summary>
        /// 接管可释放对象。
        /// </summary>
        /// <param name="disposable">待接管对象。</param>
        /// <returns>是否成功登记。</returns>
        public bool TryOwn(IDisposable disposable) => TryOwn(disposable, out _);

        /// <summary>
        /// 接管可释放对象，并返回提前释放句柄。
        /// </summary>
        /// <param name="disposable">待接管对象。</param>
        /// <param name="registration">提前释放句柄。</param>
        /// <returns>是否成功登记。</returns>
        public bool TryOwn(IDisposable disposable, out ScopeRegistration registration)
        {
            CheckThread();
            if (disposable == null) throw new ArgumentNullException(nameof(disposable));
            if (!IsActive)
            {
                registration = null;
                CleanSafely(disposable.Dispose);
                return false;
            }
            registration = new ScopeRegistration(this, disposable.Dispose);
            registration.Attach(_registrations.AddLast(registration));
            return true;
        }

        /// <summary>
        /// 登记自定义清理操作。
        /// </summary>
        /// <param name="cleanup">清理操作。</param>
        /// <returns>提前清理句柄；作用域失活时已立即清理。</returns>
        public ScopeRegistration Defer(Action cleanup)
        {
            CheckThread();
            if (cleanup == null) throw new ArgumentNullException(nameof(cleanup));
            var registration = new ScopeRegistration(this, cleanup);
            if (IsActive) registration.Attach(_registrations.AddLast(registration));
            else registration.Dispose();
            return registration;
        }

        /// <summary>
        /// 先停止子作用域，再通知本作用域的取消订阅者。
        /// </summary>
        public void Stop()
        {
            CheckThread();
            if (_state != LifetimeScopeState.Active || _stopping) return;
            _state = LifetimeScopeState.Stopping;
            _stopping = true;
            try
            {
                var children = new List<LifetimeScope>(_children);
                for (int index = children.Count - 1; index >= 0; index--)
                    children[index].Stop();
                try
                {
                    _source.Cancel();
                }
                catch (AggregateException error)
                {
                    foreach (Exception inner in error.InnerExceptions) LogCleanupError(inner);
                }
                catch (Exception error) { LogCleanupError(error); }
            }
            finally
            {
                _stopping = false;
                if (_disposeRequested) DisposeCore();
            }
        }

        /// <summary>
        /// 停止并逆序释放所有持有物。
        /// </summary>
        public void Dispose()
        {
            CheckThread();
            if (_state == LifetimeScopeState.Disposed || _disposing) return;
            _disposeRequested = true;
            if (_state == LifetimeScopeState.Active) Stop();
            if (!_stopping) DisposeCore();
        }

        /// <summary>
        /// 移除登记节点。
        /// </summary>
        /// <param name="node">登记节点。</param>
        internal void Remove(LinkedListNode<ScopeRegistration> node)
        {
            CheckThread();
            if (node?.List == _registrations) _registrations.Remove(node);
        }

        /// <summary>
        /// 在清理边界记录异常并继续。
        /// </summary>
        /// <param name="cleanup">清理操作。</param>
        internal static void CleanSafely(Action cleanup)
        {
            try
            {
                cleanup();
            }
            catch (Exception error)
            {
                LogCleanupError(error);
            }
        }

        /// <summary>
        /// 保证所有权结构仅从创建线程修改。
        /// </summary>
        internal void CheckThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _threadId)
                throw new InvalidOperationException("LifetimeScope 只能在创建线程操作。");
        }

        /// <summary>
        /// 接收外部取消，并切回创建线程停止作用域。
        /// </summary>
        private void OnExternalCanceled()
        {
            if (Thread.CurrentThread.ManagedThreadId == _threadId) Stop();
            else if (_context != null) _context.Post(_ => Stop(), null);
            else UniTask.Post(Stop);
        }

        /// <summary>
        /// 完成释放，允许取消回调请求释放时延后执行。
        /// </summary>
        private void DisposeCore()
        {
            if (_disposing || _state == LifetimeScopeState.Disposed) return;
            _disposing = true;
            try
            {
                while (_children.Last != null)
                {
                    var child = _children.Last.Value;
                    if (child._stopping || child._disposing)
                    {
                        child.Dispose();
                        return;
                    }
                    child.Dispose();
                }
                while (_registrations.Last != null) _registrations.Last.Value.Dispose();
                _externalRegistration.Dispose();
                _source.Dispose();
                if (_parentNode != null)
                {
                    var parent = _parent;
                    parent._children.Remove(_parentNode);
                    _parentNode = null;
                    _parent = null;
                    if (parent._disposeRequested && !parent._stopping) parent.DisposeCore();
                }
                _state = LifetimeScopeState.Disposed;
            }
            finally { _disposing = false; }
        }

        /// <summary>
        /// 向框架日志记录生命周期清理失败；日志自身失败时继续后续清理。
        /// </summary>
        /// <param name="error">清理异常。</param>
        private static void LogCleanupError(Exception error)
        {
            try
            {
                GameEntry.Get<ILogModule>()?.Error("LifetimeScope", $"清理失败：{error}");
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 可提前移除并清理的登记句柄。
    /// </summary>
    public sealed class ScopeRegistration : IDisposable
    {
        private LifetimeScope _owner;
        private LinkedListNode<ScopeRegistration> _node;
        private Action _cleanup;

        /// <summary>
        /// 创建待加入作用域的登记。
        /// </summary>
        /// <param name="owner">所属作用域。</param>
        /// <param name="cleanup">清理操作。</param>
        internal ScopeRegistration(LifetimeScope owner, Action cleanup)
        {
            _owner = owner;
            _cleanup = cleanup;
        }

        /// <summary>
        /// 保存所属链表节点。
        /// </summary>
        /// <param name="node">登记节点。</param>
        internal void Attach(LinkedListNode<ScopeRegistration> node) => _node = node;

        /// <summary>
        /// 移除登记且不运行清理，供自然完成的一次性任务使用。
        /// </summary>
        internal void Forget()
        {
            _owner?.CheckThread();
            _owner?.Remove(_node);
            _node = null;
            _owner = null;
            _cleanup = null;
        }

        /// <summary>
        /// 提前释放登记对象，重复调用无效。
        /// </summary>
        public void Dispose()
        {
            _owner?.CheckThread();
            Action cleanup = _cleanup;
            Forget();
            if (cleanup != null) LifetimeScope.CleanSafely(cleanup);
        }
    }
}
