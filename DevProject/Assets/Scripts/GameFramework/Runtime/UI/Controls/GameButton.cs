using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 项目统一按钮组件，提供基于 UGUI Button 的稳定业务引用类型。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Button")]
    public sealed class GameButton : Button
    {
        private const int InvalidPointerId = int.MinValue;

        private int _pressedPointerId = InvalidPointerId;

        /// <summary>
        /// 指针在可交互按钮上按下时触发。
        /// </summary>
        public event Action<PointerEventData> OnPressDown;

        /// <summary>
        /// 在本按钮上开始的按压抬起时触发。
        /// </summary>
        public event Action<PointerEventData> OnPressUp;

        /// <inheritdoc />
        public override void OnPointerDown(PointerEventData eventData)
        {
            base.OnPointerDown(eventData);

            if (eventData.button != PointerEventData.InputButton.Left ||
                !IsActive() ||
                !IsInteractable() ||
                _pressedPointerId != InvalidPointerId)
            {
                return;
            }

            _pressedPointerId = eventData.pointerId;
            OnPressDown?.Invoke(eventData);
        }

        /// <inheritdoc />
        public override void OnPointerUp(PointerEventData eventData)
        {
            base.OnPointerUp(eventData);

            if (eventData.button != PointerEventData.InputButton.Left ||
                eventData.pointerId != _pressedPointerId)
            {
                return;
            }

            _pressedPointerId = InvalidPointerId;
            OnPressUp?.Invoke(eventData);
        }

        /// <summary>
        /// 在组件有效且可交互时触发一次点击回调。
        /// </summary>
        /// <returns>成功触发返回 true，否则返回 false。</returns>
        public bool TryInvokeClick()
        {
            if (!IsActive() || !IsInteractable())
                return false;

            onClick.Invoke();
            return true;
        }

        /// <inheritdoc />
        protected override void OnDisable()
        {
            _pressedPointerId = InvalidPointerId;
            base.OnDisable();
        }
    }
}
