using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameFramework
{
    /// <summary>框架内部：供 <see cref="PoolModule"/> 在卸载时批量清空闲置实例。</summary>
    internal interface IClearablePool
    {
        /// <summary>清空所有闲置实例。</summary>
        void ClearInactive();
    }

    /// <summary>框架内部：供 <see cref="PoolModule"/> 按运行时类型归还多态实例。</summary>
    internal interface IUntypedObjectPool
    {
        /// <summary>将实例归还池中。</summary>
        void ReturnObject(object item);
    }

    /// <summary>
    /// 泛型对象池。通过工厂创建实例，租出 / 归还时可选回调；支持预分配与闲置上限。
    /// 非线程安全，适用于 Unity 主线程（含微信小游戏）。
    /// </summary>
    /// <typeparam name="T">引用类型池元素。</typeparam>
    public sealed class ObjectPool<T> : IClearablePool, IUntypedObjectPool where T : class
    {
        private readonly Func<T> _factory;
        private readonly Action<T> _onRent;
        private readonly Action<T> _onReturn;
        private readonly int _maxSize;
        private readonly Stack<T> _inactive;
        private readonly HashSet<T> _active;

        private int _countAll;

        /// <summary>
        /// 创建对象池。
        /// </summary>
        /// <param name="initialCapacity">初始闲置容量（Stack 预分配 + 可选预创建数量，见 <paramref name="prewarmCount"/>）。</param>
        /// <param name="factory">实例工厂，池空时租出会调用。</param>
        /// <param name="onRent">租出后额外回调（在 <see cref="IPoolable.OnRent"/> 之后）。</param>
        /// <param name="onReturn">归还前额外回调（在 <see cref="IPoolable.OnReturn"/> 之后）。</param>
        /// <param name="maxSize">闲置实例上限；0 表示不限制。超出时归还的对象不再入池，交由 GC 回收。</param>
        /// <param name="prewarmCount">构造时预创建并放入池中的实例数，不超过 <paramref name="maxSize"/>（若 maxSize &gt; 0）。</param>
        public ObjectPool(
            int initialCapacity,
            Func<T> factory,
            Action<T> onRent = null,
            Action<T> onReturn = null,
            int maxSize = 0,
            int prewarmCount = 0)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            if (maxSize < 0) throw new ArgumentOutOfRangeException(nameof(maxSize));
            if (prewarmCount < 0) throw new ArgumentOutOfRangeException(nameof(prewarmCount));

            _factory = factory;
            _onRent = onRent;
            _onReturn = onReturn;
            _maxSize = maxSize;
            _inactive = new Stack<T>(initialCapacity > 0 ? initialCapacity : 4);
            _active = new HashSet<T>(ReferenceEqualityComparer.Instance);

            if (prewarmCount > 0)
            {
                Warmup(prewarmCount);
            }
        }

        /// <summary>池中实例总数（闲置 + 已租出）。</summary>
        public int CountAll => _countAll;

        /// <summary>当前闲置、可立即租出的实例数。</summary>
        public int CountInactive => _inactive.Count;

        /// <summary>当前已租出、尚未归还的实例数。</summary>
        public int CountActive => _countAll - _inactive.Count;

        /// <summary>闲置上限；0 表示不限制。</summary>
        public int MaxSize => _maxSize;

        /// <summary>
        /// 从池中租出一个实例。池中有闲置则复用，否则通过工厂新建。
        /// </summary>
        /// <returns>租出的实例。</returns>
        public T Rent()
        {
            bool reused = _inactive.Count > 0;
            T item = reused ? _inactive.Pop() : CreateInstance();
            if (!_active.Add(item))
            {
                if (reused) _inactive.Push(item);
                else _countAll--;
                throw new InvalidOperationException($"对象池租出重复实例: {typeof(T).Name}");
            }

            try
            {
                InvokeOnRent(item);
                return item;
            }
            catch
            {
                _active.Remove(item);
                _countAll--;
                throw;
            }
        }

        /// <summary>
        /// 将实例归还池中。仅允许归还从本池租出的实例；重复归还会抛异常。
        /// 当闲置数已达 <see cref="MaxSize"/> 时，对象不再入池。
        /// </summary>
        /// <param name="item">待归还实例，不可为 null。</param>
        public void Return(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!_active.Remove(item))
            {
                throw new InvalidOperationException($"对象不属于当前池或已归还: {typeof(T).Name}");
            }

            try
            {
                InvokeOnReturn(item);
            }
            catch
            {
                _countAll--;
                throw;
            }

            if (_maxSize > 0 && _inactive.Count >= _maxSize)
            {
                _countAll--;
                return;
            }

            _inactive.Push(item);
        }

        /// <summary>预分配指定数量的闲置实例；受 <see cref="MaxSize"/> 约束。</summary>
        /// <param name="count">希望新增的闲置数量。</param>
        public void Warmup(int count)
        {
            if (count <= 0) return;

            for (int i = 0; i < count; i++)
            {
                if (_maxSize > 0 && _inactive.Count >= _maxSize) break;
                _inactive.Push(CreateInstance());
            }
        }

        /// <summary>清空所有闲置实例；已租出未归还的实例不计入池，但仍计入 <see cref="CountAll"/> 直至归还或丢弃。</summary>
        public void Clear()
        {
            _countAll -= _inactive.Count;
            _inactive.Clear();
        }

        /// <inheritdoc />
        void IClearablePool.ClearInactive() => Clear();

        /// <inheritdoc />
        void IUntypedObjectPool.ReturnObject(object item) => Return((T)item);

        /// <summary>
        /// 调用工厂创建并计入池中实例。
        /// </summary>
        /// <returns>新创建的实例。</returns>
        private T CreateInstance()
        {
            T item = _factory();
            if (item == null) throw new InvalidOperationException($"对象池工厂返回 null: {typeof(T).Name}");
            _countAll++;
            return item;
        }

        private void InvokeOnRent(T item)
        {
            if (item is IPoolable poolable) poolable.OnRent();
            _onRent?.Invoke(item);
        }

        private void InvokeOnReturn(T item)
        {
            if (item is IPoolable poolable) poolable.OnReturn();
            _onReturn?.Invoke(item);
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceEqualityComparer Instance = new();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
