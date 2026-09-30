using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 使用独立触指驱动移动意图的局内摇杆。
    /// </summary>
    public sealed class ExpeditionJoystick : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        private int pointerId = int.MinValue;
        private Action<Vector2> changed;

        /// <summary>
        /// 绑定本局移动意图接收方。
        /// </summary>
        /// <param name="handler">摇杆变化回调。</param>
        public void Bind(Action<Vector2> handler) => changed = handler;

        /// <summary>
        /// 锁存控制该摇杆的触指。
        /// </summary>
        /// <param name="eventData">按下事件。</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (pointerId != int.MinValue) return;
            pointerId = eventData.pointerId;
            OnDrag(eventData);
        }

        /// <summary>
        /// 将触点相对中心位置转换为归一化方向。
        /// </summary>
        /// <param name="eventData">拖动事件。</param>
        public void OnDrag(PointerEventData eventData)
        {
            if (pointerId != eventData.pointerId) return;
            var rect = (RectTransform)transform;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, eventData.position, eventData.pressEventCamera, out Vector2 point)) return;
            float radius = Mathf.Max(1, Mathf.Min(rect.rect.width, rect.rect.height) * 0.5f);
            changed?.Invoke(Vector2.ClampMagnitude((point - rect.rect.center) / radius, 1));
        }

        /// <summary>
        /// 松开同一触指后停止移动。
        /// </summary>
        /// <param name="eventData">释放事件。</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.pointerId != pointerId) return;
            pointerId = int.MinValue;
            changed?.Invoke(Vector2.zero);
        }

        /// <summary>
        /// 关闭、切图或失焦后不保留粘住的移动状态。
        /// </summary>
        private void OnDisable()
        {
            pointerId = int.MinValue;
            changed?.Invoke(Vector2.zero);
        }
    }
}
