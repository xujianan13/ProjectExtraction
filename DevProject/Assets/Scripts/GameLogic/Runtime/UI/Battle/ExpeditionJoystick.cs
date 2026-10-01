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
        private const float DeadZone = 0.12f;
        private int pointerId = int.MinValue;
        private Action<Vector2> changed;
        private Func<bool> canInput;
        private RectTransform handle;

        /// <summary>
        /// 绑定本局移动意图接收方和操作门禁。
        /// </summary>
        /// <param name="handler">摇杆变化回调。</param>
        /// <param name="canInput">可选的本局输入门禁。</param>
        public void Bind(Action<Vector2> handler, Func<bool> canInput = null)
        {
            Release();
            changed = handler;
            this.canInput = canInput;
        }

        /// <summary>
        /// 设置随归一化移动轴移动的摇柄。
        /// </summary>
        /// <param name="value">底盘中央锚定的摇柄节点。</param>
        public void SetHandle(RectTransform value)
        {
            handle = value;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
        }

        /// <summary>
        /// 主动释放当前触点并让摇柄回中。
        /// </summary>
        public void ResetInput() => Release();

        /// <summary>
        /// 锁存控制该摇杆的触指。
        /// </summary>
        /// <param name="eventData">按下事件。</param>
        public void OnPointerDown(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || eventData == null || eventData.button != PointerEventData.InputButton.Left ||
                pointerId != int.MinValue || (canInput != null && !canInput())) return;
            pointerId = eventData.pointerId;
            OnDrag(eventData);
        }

        /// <summary>
        /// 将触点相对圆形拖动区域转换为带死区的归一化方向。
        /// </summary>
        /// <param name="eventData">拖动事件。</param>
        public void OnDrag(PointerEventData eventData)
        {
            if (!isActiveAndEnabled || eventData == null || pointerId != eventData.pointerId) return;
            if (canInput != null && !canInput())
            {
                Release();
                return;
            }
            var rect = (RectTransform)transform;
            float radius = Mathf.Min(rect.rect.width, rect.rect.height) * 0.5f;
            if (radius <= 0f || !RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rect, eventData.position, eventData.pressEventCamera, out Vector2 point))
            {
                Release();
                return;
            }
            Vector2 offset = (point - rect.rect.center) / radius;
            float length = Mathf.Min(offset.magnitude, 1f);
            Vector2 axis = length <= DeadZone ? Vector2.zero : offset.normalized * ((length - DeadZone) / (1f - DeadZone));
            if (handle != null)
            {
                float handleRadius = Mathf.Max(handle.rect.width, handle.rect.height) * 0.5f;
                handle.anchoredPosition = axis * Mathf.Max(0f, radius - handleRadius);
            }
            changed?.Invoke(axis);
        }

        /// <summary>
        /// 松开控制触指后停止移动，其他触指不改变摇杆。
        /// </summary>
        /// <param name="eventData">释放事件。</param>
        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData == null || eventData.pointerId != pointerId) return;
            Release();
        }

        /// <summary>
        /// 组件禁用时释放移动意图。
        /// </summary>
        private void OnDisable() => Release();

        /// <summary>
        /// 应用失焦时释放触点和移动意图。
        /// </summary>
        /// <param name="hasFocus">应用是否仍拥有焦点。</param>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) Release();
        }

        /// <summary>
        /// 应用暂停时释放触点和移动意图。
        /// </summary>
        /// <param name="paused">应用是否进入暂停。</param>
        private void OnApplicationPause(bool paused)
        {
            if (paused) Release();
        }

        /// <summary>
        /// 忘记控制触点并向当前单局提交零向量。
        /// </summary>
        private void Release()
        {
            pointerId = int.MinValue;
            if (handle != null) handle.anchoredPosition = Vector2.zero;
            changed?.Invoke(Vector2.zero);
        }
    }
}
