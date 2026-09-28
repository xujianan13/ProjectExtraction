using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework.Tests.UI
{
    /// <summary>
    /// GameFramework UI 基础组件测试。
    /// </summary>
    [TestFixture]
    public sealed class GameUIComponentTests
    {
        private readonly List<GameObject> _createdObjects = new(8);

        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_createdObjects[i]);

            _createdObjects.Clear();
        }

        /// <summary>
        /// 按钮仅在有效且可交互时触发点击回调。
        /// </summary>
        [Test]
        public void GameButton_TryInvokeClick_RespectsInteractableState()
        {
            GameButton button = CreateComponent<GameButton>("Button");
            int invokeCount = 0;
            button.onClick.AddListener(() => invokeCount++);

            Assert.IsTrue(button.TryInvokeClick());
            Assert.AreEqual(1, invokeCount);

            button.interactable = false;
            Assert.IsFalse(button.TryInvokeClick());
            Assert.AreEqual(1, invokeCount);
        }

        /// <summary>
        /// 按钮应仅为同一次有效的主指针按压触发按下和抬起事件。
        /// </summary>
        [Test]
        public void GameButton_PointerPress_InvokesDownAndUpEvents()
        {
            GameButton button = CreateComponent<GameButton>("Button");
            var eventSystemObject = new GameObject("EventSystem");
            _createdObjects.Add(eventSystemObject);
            EventSystem eventSystem = eventSystemObject.AddComponent<EventSystem>();
            var pointerEvent = new PointerEventData(eventSystem)
            {
                button = PointerEventData.InputButton.Left,
                pointerId = 7
            };
            int downCount = 0;
            int upCount = 0;
            button.OnPressDown += _ => downCount++;
            button.OnPressUp += _ => upCount++;

            button.OnPointerDown(pointerEvent);
            button.OnPointerUp(pointerEvent);

            Assert.AreEqual(1, downCount);
            Assert.AreEqual(1, upCount);
        }

        /// <summary>
        /// 输入框无通知赋值不应触发值变化回调。
        /// </summary>
        [Test]
        public void GameInputField_SetValue_CanSuppressNotification()
        {
            GameInputField inputField = CreateComponent<GameInputField>("InputField");
            int invokeCount = 0;
            inputField.onValueChanged.AddListener(_ => invokeCount++);

            inputField.SetValue("first", false);
            Assert.AreEqual("first", inputField.text);
            Assert.AreEqual(0, invokeCount);

            inputField.SetValue("second");
            Assert.AreEqual("second", inputField.text);
            Assert.AreEqual(1, invokeCount);
        }

        /// <summary>
        /// 滑动条归一化赋值应映射到配置范围且不触发回调。
        /// </summary>
        [Test]
        public void GameSlider_SetNormalizedValueWithoutNotify_MapsRange()
        {
            GameSlider slider = CreateComponent<GameSlider>("Slider");
            slider.SetRange(10f, 30f);
            int invokeCount = 0;
            slider.onValueChanged.AddListener(_ => invokeCount++);

            slider.SetNormalizedValueWithoutNotify(0.25f);

            Assert.AreEqual(15f, slider.value, 0.001f);
            Assert.AreEqual(0, invokeCount);
        }

        /// <summary>
        /// 进度条应同步填充比例和百分比文本。
        /// </summary>
        [Test]
        public void GameProgressBar_Value_RefreshesFillAndText()
        {
            GameProgressBar progressBar = CreateComponent<GameProgressBar>("ProgressBar");
            GameImage fillImage = CreateComponent<GameImage>("Fill");
            GameText valueText = CreateComponent<GameText>("ValueText");
            fillImage.type = Image.Type.Filled;

            progressBar.SetVisuals(fillImage, valueText);
            progressBar.SetRange(0f, 200f);
            progressBar.Value = 50f;

            Assert.AreEqual(0.25f, fillImage.fillAmount, 0.001f);
            Assert.AreEqual("25%", valueText.text);
        }

        /// <summary>
        /// 标签无通知赋值不应触发选中状态回调。
        /// </summary>
        [Test]
        public void GameTab_SetSelectedWithoutNotify_SuppressesCallback()
        {
            GameTab tab = CreateComponent<GameTab>("Tab");
            int invokeCount = 0;
            tab.onValueChanged.AddListener(_ => invokeCount++);

            tab.SetSelectedWithoutNotify(true);

            Assert.IsTrue(tab.IsSelected);
            Assert.AreEqual(0, invokeCount);
        }

        /// <summary>
        /// 文本组件应将 null 规范化为空字符串。
        /// </summary>
        [Test]
        public void GameText_SetTextOrEmpty_NormalizesNull()
        {
            GameText text = CreateComponent<GameText>("Text");

            text.SetTextOrEmpty(null);

            Assert.AreEqual(string.Empty, text.text);
        }

        private T CreateComponent<T>(string name) where T : Component
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            _createdObjects.Add(gameObject);
            return gameObject.AddComponent<T>();
        }
    }
}
