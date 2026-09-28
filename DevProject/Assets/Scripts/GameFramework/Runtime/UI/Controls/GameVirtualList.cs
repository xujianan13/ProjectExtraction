using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 基于现有 ScrollRect 的固定尺寸单列或单行虚拟列表。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Virtual List")]
    [DisallowMultipleComponent]
    public sealed class GameVirtualList : MonoBehaviour
    {
        [SerializeField] private UnityEngine.UI.ScrollRect scrollRect;
        [SerializeField] private GameVirtualListItem itemPrefab;
        [SerializeField] private bool horizontal;
        [SerializeField, Min(1f)] private float itemExtent = 100f;
        [SerializeField, Min(0f)] private float spacing;
        [SerializeField, Min(0)] private int bufferItems = 1;

        private readonly Dictionary<int, GameVirtualListItem> _visible = new();
        private readonly Stack<GameVirtualListItem> _available = new();
        private readonly List<int> _remove = new();
        private readonly List<GameVirtualListItem> _all = new();
        private int _count;
        private Vector2 _lastViewportSize;
        private bool _initialized;
        private bool _listening;

        /// <summary>
        /// 当前数据数量。
        /// </summary>
        public int Count => _count;

        /// <summary>
        /// 当前已绑定的条目数量。
        /// </summary>
        public int VisibleCount => _visible.Count;

        /// <summary>
        /// 已实例化条目数量，包括复用池。
        /// </summary>
        public int InstanceCount => _all.Count;

        /// <summary>
        /// 按需配置列表，可在窗口初始化或打开时调用。
        /// </summary>
        /// <param name="scroll">已有的滚动组件。</param>
        /// <param name="prefab">带有 GameVirtualListItem 的条目预制体。</param>
        /// <param name="extent">条目在滚动方向的尺寸。</param>
        /// <param name="gap">条目间距。</param>
        /// <param name="isHorizontal">是否水平滚动。</param>
        /// <param name="buffer">视口两侧额外保留的条目数。</param>
        public void Configure(UnityEngine.UI.ScrollRect scroll, GameVirtualListItem prefab, float extent, float gap = 0f,
            bool isHorizontal = false, int buffer = 1)
        {
            if (scroll == null || scroll.viewport == null || scroll.content == null || prefab == null)
                throw new ArgumentException("虚拟列表需要 ScrollRect、Viewport、Content 和条目预制体。");
            if (float.IsNaN(extent) || float.IsInfinity(extent) || float.IsNaN(gap) || float.IsInfinity(gap) || extent <= 0f || gap < 0f || buffer < 0) throw new ArgumentOutOfRangeException(nameof(extent));
            if (prefab.transform is not RectTransform || !scroll.content.IsChildOf(scroll.viewport))
                throw new InvalidOperationException("条目根节点必须为 RectTransform，Content 必须位于 Viewport 下。");
            if (scroll.content.GetComponent<UnityEngine.UI.LayoutGroup>() != null ||
                scroll.content.GetComponent<UnityEngine.UI.ContentSizeFitter>() != null)
                throw new InvalidOperationException("虚拟列表自行控制 Content 尺寸和条目位置，请移除 Content 上的 LayoutGroup/ContentSizeFitter。");
            for (int index = 0; index < scroll.content.childCount; index++)
            {
                Transform child = scroll.content.GetChild(index);
                if (!_all.Exists(item => item != null && item.transform == child))
                    throw new InvalidOperationException("虚拟列表的 Content 不得包含非本列表托管的子对象。");
            }

            scrollRect?.StopMovement();
            if (scroll != scrollRect) scroll.StopMovement();
            Unsubscribe();
            ClearItems();
            scrollRect = scroll;
            itemPrefab = prefab;
            itemExtent = extent;
            spacing = gap;
            horizontal = isHorizontal;
            bufferItems = buffer;
            _initialized = true;
            scrollRect.horizontal = horizontal;
            scrollRect.vertical = !horizontal;
            RectTransform content = scrollRect.content;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            _lastViewportSize = scrollRect.viewport.rect.size;
            ResizeContent();
            Subscribe();
            Rebuild();
        }

        /// <summary>
        /// 设置数据总量并更新可见范围；已有同索引条目不会重新绑定。
        /// </summary>
        /// <param name="count">非负数据数量。</param>
        public void SetCount(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            _count = count;
            if (!_initialized) return;
            ResizeContent();
            ClampOffset();
            Rebuild();
        }

        /// <summary>
        /// 强制重绑所有可见条目。
        /// </summary>
        public void Refresh()
        {
            if (!_initialized) return;
            foreach (KeyValuePair<int, GameVirtualListItem> pair in _visible)
            {
                pair.Value.Detach();
                pair.Value.Attach(pair.Key);
            }
            Rebuild();
        }

        /// <summary>
        /// 当前可见时重绑指定条目。
        /// </summary>
        /// <param name="index">数据索引。</param>
        public void RefreshItem(int index)
        {
            if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
            if (_visible.TryGetValue(index, out GameVirtualListItem item)) item.Attach(index);
        }

        /// <summary>
        /// 把指定条目滚动到视口起始位置。
        /// </summary>
        /// <param name="index">数据索引。</param>
        public void ScrollTo(int index)
        {
            if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
            if (!_initialized) return;
            float viewportExtent = horizontal ? scrollRect.viewport.rect.width : scrollRect.viewport.rect.height;
            float maxOffset = Mathf.Max(0f, ContentExtent - viewportExtent);
            float offset = Mathf.Min(index * Stride, maxOffset);
            scrollRect.StopMovement();
            Vector2 position = scrollRect.content.anchoredPosition;
            if (horizontal) position.x = -offset;
            else position.y = offset;
            scrollRect.content.anchoredPosition = position;
            Rebuild();
        }

        /// <summary>
        /// 获取当前可见条目。
        /// </summary>
        /// <param name="index">数据索引。</param>
        /// <returns>当前绑定的条目，不可见时为 null。</returns>
        public GameVirtualListItem GetVisibleItem(int index) => _visible.TryGetValue(index, out GameVirtualListItem item) ? item : null;

        private float Stride => itemExtent + spacing;
        private float ContentExtent => _count == 0 ? 0f : _count * Stride - spacing;

        /// <summary>
        /// 启用时订阅滚动事件并恢复可见条目。
        /// </summary>
        private void OnEnable()
        {
            Subscribe();
            Rebuild();
        }

        /// <summary>
        /// 禁用时解除订阅并释放当前绑定。
        /// </summary>
        private void OnDisable()
        {
            Unsubscribe();
            foreach (GameVirtualListItem item in _visible.Values)
            {
                item.Detach();
                item.gameObject.SetActive(false);
                _available.Push(item);
            }
            _visible.Clear();
        }

        /// <summary>
        /// 销毁所有实例及其绑定。
        /// </summary>
        private void OnDestroy()
        {
            Unsubscribe();
            ClearItems();
        }

        /// <summary>
        /// 响应视口尺寸变化。
        /// </summary>
        private void LateUpdate()
        {
            if (!_initialized || !isActiveAndEnabled || scrollRect == null || scrollRect.viewport == null) return;
            Vector2 size = scrollRect.viewport.rect.size;
            if (size == _lastViewportSize) return;
            _lastViewportSize = size;
            ResizeContent();
            ClampOffset();
            Rebuild();
        }

        /// <summary>
        /// 在启用期间订阅滚动事件。
        /// </summary>
        private void Subscribe()
        {
            if (!_initialized || _listening || !isActiveAndEnabled) return;
            scrollRect.onValueChanged.AddListener(OnScrolled);
            _listening = true;
        }

        /// <summary>
        /// 移除当前滚动事件订阅。
        /// </summary>
        private void Unsubscribe()
        {
            if (!_listening) return;
            if (scrollRect != null) scrollRect.onValueChanged.RemoveListener(OnScrolled);
            _listening = false;
        }

        /// <summary>
        /// 滚动位置变化时更新可见范围。
        /// </summary>
        /// <param name="value">归一化滚动位置。</param>
        private void OnScrolled(Vector2 value) => Rebuild();

        /// <summary>
        /// 根据数量和视口调整内容尺寸。
        /// </summary>
        private void ResizeContent()
        {
            RectTransform content = scrollRect.content;
            Vector2 size = scrollRect.viewport.rect.size;
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, horizontal ? ContentExtent : size.x);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, horizontal ? size.y : ContentExtent);
        }

        /// <summary>
        /// 将当前滚动偏移限制在内容范围内。
        /// </summary>
        private void ClampOffset()
        {
            RectTransform content = scrollRect.content;
            float viewportExtent = horizontal ? scrollRect.viewport.rect.width : scrollRect.viewport.rect.height;
            float offset = Mathf.Clamp(horizontal ? -content.anchoredPosition.x : content.anchoredPosition.y, 0f,
                Mathf.Max(0f, ContentExtent - viewportExtent));
            Vector2 position = content.anchoredPosition;
            if (horizontal) position.x = -offset;
            else position.y = offset;
            scrollRect.StopMovement();
            content.anchoredPosition = position;
        }

        /// <summary>
        /// 更新当前可见范围并复用条目。
        /// </summary>
        private void Rebuild()
        {
            if (!_initialized || !isActiveAndEnabled || scrollRect == null || scrollRect.viewport == null || scrollRect.content == null) return;
            float viewportExtent = horizontal ? scrollRect.viewport.rect.width : scrollRect.viewport.rect.height;
            if (viewportExtent <= 0f || _count == 0)
            {
                ReleaseOutside(0, 0);
                TrimPool();
                return;
            }
            float offset = Mathf.Max(0f, horizontal ? -scrollRect.content.anchoredPosition.x : scrollRect.content.anchoredPosition.y);
            int start = Mathf.Clamp(Mathf.FloorToInt(offset / Stride) - bufferItems, 0, _count);
            int end = Mathf.Clamp(Mathf.CeilToInt((offset + viewportExtent) / Stride) + bufferItems, start, _count);
            ReleaseOutside(start, end);
            for (int index = start; index < end; index++)
            {
                if (_visible.TryGetValue(index, out GameVirtualListItem existing))
                {
                    PositionItem(existing, index);
                    continue;
                }
                GameVirtualListItem item = _available.Count > 0 ? _available.Pop() : Instantiate(itemPrefab, scrollRect.content);
                if (!_all.Contains(item)) _all.Add(item);
                try
                {
                    PositionItem(item, index);
                    item.gameObject.SetActive(true);
                    item.Attach(index);
                    _visible.Add(index, item);
                }
                catch
                {
                    _all.Remove(item);
                    DestroyItem(item);
                    throw;
                }
            }
            TrimPool();
        }

        /// <summary>
        /// 设置条目的尺寸和轴向位置。
        /// </summary>
        /// <param name="item">条目实例。</param>
        /// <param name="index">数据索引。</param>
        private void PositionItem(GameVirtualListItem item, int index)
        {
            RectTransform rect = (RectTransform)item.transform;
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.SetSizeWithCurrentAnchors(horizontal ? RectTransform.Axis.Horizontal : RectTransform.Axis.Vertical, itemExtent);
            rect.SetSizeWithCurrentAnchors(horizontal ? RectTransform.Axis.Vertical : RectTransform.Axis.Horizontal,
                horizontal ? scrollRect.viewport.rect.height : scrollRect.viewport.rect.width);
            rect.anchoredPosition = horizontal ? new Vector2(index * Stride, 0f) : new Vector2(0f, -index * Stride);
        }

        /// <summary>
        /// 回收范围外的条目。
        /// </summary>
        /// <param name="start">可见范围起始索引。</param>
        /// <param name="end">可见范围结束索引。</param>
        private void ReleaseOutside(int start, int end)
        {
            _remove.Clear();
            foreach (KeyValuePair<int, GameVirtualListItem> pair in _visible)
                if (pair.Key < start || pair.Key >= end) _remove.Add(pair.Key);
            foreach (int index in _remove)
            {
                GameVirtualListItem item = _visible[index];
                _visible.Remove(index);
                item.Detach();
                item.gameObject.SetActive(false);
                _available.Push(item);
            }
        }

        /// <summary>
        /// 删除当前视口不再需要的闲置实例。
        /// </summary>
        private void TrimPool()
        {
            while (_available.Count > 0)
            {
                GameVirtualListItem item = _available.Pop();
                _all.Remove(item);
                DestroyItem(item);
            }
        }

        /// <summary>
        /// 立即停止条目交互与绑定，并销毁其对象。
        /// </summary>
        /// <param name="item">待销毁的条目。</param>
        private void DestroyItem(GameVirtualListItem item)
        {
            if (item == null) return;
            try
            {
                item.Detach();
            }
            finally
            {
                item.gameObject.SetActive(false);
                item.transform.SetParent(null, false);
                if (Application.isPlaying) Destroy(item.gameObject);
                else DestroyImmediate(item.gameObject);
            }
        }

        /// <summary>
        /// 释放全部实例及其绑定。
        /// </summary>
        private void ClearItems()
        {
            foreach (GameVirtualListItem item in _all)
            {
                if (item == null) continue;
                DestroyItem(item);
            }
            _all.Clear();
            _visible.Clear();
            _available.Clear();
        }
    }
}
