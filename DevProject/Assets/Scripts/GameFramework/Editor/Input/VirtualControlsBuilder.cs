using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem.OnScreen;
using UnityEngine.UI;

namespace GameFramework.Editor.Input
{
    /// <summary>
    /// 创建可热更的触屏虚拟摇杆与攻击按钮 Prefab。
    /// </summary>
    public static class VirtualControlsBuilder
    {
        private const string PrefabPath = "Assets/GameResources/Input/UI_VirtualControls.prefab";

        /// <summary>
        /// 创建虚拟控件 Prefab；已有资产不会被覆盖。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Input/Create Virtual Controls Prefab")]
        public static void Create()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
            {
                Debug.Log($"虚拟控件 Prefab 已存在：{PrefabPath}");
                return;
            }

            RectTransform root = CreateRect("UI_VirtualControls", null, Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero);
            Canvas canvas = root.gameObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 250;
            CanvasScaler scaler = root.gameObject.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
            root.gameObject.AddComponent<GraphicRaycaster>();

            Sprite roundSprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            RectTransform baseRect = CreateRect("MoveStickBase", root, Vector2.zero, Vector2.zero, Vector2.zero,
                new Vector2(240f, 240f), new Vector2(190f, 170f));
            AddImage(baseRect, roundSprite, new Color(0.09f, 0.24f, 0.33f, 0.48f), false);
            RectTransform touchArea = CreateRect("MoveStickTouchArea", baseRect, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(230f, 230f), Vector2.zero);
            AddImage(touchArea, null, Color.clear, true);
            OnScreenStick stick = touchArea.gameObject.AddComponent<OnScreenStick>();
            stick.controlPath = "<Gamepad>/leftStick";
            stick.movementRange = 72f;
            RectTransform handle = CreateRect("MoveStickHandle", touchArea, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(98f, 98f), Vector2.zero);
            AddImage(handle, roundSprite, new Color(0.31f, 0.79f, 0.92f, 0.9f), false);

            RectTransform attack = CreateRect("AttackButton", root, Vector2.right, Vector2.right, Vector2.right,
                new Vector2(164f, 164f), new Vector2(-185f, 165f));
            AddImage(attack, roundSprite, new Color(0.95f, 0.46f, 0.22f, 0.88f), true);
            OnScreenButton button = attack.gameObject.AddComponent<OnScreenButton>();
            button.controlPath = "<Gamepad>/buttonSouth";
            RectTransform attackCenter = CreateRect("AttackButtonCenter", attack, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(74f, 74f), Vector2.zero);
            AddImage(attackCenter, roundSprite, new Color(1f, 1f, 1f, 0.42f), false);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root.gameObject, PrefabPath);
            Object.DestroyImmediate(root.gameObject);
            if (prefab == null)
            {
                Debug.LogError($"虚拟控件 Prefab 保存失败：{PrefabPath}");
                return;
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"已创建虚拟控件 Prefab：{PrefabPath}，GamePackage 地址 UI_VirtualControls。");
        }

        /// <summary>
        /// 创建带锚点和尺寸的 UI 矩形节点。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="parent">父节点；根节点使用 null。</param>
        /// <param name="anchorMin">最小锚点。</param>
        /// <param name="anchorMax">最大锚点。</param>
        /// <param name="pivot">轴心。</param>
        /// <param name="size">尺寸。</param>
        /// <param name="position">锚点偏移。</param>
        /// <returns>创建完成的矩形节点。</returns>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax,
            Vector2 pivot, Vector2 size, Vector2 position)
        {
            RectTransform rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            if (parent != null) rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>
        /// 给节点添加图像并设置是否接收 UI 射线。
        /// </summary>
        /// <param name="rect">目标矩形节点。</param>
        /// <param name="sprite">图像精灵，可为空。</param>
        /// <param name="color">显示颜色。</param>
        /// <param name="raycastTarget">是否接收 UI 射线。</param>
        /// <returns>创建完成的图像组件。</returns>
        private static UnityEngine.UI.Image AddImage(RectTransform rect, Sprite sprite, Color color, bool raycastTarget)
        {
            UnityEngine.UI.Image image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }
    }
}
