using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 使用 UI 网格绘制圆形摇杆，避免新增纹理依赖。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasRenderer))]
    public sealed class JoystickDiscGraphic : UnityEngine.UI.MaskableGraphic, ICanvasRaycastFilter
    {
        /// <summary>
        /// 按当前矩形生成圆盘三角形。
        /// </summary>
        /// <param name="mesh">待填充的 UI 网格。</param>
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = rectTransform.rect;
            float radius = Mathf.Min(rect.width, rect.height) * 0.5f;
            if (radius <= 0f) return;
            Vector2 center = rect.center;
            mesh.AddVert(center, color, new Vector2(0.5f, 0.5f));
            const int segments = 48;
            for (int index = 0; index <= segments; index++)
            {
                float angle = index * Mathf.PI * 2f / segments;
                Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                mesh.AddVert(center + direction * radius, color, direction * 0.5f + Vector2.one * 0.5f);
                if (index > 0) mesh.AddTriangle(0, index, index + 1);
            }
        }

        /// <summary>
        /// 圆盘外的矩形角落不抢占触摸事件。
        /// </summary>
        /// <param name="screenPoint">屏幕触点。</param>
        /// <param name="eventCamera">UI 事件相机。</param>
        /// <returns>触点位于可见圆盘内时为真。</returns>
        public bool IsRaycastLocationValid(Vector2 screenPoint, Camera eventCamera)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rectTransform, screenPoint, eventCamera, out Vector2 local)) return false;
            float radius = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            return radius > 0f && (local - rectTransform.rect.center).sqrMagnitude <= radius * radius;
        }
    }
}
