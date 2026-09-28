using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 模块管理器：维护已注册模块，按 Priority 顺序驱动 Init / Update / Shutdown。
    /// 框架内部使用，业务通过 GameEntry 的静态 API 间接访问。
    /// </summary>
    internal sealed class ModuleManager
    {
        // 按接口类型索引，便于 Get<T>() 快速取出
        private readonly Dictionary<Type, IModule> _modules = new(32);
        // 按优先级排序的执行列表，避免每帧排序
        private readonly List<IModule> _ordered = new(32);
        private int _generation;
        private bool _initialized;
        private bool _initializing;
        private bool _shuttingDown;

        /// <summary>
        /// 所有模块是否已经完成初始化。
        /// </summary>
        public bool IsInitialized => _initialized;

        /// <summary>
        /// 注册模块。框架未初始化时，同接口已注册则替换；框架已初始化后禁止注册。
        /// T 为模块接口时直接用作 Key；T 为具体类时从实例解析唯一的 IModule 子接口。
        /// </summary>
        /// <typeparam name="T">模块的接口或实现类型。</typeparam>
        /// <param name="module">待注册的模块实例。</param>
        /// <returns>已经注册的模块实例。</returns>
        /// <exception cref="ArgumentNullException">module 为 null。</exception>
        /// <exception cref="InvalidOperationException">框架已初始化后尝试注册模块，或实例实现多个 IModule 子接口。</exception>
        public T Register<T>(T module) where T : class, IModule
        {
            if (module == null) throw new ArgumentNullException(nameof(module));

            Type keyType = ResolveModuleKeyType(typeof(T), module);
            if (_initialized || _initializing || _shuttingDown)
                throw new InvalidOperationException($"无法在框架已初始化后注册模块 {keyType.Name}");

            if (_modules.TryGetValue(keyType, out var existing))
            {
                RemoveFromOrdered(existing);
                _modules[keyType] = module;
                InsertByPriority(module);
                return module;
            }

            _modules.Add(keyType, module);
            InsertByPriority(module);
            return module;
        }

        /// <summary>
        /// 获取已注册的模块；未注册返回 null。
        /// </summary>
        public T Get<T>() where T : class, IModule
        {
            return _modules.TryGetValue(typeof(T), out var m) ? (T)m : null;
        }

        /// <summary>
        /// 按优先级初始化模块，失败时连同当前模块的部分初始化状态一起回滚；成功后重复调用无副作用。
        /// </summary>
        /// <param name="beforeRollback">初始化失败时，在模块回滚前释放上层业务所有权的操作。</param>
        public void InitAll(Action beforeRollback = null)
        {
            if (_initialized || _initializing || _shuttingDown) return;
            _initializing = true;
            int initializedCount = 0;
            try
            {
                while (initializedCount < _ordered.Count)
                {
                    IModule module = _ordered[initializedCount++];
                    module.OnInit();
                }
            }
            catch (Exception initializationError)
            {
                List<Exception> rollbackErrors = null;
                try
                {
                    beforeRollback?.Invoke();
                }
                catch (Exception rollbackError)
                {
                    rollbackErrors = new List<Exception> { rollbackError };
                }
                for (int i = initializedCount - 1; i >= 0; i--)
                {
                    try
                    {
                        _ordered[i].OnShutdown();
                    }
                    catch (Exception rollbackError)
                    {
                        rollbackErrors ??= new List<Exception>();
                        rollbackErrors.Add(rollbackError);
                    }
                }

                if (rollbackErrors != null)
                {
                    rollbackErrors.Insert(0, initializationError);
                    throw new AggregateException("模块初始化失败，回滚时也发生错误。", rollbackErrors);
                }

                throw;
            }
            finally
            {
                _initializing = false;
            }
            _initialized = true;
            _generation++;
        }

        /// <summary>
        /// 每帧驱动所有模块的 OnUpdate。位于热路径，用 for 循环避免 foreach 迭代器分配。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔。</param>
        public void UpdateAll(float deltaTime)
        {
            if (!_initialized) return;
            int generation = _generation;
            for (int i = 0; i < _ordered.Count && _initialized && _generation == generation; i++)
                _ordered[i].OnUpdate(deltaTime);
        }

        /// <summary>
        /// 固定帧驱动可选模块，回调关闭框架后立即停止本轮派发。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        public void FixedUpdateAll(float deltaTime)
        {
            if (!_initialized) return;
            int generation = _generation;
            for (int i = 0; i < _ordered.Count && _initialized && _generation == generation; i++)
                if (_ordered[i] is IFixedUpdateModule module) module.OnFixedUpdate(deltaTime);
        }

        /// <summary>
        /// 帧末驱动可选模块，回调关闭框架后立即停止本轮派发。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        public void LateUpdateAll(float deltaTime)
        {
            if (!_initialized) return;
            int generation = _generation;
            for (int i = 0; i < _ordered.Count && _initialized && _generation == generation; i++)
                if (_ordered[i] is ILateUpdateModule module) module.OnLateUpdate(deltaTime);
        }

        /// <summary>
        /// 批量卸载已启动模块。反向迭代以保证按优先级降序卸载。
        /// </summary>
        public void ShutdownAll()
        {
            if (_shuttingDown) return;
            _shuttingDown = true;
            _generation++;
            ILogModule log = Get<ILogModule>();
            try
            {
                if (_initialized)
                {
                    _initialized = false;
                    for (int i = _ordered.Count - 1; i >= 0; i--)
                    {
                        try
                        {
                            _ordered[i].OnShutdown();
                        }
                        catch (Exception error)
                        {
                            log?.Error("ModuleManager", $"模块 {_ordered[i].GetType().Name} 关闭失败：{error}");
                        }
                    }
                }
            }
            finally
            {
                _ordered.Clear();
                _modules.Clear();
                _shuttingDown = false;
            }
        }

        // 按优先级稳定插入：相同 Priority 时保持注册顺序
        private void InsertByPriority(IModule module)
        {
            int i = 0;
            while (i < _ordered.Count && _ordered[i].Priority <= module.Priority) i++;
            _ordered.Insert(i, module);
        }

        private void RemoveFromOrdered(IModule module)
        {
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i] != module) continue;
                _ordered.RemoveAt(i);
                return;
            }
        }

        /// <summary>
        /// 解析模块注册 Key：显式接口类型优先；具体类则从实例查找唯一的 IModule 子接口。
        /// </summary>
        private static Type ResolveModuleKeyType(Type declaredType, IModule module)
        {
            if (declaredType.IsInterface)
            {
                return declaredType;
            }

            Type runtimeType = module.GetType();
            Type moduleInterface = null;
            Type[] interfaces = runtimeType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                Type iface = interfaces[i];
                if (iface == typeof(IModule) || !typeof(IModule).IsAssignableFrom(iface))
                {
                    continue;
                }

                if (moduleInterface != null)
                {
                    throw new InvalidOperationException(
                        $"模块 {runtimeType.Name} 实现了多个 IModule 子接口，请显式指定注册类型。");
                }

                moduleInterface = iface;
            }

            return moduleInterface ?? runtimeType;
        }

    }
}
