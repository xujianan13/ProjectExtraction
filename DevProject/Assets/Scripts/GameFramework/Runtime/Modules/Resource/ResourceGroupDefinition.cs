using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>定义一批需要统一进度、失败策略和生命周期的资源。</summary>
    public sealed class ResourceGroupDefinition
    {
        /// <summary>资源组失败策略。</summary>
        public enum FailurePolicy : byte
        {
            /// <summary>任一资源失败时释放本组已取得的全部租约并抛出异常。</summary>
            AllOrNothing,

            /// <summary>保留成功资源，并在结果中报告失败项。</summary>
            BestEffort
        }

        /// <summary>受 AOT 保护的资源类型集合，避免根据配置反射构造泛型调用。</summary>
        public enum AssetKind : byte
        {
            Object,
            GameObject,
            Sprite,
            Texture2D,
            Material,
            AudioClip,
            TextAsset,
            AnimationClip,
            RuntimeAnimatorController,
            ScriptableObject
        }

        /// <summary>资源组中的一个具名资源。</summary>
        public sealed class Item
        {
            /// <summary>创建资源组条目。</summary>
            public Item(
                string id,
                string location,
                AssetKind kind,
                ResourceLoadOptions.RetentionMode retention = ResourceLoadOptions.RetentionMode.Cacheable,
                long estimatedMemoryBytes = 0,
                float progressWeight = 1f)
            {
                if (string.IsNullOrWhiteSpace(id) || id != id.Trim())
                    throw new ArgumentException("资源组条目 ID 无效。", nameof(id));
                if (string.IsNullOrWhiteSpace(location) || location != location.Trim())
                    throw new ArgumentException("资源组条目地址无效。", nameof(location));
                if (estimatedMemoryBytes < 0)
                    throw new ArgumentOutOfRangeException(nameof(estimatedMemoryBytes));
                if (progressWeight <= 0f || float.IsNaN(progressWeight) || float.IsInfinity(progressWeight))
                    throw new ArgumentOutOfRangeException(nameof(progressWeight));

                Id = id;
                Location = location;
                Kind = kind;
                Retention = retention;
                EstimatedMemoryBytes = estimatedMemoryBytes;
                ProgressWeight = progressWeight;
            }

            /// <summary>组内唯一标识。</summary>
            public string Id { get; }

            /// <summary>逻辑资源地址。</summary>
            public string Location { get; }

            /// <summary>加载资源时使用的具体类型。</summary>
            public AssetKind Kind { get; }

            /// <summary>资源保留方式。</summary>
            public ResourceLoadOptions.RetentionMode Retention { get; }

            /// <summary>估算内存字节数。</summary>
            public long EstimatedMemoryBytes { get; }

            /// <summary>聚合进度权重。</summary>
            public float ProgressWeight { get; }
        }

        private readonly IReadOnlyList<Item> _items;

        /// <summary>创建资源组定义。</summary>
        public ResourceGroupDefinition(
            string name,
            IReadOnlyList<Item> items,
            FailurePolicy policy = FailurePolicy.AllOrNothing,
            int maxConcurrency = 8)
        {
            if (string.IsNullOrWhiteSpace(name) || name != name.Trim())
                throw new ArgumentException("资源组名称无效。", nameof(name));
            if (items == null) throw new ArgumentNullException(nameof(items));
            if (maxConcurrency <= 0) throw new ArgumentOutOfRangeException(nameof(maxConcurrency));

            Name = name;
            Policy = policy;
            MaxConcurrency = maxConcurrency;
            var copiedItems = new Item[items.Count];
            var ids = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0; index < items.Count; index++)
            {
                Item item = items[index] ?? throw new ArgumentException("资源组不能包含空条目。", nameof(items));
                if (!ids.Add(item.Id))
                    throw new ArgumentException($"资源组条目 ID 重复：{item.Id}", nameof(items));
                copiedItems[index] = item;
            }
            _items = Array.AsReadOnly(copiedItems);
        }

        /// <summary>资源组名称。</summary>
        public string Name { get; }

        /// <summary>失败处理策略。</summary>
        public FailurePolicy Policy { get; }

        /// <summary>业务层同时发起的最大资源请求数。</summary>
        public int MaxConcurrency { get; }

        /// <summary>不可变资源条目列表。</summary>
        public IReadOnlyList<Item> Items => _items;
    }
}
