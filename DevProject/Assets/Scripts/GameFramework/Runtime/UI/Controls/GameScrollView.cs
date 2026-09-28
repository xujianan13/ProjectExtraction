using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 项目统一普通滚动容器，提供拖拽状态和常用边界跳转能力。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Scroll View")]
    public sealed class GameScrollView : ScrollRect
    {
        /// <summary>
        /// 开始左键或单指拖拽时触发。
        /// </summary>
        public event Action DragStarted;

        /// <summary>
        /// 结束左键或单指拖拽时触发。
        /// </summary>
        public event Action DragEnded;

        /// <summary>
        /// 获取当前是否处于拖拽状态。
        /// </summary>
        public bool IsDragging { get; private set; }

        /// <summary>
        /// 跳转到垂直内容顶部并停止惯性。
        /// </summary>
        public void JumpToTop()
        {
            if (!vertical)
                return;

            StopMovement();
            verticalNormalizedPosition = 1f;
        }

        /// <summary>
        /// 跳转到垂直内容底部并停止惯性。
        /// </summary>
        public void JumpToBottom()
        {
            if (!vertical)
                return;

            StopMovement();
            verticalNormalizedPosition = 0f;
        }

        /// <summary>
        /// 跳转到水平内容左侧并停止惯性。
        /// </summary>
        public void JumpToLeft()
        {
            if (!horizontal)
                return;

            StopMovement();
            horizontalNormalizedPosition = 0f;
        }

        /// <summary>
        /// 跳转到水平内容右侧并停止惯性。
        /// </summary>
        public void JumpToRight()
        {
            if (!horizontal)
                return;

            StopMovement();
            horizontalNormalizedPosition = 1f;
        }

        /// <inheritdoc />
        public override void OnBeginDrag(PointerEventData eventData)
        {
            base.OnBeginDrag(eventData);
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            IsDragging = true;
            DragStarted?.Invoke();
        }

        /// <inheritdoc />
        public override void OnEndDrag(PointerEventData eventData)
        {
            base.OnEndDrag(eventData);
            if (eventData.button != PointerEventData.InputButton.Left)
                return;

            IsDragging = false;
            DragEnded?.Invoke();
        }

        protected override void OnDisable()
        {
            IsDragging = false;
            base.OnDisable();
        }
    }
}
