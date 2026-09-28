using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace GameFramework.Tests.UI
{
    /// <summary>
    /// 游戏 UI 布局辅助组件测试。
    /// </summary>
    [TestFixture]
    public sealed class GameUIUtilityTests
    {
        private readonly List<GameObject> _createdObjects = new();

        /// <summary>
        /// 销毁测试创建的游戏对象。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            for (int i = _createdObjects.Count - 1; i >= 0; i--)
                Object.DestroyImmediate(_createdObjects[i]);
            _createdObjects.Clear();
        }

        /// <summary>
        /// 安全区将像素范围转换为归一化锚点并清除边距。
        /// </summary>
        [Test]
        public void SafeAreaFitter_Apply_UpdatesAnchorsAndOffsets()
        {
            var gameObject = CreateObject("SafeArea", typeof(RectTransform));
            GameSafeAreaFitter fitter = gameObject.AddComponent<GameSafeAreaFitter>();
            RectTransform rectTransform = gameObject.GetComponent<RectTransform>();

            Assert.IsTrue(fitter.Apply(new Rect(10f, 20f, 80f, 50f), new Vector2Int(100, 100)));
            Assert.AreEqual(new Vector2(0.1f, 0.2f), rectTransform.anchorMin);
            Assert.AreEqual(new Vector2(0.9f, 0.7f), rectTransform.anchorMax);
            Assert.AreEqual(Vector2.zero, rectTransform.offsetMin);
            Assert.AreEqual(Vector2.zero, rectTransform.offsetMax);
            Assert.IsFalse(fitter.Apply(new Rect(0f, 0f, 10f, 10f), Vector2Int.zero));
        }

        /// <summary>
        /// 世界目标位于相机背后时隐藏 UI，并停止射线拦截。
        /// </summary>
        [Test]
        public void WorldUIFollower_TargetBehindCamera_HidesWithoutDisablingUpdater()
        {
            GameObject canvasObject = CreateObject("Canvas", typeof(RectTransform), typeof(Canvas));
            canvasObject.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            GameObject uiObject = CreateObject("WorldUI", typeof(RectTransform));
            uiObject.transform.SetParent(canvasObject.transform, false);
            GameWorldUIFollower follower = uiObject.AddComponent<GameWorldUIFollower>();
            GameObject cameraObject = CreateObject("Camera", typeof(Camera));
            GameObject targetObject = CreateObject("Target");
            targetObject.transform.position = new Vector3(0f, 0f, -10f);
            follower.WorldCamera = cameraObject.GetComponent<Camera>();
            follower.Target = targetObject.transform;

            Assert.IsFalse(follower.Refresh());
            Assert.IsFalse(follower.IsVisible);
            Assert.IsTrue(follower.isActiveAndEnabled);
            Assert.AreEqual(0f, uiObject.GetComponent<CanvasGroup>().alpha);
            Assert.IsFalse(uiObject.GetComponent<CanvasGroup>().blocksRaycasts);
        }

        /// <summary>
        /// 创建并跟踪测试游戏对象。
        /// </summary>
        /// <param name="name">游戏对象名称。</param>
        /// <param name="components">创建时添加的组件类型。</param>
        /// <returns>创建的游戏对象。</returns>
        private GameObject CreateObject(string name, params System.Type[] components)
        {
            var gameObject = new GameObject(name, components);
            _createdObjects.Add(gameObject);
            return gameObject;
        }
    }
}
