using GameLogic.Gameplay.Level;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameLogic.Tests.Editor.Level
{
    /// <summary>
    /// 验证地图点击反投影、可见路线裁切及单击与地图手势的边界。
    /// </summary>
    public sealed class RegionMapNavigationUiTests
    {
        private GameObject gestureFixture;
        private GameObject eventFixture;

        /// <summary>
        /// 断言失败时仍释放测试对象，避免下一项测试继承残留手势或事件系统。
        /// </summary>
        [TearDown]
        public void ClearFixtures()
        {
            if (gestureFixture != null) Object.DestroyImmediate(gestureFixture);
            if (eventFixture != null) Object.DestroyImmediate(eventFixture);
        }

        /// <summary>
        /// 非方形地图和任意北向均保持世界坐标与底图坐标双向一致。
        /// </summary>
        /// <param name="yaw">底图北向角度。</param>
        [TestCase(0f)]
        [TestCase(37f)]
        [TestCase(90f)]
        [TestCase(-90f)]
        [TestCase(145f)]
        public void WorldProjectionRoundTripsWithNorthYaw(float yaw)
        {
            var map = new RegionMapDefinition { Bounds = new Rect(-60f, 15f, 200f, 100f), NorthYawDegrees = yaw };
            Vector3 original = new(63f, 2f, 54f);
            Vector2 uv = RegionMapProjection.ProjectWorldUv(map, original);
            Assert.That(RegionMapProjection.TryUnprojectWorld(map, uv, original.y, out Vector3 restored), Is.True);
            Assert.That(Vector3.Distance(restored, original), Is.LessThan(0.0001f));
        }

        /// <summary>
        /// 当前缩放与平移决定点击位置，点击视口之外必须失败。
        /// </summary>
        [Test]
        public void ZoomedViewportClickUsesCurrentUvRectAndRejectsOutside()
        {
            var map = new RegionMapDefinition { Bounds = new Rect(-100f, -50f, 200f, 100f) };
            var viewport = new Rect(0.45f, 0.3f, 0.2f, 0.2f);
            Assert.That(RegionMapProjection.TryUnprojectViewport(map, viewport, new Vector2(0.75f, 0.25f), 3f, out Vector3 world), Is.True);
            Assert.That(world.x, Is.EqualTo(20f).Within(0.0001f));
            Assert.That(world.z, Is.EqualTo(-15f).Within(0.0001f));
            Assert.That(world.y, Is.EqualTo(3f));
            Assert.That(RegionMapProjection.TryUnprojectViewport(map, viewport, new Vector2(1.01f, 0.5f), 3f, out _), Is.False);
            Assert.That(RegionMapProjection.TryUnprojectViewport(map, viewport, new Vector2(float.NaN, 0.5f), 3f, out _), Is.False);
        }

        /// <summary>
        /// 旋转后底图留白和世界边界外的点击不能被夹成边缘目的地。
        /// </summary>
        [Test]
        public void RotatedBlankMapCornerAndWorldOutsideAreRejected()
        {
            var map = new RegionMapDefinition { Bounds = new Rect(-100f, -50f, 200f, 100f), NorthYawDegrees = 45f };
            Assert.That(RegionMapProjection.TryUnprojectViewport(map, new Rect(0f, 0f, 1f, 1f), Vector2.zero, 0f, out _), Is.False);
            map.NorthYawDegrees = 0f;
            Assert.That(RegionMapProjection.TryUnprojectWorld(map, new Vector2(1.001f, 0.5f), 0f, out _), Is.False);
            Assert.That(RegionMapProjection.TryUnprojectWorld(map, Vector2.one, 0f, out Vector3 edge), Is.True);
            Assert.That(edge, Is.EqualTo(new Vector3(100f, 0f, 50f)));
        }

        /// <summary>
        /// 实际地图框按本地矩形反投影，框外留白不可选中。
        /// </summary>
        [Test]
        public void ScreenClickRespectsFrameSizePivotAndOutsideLetterbox()
        {
            var node = gestureFixture = new GameObject("MapFrameTest", typeof(RectTransform));
            var rect = node.GetComponent<RectTransform>();
            rect.pivot = new Vector2(0.2f, 0.7f);
            rect.sizeDelta = new Vector2(400f, 200f);
            rect.position = new Vector3(280f, 360f, 0f);
            Assert.That(RegionMapWidget.TryScreenToViewport(rect, new Vector2(500f, 270f), null, out Vector2 point), Is.True);
            Assert.That(point.x, Is.EqualTo(0.75f).Within(0.0001f));
            Assert.That(point.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(RegionMapWidget.TryScreenToViewport(rect, new Vector2(610f, 270f), null, out _), Is.False);
            Object.DestroyImmediate(node);
        }

        /// <summary>
        /// 横跨视口的长线段按真实交点裁切，不改变斜率。
        /// </summary>
        [Test]
        public void RouteSegmentClipsAtTrueIntersections()
        {
            Vector2 start = new(-1f, 0f), end = new(2f, 1f);
            Assert.That(RegionMapRouteGraphic.TryClipSegment(ref start, ref end), Is.True);
            Assert.That(start.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(start.y, Is.EqualTo(1f / 3f).Within(0.0001f));
            Assert.That(end.x, Is.EqualTo(1f).Within(0.0001f));
            Assert.That(end.y, Is.EqualTo(2f / 3f).Within(0.0001f));
            start = new Vector2(-1f, 2f);
            end = new Vector2(2f, 2f);
            Assert.That(RegionMapRouteGraphic.TryClipSegment(ref start, ref end), Is.False);
            start = new Vector2(-2f, 0.1f);
            end = new Vector2(-1f, 0.9f);
            Assert.That(RegionMapRouteGraphic.TryClipSegment(ref start, ref end), Is.False);
        }

        /// <summary>
        /// 框架像素门槛以内的点击选点，拖动结束和按钮原始命中不触发背景选点。
        /// </summary>
        [Test]
        public void BackgroundClickUsesDragThresholdAndDoesNotBubbleMarkerClicks()
        {
            var system = eventFixture = new GameObject("MapEventSystem", typeof(EventSystem));
            var events = system.GetComponent<EventSystem>();
            var node = gestureFixture = new GameObject("MapGesture", typeof(RectTransform), typeof(RegionMapGesture));
            var gesture = node.GetComponent<RegionMapGesture>();
            int clicks = 0;
            gesture.Configure(null, () => true, _ => clicks++);
            PointerEventData point = Pointer(events, node, 1, new Vector2(100f, 100f));
            gesture.OnPointerDown(point);
            point.position += new Vector2(3f, 0f);
            gesture.OnPointerUp(point);
            gesture.OnPointerClick(point);
            Assert.That(clicks, Is.EqualTo(1));
            point = Pointer(events, node, 1, new Vector2(100f, 100f));
            gesture.OnPointerDown(point);
            gesture.OnBeginDrag(point);
            point.dragging = true;
            gesture.OnPointerUp(point);
            gesture.OnEndDrag(point);
            point.dragging = false;
            gesture.OnPointerClick(point);
            Assert.That(clicks, Is.EqualTo(1));
            point = Pointer(events, system, 1, new Vector2(100f, 100f));
            gesture.OnPointerDown(point);
            gesture.OnPointerUp(point);
            gesture.OnPointerClick(point);
            Assert.That(clicks, Is.EqualTo(1));
            Object.DestroyImmediate(node);
            Object.DestroyImmediate(system);
        }

        /// <summary>
        /// 两个手指均失去点击资格，下一次独立点击恢复正常。
        /// </summary>
        [Test]
        public void BothPinchPointersStaySuppressedAfterFirstFingerRelease()
        {
            var system = eventFixture = new GameObject("MapEventSystem", typeof(EventSystem));
            var events = system.GetComponent<EventSystem>();
            var node = gestureFixture = new GameObject("MapGesture", typeof(RectTransform), typeof(RegionMapGesture));
            var gesture = node.GetComponent<RegionMapGesture>();
            int clicks = 0;
            gesture.Configure(null, () => true, _ => clicks++);
            PointerEventData first = Pointer(events, node, 10, new Vector2(100f, 100f));
            PointerEventData second = Pointer(events, node, 11, new Vector2(200f, 100f));
            gesture.OnPointerDown(first);
            gesture.OnPointerDown(second);
            gesture.OnPointerUp(first);
            gesture.OnPointerClick(first);
            gesture.OnPointerUp(second);
            gesture.OnPointerClick(second);
            Assert.That(clicks, Is.Zero);
            gesture.OnPointerDown(first);
            gesture.OnPointerUp(first);
            gesture.OnPointerClick(first);
            Assert.That(clicks, Is.EqualTo(1));
            Object.DestroyImmediate(node);
            Object.DestroyImmediate(system);
        }

        /// <summary>
        /// 抬起位置越过门槛和右键操作均不构成地图单击。
        /// </summary>
        [Test]
        public void PointerReleaseDistanceAndRightButtonDoNotSelect()
        {
            var system = eventFixture = new GameObject("MapEventSystem", typeof(EventSystem));
            var events = system.GetComponent<EventSystem>();
            var node = gestureFixture = new GameObject("MapGesture", typeof(RectTransform), typeof(RegionMapGesture));
            var gesture = node.GetComponent<RegionMapGesture>();
            gesture.Configure(null, () => true);
            PointerEventData point = Pointer(events, node, 1, new Vector2(100f, 100f));
            gesture.OnPointerDown(point);
            point.position += new Vector2(8f, 0f);
            gesture.OnPointerUp(point);
            Assert.That(gesture.CanSelectPointer(point), Is.False);
            point = Pointer(events, node, 1, new Vector2(100f, 100f));
            point.button = PointerEventData.InputButton.Right;
            gesture.OnPointerDown(point);
            gesture.OnPointerUp(point);
            Assert.That(gesture.CanSelectPointer(point), Is.False);
            Object.DestroyImmediate(node);
            Object.DestroyImmediate(system);
        }

        /// <summary>
        /// 标记按钮保留单击回调，从标记开始拖动后不触发详情或背景选点。
        /// </summary>
        [Test]
        public void MarkerButtonForwardsGestureAndSuppressesClickAfterDrag()
        {
            var system = eventFixture = new GameObject("MapEventSystem", typeof(EventSystem));
            var events = system.GetComponent<EventSystem>();
            var node = gestureFixture = new GameObject("MapGesture", typeof(RectTransform), typeof(RegionMapGesture));
            var gesture = node.GetComponent<RegionMapGesture>();
            int groundClicks = 0, markerClicks = 0;
            gesture.Configure(null, () => true, _ => groundClicks++);
            var marker = new GameObject("Marker", typeof(RectTransform), typeof(RegionMapMarkerButton));
            marker.transform.SetParent(node.transform, false);
            var button = marker.GetComponent<RegionMapMarkerButton>();
            button.onClick.AddListener(() => markerClicks++);
            PointerEventData point = Pointer(events, marker, 1, new Vector2(100f, 100f));
            button.OnPointerDown(point);
            button.OnPointerUp(point);
            button.OnPointerClick(point);
            Assert.That(markerClicks, Is.EqualTo(1));
            point = Pointer(events, marker, 1, new Vector2(100f, 100f));
            button.OnPointerDown(point);
            gesture.OnBeginDrag(point);
            point.dragging = true;
            button.OnPointerUp(point);
            gesture.OnEndDrag(point);
            point.dragging = false;
            button.OnPointerClick(point);
            gesture.OnPointerClick(point);
            Assert.That(markerClicks, Is.EqualTo(1));
            Assert.That(groundClicks, Is.Zero);
            Object.DestroyImmediate(node);
            Object.DestroyImmediate(system);
        }

        /// <summary>
        /// 按住地图时滚轮缩放会取消本次点击，下一次独立点击恢复正常。
        /// </summary>
        [Test]
        public void ScrollDuringPressSuppressesSelection()
        {
            var system = eventFixture = new GameObject("MapEventSystem", typeof(EventSystem));
            var events = system.GetComponent<EventSystem>();
            var node = gestureFixture = new GameObject("MapGesture", typeof(RectTransform), typeof(RegionMapWidget), typeof(RegionMapGesture));
            var gesture = node.GetComponent<RegionMapGesture>();
            int clicks = 0;
            gesture.Configure(node.GetComponent<RegionMapWidget>(), () => true, _ => clicks++);
            PointerEventData point = Pointer(events, node, 1, new Vector2(100f, 100f));
            gesture.OnPointerDown(point);
            point.scrollDelta = Vector2.up;
            gesture.OnScroll(point);
            gesture.OnPointerUp(point);
            gesture.OnPointerClick(point);
            Assert.That(clicks, Is.Zero);
            gesture.OnPointerDown(point);
            gesture.OnPointerUp(point);
            gesture.OnPointerClick(point);
            Assert.That(clicks, Is.EqualTo(1));
            Object.DestroyImmediate(node);
            Object.DestroyImmediate(system);
        }

        /// <summary>
        /// 创建具有真实按下坐标和命中对象的指针事件。
        /// </summary>
        /// <param name="events">当前事件系统。</param>
        /// <param name="target">最初命中的图形对象。</param>
        /// <param name="id">指针身份。</param>
        /// <param name="position">按下坐标。</param>
        /// <returns>可用于单击和拖动判断的事件。</returns>
        private static PointerEventData Pointer(EventSystem events, GameObject target, int id, Vector2 position) => new(events)
        {
            pointerId = id, position = position, pressPosition = position, button = PointerEventData.InputButton.Left,
            pointerPressRaycast = new RaycastResult { gameObject = target }
        };
    }
}
