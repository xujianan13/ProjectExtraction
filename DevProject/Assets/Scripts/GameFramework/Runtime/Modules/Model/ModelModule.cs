using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>跨场景业务数据模型模块默认实现。</summary>
    public sealed class ModelModule : ModuleBase, IModelModule
    {
        private const string Tag = "Model";

        private readonly Dictionary<Type, ModelBase> _models = new(16);
        private readonly List<ModelBase> _ordered = new(16);
        private readonly HashSet<ModelBase> _active = new();
        private bool _initialized;
        private ILogModule _log;

        /// <inheritdoc />
        public override int Priority => 48;

        /// <inheritdoc />
        public bool IsInitialized => _initialized;

        /// <inheritdoc />
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
            if (!_initialized) return;

            for (int i = 0; i < _ordered.Count; i++)
            {
                ModelBase model = _ordered[i];
                if (!_active.Contains(model)) continue;
                try
                {
                    model.OnUpdate(deltaTime);
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"Model {model.GetType().Name} OnUpdate failed: {ex}");
                }
            }
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            ShutdownAll();
            _log = null;
        }

        /// <inheritdoc />
        public void RegisterModel<T>(T model) where T : ModelBase
        {
            if (model == null) throw new ArgumentNullException(nameof(model));
            if (_initialized)
                throw new InvalidOperationException($"无法在 InitAll 之后注册模型 {typeof(T).Name}");

            Type type = typeof(T);
            if (_models.TryGetValue(type, out ModelBase existing))
            {
                RemoveFromOrdered(existing);
                _models[type] = model;
                InsertByOrder(model);
                return;
            }

            _models.Add(type, model);
            InsertByOrder(model);
        }

        /// <inheritdoc />
        public T Get<T>() where T : ModelBase
        {
            return _models.TryGetValue(typeof(T), out ModelBase model) && (!_initialized || _active.Contains(model)) ? (T)model : null;
        }

        /// <inheritdoc />
        public void InitAll()
        {
            if (_initialized) return;

            for (int i = 0; i < _ordered.Count; i++)
            {
                ModelBase model = _ordered[i];
                try
                {
                    model.OnInit();
                    _active.Add(model);
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"Model {model.GetType().Name} OnInit failed: {ex}");
                    try { model.OnShutdown(); }
                    catch (Exception cleanupError) { _log?.Error(Tag, $"Model {model.GetType().Name} cleanup failed: {cleanupError}"); }
                }
            }

            _initialized = true;
            _log?.Info(Tag, $"InitAll finished, {_ordered.Count} model(s).");
        }

        /// <inheritdoc />
        public void ResetAll(ModelScope scope, ModelResetReason reason)
        {
            if (!_initialized) return;
            ValidateScope(scope);
            ValidateReason(reason);

            for (int i = _ordered.Count - 1; i >= 0; i--)
            {
                ModelBase model = _ordered[i];
                if (!_active.Contains(model)) continue;
                if (!ShouldReset(model.Scope, scope)) continue;

                try
                {
                    model.OnReset(reason);
                }
                catch (Exception ex)
                {
                    _log?.Error(Tag, $"Model {model.GetType().Name} OnReset failed: {ex}");
                }
            }
        }

        /// <inheritdoc />
        public void ShutdownAll()
        {
            if (_initialized)
            {
                for (int i = _ordered.Count - 1; i >= 0; i--)
                {
                    ModelBase model = _ordered[i];
                    if (!_active.Contains(model)) continue;
                    try
                    {
                        model.OnShutdown();
                    }
                    catch (Exception ex)
                    {
                        _log?.Error(Tag, $"Model {model.GetType().Name} OnShutdown failed: {ex}");
                    }
                }
            }

            _ordered.Clear();
            _models.Clear();
            _active.Clear();
            _initialized = false;
        }

        private static bool ShouldReset(ModelScope modelScope, ModelScope resetScope)
        {
            return resetScope switch
            {
                ModelScope.Application => true,
                ModelScope.Account =>
                    modelScope == ModelScope.Account || modelScope == ModelScope.Player,
                ModelScope.Player => modelScope == ModelScope.Player,
                _ => false
            };
        }

        private static void ValidateScope(ModelScope scope)
        {
            if (scope != ModelScope.Application &&
                scope != ModelScope.Account &&
                scope != ModelScope.Player)
            {
                throw new ArgumentOutOfRangeException(nameof(scope), scope, "模型重置作用域无效。");
            }
        }

        private static void ValidateReason(ModelResetReason reason)
        {
            if (reason != ModelResetReason.Logout &&
                reason != ModelResetReason.AccountChanged &&
                reason != ModelResetReason.PlayerChanged &&
                reason != ModelResetReason.SessionInvalidated)
            {
                throw new ArgumentOutOfRangeException(nameof(reason), reason, "模型重置原因无效。");
            }
        }

        private void InsertByOrder(ModelBase model)
        {
            int i = 0;
            while (i < _ordered.Count && _ordered[i].Order <= model.Order) i++;
            _ordered.Insert(i, model);
        }

        private void RemoveFromOrdered(ModelBase model)
        {
            for (int i = 0; i < _ordered.Count; i++)
            {
                if (_ordered[i] != model) continue;
                _ordered.RemoveAt(i);
                return;
            }
        }
    }
}
