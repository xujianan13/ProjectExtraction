using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 大地图的拖动、滚轮和双指缩放，只调整视口。
    /// </summary>
    public sealed class RegionMapGesture : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
    {
        private RegionMapWidget map;
        private Func<bool> available;
        private float zoom = 1f;
        private Vector2 center = Vector2.one * 0.5f;
        private readonly Dictionary<int, Vector2> pointers = new();
        private float pinchDistance;

        /// <summary>
        /// 绑定地图视口与保存门禁。
        /// </summary>
        /// <param name="map">绘制视口的地图。</param>
        /// <param name="available">当前是否允许操作。</param>
        public void Configure(RegionMapWidget map, Func<bool> available) { this.map = map; this.available = available; }

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
            pointers.Clear();
            pinchDistance = 0f;
            Apply();
        }

        /// <summary>
        /// 将视口定位到给定地图位置，使用局部缩放以便玩家辨认周边道路。
        /// </summary>
        /// <param name="position">整张地图中的归一化位置。</param>
        public void FocusOn(Vector2 position)
        {
            if (!available()) return;
            zoom = Mathf.Max(zoom, 2f);
            center = position;
            pointers.Clear();
            pinchDistance = 0f;
            Apply();
        }

        /// <summary>
        /// 记录当前触点以支持双指缩放。
        /// </summary>
        /// <param name="eventData">按下事件。</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!available()) return;
            pointers[eventData.pointerId] = eventData.position;
            pinchDistance = PointerDistance();
        }

        /// <summary>
        /// 释放触点并重置捏合基准。
        /// </summary>
        /// <param name="eventData">抬起事件。</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            pointers.Remove(eventData.pointerId);
            pinchDistance = PointerDistance();
        }

        /// <summary>
        /// 捏合时调整倍率，单指时按当前缩放移动地图。
        /// </summary>
        /// <param name="eventData">拖动事件。</param>
        public void OnDrag(PointerEventData eventData)
        {
            if (!available()) return;
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
        /// 使用鼠标滚轮连续缩放。
        /// </summary>
        /// <param name="eventData">滚轮事件。</param>
        public void OnScroll(PointerEventData eventData) => ZoomBy(Mathf.Pow(1.15f, eventData.scrollDelta.y));

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
        /// 隐藏时清理触点，防止重新打开后残留拖动。
        /// </summary>
        private void OnDisable() { pointers.Clear(); pinchDistance = 0f; }
    }
}
