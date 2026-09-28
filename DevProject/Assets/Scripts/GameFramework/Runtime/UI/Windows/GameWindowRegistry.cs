using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 窗口类型显式注册表。避免运行时程序集扫描和反射构造，兼容 HybridCLR、AOT 与代码裁剪。
    /// </summary>
    public sealed class GameWindowRegistry
    {
        private readonly Dictionary<int, Registration> _byId = new();
        private readonly Dictionary<Type, int> _byType = new();

        /// <summary>
        /// 注册窗口 ID、类型和工厂。
        /// </summary>
        /// <typeparam name="TWindow">具体窗口类型。</typeparam>
        /// <param name="id">与窗口表一致的 ID。</param>
        /// <param name="factory">实例工厂；为空时要求公开无参构造。</param>
        public void Register<TWindow>(int id, Func<TWindow> factory = null)
            where TWindow : GameWindow, new()
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (_byId.ContainsKey(id))
                throw new InvalidOperationException($"窗口 ID 已注册：{id}");
            Type type = typeof(TWindow);
            if (_byType.ContainsKey(type))
                throw new InvalidOperationException($"窗口类型已注册：{type.FullName}");

            Func<GameWindow> normalizedFactory = factory == null
                ? static () => new TWindow()
                : () => factory();
            _byId.Add(id, new Registration(type, normalizedFactory));
            _byType.Add(type, id);
        }

        /// <summary>
        /// 按 ID 创建窗口逻辑实例。
        /// </summary>
        /// <returns>创建的窗口实例。</returns>
        /// <param name="id">窗口 ID。</param>
        public GameWindow Create(int id)
        {
            if (!_byId.TryGetValue(id, out Registration registration))
                throw new KeyNotFoundException($"窗口 ID 未显式注册：{id}");
            GameWindow window = registration.Factory();
            return window ?? throw new InvalidOperationException($"窗口工厂返回 null：{id}");
        }

        /// <summary>
        /// 查询窗口类型对应的配置 ID。
        /// </summary>
        /// <param name="windowType">窗口类型。</param>
        /// <param name="id">窗口 ID。</param>
        /// <returns>是否找到对应窗口 ID。</returns>
        public bool TryGetId(Type windowType, out int id)
        {
            if (windowType == null)
            {
                id = default;
                return false;
            }
            return _byType.TryGetValue(windowType, out id);
        }

        /// <summary>
        /// 查询 ID 对应的已注册窗口类型。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="windowType">窗口类型。</param>
        /// <returns>是否找到对应窗口类型。</returns>
        public bool TryGetType(int id, out Type windowType)
        {
            if (_byId.TryGetValue(id, out Registration registration))
            {
                windowType = registration.WindowType;
                return true;
            }
            windowType = null;
            return false;
        }

        private sealed class Registration
        {
            /// <summary>
            /// 保存窗口类型与实例工厂。
            /// </summary>
            /// <param name="windowType">窗口类型。</param>
            /// <param name="factory">窗口实例工厂。</param>
            internal Registration(Type windowType, Func<GameWindow> factory)
            {
                WindowType = windowType;
                Factory = factory;
            }

            internal Type WindowType { get; }
            internal Func<GameWindow> Factory { get; }
        }
    }

}
