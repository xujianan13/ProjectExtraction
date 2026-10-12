using System.Collections.Generic;
using GameLogic.Gameplay.Level;
using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 在同一可遮罩网格内绘制导航路线和终点，避免逐线段创建对象。
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class RegionMapRouteGraphic : UnityEngine.UI.MaskableGraphic
    {
        private readonly List<Vector2> active = new();
        private readonly List<Vector2> preview = new();
        private Vector2 activeDestination;
        private Vector2 previewDestination;
        private bool hasActive;
        private bool hasPreview;
        private bool previewValid;
        private float lineWidth = 4f;
        private static readonly Color ActiveColor = new(0.25f, 1f, 1f, 0.95f);
        private static readonly Color PreviewColor = new(1f, 0.85f, 0.25f, 0.95f);

        /// <summary>
        /// 同步当前视口内的路线，数据变化时才重新生成网格。
        /// </summary>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="viewport">当前底图裁切矩形。</param>
        /// <param name="activeRoute">运行中的剩余世界路线。</param>
        /// <param name="activeTarget">运行中的实际终点。</param>
        /// <param name="showActive">是否显示运行中路线。</param>
        /// <param name="previewRoute">本次选点的世界路线。</param>
        /// <param name="previewTarget">本次选点的终点。</param>
        /// <param name="showPreview">是否显示选点终点。</param>
        /// <param name="validPreview">预览路线是否可达。</param>
        /// <param name="width">线条在画布上的宽度。</param>
        public void SetRoutes(RegionMapDefinition map, Rect viewport, IReadOnlyList<Vector3> activeRoute, Vector3 activeTarget, bool showActive,
            IReadOnlyList<Vector3> previewRoute, Vector3 previewTarget, bool showPreview, bool validPreview, float width)
        {
            bool changed = CopyRoute(active, activeRoute, map, viewport) | CopyRoute(preview, previewRoute, map, viewport);
            Vector2 activeEnd = ToViewport(map, viewport, activeTarget), previewEnd = ToViewport(map, viewport, previewTarget);
            changed |= hasActive != showActive || hasPreview != showPreview || previewValid != validPreview || lineWidth != width ||
                activeDestination != activeEnd || previewDestination != previewEnd;
            hasActive = showActive;
            hasPreview = showPreview;
            previewValid = validPreview;
            lineWidth = width;
            activeDestination = activeEnd;
            previewDestination = previewEnd;
            raycastTarget = false;
            if (changed) SetVerticesDirty();
        }

        /// <summary>
        /// 按矩形裁切线段，完全在外的线段不会被压成边缘伪路线。
        /// </summary>
        /// <param name="start">输入与输出的线段起点。</param>
        /// <param name="end">输入与输出的线段终点。</param>
        /// <returns>线段与零到一视口相交时为真。</returns>
        public static bool TryClipSegment(ref Vector2 start, ref Vector2 end)
        {
            Vector2 origin = start, delta = end - start;
            float first = 0f, last = 1f;
            if (!Clip(-delta.x, origin.x, ref first, ref last) || !Clip(delta.x, 1f - origin.x, ref first, ref last) ||
                !Clip(-delta.y, origin.y, ref first, ref last) || !Clip(delta.y, 1f - origin.y, ref first, ref last)) return false;
            start = origin + delta * first;
            end = origin + delta * last;
            return true;
        }

        /// <summary>
        /// 生成可由父级矩形遮罩裁切的路线和终点网格。
        /// </summary>
        /// <param name="helper">本次重建使用的网格容器。</param>
        protected override void OnPopulateMesh(UnityEngine.UI.VertexHelper helper)
        {
            helper.Clear();
            if (hasActive)
            {
                AddRoute(helper, active, ActiveColor);
                AddDestination(helper, activeDestination, ActiveColor);
            }
            if (hasPreview)
            {
                Color tint = previewValid ? PreviewColor : new Color(1f, 0.35f, 0.3f, 0.95f);
                if (previewValid) AddRoute(helper, preview, tint);
                AddDestination(helper, previewDestination, tint);
            }
        }

        /// <summary>
        /// 将世界路线复制到独立的视口坐标，检测可见网格变化。
        /// </summary>
        /// <param name="buffer">可复用的归一化路线缓冲。</param>
        /// <param name="route">世界路线。</param>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="viewport">当前底图裁切矩形。</param>
        /// <returns>路线内容变化时为真。</returns>
        private static bool CopyRoute(List<Vector2> buffer, IReadOnlyList<Vector3> route, RegionMapDefinition map, Rect viewport)
        {
            bool changed = buffer.Count != route.Count;
            for (int i = 0; i < route.Count; i++)
            {
                Vector2 point = ToViewport(map, viewport, route[i]);
                if (i >= buffer.Count) buffer.Add(point);
                else
                {
                    changed |= buffer[i] != point;
                    buffer[i] = point;
                }
            }
            if (buffer.Count > route.Count) buffer.RemoveRange(route.Count, buffer.Count - route.Count);
            return changed;
        }

        /// <summary>
        /// 保留画面外的原始坐标供线段裁切使用。
        /// </summary>
        /// <param name="map">地图边界及北向。</param>
        /// <param name="viewport">当前底图裁切矩形。</param>
        /// <param name="position">世界位置。</param>
        /// <returns>未经夹取的视口坐标。</returns>
        private static Vector2 ToViewport(RegionMapDefinition map, Rect viewport, Vector3 position)
        {
            Vector2 uv = RegionMapProjection.ProjectWorldUv(map, position) - viewport.position;
            return new Vector2(uv.x / viewport.width, uv.y / viewport.height);
        }

        /// <summary>
        /// 更新半平面内的有效线段参数区间。
        /// </summary>
        /// <param name="direction">朝向该边界的方向量。</param>
        /// <param name="distance">起点到该边界的有符号距离。</param>
        /// <param name="first">进入视口的参数。</param>
        /// <param name="last">离开视口的参数。</param>
        /// <returns>仍有可见线段区间时为真。</returns>
        private static bool Clip(float direction, float distance, ref float first, ref float last)
        {
            if (Mathf.Abs(direction) < 0.000001f) return distance >= 0f;
            float amount = distance / direction;
            if (direction < 0f) first = Mathf.Max(first, amount);
            else last = Mathf.Min(last, amount);
            return first <= last;
        }

        /// <summary>
        /// 绘制逐段裁切后的折线。
        /// </summary>
        /// <param name="helper">网格容器。</param>
        /// <param name="route">未经裁切的视口路线。</param>
        /// <param name="tint">路线颜色。</param>
        private void AddRoute(UnityEngine.UI.VertexHelper helper, List<Vector2> route, Color tint)
        {
            for (int i = 1; i < route.Count; i++)
            {
                Vector2 start = route[i - 1], end = route[i];
                if (!TryClipSegment(ref start, ref end)) continue;
                start = ToLocal(start);
                end = ToLocal(end);
                Vector2 delta = end - start;
                if (delta.sqrMagnitude < 0.000001f) continue;
                Vector2 normal = new Vector2(-delta.y, delta.x).normalized * (lineWidth * 0.5f);
                AddQuad(helper, start - normal, start + normal, end + normal, end - normal, tint);
            }
        }

        /// <summary>
        /// 绘制位于视口内的圆环终点，越界终点不贴到边缘。
        /// </summary>
        /// <param name="helper">网格容器。</param>
        /// <param name="destination">视口终点坐标。</param>
        /// <param name="tint">终点颜色。</param>
        private void AddDestination(UnityEngine.UI.VertexHelper helper, Vector2 destination, Color tint)
        {
            if (destination.x < 0f || destination.x > 1f || destination.y < 0f || destination.y > 1f) return;
            Vector2 origin = ToLocal(destination);
            float outer = lineWidth * 2.5f, inner = lineWidth * 1.5f;
            const int segments = 20;
            for (int i = 0; i < segments; i++)
            {
                float first = i * Mathf.PI * 2f / segments, last = (i + 1) * Mathf.PI * 2f / segments;
                Vector2 a = new(Mathf.Cos(first), Mathf.Sin(first)), b = new(Mathf.Cos(last), Mathf.Sin(last));
                AddQuad(helper, origin + a * inner, origin + a * outer, origin + b * outer, origin + b * inner, tint);
            }
        }

        /// <summary>
        /// 将归一化坐标转换为当前图形矩形的本地坐标。
        /// </summary>
        /// <param name="point">归一化视口坐标。</param>
        /// <returns>图形本地坐标。</returns>
        private Vector2 ToLocal(Vector2 point) => rectTransform.rect.min + Vector2.Scale(point, rectTransform.rect.size);

        /// <summary>
        /// 在同一网格追加一个颜色四边形。
        /// </summary>
        /// <param name="helper">网格容器。</param>
        /// <param name="a">第一个顶点。</param>
        /// <param name="b">第二个顶点。</param>
        /// <param name="c">第三个顶点。</param>
        /// <param name="d">第四个顶点。</param>
        /// <param name="tint">顶点颜色。</param>
        private static void AddQuad(UnityEngine.UI.VertexHelper helper, Vector2 a, Vector2 b, Vector2 c, Vector2 d, Color tint)
        {
            int index = helper.currentVertCount;
            helper.AddVert(a, tint, Vector2.zero);
            helper.AddVert(b, tint, Vector2.zero);
            helper.AddVert(c, tint, Vector2.zero);
            helper.AddVert(d, tint, Vector2.zero);
            helper.AddTriangle(index, index + 1, index + 2);
            helper.AddTriangle(index + 2, index + 3, index);
        }
    }
}
