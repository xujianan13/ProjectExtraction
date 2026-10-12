using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 通过真实地图射线和事件系统顺序验证选点、导航、追踪与路线显示。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 小地图入口、地面预览和前往按钮形成完整移动流程，重开地图后可停止前往。
        /// </summary>
        /// <returns>真实地图窗口、运行移动和路线网格的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator NavigationUi_GroundClickPreviewsStartsMovesAndStopsThroughRealButtons() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationUiGround");
            ConfigureNavigationFixture(scope);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await WaitRunSavedAsync(run);
            SetNavigationAttackRange(run, 0f);
            List<Vector3> route = FindNavigationObstacleRoute(run);
            Vector3 destination = route[route.Count - 1], before = InputPersistencePlayer(run).Position;
            GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
            await SettleNavigationUiAsync();
            NavigationUiClick(hud.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false).Single(value => value.name == "Minimap").gameObject);
            GameWindow map = await WaitForWindowAsync(ProjectServices.ExpeditionMapWindow);
            await SettleNavigationUiAsync();
            RegionMapWidget widget = map.Root.GetComponentInChildren<RegionMapWidget>(false);
            Vector2 uv = RegionMapProjection.ProjectWorldUv(run.CurrentMap, destination);
            Vector2 screen = RectTransformUtility.WorldToScreenPoint(null,
                widget.Frame.TransformPoint(widget.Frame.rect.min + Vector2.Scale(uv, widget.Frame.rect.size)));
            PointerEventData point = NavigationUiPress(screen);
            Assert.That(point.pointerPress, Is.EqualTo(widget.Frame.gameObject), "真实地面点击必须命中地图框，不能被路线或图标拦截。");
            NavigationUiRelease(point, screen);
            StringAssert.Contains("地面位置", NavigationUiText(map));
            StringAssert.Contains("路线距离", NavigationUiText(map));
            UnityEngine.UI.Button go = NavigationUiButton(map, "前往");
            Assert.That(go.interactable, Is.True);
            Assert.That(run.IsNavigating, Is.False, "预览不能提前移动玩家。");
            Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(before));
            await SettleNavigationUiAsync();
            AssertNavigationUiMesh(widget.GetComponentInChildren<RegionMapRouteGraphic>(false));
            NavigationUiClick(go.gameObject);
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None &&
                GameEntry.Get<IUIModule>().OpenedWindows.All(value => value.Id != ProjectServices.ExpeditionMapWindow));
            Assert.That(run.IsNavigating, Is.True);
            Assert.That(Vector3.Distance(run.NavigationDestination, destination), Is.LessThan(0.001f));
            await AdvanceNavigationTicksAsync(run, 1, 1);
            Assert.That(Vector3.Distance(InputPersistencePlayer(run).Position, before), Is.GreaterThan(0f));
            await SettleNavigationUiAsync();
            RegionMapRouteGraphic minimapRoute = hud.Root.GetComponentInChildren<RegionMapRouteGraphic>(false);
            Assert.That(minimapRoute.raycastTarget, Is.False);
            AssertNavigationUiMesh(minimapRoute);
            NavigationUiClick(hud.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false).Single(value => value.name == "Minimap").gameObject);
            map = await WaitForWindowAsync(ProjectServices.ExpeditionMapWindow);
            await SettleNavigationUiAsync();
            UnityEngine.UI.Button stop = NavigationUiButton(map, "停止前往");
            Assert.That(stop.interactable, Is.True);
            NavigationUiClick(stop.gameObject);
            Assert.That(run.IsNavigating, Is.False);
            Assert.That(run.NavigationRoute, Is.Empty);
            Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Map));
            Assert.That(stop.interactable, Is.False);
            NavigationUiClick(NavigationUiButton(map, "返回探索").gameObject);
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
        });

        /// <summary>
        /// 标记开始的拖动遵守按钮提前释放顺序，随后正常单击仍显示详情并支持追踪。
        /// </summary>
        /// <returns>真实射线、父级拖动路由和追踪保存的逐帧验收任务。</returns>
        [UnityTest]
        public IEnumerator NavigationUi_MarkerDragDoesNotSelectAndSubsequentClickStillTracks() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("NavigationUiMarker");
            ConfigureNavigationFixture(scope);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await WaitRunSavedAsync(run);
            Assert.That(run.TryOpenMap(), Is.True);
            GameWindow map = await WaitForWindowAsync(ProjectServices.ExpeditionMapWindow);
            await SettleNavigationUiAsync();
            RegionMapMarker target = run.GetMapProjection().Markers.Single(value => value.Kind == RegionMapMarkerKind.Boss);
            RegionMapMarkerButton marker = map.Root.GetComponentsInChildren<RegionMapMarkerButton>(false).Single(value => value.name == "Marker_" + target.Id);
            Vector2 screen = ScreenPoint((RectTransform)marker.transform, Vector2.zero);
            PointerEventData point = NavigationUiPress(screen);
            Assert.That(point.pointerPress, Is.EqualTo(marker.gameObject));
            Assert.That(point.pointerDrag, Is.EqualTo(marker.transform.parent.gameObject));
            string initialDetail = NavigationUiText(map);
            Vector2 moved = screen + Vector2.right * (EventSystem.current.pixelDragThreshold + 20f);
            NavigationUiDrag(point, moved);
            NavigationUiRelease(point, moved);
            Assert.That(NavigationUiText(map), Is.EqualTo(initialDetail), "从标记开始拖动不能在松手时选中标记或地面。");
            Assert.That(NavigationUiButton(map, "前往").interactable, Is.False);
            Assert.That(NavigationUiButton(map, "追踪目标").interactable, Is.False);
            Assert.That(run.IsNavigating, Is.False);
            NavigationUiClick(marker.gameObject);
            StringAssert.Contains("直线距离", NavigationUiText(map));
            StringAssert.Contains("标记位置", NavigationUiText(map));
            StringAssert.Contains("路线距离", NavigationUiText(map));
            Assert.That(NavigationUiButton(map, "追踪目标").interactable, Is.True);
            NavigationUiClick(NavigationUiButton(map, "追踪目标").gameObject);
            await WaitRunSavedAsync(run);
            Assert.That(run.CurrentRegion.TrackingTargetId, Is.EqualTo(target.Id));
            Assert.That(run.GetMapProjection().Markers.Single(value => value.Id == target.Id).IsTracked, Is.True);
            Assert.That(run.IsNavigating, Is.False, "追踪保持其原有身份保存行为，不代替前往按钮。");
            NavigationUiClick(NavigationUiButton(map, "返回探索").gameObject);
            await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
        });

        /// <summary>
        /// 等待地图绘图和布局组件按真实帧更新。
        /// </summary>
        /// <returns>布局与网格稳定任务。</returns>
        private static async UniTask SettleNavigationUiAsync()
        {
            for (int frame = 0; frame < 3; frame++)
            {
                Canvas.ForceUpdateCanvases();
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            }
            Canvas.ForceUpdateCanvases();
        }

        /// <summary>
        /// 按显示文字找到窗口中的真实按钮。
        /// </summary>
        /// <param name="window">已打开的窗口。</param>
        /// <param name="caption">按钮显示文字。</param>
        /// <returns>匹配的按钮组件。</returns>
        private static UnityEngine.UI.Button NavigationUiButton(GameWindow window, string caption) =>
            window.Root.GetComponentsInChildren<UnityEngine.UI.Button>(false).Single(value => value.GetComponentInChildren<UnityEngine.UI.Text>().text == caption);

        /// <summary>
        /// 读取真实窗口显示的文字，以检验玩家可见的预览和详情。
        /// </summary>
        /// <param name="window">已打开的地图窗口。</param>
        /// <returns>窗口文字的连接结果。</returns>
        private static string NavigationUiText(GameWindow window) =>
            string.Join("\n", window.Root.GetComponentsInChildren<UnityEngine.UI.Text>(false).Select(value => value.text));

        /// <summary>
        /// 经射线命中和真实按下抬起顺序点击控件，不直接调用按钮事件。
        /// </summary>
        /// <param name="target">预期接收点击的控件对象。</param>
        private static void NavigationUiClick(GameObject target)
        {
            Vector2 screen = ScreenPoint((RectTransform)target.transform, Vector2.zero);
            PointerEventData point = NavigationUiPress(screen);
            Assert.That(point.pointerPress, Is.EqualTo(target), "控件中心真实射线被其他图形拦截：" + target.name);
            NavigationUiRelease(point, screen);
        }

        /// <summary>
        /// 使用真实事件系统建立命中、按下对象和父级拖动路由。
        /// </summary>
        /// <param name="screen">按下的屏幕位置。</param>
        /// <returns>具有真实射线和按下路由的指针事件。</returns>
        private static PointerEventData NavigationUiPress(Vector2 screen)
        {
            Assert.That(EventSystem.current, Is.Not.Null);
            var point = new PointerEventData(EventSystem.current)
            { pointerId = 81, position = screen, pressPosition = screen, button = PointerEventData.InputButton.Left, eligibleForClick = true };
            RaycastResult hit = NavigationUiRaycast(point);
            point.pointerCurrentRaycast = point.pointerPressRaycast = hit;
            point.rawPointerPress = hit.gameObject;
            point.pointerPress = ExecuteEvents.ExecuteHierarchy(hit.gameObject, point, ExecuteEvents.pointerDownHandler);
            if (point.pointerPress == null) point.pointerPress = ExecuteEvents.GetEventHandler<IPointerClickHandler>(hit.gameObject);
            point.pointerDrag = ExecuteEvents.GetEventHandler<IDragHandler>(hit.gameObject);
            if (point.pointerDrag != null) ExecuteEvents.Execute(point.pointerDrag, point, ExecuteEvents.initializePotentialDrag);
            return point;
        }

        /// <summary>
        /// 按输入模块的开始拖动、按钮提前释放、连续拖动顺序发送事件。
        /// </summary>
        /// <param name="point">已经完成按下的指针。</param>
        /// <param name="screen">超过拖动门槛后的屏幕位置。</param>
        private static void NavigationUiDrag(PointerEventData point, Vector2 screen)
        {
            point.delta = screen - point.position;
            point.position = screen;
            point.pointerCurrentRaycast = NavigationUiRaycast(point);
            ExecuteEvents.Execute(point.pointerDrag, point, ExecuteEvents.beginDragHandler);
            point.dragging = true;
            if (point.pointerPress != point.pointerDrag)
            {
                ExecuteEvents.Execute(point.pointerPress, point, ExecuteEvents.pointerUpHandler);
                point.eligibleForClick = false;
                point.pointerPress = point.rawPointerPress = null;
            }
            ExecuteEvents.Execute(point.pointerDrag, point, ExecuteEvents.dragHandler);
        }

        /// <summary>
        /// 按输入模块的抬起、同目标点击、结束拖动顺序发送事件。
        /// </summary>
        /// <param name="point">本次手势的指针事件。</param>
        /// <param name="screen">抬起屏幕位置。</param>
        private static void NavigationUiRelease(PointerEventData point, Vector2 screen)
        {
            point.position = screen;
            point.pointerCurrentRaycast = NavigationUiRaycast(point);
            ExecuteEvents.Execute(point.pointerPress, point, ExecuteEvents.pointerUpHandler);
            GameObject click = ExecuteEvents.GetEventHandler<IPointerClickHandler>(point.pointerCurrentRaycast.gameObject);
            if (point.pointerPress == click && point.eligibleForClick) ExecuteEvents.Execute(point.pointerPress, point, ExecuteEvents.pointerClickHandler);
            point.eligibleForClick = false;
            point.pointerPress = point.rawPointerPress = null;
            if (point.dragging) ExecuteEvents.Execute(point.pointerDrag, point, ExecuteEvents.endDragHandler);
            point.dragging = false;
            point.pointerDrag = null;
        }

        /// <summary>
        /// 读取真实射线的最上层命中，避免绕过模态层或图形遮挡。
        /// </summary>
        /// <param name="point">当前屏幕指针。</param>
        /// <returns>真实射线的首个命中结果。</returns>
        private static RaycastResult NavigationUiRaycast(PointerEventData point)
        {
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(point, hits);
            Assert.That(hits, Is.Not.Empty, "地图交互位置没有真实射线命中：" + point.position);
            return hits[0];
        }

        /// <summary>
        /// 验证路线组件真实提交了可见网格并拒绝射线。
        /// </summary>
        /// <param name="graphic">需要检查的大小地图路线图形。</param>
        private static void AssertNavigationUiMesh(RegionMapRouteGraphic graphic)
        {
            Assert.That(graphic, Is.Not.Null);
            Assert.That(graphic.raycastTarget, Is.False);
            Mesh mesh = graphic.canvasRenderer.GetMesh();
            int vertices = mesh.vertexCount;
            Assert.That(vertices, Is.GreaterThan(0), "导航路线必须产生真实大小地图网格。");
        }
    }
}
