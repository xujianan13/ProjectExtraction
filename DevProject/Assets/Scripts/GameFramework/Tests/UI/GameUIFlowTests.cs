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
}
