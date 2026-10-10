using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameFramework.Tests.UI
{
    /// <summary>
    /// 验收游戏 UI 的模态输入、等待层与窗口遮罩恢复流程。
    /// </summary>
    public sealed class GameUIFlowTests
    {
        private UIModule _ui;
        private GameWindowManager _windows;
        private GameObject _testEventSystem;

        /// <summary>
        /// 关闭模块与窗口，并等待运行时对象销毁。
        /// </summary>
        /// <returns>测试清理协程。</returns>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            _windows?.Shutdown();
            _windows = null;
            _ui?.OnShutdown();
            _ui = null;
            if (_testEventSystem != null) Object.Destroy(_testEventSystem);
            _testEventSystem = null;
            yield return null;
        }

        /// <summary>
        /// 确认框的黑色全屏遮罩拦截射线，点击确认后返回 true。
        /// </summary>
        /// <returns>弹窗交互测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator ConfirmDialog_BlocksBackgroundAndReturnsConfirmed()
        {
            _ui = new UIModule();
            _ui.OnInit();
            var confirmation = _ui.ConfirmAsync("标题", "正文").AsTask();
            yield return null;

            GameObject root = GameObject.Find("[GameUI]");
            Assert.IsNotNull(root);
            Transform dialog = root.transform.Find("Dialog Layer/Modal Dialog");
            Assert.IsNotNull(dialog);
            Image mask = dialog.GetComponent<Image>();
            Assert.IsNotNull(mask);
            Assert.IsTrue(mask.raycastTarget);
            Assert.AreEqual(0f, mask.color.r, 0.001f);
            Assert.AreEqual(0f, mask.color.g, 0.001f);
            Assert.AreEqual(0f, mask.color.b, 0.001f);
            Assert.Greater(mask.color.a, 0f);
            Assert.AreSame(dialog.gameObject, RaycastFirst(root.GetComponent<GraphicRaycaster>(), new Vector2(5f, 5f)));

            Button button = dialog.Find("Panel/Confirm").GetComponent<Button>();
            button.onClick.Invoke();
            yield return new WaitUntil(() => confirmation.IsCompleted);
            Assert.IsTrue(confirmation.Result);
        }

        /// <summary>
        /// 多个等待句柄分别持有全屏输入阻断层，全部释放后才关闭。
        /// </summary>
        /// <returns>等待层引用计数测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator BusyHandles_KeepInputBlockedUntilLastRelease()
        {
            _ui = new UIModule();
            _ui.OnInit();
            IUIBusyHandle first = _ui.BeginBusy("加载一");
            IUIBusyHandle second = _ui.BeginBusy("加载二");
            yield return null;

            GameObject root = GameObject.Find("[GameUI]");
            Assert.IsNotNull(root);
            Transform busy = root.transform.Find("Busy Layer");
            Assert.IsNotNull(busy);
            Assert.IsTrue(busy.gameObject.activeSelf);
            Assert.AreEqual("加载二", busy.Find("Message").GetComponent<Text>().text);
            Assert.IsTrue(busy.GetComponent<Image>().raycastTarget);
            Assert.AreSame(busy.gameObject, RaycastFirst(root.GetComponent<GraphicRaycaster>(), new Vector2(5f, 5f)));

            first.Dispose();
            Assert.IsTrue(busy.gameObject.activeSelf);
            second.SetMessage("仍在加载");
            Assert.AreEqual("仍在加载", busy.Find("Message").GetComponent<Text>().text);
            second.Dispose();
            Assert.IsFalse(busy.gameObject.activeSelf);
        }

        /// <summary>
        /// 较高层模态窗口关闭后，遮罩回到较低层窗口且继续拦截输入。
        /// </summary>
        /// <returns>多层窗口测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator MaskedWindows_CloseTop_RestoresLowerMask()
        {
            if (EventSystem.current == null)
                _testEventSystem = new GameObject("[TestEventSystem]", typeof(EventSystem));
            Color lowerColor = new(0f, 0f, 0f, 0.4f);
            Color upperColor = new(0f, 0f, 0f, 0.7f);
            var catalog = new GameWindowConfigCatalog(new[]
            {
                CreateWindowConfig(1, nameof(LowerWindow), 10, lowerColor),
                CreateWindowConfig(2, nameof(UpperWindow), 20, upperColor)
            });
            var registry = new GameWindowRegistry();
            registry.Register<LowerWindow>(1);
            registry.Register<UpperWindow>(2);
            _windows = new GameWindowManager(catalog, registry);
            _windows.Initialize();

            var lowerOpen = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => lowerOpen.IsCompleted);
            Assert.IsFalse(lowerOpen.IsFaulted);
            var upperOpen = _windows.OpenAsync(2).AsTask();
            yield return new WaitUntil(() => upperOpen.IsCompleted);
            Assert.IsFalse(upperOpen.IsFaulted);
            yield return null;

            GameObject root = GameObject.Find("[GameWindowRoot]");
            Assert.IsNotNull(root);
            Transform mask = root.transform.Find("Layer_20/[WindowMask]");
            Assert.IsNotNull(mask);
            Assert.AreEqual(upperColor, mask.GetComponent<Image>().color);
            Assert.AreEqual(2, _windows.MaskWindow.Id);
            Assert.AreSame(mask.gameObject, RaycastFirst(root.GetComponent<GraphicRaycaster>(), new Vector2(5f, 5f)));

            var closeTop = _windows.CloseTopAsync().AsTask();
            yield return new WaitUntil(() => closeTop.IsCompleted);
            Assert.IsTrue(closeTop.Result);
            Assert.AreEqual(1, _windows.MaskWindow.Id);
            mask = root.transform.Find("Layer_10/[WindowMask]");
            Assert.IsNotNull(mask);
            Assert.AreEqual(lowerColor, mask.GetComponent<Image>().color);
            Assert.AreSame(mask.gameObject, RaycastFirst(root.GetComponent<GraphicRaycaster>(), new Vector2(5f, 5f)));
        }

        /// <summary>
        /// 仅有遮罩的窗口连续刷新时保持按钮可点击，且不重复修改同级顺序。
        /// </summary>
        /// <returns>单窗口遮罩稳定性回归测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator MaskedWindow_Update_KeepsMaskBehindClickableButton()
        {
            InitializeOverlayWindows(false);
            var open = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => open.IsCompleted);
            Assert.IsFalse(open.IsFaulted);
            yield return AssertStableOverlayFrames(open.Result, null);
        }

        /// <summary>
        /// 同一窗口同时持有遮罩和外点击层时，连续刷新保持输入与层级稳定。
        /// </summary>
        /// <returns>同窗口双交互层回归测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator MaskedOutsideWindow_Update_KeepsBothOverlaysBehindClickableButton()
        {
            InitializeOverlayWindows(true);
            var open = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => open.IsCompleted);
            Assert.IsFalse(open.IsFaulted);
            yield return AssertStableOverlayFrames(open.Result, open.Result);
        }

        /// <summary>
        /// 同层聚焦及跨层打开关闭切换后，遮罩与外点击层恢复各自所有者并保持稳定。
        /// </summary>
        /// <returns>多窗口交互层切换回归测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator OverlayWindows_FocusAndClose_RestoreSameAndDifferentLayerOwners()
        {
            InitializeOverlayWindows(true);
            var firstOpen = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => firstOpen.IsCompleted);
            Assert.IsFalse(firstOpen.IsFaulted);
            GameWindow first = firstOpen.Result;
            var secondOpen = _windows.OpenAsync(2).AsTask();
            yield return new WaitUntil(() => secondOpen.IsCompleted);
            Assert.IsFalse(secondOpen.IsFaulted);
            GameWindow second = secondOpen.Result;
            Assert.AreSame(first.RootTransform.parent, second.RootTransform.parent);
            yield return AssertStableOverlayFrames(second, null);

            var upperOpen = _windows.OpenAsync(3).AsTask();
            yield return new WaitUntil(() => upperOpen.IsCompleted);
            Assert.IsFalse(upperOpen.IsFaulted);
            Assert.AreNotSame(second.RootTransform.parent, upperOpen.Result.RootTransform.parent);
            yield return AssertStableOverlayFrames(second, upperOpen.Result);

            var closeUpper = _windows.CloseTopAsync().AsTask();
            yield return new WaitUntil(() => closeUpper.IsCompleted);
            Assert.IsTrue(closeUpper.Result);
            yield return AssertStableOverlayFrames(second, null);

            var focusFirst = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => focusFirst.IsCompleted);
            Assert.IsFalse(focusFirst.IsFaulted);
            Assert.AreSame(first, focusFirst.Result);
            Assert.Greater(first.RootTransform.GetSiblingIndex(), second.RootTransform.GetSiblingIndex());
            yield return AssertStableOverlayFrames(first, first);

            var closeFirst = _windows.CloseTopAsync().AsTask();
            yield return new WaitUntil(() => closeFirst.IsCompleted);
            Assert.IsTrue(closeFirst.Result);
            yield return AssertStableOverlayFrames(second, null);

            var closeLast = _windows.CloseTopAsync().AsTask();
            yield return new WaitUntil(() => closeLast.IsCompleted);
            Assert.IsTrue(closeLast.Result);
            yield return null;
            GameObject root = GameObject.Find("[GameWindowRoot]");
            Transform mask = root.transform.Find("Layer_10/[WindowMask]");
            Transform outside = root.transform.Find("Layer_10/[WindowOutsideClick]");
            Assert.IsNotNull(mask);
            Assert.IsNotNull(outside);
            Assert.IsFalse(mask.gameObject.activeSelf);
            Assert.IsFalse(outside.gameObject.activeSelf);
            Assert.IsNull(_windows.MaskWindow);
            Assert.IsNull(_windows.OutsideClickWindow);
            Assert.AreSame(root, RaycastFirst(root.GetComponent<UnityEngine.UI.GraphicRaycaster>(), new Vector2(5f, 5f)));
        }

        /// <summary>
        /// 目标窗口组件绑定失败时保留原有的关联窗口。
        /// </summary>
        /// <returns>初始化失败回归测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator FailedWindowInitialization_DoesNotCloseAssociatedWindow()
        {
            var catalog = new GameWindowConfigCatalog(new[]
            {
                new GameWindowConfig(1, nameof(OriginalWindow), nameof(OriginalWindow), null, 10, GameWindowScope.Global,
                    GameWindowCacheMode.Resident, 0f, false, false, Color.clear),
                new GameWindowConfig(2, nameof(FailingWindow), nameof(FailingWindow), null, 20, GameWindowScope.Global,
                    GameWindowCacheMode.Immediate, 0f, false, false, Color.clear, closeOnOpenIds: new[] { 1 })
            });
            var registry = new GameWindowRegistry();
            registry.Register<OriginalWindow>(1);
            registry.Register<FailingWindow>(2);
            _windows = new GameWindowManager(catalog, registry);
            _windows.Initialize();

            var originalOpen = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => originalOpen.IsCompleted);
            Assert.IsFalse(originalOpen.IsFaulted);

            var failedOpen = _windows.OpenAsync(2).AsTask();
            yield return new WaitUntil(() => failedOpen.IsCompleted);
            Assert.IsTrue(failedOpen.IsFaulted);
            Assert.AreEqual(GameWindowState.Opened, originalOpen.Result.State);
            Assert.AreSame(originalOpen.Result, _windows.TopWindow);
            Assert.IsFalse(_windows.TryGet(2, out _));
        }

        /// <summary>
        /// 前置窗口初始化失败时保留目标关联的原有窗口。
        /// </summary>
        /// <returns>前置窗口失败回归测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator FailedOpenBefore_DoesNotCloseAssociatedWindow()
        {
            var catalog = new GameWindowConfigCatalog(new[]
            {
                new GameWindowConfig(1, nameof(OriginalWindow), nameof(OriginalWindow), null, 10, GameWindowScope.Global,
                    GameWindowCacheMode.Resident, 0f, false, false, Color.clear),
                new GameWindowConfig(2, nameof(DependentWindow), nameof(DependentWindow), null, 20, GameWindowScope.Global,
                    GameWindowCacheMode.Immediate, 0f, false, false, Color.clear, openBeforeIds: new[] { 3 },
                    closeOnOpenIds: new[] { 1 }),
                new GameWindowConfig(3, nameof(FailingWindow), nameof(FailingWindow), null, 15, GameWindowScope.Global,
                    GameWindowCacheMode.Immediate, 0f, false, false, Color.clear)
            });
            var registry = new GameWindowRegistry();
            registry.Register<OriginalWindow>(1);
            registry.Register<DependentWindow>(2);
            registry.Register<FailingWindow>(3);
            _windows = new GameWindowManager(catalog, registry);
            _windows.Initialize();

            var originalOpen = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => originalOpen.IsCompleted);
            Assert.IsFalse(originalOpen.IsFaulted);

            var failedOpen = _windows.OpenAsync(2).AsTask();
            yield return new WaitUntil(() => failedOpen.IsCompleted);
            Assert.IsTrue(failedOpen.IsFaulted);
            Assert.AreEqual(GameWindowState.Opened, originalOpen.Result.State);
            Assert.AreSame(originalOpen.Result, _windows.TopWindow);
            Assert.IsFalse(_windows.TryGet(2, out _));
        }

        /// <summary>
        /// 目标初始化期间取消时保留原有的关联窗口。
        /// </summary>
        /// <returns>初始化取消回归测试协程。</returns>
        [UnityTest]
        [Timeout(10000)]
        public IEnumerator CancelDuringWindowInitialization_DoesNotCloseAssociatedWindow()
        {
            var catalog = new GameWindowConfigCatalog(new[]
            {
                new GameWindowConfig(1, nameof(OriginalWindow), nameof(OriginalWindow), null, 10, GameWindowScope.Global,
                    GameWindowCacheMode.Resident, 0f, false, false, Color.clear),
                new GameWindowConfig(2, nameof(CancelingWindow), nameof(CancelingWindow), null, 20, GameWindowScope.Global,
                    GameWindowCacheMode.Immediate, 0f, false, false, Color.clear, closeOnOpenIds: new[] { 1 })
            });
            var registry = new GameWindowRegistry();
            registry.Register<OriginalWindow>(1);
            registry.Register<CancelingWindow>(2);
            _windows = new GameWindowManager(catalog, registry);
            _windows.Initialize();

            var originalOpen = _windows.OpenAsync(1).AsTask();
            yield return new WaitUntil(() => originalOpen.IsCompleted);
            Assert.IsFalse(originalOpen.IsFaulted);

            var cancellation = new CancellationTokenSource();
            CancelingWindow.Cancellation = cancellation;
            var canceledOpen = _windows.OpenAsync(2, ct: cancellation.Token).AsTask();
            yield return new WaitUntil(() => canceledOpen.IsCompleted);
            bool cancellationRequested = cancellation.IsCancellationRequested;
            CancelingWindow.Cancellation = null;
            cancellation.Dispose();

            Assert.IsTrue(cancellationRequested, $"初始化未触发取消；任务异常：{canceledOpen.Exception}");
            Assert.IsTrue(canceledOpen.IsCanceled || canceledOpen.Exception?.GetBaseException() is System.OperationCanceledException,
                $"打开请求未因取消而结束；任务状态：{canceledOpen.Status}；任务异常：{canceledOpen.Exception}");
            Assert.AreEqual(GameWindowState.Opened, originalOpen.Result.State);
            Assert.AreSame(originalOpen.Result, _windows.TopWindow);
            Assert.IsFalse(_windows.TryGet(2, out _));
        }

        /// <summary>
        /// 创建无需 Prefab 的模态窗口配置。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="className">窗口逻辑类名。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="maskColor">遮罩颜色。</param>
        /// <returns>窗口配置。</returns>
        private static GameWindowConfig CreateWindowConfig(int id, string className, int layer, Color maskColor)
        {
            return new GameWindowConfig(id, className, className, null, layer, GameWindowScope.Global,
                GameWindowCacheMode.Immediate, 0f, false, true, maskColor);
        }

        /// <summary>
        /// 初始化带有按钮的同层与跨层测试窗口。
        /// </summary>
        /// <param name="closeFirstOnOutsideClick">第一个窗口是否接收外点击关闭。</param>
        private void InitializeOverlayWindows(bool closeFirstOnOutsideClick)
        {
            if (EventSystem.current == null)
                _testEventSystem = new GameObject("[TestEventSystem]", typeof(EventSystem));
            var catalog = new GameWindowConfigCatalog(new[]
            {
                CreateOverlayConfig(1, nameof(MaskedButtonWindow), 10, true, closeFirstOnOutsideClick),
                CreateOverlayConfig(2, nameof(SameLayerButtonWindow), 10, true, false),
                CreateOverlayConfig(3, nameof(UpperOutsideButtonWindow), 20, false, true)
            });
            var registry = new GameWindowRegistry();
            registry.Register<MaskedButtonWindow>(1);
            registry.Register<SameLayerButtonWindow>(2);
            registry.Register<UpperOutsideButtonWindow>(3);
            _windows = new GameWindowManager(catalog, registry);
            _windows.Initialize();
            GameObject.Find("[GameWindowRoot]").AddComponent<UnityEngine.UI.Image>().color = Color.blue;
        }

        /// <summary>
        /// 创建可独立配置遮罩与外点击行为的测试窗口配置。
        /// </summary>
        /// <param name="id">窗口 ID。</param>
        /// <param name="className">窗口逻辑类名。</param>
        /// <param name="layer">窗口层级。</param>
        /// <param name="useMask">是否启用遮罩。</param>
        /// <param name="closeOnOutsideClick">是否接收外点击关闭。</param>
        /// <returns>窗口配置。</returns>
        private static GameWindowConfig CreateOverlayConfig(int id, string className, int layer, bool useMask, bool closeOnOutsideClick)
        {
            return new GameWindowConfig(id, className, className, null, layer, GameWindowScope.Global,
                GameWindowCacheMode.Immediate, 0f, closeOnOutsideClick, useMask, new Color(0f, 0f, 0f, 0.5f));
        }

        /// <summary>
        /// 连续更新窗口管理器，验证交互层顺序、实际按钮点击及背景阻断，同时检查层级没有重复变化。
        /// </summary>
        /// <param name="maskOwner">应持有遮罩的窗口。</param>
        /// <param name="outsideOwner">应持有外点击层的窗口；未启用时为 null。</param>
        /// <returns>连续帧验证协程。</returns>
        private IEnumerator AssertStableOverlayFrames(GameWindow maskOwner, GameWindow outsideOwner)
        {
            yield return null;
            GameObject root = GameObject.Find("[GameWindowRoot]");
            UnityEngine.UI.GraphicRaycaster raycaster = root.GetComponent<UnityEngine.UI.GraphicRaycaster>();
            Transform mask = maskOwner.RootTransform.parent.Find("[WindowMask]");
            Transform outside = outsideOwner != null ? outsideOwner.RootTransform.parent.Find("[WindowOutsideClick]") : null;
            Assert.IsNotNull(mask);
            if (outsideOwner != null) Assert.IsNotNull(outside);
            var probes = new List<WindowLayerChangeProbe>();
            var changes = new List<int>();
            foreach (Transform layer in root.transform)
            {
                if (!layer.name.StartsWith("Layer_")) continue;
                WindowLayerChangeProbe probe = layer.GetComponent<WindowLayerChangeProbe>();
                if (probe == null) probe = layer.gameObject.AddComponent<WindowLayerChangeProbe>();
                probes.Add(probe);
                changes.Add(probe.ChangeCount);
            }

            var clickableWindow = (ButtonWindow)(outsideOwner ?? maskOwner);
            for (int frame = 0; frame < 6; frame++)
            {
                _windows.Update();
                Assert.AreSame(maskOwner, _windows.MaskWindow);
                Assert.AreSame(outsideOwner, _windows.OutsideClickWindow);
                Assert.IsTrue(mask.gameObject.activeSelf);
                Assert.AreSame(maskOwner.RootTransform.parent, mask.parent);
                Assert.Less(mask.GetSiblingIndex(), maskOwner.RootTransform.GetSiblingIndex(), $"第 {frame} 帧遮罩覆盖了窗口。");
                if (outsideOwner != null)
                {
                    Assert.IsTrue(outside.gameObject.activeSelf);
                    Assert.AreSame(outsideOwner.RootTransform.parent, outside.parent);
                    Assert.Less(outside.GetSiblingIndex(), outsideOwner.RootTransform.GetSiblingIndex());
                    if (outsideOwner == maskOwner) Assert.Less(mask.GetSiblingIndex(), outside.GetSiblingIndex());
                }
                Vector2 buttonPosition = RectTransformUtility.WorldToScreenPoint(null, clickableWindow.Button.transform.position);
                GameObject hit = RaycastFirst(raycaster, buttonPosition);
                Assert.AreSame(clickableWindow.Button.gameObject, hit, $"第 {frame} 帧窗口按钮被交互层挡住。");
                int clicks = clickableWindow.ClickCount;
                ExecuteEvents.Execute(hit, new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left },
                    ExecuteEvents.pointerClickHandler);
                Assert.AreEqual(clicks + 1, clickableWindow.ClickCount);
                Assert.AreSame((outside != null ? outside : mask).gameObject, RaycastFirst(raycaster, new Vector2(5f, 5f)));
                for (int index = 0; index < probes.Count; index++)
                    Assert.AreEqual(changes[index], probes[index].ChangeCount, $"第 {frame} 帧 {probes[index].name} 重复改变了层级。");
                yield return null;
            }
        }

        /// <summary>
        /// 查找指定屏幕位置最上层的 UGUI 射线目标。
        /// </summary>
        /// <param name="raycaster">目标 Canvas 的射线检测组件。</param>
        /// <param name="position">屏幕像素坐标。</param>
        /// <returns>最上层射线目标；没有命中时返回 null。</returns>
        private static GameObject RaycastFirst(GraphicRaycaster raycaster, Vector2 position)
        {
            Canvas.ForceUpdateCanvases();
            var results = new List<RaycastResult>();
            raycaster.Raycast(new PointerEventData(EventSystem.current) { position = position }, results);
            return results.Count > 0 ? results[0].gameObject : null;
        }

        /// <summary>
        /// 在窗口中心提供真实 UGUI 按钮以验收射线与点击。
        /// </summary>
        private abstract class ButtonWindow : GameWindow
        {
            /// <summary>
            /// 窗口中心的测试按钮。
            /// </summary>
            internal UnityEngine.UI.Button Button { get; private set; }

            /// <summary>
            /// 已收到的按钮点击次数。
            /// </summary>
            internal int ClickCount { get; private set; }

            /// <summary>
            /// 创建中心按钮并登记点击计数。
            /// </summary>
            protected override void BindComponents()
            {
                var button = new GameObject("ActionButton", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.Button));
                var rect = button.GetComponent<RectTransform>();
                rect.SetParent(RootTransform, false);
                rect.anchorMin = new Vector2(0.4f, 0.4f);
                rect.anchorMax = new Vector2(0.6f, 0.6f);
                rect.offsetMin = Vector2.zero;
                rect.offsetMax = Vector2.zero;
                Button = button.GetComponent<UnityEngine.UI.Button>();
                Button.transition = UnityEngine.UI.Selectable.Transition.None;
                Button.onClick.AddListener(() => ClickCount++);
            }
        }

        /// <summary>
        /// 带有遮罩的按钮窗口。
        /// </summary>
        private sealed class MaskedButtonWindow : ButtonWindow
        {
            /// <summary>
            /// 创建遮罩按钮窗口。
            /// </summary>
            public MaskedButtonWindow()
            {
            }
        }

        /// <summary>
        /// 用于同层打开及聚焦恢复的按钮窗口。
        /// </summary>
        private sealed class SameLayerButtonWindow : ButtonWindow
        {
            /// <summary>
            /// 创建同层按钮窗口。
            /// </summary>
            public SameLayerButtonWindow()
            {
            }
        }

        /// <summary>
        /// 仅持有外点击层的高层按钮窗口。
        /// </summary>
        private sealed class UpperOutsideButtonWindow : ButtonWindow
        {
            /// <summary>
            /// 创建高层外点击窗口。
            /// </summary>
            public UpperOutsideButtonWindow()
            {
            }
        }

        /// <summary>
        /// 保持打开以检验关联关闭顺序的窗口。
        /// </summary>
        private sealed class OriginalWindow : GameWindow
        {
            /// <summary>
            /// 创建原有窗口。
            /// </summary>
            public OriginalWindow()
            {
            }
        }

        /// <summary>
        /// 用于检验前置窗口失败路径的窗口。
        /// </summary>
        private sealed class DependentWindow : GameWindow
        {
            /// <summary>
            /// 创建依赖其他窗口的窗口。
            /// </summary>
            public DependentWindow()
            {
            }
        }

        /// <summary>
        /// 在组件绑定阶段取消打开请求的窗口。
        /// </summary>
        private sealed class CancelingWindow : GameWindow
        {
            internal static CancellationTokenSource Cancellation;

            /// <summary>
            /// 创建用于取消回归测试的窗口。
            /// </summary>
            public CancelingWindow()
            {
            }

            /// <summary>
            /// 模拟初始化期间取消打开请求。
            /// </summary>
            protected override void BindComponents()
            {
                Cancellation?.Cancel();
            }
        }

        /// <summary>
        /// 在组件绑定阶段模拟初始化失败的窗口。
        /// </summary>
        private sealed class FailingWindow : GameWindow
        {
            /// <summary>
            /// 创建初始化失败的窗口。
            /// </summary>
            public FailingWindow()
            {
            }

            /// <summary>
            /// 模拟业务 Prefab 缺少必需组件。
            /// </summary>
            protected override void BindComponents()
            {
                FindRequired<Button>("MissingButton");
            }
        }

        /// <summary>
        /// 较低层的测试窗口。
        /// </summary>
        private sealed class LowerWindow : GameWindow
        {
            /// <summary>
            /// 创建较低层的测试窗口。
            /// </summary>
            public LowerWindow()
            {
            }
        }

        /// <summary>
        /// 较高层的测试窗口。
        /// </summary>
        private sealed class UpperWindow : GameWindow
        {
            /// <summary>
            /// 创建较高层的测试窗口。
            /// </summary>
            public UpperWindow()
            {
            }
        }
    }

    /// <summary>
    /// 记录窗口层子节点变化，检测稳定帧中的无效重排。
    /// </summary>
    internal sealed class WindowLayerChangeProbe : MonoBehaviour
    {
        /// <summary>
        /// 收到的层级变化通知次数。
        /// </summary>
        public int ChangeCount { get; private set; }

        /// <summary>
        /// 记录添加、删除及重排子节点的通知。
        /// </summary>
        private void OnTransformChildrenChanged()
        {
            ChangeCount++;
        }
    }
}
