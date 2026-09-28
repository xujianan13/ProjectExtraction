using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 全局对象池管理模块默认实现。以 <see cref="PoolKey"/>（类型 + 名称）索引池实例。
    /// </summary>
    public sealed class PoolModule : ModuleBase, IPoolModule
    {
        /// <inheritdoc />
        public override int Priority => -60;

        private readonly Dictionary<PoolKey, object> _pools = new(16);
        private readonly Dictionary<Type, IUntypedObjectPool> _defaultPoolsByType = new(16);
        private readonly Dictionary<Type, object> _defaultDescriptors = new(16);
        private readonly List<IClearablePool> _clearables = new(16);

        /// <inheritdoc />
        public override void OnShutdown()
        {
            for (int i = 0; i < _clearables.Count; i++)
            {
                _clearables[i].ClearInactive();
            }

            _clearables.Clear();
            _pools.Clear();
            _defaultPoolsByType.Clear();
            _defaultDescriptors.Clear();
        }

        /// <inheritdoc />
        public void ConfigureDefault<T>(PoolDescriptor<T> descriptor) where T : class
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));
            if (IsRegistered<T>()) return;

            _defaultDescriptors[typeof(T)] = descriptor;
        }

        /// <inheritdoc />
        public void Register<T>(PoolDescriptor<T> descriptor, string name = null) where T : class
        {
            if (descriptor == null) throw new ArgumentNullException(nameof(descriptor));

            var pool = new ObjectPool<T>(
                descriptor.InitialCapacity,
                descriptor.Factory,
                descriptor.OnRent,
                descriptor.OnReturn,
                descriptor.MaxSize,
                descriptor.PrewarmCount);

            Register(pool, name);
        }

        /// <inheritdoc />
        public void Register<T>(ObjectPool<T> pool, string name = null) where T : class
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));

            var key = PoolKey.For<T>(name);
            if (_pools.TryGetValue(key, out var existing))
            {
                RemoveClearable((IClearablePool)existing);
            }

            _pools[key] = pool;
            _clearables.Add(pool);

            if (string.IsNullOrEmpty(name))
            {
                _defaultDescriptors.Remove(typeof(T));
                _defaultPoolsByType[typeof(T)] = pool;
            }
        }

        /// <inheritdoc />
        public bool IsRegistered<T>(string name = null) where T : class
        {
            return _pools.ContainsKey(PoolKey.For<T>(name));
        }

        /// <inheritdoc />
        public bool TryGetPool<T>(out ObjectPool<T> pool, string name = null) where T : class
        {
            if (_pools.TryGetValue(PoolKey.For<T>(name), out var value))
            {
                pool = (ObjectPool<T>)value;
                return true;
            }

            pool = null;
            return false;
        }

        /// <inheritdoc />
        public ObjectPool<T> GetOrCreatePool<T>() where T : class, new()
        {
            if (TryGetPool(out ObjectPool<T> pool, null))
            {
                return pool;
            }

            if (TryCreateFromDefaultDescriptor<T>())
            {
                return (ObjectPool<T>)_pools[PoolKey.For<T>(null)];
            }

            Register(new PoolDescriptor<T>(
                () => new T(),
                PoolDefaults.InitialCapacity,
                PoolDefaults.MaxSize,
                PoolDefaults.PrewarmCount));

            return (ObjectPool<T>)_pools[PoolKey.For<T>(null)];
        }

        /// <inheritdoc />
        public ObjectPool<T> GetOrCreatePoolConfigured<T>() where T : class
        {
            if (TryGetPool(out ObjectPool<T> pool, null))
            {
                return pool;
            }

            if (TryCreateFromDefaultDescriptor<T>())
            {
                return (ObjectPool<T>)_pools[PoolKey.For<T>(null)];
            }

            throw new InvalidOperationException(
                $"对象池未配置: {typeof(T).Name}。请 ConfigureDefault、Register，或使用 GetOrCreatePool<{typeof(T).Name}>()（要求无参构造）。");
        }

        /// <inheritdoc />
        public ObjectPool<T> GetPool<T>(string name) where T : class
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("命名池须指定非空 name。", nameof(name));

            if (TryGetPool(out ObjectPool<T> pool, name))
            {
                return pool;
            }

            throw new InvalidOperationException(
                $"对象池未注册: {typeof(T).Name} name='{name}'");
        }

        /// <inheritdoc />
        public T Rent<T>() where T : class, new()
        {
            T item = GetOrCreatePool<T>().Rent();
            BindPooledRelease(item, null);
            return item;
        }

        /// <inheritdoc />
        public T Rent<T>(string name) where T : class
        {
            T item = GetPool<T>(name).Rent();
            BindPooledRelease(item, name);
            return item;
        }

        /// <inheritdoc />
        public void Return<T>(T item) where T : class
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            if (TryGetPool(out ObjectPool<T> pool, null))
            {
                pool.Return(item);
                return;
            }

            throw new InvalidOperationException(
                $"无法归还到默认池: {typeof(T).Name} 未创建。请先 Rent<{typeof(T).Name}>() / GetOrCreatePool，或显式 Register。");
        }

        /// <inheritdoc />
        public void Return<T>(T item, string name) where T : class
        {
            GetPool<T>(name).Return(item);
        }

        /// <inheritdoc />
        public void ReturnRuntime(object item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));

            Type type = item.GetType();
            if (_defaultPoolsByType.TryGetValue(type, out IUntypedObjectPool pool))
            {
                pool.ReturnObject(item);
                return;
            }

            throw new InvalidOperationException(
                $"无法按运行时类型归还: {type.Name} 的默认池未创建。请先为每个具体子类型 Rent / GetOrCreatePool，或显式 Register。");
        }

        private void BindPooledRelease<T>(T item, string name) where T : class
        {
            if (item is not IPooledObject pooled) return;

            pooled.BindPool(this, name);
        }

        private bool TryCreateFromDefaultDescriptor<T>() where T : class
        {
            if (!_defaultDescriptors.TryGetValue(typeof(T), out var descriptorObj))
            {
                return false;
            }

            Register((PoolDescriptor<T>)descriptorObj);
            return true;
        }

        private void RemoveClearable(IClearablePool pool)
        {
            pool.ClearInactive();
            for (int i = _clearables.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(_clearables[i], pool)) continue;
                _clearables.RemoveAt(i);
                return;
            }
        }

        /// <summary>池索引键：元素类型 + 名称（空字符串表示默认池）。</summary>
        private readonly struct PoolKey : IEquatable<PoolKey>
        {
            private readonly Type _elementType;
            private readonly string _name;

            public PoolKey(Type elementType, string name)
            {
                _elementType = elementType;
                _name = name ?? string.Empty;
            }

            public static PoolKey For<T>(string name) => new(typeof(T), name ?? string.Empty);

            /// <inheritdoc />
            public bool Equals(PoolKey other) =>
                _elementType == other._elementType && _name == other._name;

            /// <inheritdoc />
            public override bool Equals(object obj) => obj is PoolKey other && Equals(other);

            /// <inheritdoc />
            public override int GetHashCode()
            {
                unchecked
                {
                    return (_elementType.GetHashCode() * 397) ^ _name.GetHashCode();
                }
            }
        }
    }
}
