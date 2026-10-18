using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 真实战斗 HUD 的触屏摇杆与角色移动集成测试。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 小幅、大幅和斜向拖动均按统一速度移动，独占触指且在松手、暂停及成长面板打开时清空意图。
        /// </summary>
        /// <returns>等待真实场景启动和暂停切换的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Joystick_PointerEventsMoveOnlyWhileOwnedAndActive() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            BattleHudWindow hud = GameEntry.Get<IUIModule>().OpenedWindows.OfType<BattleHudWindow>().Single();
            ExpeditionJoystick joystick = hud.RootTransform.GetComponentInChildren<ExpeditionJoystick>(false);
            Assert.That(joystick, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            var rect = (RectTransform)joystick.transform;
            float radius = Mathf.Min(rect.rect.width, rect.rect.height) * 0.5f;
            RectTransform handle = rect.Find("Handle") as RectTransform;
            Assert.That(handle, Is.Not.Null);
            Assert.That(radius, Is.GreaterThan(0f));
            Assert.That(EventSystem.current, Is.Not.Null);
            PointerEventData first = Pointer(17, rect, Vector2.right * radius * 0.05f);
            PointerEventData second = Pointer(18, rect, Vector2.left * radius * 2f);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot start), Is.True);
            ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, first, ExecuteEvents.pointerDownHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot deadZone), Is.True);
            Assert.That(deadZone.Position.x, Is.EqualTo(start.Position.x).Within(0.0001f));

            first.position = ScreenPoint(rect, Vector2.right * radius * 0.2f);
            ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, first, ExecuteEvents.dragHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot small), Is.True);
            float smallStep = small.Position.x - deadZone.Position.x;
            Assert.That(smallStep, Is.GreaterThan(0f));
            Assert.That(handle.anchoredPosition.x, Is.GreaterThan(0f));
            float smallHandleOffset = handle.anchoredPosition.x;

            first.position = ScreenPoint(rect, Vector2.right * radius * 2f);
            ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, first, ExecuteEvents.dragHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot full), Is.True);
            float fullStep = full.Position.x - small.Position.x;
            Assert.That(fullStep, Is.EqualTo(smallStep).Within(0.0001f), "超过死区的小幅和满幅拖动应使用相同角色移速。");
            Assert.That(handle.anchoredPosition.x, Is.GreaterThan(smallHandleOffset), "移动等速不应改变摇柄的幅度表现。");

            first.position = ScreenPoint(rect, new Vector2(1f, 1f).normalized * radius * 0.5f);
            ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, first, ExecuteEvents.dragHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot diagonal), Is.True);
            Assert.That(Vector3.Distance(diagonal.Position, full.Position), Is.EqualTo(fullStep).Within(0.0001f), "斜向移动不能加速。");
            Assert.That(diagonal.Position.x, Is.GreaterThan(full.Position.x));
            Assert.That(diagonal.Position.z, Is.GreaterThan(full.Position.z));

            ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, second, ExecuteEvents.pointerDownHandler);
            ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, second, ExecuteEvents.dragHandler);
            ExecuteEvents.Execute<IPointerUpHandler>(joystick.gameObject, second, ExecuteEvents.pointerUpHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot afterSecond), Is.True);
            Assert.That(afterSecond.Position.x, Is.GreaterThan(diagonal.Position.x), "第二触指不得抢占或释放第一触指。");

            ExecuteEvents.Execute<IPointerUpHandler>(joystick.gameObject, first, ExecuteEvents.pointerUpHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot released), Is.True);
            Assert.That(released.Position.x, Is.EqualTo(afterSecond.Position.x).Within(0.0001f));
            Assert.That(handle.anchoredPosition, Is.EqualTo(Vector2.zero));

            first = Pointer(21, rect, Vector2.right * radius);
            ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, first, ExecuteEvents.pointerDownHandler);
            joystick.SendMessage("OnApplicationFocus", false);
            ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, first, ExecuteEvents.dragHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot unfocused), Is.True);
            Assert.That(unfocused.Position.x, Is.EqualTo(released.Position.x).Within(0.0001f));

            first = Pointer(19, rect, Vector2.right * radius);
            ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, first, ExecuteEvents.pointerDownHandler);
            run.Advance(CombatWorld.TickSeconds);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
            run.Advance(1f);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot paused), Is.True);
            await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
            ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, first, ExecuteEvents.dragHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot resumed), Is.True);
            Assert.That(resumed.Position.x, Is.EqualTo(paused.Position.x).Within(0.0001f), "暂停释放后旧触点不可继续移动。");

            first = Pointer(20, rect, Vector2.right * radius);
            ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, first, ExecuteEvents.pointerDownHandler);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.TryOpenProgression(), Is.True);
            Assert.That(joystick.enabled, Is.False);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot choosing), Is.True);
            run.Advance(1f);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot stillChoosing), Is.True);
            Assert.That(stillChoosing.Position.x, Is.EqualTo(choosing.Position.x).Within(0.0001f));
        });

        /// <summary>
        /// 直接提交的触屏方向也统一使用当前角色移速，零向量和非法值清空实际位移。
        /// </summary>
        /// <returns>真实运行角色在多种输入幅度下的逐帧位移验证任务。</returns>
        [UnityTest]
        public IEnumerator Joystick_RunDirectionUsesConfiguredSpeedAndZeroStops() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            Assert.That(run.TryCaptureState(out var checkpoint, out string error), Is.True, error);
            float stepDistance = checkpoint.PlayerState.Spec.MoveSpeed * CombatWorld.TickSeconds;
            Assert.That(stepDistance, Is.GreaterThan(0f));
            foreach (Vector2 direction in new[] { new Vector2(0.03f, 0f), Vector2.right, new Vector2(0.2f, 0.2f) })
            {
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
                run.SetTouchMove(direction);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
                Assert.That(Vector3.Distance(before.Position, after.Position), Is.EqualTo(stepDistance).Within(0.0001f), direction.ToString());
            }
            foreach (Vector2 direction in new[] { Vector2.zero, new Vector2(float.NaN, 0f), new Vector2(0f, float.PositiveInfinity) })
            {
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
                run.SetTouchMove(direction);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
                Assert.That(after.Position, Is.EqualTo(before.Position), "零输入和非法方向必须停止。");
            }
        });

        /// <summary>
        /// 真实 HUD 射线首先命中圆形摇杆，且框架只拥有一个事件系统。
        /// </summary>
        /// <returns>等待真实场景和 Canvas 几何更新的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Joystick_RaycastHitsDiscWithOneEventSystem() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
            BattleHudWindow hud = GameEntry.Get<IUIModule>().OpenedWindows.OfType<BattleHudWindow>().Single();
            ExpeditionJoystick joystick = hud.RootTransform.GetComponentInChildren<ExpeditionJoystick>(false);
            Assert.That(joystick, Is.Not.Null);
            Canvas.ForceUpdateCanvases();
            Assert.That(UnityEngine.Object.FindObjectsOfType<EventSystem>().Length, Is.EqualTo(1));
            var rect = (RectTransform)joystick.transform;
            var pointer = Pointer(37, rect, Vector2.zero);
            var hits = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointer, hits);
            Assert.That(hits.Count, Is.GreaterThan(0));
            Assert.That(hits[0].gameObject, Is.EqualTo(joystick.gameObject),
                "摇杆中心应先命中 MoveStick，不能被标签、摇柄或其他窗口遮挡。");
        });

        /// <summary>
        /// 多种竖横安全区尺寸下，战斗控件均保持圆形、可见且互不遮挡。
        /// </summary>
        [Test]
        public void BattleHudLayout_FitsRepresentativeSafeAreaRects()
        {
            var host = new GameObject("BattleHudLayoutGeometry", typeof(RectTransform));
            try
            {
                var area = (RectTransform)host.transform;
                RectTransform header = GeometryChild(area, "Status");
                RectTransform stick = GeometryChild(area, "MoveStick");
                RectTransform actions = GeometryChild(area, "ControlActions");
                actions.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>().spacing = 8f;
                foreach (string name in new[] { "Inventory", "MenuActions", "ResumeChoice" })
                {
                    RectTransform row = GeometryChild(actions, name);
                    UnityEngine.UI.LayoutElement height = row.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    height.minHeight = height.preferredHeight = 90f;
                }
                BattleHudLayout layout = host.AddComponent<BattleHudLayout>();
                layout.Configure(header, stick, actions);
                var sizes = new[]
                {
                    new Vector2(1080f, 1920f), new Vector2(720f, 1280f),
                    new Vector2(1080f, 2400f), new Vector2(1920f, 1080f),
                    new Vector2(960f, 2220f), new Vector2(1780f, 920f), new Vector2(600f, 800f), new Vector2(640f, 480f)
                };
                foreach (Vector2 size in sizes)
                {
                    area.sizeDelta = size;
                    area.ForceUpdateRectTransforms();
                    layout.Refresh();
                    Canvas.ForceUpdateCanvases();
                    Rect headerRect = GeometryRect(area, header);
                    Rect stickRect = GeometryRect(area, stick);
                    Rect actionRect = GeometryRect(area, actions);
                    Assert.That(stickRect.width, Is.EqualTo(stickRect.height).Within(0.01f), size.ToString());
                    Assert.That(headerRect.Overlaps(actionRect), Is.False, size.ToString());
                    Assert.That(headerRect.Overlaps(stickRect), Is.False, size.ToString());
                    Assert.That(stickRect.Overlaps(actionRect), Is.False, size.ToString());
                    foreach (Rect child in new[] { headerRect, stickRect, actionRect })
                    {
                        Assert.That(child.xMin, Is.GreaterThanOrEqualTo(area.rect.xMin - 0.01f), size.ToString());
                        Assert.That(child.xMax, Is.LessThanOrEqualTo(area.rect.xMax + 0.01f), size.ToString());
                        Assert.That(child.yMin, Is.GreaterThanOrEqualTo(area.rect.yMin - 0.01f), size.ToString());
                        Assert.That(child.yMax, Is.LessThanOrEqualTo(area.rect.yMax + 0.01f), size.ToString());
                    }
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        /// <summary>
        /// 在独立安全区容器下创建测试控件。
        /// </summary>
        /// <param name="area">安全区矩形。</param>
        /// <param name="name">控件名称。</param>
        /// <returns>已作为子节点挂载的矩形。</returns>
        private static RectTransform GeometryChild(RectTransform area, string name)
        {
            var child = new GameObject(name, typeof(RectTransform));
            child.transform.SetParent(area, false);
            return (RectTransform)child.transform;
        }

        /// <summary>
        /// 将控件世界角点转换为安全区局部矩形。
        /// </summary>
        /// <param name="area">安全区矩形。</param>
        /// <param name="child">待检查控件。</param>
        /// <returns>相对安全区的控件边界。</returns>
        private static Rect GeometryRect(RectTransform area, RectTransform child)
        {
            var corners = new Vector3[4];
            child.GetWorldCorners(corners);
            Vector3 lower = area.InverseTransformPoint(corners[0]);
            Vector3 upper = area.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(lower.x, lower.y, upper.x, upper.y);
        }

        /// <summary>
        /// 创建带稳定身份和屏幕位置的真实 uGUI 指针事件。
        /// </summary>
        /// <param name="id">触点编号。</param>
        /// <param name="rect">摇杆区域。</param>
        /// <param name="offset">相对区域中心的局部偏移。</param>
        /// <returns>可传给摇杆事件接口的指针数据。</returns>
        private static PointerEventData Pointer(int id, RectTransform rect, Vector2 offset) =>
            new PointerEventData(EventSystem.current) { pointerId = id, position = ScreenPoint(rect, offset) };

        /// <summary>
        /// 把摇杆局部偏移转换为覆盖层 Canvas 的屏幕坐标。
        /// </summary>
        /// <param name="rect">摇杆区域。</param>
        /// <param name="offset">相对区域中心的局部偏移。</param>
        /// <returns>事件系统使用的屏幕位置。</returns>
        private static Vector2 ScreenPoint(RectTransform rect, Vector2 offset) =>
            RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center + offset));
    }
}
