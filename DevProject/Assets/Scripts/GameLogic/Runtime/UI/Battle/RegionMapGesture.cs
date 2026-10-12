using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 区分地图单击、拖动及双指操作，并统一处理背景与标记手势。
    /// </summary>
    public sealed class RegionMapGesture : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler,
        IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        private RegionMapWidget map;
        private Func<bool> available;
        private Action<PointerEventData> selectPoint;
        private float zoom = 1f;
        private Vector2 center = Vector2.one * 0.5f;
        private readonly Dictionary<int, Vector2> pointers = new();
        private readonly Dictionary<int, Vector2> origins = new();
        private readonly HashSet<int> suppressed = new();
        private float pinchDistance;

        /// <summary>
        /// 绑定地图视口、保存门禁及背景选点回调。
        /// </summary>
        /// <param name="map">绘制视口的地图。</param>
        /// <param name="available">当前是否允许操作。</param>
        /// <param name="selectPoint">背景单击时的选点回调。</param>
        public void Configure(RegionMapWidget map, Func<bool> available, Action<PointerEventData> selectPoint = null)
        {
            this.map = map;
            this.available = available;
            this.selectPoint = selectPoint;
        }

        /// <summary>
        /// 使用按钮或滚轮改变缩放倍率。
        /// </summary>
        /// <param name="factor">相对缩放倍率。</param>
        public void ZoomBy(float factor)
        {
            if (!available()) return;
            zoom = Mathf.Clamp(zoom * factor, 1f, 5f);
            Apply();
        }

        /// <summary>
        /// 恢复完整地图初始视口。
        /// </summary>
        public void ResetView()
        {
            if (!available()) return;
            zoom = 1f;
            center = Vector2.one * 0.5f;
            ClearPointers();
            Apply();
        }

        /// <summary>
        /// 将视口定位到给定地图位置，使用局部缩放辨认周边道路。
        /// </summary>
        /// <param name="position">整张地图中的归一化位置。</param>
        public void FocusOn(Vector2 position)
        {
            if (!available()) return;
            zoom = Mathf.Max(zoom, 2f);
            center = position;
            ClearPointers();
            Apply();
        }

        /// <summary>
        /// 记录背景或标记触点，双指参与的触点始终不产生单击。
        /// </summary>
        /// <param name="eventData">按下事件。</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!available() || eventData.button != PointerEventData.InputButton.Left) return;
            pointers[eventData.pointerId] = origins[eventData.pointerId] = eventData.position;
            suppressed.Remove(eventData.pointerId);
            if (pointers.Count > 1) SuppressPointers();
            pinchDistance = PointerDistance();
        }

        /// <summary>
        /// 释放触点，保留本次手势是否允许单击的判断直到点击事件结束。
        /// </summary>
        /// <param name="eventData">抬起事件。</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.dragging)
            {
                suppressed.Add(eventData.pointerId);
                return;
            }
            if (origins.TryGetValue(eventData.pointerId, out Vector2 origin) && ExceedsThreshold(origin, eventData.position))
                suppressed.Add(eventData.pointerId);
            pointers.Remove(eventData.pointerId);
            origins.Remove(eventData.pointerId);
            pinchDistance = PointerDistance();
        }

        /// <summary>
        /// 仅地图背景的完整单击产生地面选点，标记按钮独立显示详情。
        /// </summary>
        /// <param name="eventData">点击事件。</param>
        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.pointerPressRaycast.gameObject == gameObject && CanSelectPointer(eventData)) selectPoint?.Invoke(eventData);
        }

        /// <summary>
        /// 检查标记或背景是否完成未参与拖动、滚轮及双指的左键单击。
        /// </summary>
        /// <param name="eventData">待检查的点击事件。</param>
        /// <returns>本次手势允许选点时为真。</returns>
        public bool CanSelectPointer(PointerEventData eventData) => available() && eventData.button == PointerEventData.InputButton.Left &&
            !suppressed.Contains(eventData.pointerId) && !eventData.dragging && !ExceedsThreshold(eventData.pressPosition, eventData.position);

        /// <summary>
        /// 使用事件系统的拖动门槛取消本次单击资格。
        /// </summary>
        /// <param name="eventData">开始拖动事件。</param>
        public void OnBeginDrag(PointerEventData eventData)
        {
            SuppressPointers();
            suppressed.Add(eventData.pointerId);
            eventData.eligibleForClick = false;
        }

        /// <summary>
        /// 捏合时调整倍率，单指时按当前缩放移动地图。
        /// </summary>
        /// <param name="eventData">拖动事件。</param>
        public void OnDrag(PointerEventData eventData)
        {
            if (!available() || !pointers.ContainsKey(eventData.pointerId)) return;
            pointers[eventData.pointerId] = eventData.position;
            if (pointers.Count > 1)
            {
                float distance = PointerDistance();
                if (pinchDistance > 0f) zoom = Mathf.Clamp(zoom * distance / pinchDistance, 1f, 5f);
                pinchDistance = distance;
            }
            else
            {
                RectTransform frame = map.Frame;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, eventData.position, eventData.pressEventCamera, out Vector2 current);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(frame, eventData.position - eventData.delta, eventData.pressEventCamera, out Vector2 previous);
                Vector2 delta = current - previous;
                center -= new Vector2(delta.x / frame.rect.width, delta.y / frame.rect.height) / zoom;
            }
            Apply();
        }

        /// <summary>
        /// 拖动结束后确保触点不会残留或触发选点。
        /// </summary>
        /// <param name="eventData">结束拖动事件。</param>
        public void OnEndDrag(PointerEventData eventData)
        {
            suppressed.Add(eventData.pointerId);
            pointers.Remove(eventData.pointerId);
            origins.Remove(eventData.pointerId);
            pinchDistance = PointerDistance();
        }

        /// <summary>
        /// 使用鼠标滚轮连续缩放，同时取消按住触点的单击资格。
        /// </summary>
        /// <param name="eventData">滚轮事件。</param>
        public void OnScroll(PointerEventData eventData)
        {
            SuppressPointers();
            ZoomBy(Mathf.Pow(1.15f, eventData.scrollDelta.y));
        }

        /// <summary>
        /// 按事件系统的屏幕像素门槛识别位移。
        /// </summary>
        /// <param name="origin">按下时的屏幕位置。</param>
        /// <param name="current">当前屏幕位置。</param>
        /// <returns>达到拖动门槛时为真。</returns>
        private static bool ExceedsThreshold(Vector2 origin, Vector2 current)
        {
            int threshold = EventSystem.current != null ? EventSystem.current.pixelDragThreshold : 5;
            return (current - origin).sqrMagnitude >= Mathf.Max(1, threshold * threshold);
        }

        /// <summary>
        /// 取消所有参与当前手势的触点单击资格。
        /// </summary>
        private void SuppressPointers() { foreach (int id in pointers.Keys) suppressed.Add(id); }

        /// <summary>
        /// 取得前两个触点间距。
        /// </summary>
        /// <returns>不足两点时为零，否则为屏幕间距。</returns>
        private float PointerDistance()
        {
            if (pointers.Count < 2) return 0f;
            using var values = pointers.Values.GetEnumerator();
            values.MoveNext();
            Vector2 first = values.Current;
            values.MoveNext();
            return Vector2.Distance(first, values.Current);
        }

        /// <summary>
        /// 约束可见视口并同步地图绘制。
        /// </summary>
        private void Apply()
        {
            float half = 0.5f / zoom;
            center = new Vector2(Mathf.Clamp(center.x, half, 1f - half), Mathf.Clamp(center.y, half, 1f - half));
            map.SetViewport(zoom, center);
        }

        /// <summary>
        /// 清理手势状态，避免重新打开后残留触点。
        /// </summary>
        private void ClearPointers()
        {
            pointers.Clear();
            origins.Clear();
            suppressed.Clear();
            pinchDistance = 0f;
        }

        /// <summary>
        /// 隐藏地图时清理手势。
        /// </summary>
        private void OnDisable() => ClearPointers();
    }
}
