using UnityEngine.EventSystems;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 将标记触点交给地图手势，防止拖动或双指操作结束时误选目标。
    /// </summary>
    public sealed class RegionMapMarkerButton : UnityEngine.UI.Button
    {
        private RegionMapGesture gesture;

        /// <summary>
        /// 按下标记时记录触点，同时保留按钮的视觉反馈。
        /// </summary>
        /// <param name="eventData">按下事件。</param>
        public override void OnPointerDown(PointerEventData eventData)
        {
            gesture = transform.parent.GetComponent<RegionMapGesture>();
            gesture.OnPointerDown(eventData);
            base.OnPointerDown(eventData);
        }

        /// <summary>
        /// 抬起标记时结束手势登记，同时释放按钮视觉反馈。
        /// </summary>
        /// <param name="eventData">抬起事件。</param>
        public override void OnPointerUp(PointerEventData eventData)
        {
            gesture.OnPointerUp(eventData);
            base.OnPointerUp(eventData);
        }

        /// <summary>
        /// 仅完整单击调用原有目标选择回调。
        /// </summary>
        /// <param name="eventData">点击事件。</param>
        public override void OnPointerClick(PointerEventData eventData)
        {
            if (gesture.CanSelectPointer(eventData)) base.OnPointerClick(eventData);
        }
    }
}
