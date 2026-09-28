using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 业务服务管理模块默认实现。服务在热更后由 GameLogic 注册，<see cref="InitAll"/> 与场景注册相互独立。
    /// </summary>
    public sealed class ServiceModule : ModuleBase, IServiceModule
    {
        private const string Tag = "Service";

        /// <inheritdoc />
        public override int Priority => 49;

        private readonly Dictionary<Type, IAppService> _services = new(16);
        private readonly List<IAppService> _ordered = new(16);
        private readonly HashSet<IAppService> _active = new();
        private bool _initialized;
        private ILogModule _log;

        /// <inheritdoc />
        public bool IsInitialized => _initialized;

        /// <inheritdoc />
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            ShutdownAll();
            _log = null;
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            if (!_initialized) return;

            for (int i = 0; i < _ordered.Count; i++)
            {
                var service = _ordered[i];
                if (!_active.Contains(service)) continue;
                try
                {
                    service.OnUpdate(deltaTime);
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"Service {service.GetType().Name} OnUpdate failed: {ex}");
                }
            }
        }

        /// <inheritdoc />
        public void RegisterService<T>(T service) where T : class, IAppService
        {
            if (service == null) throw new ArgumentNullException(nameof(service));
            if (_initialized)
                throw new InvalidOperationException($"无法在 InitAll 之后注册服务 {typeof(T).Name}");

            var type = typeof(T);
            if (_services.TryGetValue(type, out var existing))
            {
                RemoveFromOrdered(existing);
                _services[type] = service;
                InsertByOrder(service);
                return;
            }

            _services.Add(type, service);
            InsertByOrder(service);
        }

        /// <inheritdoc />
        public T Get<T>() where T : class, IAppService
        {
            return _services.TryGetValue(typeof(T), out var service) && (!_initialized || _active.Contains(service)) ? (T)service : null;
        }

        /// <inheritdoc />
        public void InitAll()
        {
            if (_initialized) return;

            for (int i = 0; i < _ordered.Count; i++)
            {
                var service = _ordered[i];
                try
                {
                    service.OnInit();
                    _active.Add(service);
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"Service {service.GetType().Name} OnInit failed: {ex}");
                    try { service.OnShutdown(); }
                    catch (Exception cleanupError) { _log?.Error(Tag, $"Service {service.GetType().Name} cleanup failed: {cleanupError}"); }
                }
            }

            _initialized = true;
            _log?.Info(Tag, $"InitAll finished, {_ordered.Count} service(s).");
        }

        /// <inheritdoc />
        public void ShutdownAll()
        {
            if (!_initialized) return;

            for (int i = _ordered.Count - 1; i >= 0; i--)
            {
                var service = _ordered[i];
                if (!_active.Contains(service)) continue;
                try
                {
                    service.OnShutdown();
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"Service {service.GetType().Name} OnShutdown failed: {ex}");
                }
            }

            _ordered.Clear();
            _services.Clear();
            _active.Clear();
            _initialized = false;
        }

        private void InsertByOrder(IAppService service)
        {
            int i = 0;
            while (i < _ordered.Count && _ordered[i].Order <= service.Order) i++;
            _ordered.Insert(i, service);
        }

        private void RemoveFromOrdered(IAppService service)
        {
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i] != service) continue;
                _ordered.RemoveAt(i);
                return;
            }
        }
    }
}
