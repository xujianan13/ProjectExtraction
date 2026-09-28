using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 一条只读窗口配置；用于描述窗口实例和显示策略。
    /// </summary>
    public sealed class GameWindowConfig
    {
        /// <summary>
        /// 创建并校验一条窗口配置。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="name">窗口名称。</param>
        /// <param name="className">窗口逻辑类型名。</param>
        /// <param name="prefabAddress">Prefab 资源地址。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="scope">窗口作用域。</param>
        /// <param name="cacheMode">关闭后的缓存策略。</param>
        /// <param name="destroyDelaySeconds">缓存超时时间（秒）。</param>
        /// <param name="closeOnOutsideClick">是否允许点击窗口外关闭。</param>
        /// <param name="useMask">是否显示模态遮罩。</param>
        /// <param name="maskColor">遮罩颜色。</param>
        /// <param name="openSoundAddress">打开音效地址。</param>
        /// <param name="closeSoundAddress">关闭音效地址。</param>
        /// <param name="openBeforeIds">打开前需要打开的窗口 ID。</param>
        /// <param name="closeOnOpenIds">打开时需要关闭的窗口 ID。</param>
        /// <param name="closeWithSelfIds">关闭时一同关闭的窗口 ID。</param>
        public GameWindowConfig(
            int id,
            string name,
            string className,
            string prefabAddress,
            int layer,
            GameWindowScope scope,
            GameWindowCacheMode cacheMode,
            float destroyDelaySeconds,
            bool closeOnOutsideClick,
            bool useMask,
            Color maskColor,
            string openSoundAddress = null,
            string closeSoundAddress = null,
            IReadOnlyList<int> openBeforeIds = null,
            IReadOnlyList<int> closeOnOpenIds = null,
            IReadOnlyList<int> closeWithSelfIds = null)
        {
            if (id <= 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
                throw new ArgumentException("窗口名称无效。", nameof(name));
            if (string.IsNullOrWhiteSpace(className) || className != className.Trim())
                throw new ArgumentException("窗口类名无效。", nameof(className));
            if (prefabAddress != null && prefabAddress != prefabAddress.Trim())
                throw new ArgumentException("Prefab Address 不能包含首尾空白。", nameof(prefabAddress));
            if (destroyDelaySeconds < 0f || float.IsNaN(destroyDelaySeconds))
                throw new ArgumentOutOfRangeException(nameof(destroyDelaySeconds));

            Id = id;
            Name = name;
            ClassName = className;
            PrefabAddress = prefabAddress ?? string.Empty;
            Layer = layer;
            Scope = scope;
            CacheMode = cacheMode;
            DestroyDelaySeconds = destroyDelaySeconds;
            CloseOnOutsideClick = closeOnOutsideClick;
            UseMask = useMask;
            MaskColor = maskColor;
            OpenSoundAddress = NormalizeOptional(openSoundAddress, nameof(openSoundAddress));
            CloseSoundAddress = NormalizeOptional(closeSoundAddress, nameof(closeSoundAddress));
            OpenBeforeIds = CopyIds(openBeforeIds);
            CloseOnOpenIds = CopyIds(closeOnOpenIds);
            CloseWithSelfIds = CopyIds(closeWithSelfIds);
        }

        /// <summary>
        /// 窗口唯一 ID。
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// 用于日志和层级对象命名的窗口名称。
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// 显式注册的窗口类名。
        /// </summary>
        public string ClassName { get; }

        /// <summary>
        /// Prefab 逻辑地址；为空时创建空 RectTransform，便于纯逻辑窗口和测试。
        /// </summary>
        public string PrefabAddress { get; }

        /// <summary>
        /// 渲染层级，数值越大越靠上。
        /// </summary>
        public int Layer { get; }

        /// <summary>
        /// 窗口所属逻辑作用域。
        /// </summary>
        public GameWindowScope Scope { get; }

        /// <summary>
        /// 关闭后的缓存策略。
        /// </summary>
        public GameWindowCacheMode CacheMode { get; }

        /// <summary>
        /// Timed 窗口从关闭到销毁的未缩放秒数。
        /// </summary>
        public float DestroyDelaySeconds { get; }

        /// <summary>
        /// 是否允许点击窗口外区域关闭。
        /// </summary>
        public bool CloseOnOutsideClick { get; }

        /// <summary>
        /// 是否在窗口下方显示蒙层。
        /// </summary>
        public bool UseMask { get; }

        /// <summary>
        /// 蒙层颜色。
        /// </summary>
        public Color MaskColor { get; }

        /// <summary>
        /// 打开时播放的 Sfx 地址。
        /// </summary>
        public string OpenSoundAddress { get; }

        /// <summary>
        /// 关闭时播放的 Sfx 地址。
        /// </summary>
        public string CloseSoundAddress { get; }

        /// <summary>
        /// 打开自身前需要打开的窗口。
        /// </summary>
        public IReadOnlyList<int> OpenBeforeIds { get; }

        /// <summary>
        /// 打开自身时先关闭的窗口。
        /// </summary>
        public IReadOnlyList<int> CloseOnOpenIds { get; }

        /// <summary>
        /// 关闭自身时一并关闭的窗口。
        /// </summary>
        public IReadOnlyList<int> CloseWithSelfIds { get; }

        /// <summary>
        /// 校验并标准化可选资源地址。
        /// </summary>
        /// <param name="value">输入值。</param>
        /// <param name="name">窗口名称。</param>
        /// <returns>标准化后的可选地址。</returns>
        private static string NormalizeOptional(string value, string name)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
                throw new ArgumentException(name + " 无效。", name);
            return value;
        }

        /// <summary>
        /// 复制并校验关联窗口 ID。
        /// </summary>
        /// <param name="source">原始窗口 ID 集合。</param>
        /// <returns>校验并复制后的窗口 ID 集合。</returns>
        private static IReadOnlyList<int> CopyIds(IReadOnlyList<int> source)
        {
            if (source == null || source.Count == 0) return Array.Empty<int>();
            var result = new int[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                if (source[index] <= 0)
                    throw new ArgumentOutOfRangeException(nameof(source), "关联窗口 ID 必须大于零。");
                result[index] = source[index];
            }
            return result;
        }
    }

    /// <summary>
    /// 窗口管理器使用的只读配置目录。
    /// </summary>
    public interface IGameWindowConfigCatalog
    {
        /// <summary>
        /// 全部窗口配置。
        /// </summary>
        IReadOnlyList<GameWindowConfig> Items { get; }

        /// <summary>
        /// 按 ID 查询窗口配置。
        /// </summary>
        bool TryGet(int id, out GameWindowConfig config);
    }

    /// <summary>
    /// 内存窗口配置目录，可由业务逻辑或数据表逐项注册。
    /// </summary>
    public sealed class GameWindowConfigCatalog : IGameWindowConfigCatalog
    {
        private readonly List<GameWindowConfig> _items;
        private readonly Dictionary<int, GameWindowConfig> _byId;

        /// <summary>
        /// 从配置集合创建目录并拒绝重复 ID。
        /// </summary>
        /// <param name="configs">窗口配置集合。</param>
        public GameWindowConfigCatalog(IEnumerable<GameWindowConfig> configs = null)
        {
            _items = new List<GameWindowConfig>();
            _byId = new Dictionary<int, GameWindowConfig>();
            if (configs == null) return;
            foreach (GameWindowConfig config in configs) Add(config);
        }

        /// <summary>
        /// 注册一条窗口配置。
        /// </summary>
        /// <param name="config">窗口配置。</param>
        public void Add(GameWindowConfig config)
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            if (!_byId.TryAdd(config.Id, config))
                throw new InvalidOperationException($"窗口配置 ID 重复：{config.Id}");
            _items.Add(config);
        }

        /// <inheritdoc />
        public IReadOnlyList<GameWindowConfig> Items => _items;

        /// <inheritdoc />
        /// <param name="id">窗口 ID。</param>
        /// <param name="config">窗口配置。</param>
        /// <returns>是否找到窗口配置或实例。</returns>
        public bool TryGet(int id, out GameWindowConfig config) => _byId.TryGetValue(id, out config);
    }
}
