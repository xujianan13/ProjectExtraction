using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework.Tests
{
    /// <summary>
    /// 验证开发诊断面板的显示条件与资源释放。
    /// </summary>
    public sealed class RuntimeDiagnosticsPanelTests
    {
        private InputModule _input;
        private readonly List<EventSystem> _disabledEventSystems = new();
        private GameObject _eventSystemHost;

        /// <summary>
        /// 启动诊断与输入模块，并隔离当前事件系统。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            _disabledEventSystems.Clear();
            foreach (EventSystem eventSystem in Object.FindObjectsOfType<EventSystem>())
                if (eventSystem.enabled) { _disabledEventSystems.Add(eventSystem); eventSystem.enabled = false; }
            _input = new InputModule();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IDiagnosticsModule>(new DiagnosticsModule());
            GameEntry.Register<IInputModule>(_input);
            GameEntry.StartFramework();
        }

        /// <summary>
        /// 清除面板与测试事件系统。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            RuntimeDiagnosticsPanel.Hide();
            GameEntry.StopFramework();
            if (_eventSystemHost != null) Object.DestroyImmediate(_eventSystemHost);
            foreach (EventSystem eventSystem in _disabledEventSystems)
                if (eventSystem != null) eventSystem.enabled = true;
            _disabledEventSystems.Clear();
        }

        /// <summary>
        /// 缺少事件系统时，面板拒绝显示并保持玩法输入可用。
        /// </summary>
        [Test]
        public void ShowWithoutEventSystemDoesNotBlockGameplay()
        {
            RuntimeDiagnosticsPanel.Show();
            Assert.IsFalse(RuntimeDiagnosticsPanel.IsVisible);
            Assert.AreEqual(0, ReadBlockCount());
        }

        /// <summary>
        /// 显示、停用和框架关闭都应释放输入阻断，且不改变时间缩放。
        /// </summary>
        [Test]
        public void ShowDisableAndStopReleaseGameplayBlockWithoutChangingTimeScale()
        {
            _eventSystemHost = new GameObject("Diagnostics Test EventSystem", typeof(EventSystem));
            float originalScale = Time.timeScale;
            RuntimeDiagnosticsPanel.Show();
            Assert.IsTrue(RuntimeDiagnosticsPanel.IsVisible);
            Assert.AreEqual(1, ReadBlockCount());
            Assert.AreEqual(originalScale, Time.timeScale);
            foreach (Text text in GameObject.Find("[RuntimeDiagnosticsPanel]").GetComponentsInChildren<Text>())
                Assert.IsFalse(text.supportRichText);

            Canvas.ForceUpdateCanvases();
            RectTransform viewport = GameObject.Find("[RuntimeDiagnosticsPanel]").transform
                .Find("Panel/ScrollView/Viewport") as RectTransform;
            Assert.IsNotNull(viewport);
            Assert.IsNotNull(viewport.GetComponent<RectMask2D>());
            Assert.IsNull(viewport.GetComponent<Mask>());
            RectTransform content = viewport.Find("Content") as RectTransform;
            Assert.IsNotNull(content);
            Text body = content.Find("Snapshot").GetComponent<Text>();
            Assert.IsTrue(body.gameObject.activeInHierarchy);
            Assert.IsNotEmpty(body.text);
            Assert.LessOrEqual(body.text.Length, 12000);
            Assert.Greater(viewport.rect.height, 0f);
            Assert.Greater(content.rect.height, 0f);
            Assert.Greater(body.rectTransform.rect.height, 0f);
            Bounds bodyBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, body.rectTransform);
            Assert.Greater(bodyBounds.max.y, viewport.rect.yMin);
            Assert.Less(bodyBounds.min.y, viewport.rect.yMax);

            GameObject.Find("[RuntimeDiagnosticsPanel]").SetActive(false);
            Assert.IsFalse(RuntimeDiagnosticsPanel.IsVisible);
            Assert.AreEqual(0, ReadBlockCount());

            RuntimeDiagnosticsPanel.Show();
            Assert.IsTrue(RuntimeDiagnosticsPanel.IsVisible);
            RuntimeDiagnosticsPanel.Hide();
            Assert.IsFalse(RuntimeDiagnosticsPanel.IsVisible);
            Assert.AreEqual(0, ReadBlockCount());

            RuntimeDiagnosticsPanel.Show();
            GameEntry.StopFramework();
            Assert.IsFalse(RuntimeDiagnosticsPanel.IsVisible);
            Assert.AreEqual(0, ReadBlockCount());
            Assert.AreEqual(originalScale, Time.timeScale);
        }

        /// <summary>
        /// 读取测试用的玩法输入阻断计数。
        /// </summary>
        /// <returns>当前阻断句柄数。</returns>
        private int ReadBlockCount()
        {
            FieldInfo field = typeof(InputModule).GetField("_blockCount", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            return (int)field.GetValue(_input);
        }
    }
}
