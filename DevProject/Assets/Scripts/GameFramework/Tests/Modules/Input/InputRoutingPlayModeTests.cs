#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace GameFramework.Tests.Modules.Input
{
    /// <summary>
    /// 使用模拟设备验证系统返回、玩法焦点与 UI 指针捕获。
    /// </summary>
    public sealed class InputRoutingPlayModeTests : InputTestFixture
    {
        private InputModule _module;
        private Keyboard _keyboard;
        private Mouse _mouse;
        private GameObject _eventSystem;
        private GameObject _canvas;
        private InputActionReference _pointReference;
        private InputActionReference _clickReference;

        /// <summary>
        /// 初始化隔离的输入设备和框架输入模块。
        /// </summary>
        public override void Setup()
        {
            base.Setup();
            _keyboard = InputSystem.AddDevice<Keyboard>();
            _mouse = InputSystem.AddDevice<Mouse>();
            _module = new InputModule();
            _module.OnInit();
            Assert.IsNotNull(_module.FrameworkActions, "固定 System/UI 动作资产必须可从 Resources 加载。");
        }

        /// <summary>
        /// 卸载输入模块并恢复测试前的设备状态。
        /// </summary>
        public override void TearDown()
        {
            _module?.OnShutdown();
            _module = null;
            if (_canvas != null) UnityEngine.Object.DestroyImmediate(_canvas);
            if (_eventSystem != null) UnityEngine.Object.DestroyImmediate(_eventSystem);
            if (_pointReference != null) UnityEngine.Object.DestroyImmediate(_pointReference);
            if (_clickReference != null) UnityEngine.Object.DestroyImmediate(_clickReference);
            base.TearDown();
        }

        /// <summary>
        /// 高优先级返回处理器消费按键后，低优先级处理器和玩法订阅不应收到事件。
        /// </summary>
        [Test]
        public void Back_ConsumedByHighestPriority_ThenFallsThroughAfterRelease()
        {
            var calls = new List<string>();
            int subscriberCalls = 0;
            using IDisposable subscription = _module.Subscribe("System/Back", _ => subscriberCalls++);
            using IDisposable lower = _module.RegisterBackHandler(() => { calls.Add("lower"); return false; }, 10);
            IDisposable higher = _module.RegisterBackHandler(() => { calls.Add("higher"); return true; }, 20);

            Press(_keyboard.escapeKey);
            _module.OnUpdate(0f);
            CollectionAssert.AreEqual(new[] { "higher" }, calls);
            Assert.IsFalse(_module.GetButton("System/Back").PressedThisFrame);
            Assert.AreEqual(0, subscriberCalls);

            Release(_keyboard.escapeKey);
            _module.OnUpdate(0f);
            higher.Dispose();
            Press(_keyboard.escapeKey);
            _module.OnUpdate(0f);
            CollectionAssert.AreEqual(new[] { "higher", "lower" }, calls);
            Assert.IsTrue(_module.GetButton("System/Back").PressedThisFrame);
            Assert.Greater(subscriberCalls, 0);
        }

        /// <summary>
        /// 玩法焦点释放后，正在按住的移动键不能继续向旧玩法提供移动值。
        /// </summary>
        [Test]
        public void ReleasingGameplayContext_ClearsHeldMovementUntilNewPress()
        {
            AttachGameplayActions();
            IDisposable context = _module.PushContext("Gameplay");
            Press(_keyboard.wKey);
            _module.OnUpdate(0f);
            Assert.Greater(_module.GetVector2("Gameplay/Move").y, 0.9f);

            context.Dispose();
            Assert.AreEqual(Vector2.zero, _module.GetVector2("Gameplay/Move"));
            _module.OnUpdate(0f);
            Assert.AreEqual(Vector2.zero, _module.GetVector2("Gameplay/Move"));

            Release(_keyboard.wKey);
            _module.OnUpdate(0f);
            using IDisposable nextContext = _module.PushContext("Gameplay");
            Press(_keyboard.wKey);
            _module.OnUpdate(0f);
            Assert.Greater(_module.GetVector2("Gameplay/Move").y, 0.9f);
        }

        /// <summary>
        /// 屏幕虚拟手柄输入应归类为触屏，实体手柄提示不会误出现。
        /// </summary>
        [Test]
        public void OnScreenGamepad_IsReportedAsTouch()
        {
            AttachGameplayActions();
            using IDisposable context = _module.PushContext("Gameplay");
            Gamepad gamepad = InputSystem.AddDevice<Gamepad>();
            InputSystem.AddDeviceUsage(gamepad, "OnScreen");
            Press(gamepad.buttonSouth);
            _module.OnUpdate(0f);
            Assert.AreEqual(InputDeviceKind.Touch, _module.ActiveDevice);
        }
        /// <summary>
        /// 鼠标在 UGUI 控件上按下时，不得向玩法发出攻击或世界指针事件。
        /// </summary>
        /// <returns>等待 UI 布局和模拟输入的测试协程。</returns>
        [UnityTest]
        public IEnumerator ClickingUi_DoesNotTriggerGameplayAttackOrWorldPointer()
        {
            AttachGameplayActions();
            using IDisposable context = _module.PushContext("Gameplay");
            _eventSystem = new GameObject("InputRoutingTestEventSystem", typeof(EventSystem));
            _canvas = new GameObject("InputRoutingTestCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            Canvas canvas = _canvas.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32767;
            GameObject control = new GameObject("ClickableUi", typeof(RectTransform), typeof(Image));
            control.transform.SetParent(_canvas.transform, false);
            RectTransform rect = control.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = new Vector2(240f, 240f);
            control.GetComponent<Image>().raycastTarget = true;
            EventSystem eventSystem = _eventSystem.GetComponent<EventSystem>();
            EventSystem.current = eventSystem;
            yield return null;
            Canvas.ForceUpdateCanvases();
            Vector2 position = RectTransformUtility.WorldToScreenPoint(null, rect.TransformPoint(rect.rect.center));
            var hits = new List<RaycastResult>();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, hits);
            Assert.IsTrue(hits.Exists(hit => hit.gameObject == control && hit.module is GraphicRaycaster),
                "测试控件必须实际参与 EventSystem.RaycastAll，才能验证 UI 输入捕获。");

            int attackEvents = 0;
            int worldEvents = 0;
            using IDisposable attack = _module.Subscribe("Gameplay/Attack", _ => attackEvents++);
            _module.PointerEvent += _ => worldEvents++;
            Assert.AreSame(_mouse, Mouse.current);
            Set(_mouse.position, position);
            Press(_mouse.leftButton);
            InputSystem.Update();
            Assert.IsTrue(_mouse.leftButton.isPressed, "测试按下必须已进入 Input System 状态。");
            Assert.AreEqual(position, _mouse.position.ReadValue(), "测试鼠标位置必须落在 UI 控件上。");
            _module.OnUpdate(0f);

            Assert.IsFalse(_module.GetButton("Gameplay/Attack").PressedThisFrame);
            Assert.AreEqual(0, attackEvents);
            Assert.AreEqual(0, worldEvents);
            Assert.IsEmpty(_module.Pointers, "UI 捕获的鼠标不得出现在公开玩法指针列表中。");
            Assert.IsFalse(_module.TryGetPointer(0, out _), "UI 按下不能通过世界指针查询泄漏。");
            GameObject cameraObject = new GameObject("InputRoutingTestCamera", typeof(Camera));
            Camera camera = cameraObject.GetComponent<Camera>();
            Assert.IsFalse(_module.TryGetWorldRay(camera, 0, out _));
            Vector2 outside = new Vector2(10f, 10f);
            Set(_mouse.position, outside);
            InputSystem.Update();
            Assert.AreEqual(outside, _mouse.position.ReadValue());
            Assert.IsTrue(_mouse.leftButton.isPressed);
            _module.OnUpdate(0f);
            Assert.IsFalse(_module.TryGetPointer(0, out _), "UI 按下的归属应保持到释放。");
            Release(_mouse.leftButton);
            InputSystem.Update();
            Assert.IsFalse(_mouse.leftButton.isPressed);
            _module.OnUpdate(0f);
            _module.OnUpdate(0f);
            Assert.IsTrue(_module.TryGetPointer(0, out GamePointer hover));
            Assert.AreEqual(outside, hover.Position);
            Assert.IsTrue(_module.TryGetWorldRay(camera, 0, out _));
            UnityEngine.Object.DestroyImmediate(cameraObject);
        }

        /// <summary>
        /// 世界指针只在玩法上下文有效且未阻断时可查询。
        /// </summary>
        [Test]
        public void WorldPointer_RequiresGameplayContextAndNoBlock()
        {
            AttachGameplayActions();
            Set(_mouse.position, new Vector2(30f, 30f));
            _module.OnUpdate(0f);
            Assert.IsFalse(_module.TryGetPointer(0, out _));
            using IDisposable context = _module.PushContext("Gameplay");
            _module.OnUpdate(0f);
            Assert.IsTrue(_module.TryGetPointer(0, out _));
            using IDisposable block = _module.BlockGameplay("test");
            _module.OnUpdate(0f);
            Assert.IsFalse(_module.TryGetPointer(0, out _));
        }

        /// <summary>
        /// 双触指操作虚拟摇杆和按钮时，真实 UI 输入模块应分别派发事件，玩法查询不接收 UI 触点。
        /// </summary>
        /// <returns>等待画布布局完成的测试协程。</returns>
        [UnityTest]
        public IEnumerator TwoTouches_OnScreenStickAndButton_StayOwnedByUi()
        {
            AttachGameplayActions();
            using IDisposable context = _module.PushContext("Gameplay");
            Touchscreen touchscreen = InputSystem.AddDevice<Touchscreen>();
            _eventSystem = new GameObject("InputRoutingTouchEventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            EventSystem.current = _eventSystem.GetComponent<EventSystem>();
            InputSystemUIInputModule ui = _eventSystem.GetComponent<InputSystemUIInputModule>();
            ui.actionsAsset = _module.FrameworkActions;
            _pointReference = InputActionReference.Create(_module.FrameworkActions.FindAction("UI/Point"));
            ui.point = _pointReference;
            _clickReference = InputActionReference.Create(_module.FrameworkActions.FindAction("UI/Click"));
            ui.leftClick = _clickReference;
            _canvas = new GameObject("InputRoutingTouchCanvas", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster));
            _canvas.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var stick = new GameObject("VirtualStick", typeof(RectTransform), typeof(Image), typeof(OnScreenStick));
            var button = new GameObject("VirtualButton", typeof(RectTransform), typeof(Image), typeof(OnScreenButton));
            stick.transform.SetParent(_canvas.transform, false);
            button.transform.SetParent(_canvas.transform, false);
            RectTransform stickRect = stick.GetComponent<RectTransform>();
            RectTransform buttonRect = button.GetComponent<RectTransform>();
            stickRect.anchorMin = stickRect.anchorMax = new Vector2(0.25f, 0.5f);
            buttonRect.anchorMin = buttonRect.anchorMax = new Vector2(0.75f, 0.5f);
            stickRect.sizeDelta = buttonRect.sizeDelta = new Vector2(180f, 180f);
            stick.GetComponent<OnScreenStick>().controlPath = "<Gamepad>/leftStick";
            button.GetComponent<OnScreenButton>().controlPath = "<Gamepad>/buttonSouth";
            yield return null;
            Canvas.ForceUpdateCanvases();
            Vector2 stickPosition = RectTransformUtility.WorldToScreenPoint(null, stickRect.TransformPoint(stickRect.rect.center));
            Vector2 buttonPosition = RectTransformUtility.WorldToScreenPoint(null, buttonRect.TransformPoint(buttonRect.rect.center));
            BeginTouch(1, stickPosition, screen: touchscreen);
            BeginTouch(2, buttonPosition, screen: touchscreen);
            ui.Process();
            InputSystem.Update();
            _module.OnUpdate(0f);
            Assert.IsFalse(_module.TryGetPointer(2, out _), "按钮触点不能进入世界查询。");
            Assert.IsFalse(_module.TryGetPointer(3, out _), "摇杆触点不能进入世界查询。");
            Assert.IsTrue(Gamepad.current.buttonSouth.isPressed, "第二触指应驱动虚拟按钮。");
            MoveTouch(1, stickPosition + new Vector2(40f, 0f), screen: touchscreen);
            ui.Process();
            InputSystem.Update();
            Assert.Greater(Gamepad.current.leftStick.ReadValue().x, 0f, "第一触指应独立驱动虚拟摇杆。");
            EndTouch(1, stickPosition, screen: touchscreen);
            EndTouch(2, buttonPosition, screen: touchscreen);
            ui.Process();
            InputSystem.Update();
        }
        /// <summary>
        /// 装入真实的玩法动作资产，只替代需要 GamePackage 的资源租约步骤。
        /// </summary>
        private void AttachGameplayActions()
        {
            InputActionAsset source = AssetDatabase.LoadAssetAtPath<InputActionAsset>(
                "Assets/GameResources/Input/GameInputActions.inputactions");
            Assert.IsNotNull(source, "玩法动作资产必须可由 Unity 导入。");
            InputActionAsset instance = UnityEngine.Object.Instantiate(source);
            instance.Enable();
            FieldInfo field = typeof(InputModule).GetField("_gameplayActions", BindingFlags.Instance | BindingFlags.NonPublic);
            MethodInfo register = typeof(InputModule).GetMethod("RegisterActions", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            Assert.IsNotNull(register);
            field.SetValue(_module, instance);
            register.Invoke(_module, new object[] { instance });
        }
    }
}
#endif
