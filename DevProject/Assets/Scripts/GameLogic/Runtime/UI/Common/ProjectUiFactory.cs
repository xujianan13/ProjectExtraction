using System;
using GameFramework;
using UnityEngine;

namespace GameLogic.UI.Common
{
    /// <summary>
    /// 以现有 Canvas 为宿主创建项目 uGUI 控件。
    /// </summary>
    public static class ProjectUiFactory
    {
        private static Font font;

        /// <summary>
        /// 采用既有 Home 资源的字体，保持中文显示与项目当前 UI 一致。
        /// </summary>
        /// <param name="value">现有资源使用的字体。</param>
        public static void UseFont(Font value)
        {
            if (value != null) font = value;
        }

        /// <summary>
        /// 创建铺满父节点的安全区内容。
        /// </summary>
        /// <param name="parent">窗口根节点。</param>
        /// <param name="name">内容名称。</param>
        /// <param name="color">背景颜色。</param>
        /// <returns>已适配安全区的内容节点。</returns>
        public static RectTransform Fill(RectTransform parent, string name, Color color)
        {
            parent.anchorMin = Vector2.zero;
            parent.anchorMax = Vector2.one;
            parent.offsetMin = Vector2.zero;
            parent.offsetMax = Vector2.zero;
            RectTransform rect = Panel(parent, name, color);
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            rect.gameObject.AddComponent<GameSafeAreaFitter>();
            return rect;
        }

        /// <summary>
        /// 创建面板节点。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">节点名称。</param>
        /// <param name="color">面板颜色。</param>
        /// <returns>面板矩形。</returns>
        public static RectTransform Panel(Transform parent, string name, Color color)
        {
            GameObject node = new(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            node.transform.SetParent(parent, false);
            RectTransform rect = node.GetComponent<RectTransform>();
            UnityEngine.UI.Image image = node.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = color.a > 0f;
            return rect;
        }

        /// <summary>
        /// 创建纵向布局。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">节点名称。</param>
        /// <param name="spacing">子节点间距。</param>
        /// <returns>布局矩形。</returns>
        public static RectTransform Vertical(Transform parent, string name, float spacing)
        {
            RectTransform rect = Panel(parent, name, Color.clear);
            UnityEngine.UI.VerticalLayoutGroup group = rect.gameObject.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.UpperCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = false;
            return rect;
        }

        /// <summary>
        /// 创建横向布局。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">节点名称。</param>
        /// <param name="spacing">子节点间距。</param>
        /// <returns>布局矩形。</returns>
        public static RectTransform Horizontal(Transform parent, string name, float spacing)
        {
            RectTransform rect = Panel(parent, name, Color.clear);
            UnityEngine.UI.HorizontalLayoutGroup group = rect.gameObject.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            group.spacing = spacing;
            group.childAlignment = TextAnchor.MiddleCenter;
            group.childControlWidth = true;
            group.childControlHeight = true;
            group.childForceExpandWidth = true;
            group.childForceExpandHeight = true;
            return rect;
        }

        /// <summary>
        /// 创建可读文本.
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="value">初始文本。</param>
        /// <param name="size">字号。</param>
        /// <param name="alignment">对齐方式。</param>
        /// <param name="color">文字颜色。</param>
        /// <returns>文本组件。</returns>
        public static UnityEngine.UI.Text Label(Transform parent, string value, int size, TextAnchor alignment, Color color)
        {
            GameObject node = new("Text", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            node.transform.SetParent(parent, false);
            UnityEngine.UI.Text label = node.GetComponent<UnityEngine.UI.Text>();
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            label.font = font;
            label.text = value;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.horizontalOverflow = HorizontalWrapMode.Wrap;
            label.verticalOverflow = VerticalWrapMode.Truncate;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// 创建带点击回调的按钮。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="caption">按钮文字。</param>
        /// <param name="onClick">点击回调。</param>
        /// <returns>按钮组件。</returns>
        public static UnityEngine.UI.Button ActionButton(Transform parent, string caption, Action onClick)
        {
            RectTransform rect = Panel(parent, caption + "Button", new Color(0.18f, 0.36f, 0.47f, 1f));
            UnityEngine.UI.Button button = rect.gameObject.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = rect.GetComponent<UnityEngine.UI.Image>();
            UnityEngine.UI.Text label = Label(rect, caption, 25, TextAnchor.MiddleCenter, Color.white);
            Stretch(label.rectTransform, 8f);
            if (onClick != null) button.onClick.AddListener(() => onClick());
            return button;
        }

        /// <summary>
        /// 创建满足 UnityEngine.UI.ScrollRect 层级要求的滚动区域。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">滚动区域名称。</param>
        /// <param name="content">输出内容根节点。</param>
        /// <returns>滚动组件。</returns>
        public static UnityEngine.UI.ScrollRect Scroll(Transform parent, string name, out RectTransform content)
        {
            RectTransform root = Panel(parent, name, new Color(0.09f, 0.15f, 0.21f, 0.92f));
            UnityEngine.UI.ScrollRect scroll = root.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            RectTransform viewport = Panel(root, "Viewport", new Color(1f, 1f, 1f, 0.01f));
            Stretch(viewport, 0f);
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;
            content = Vertical(viewport, "Content", 8f);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            UnityEngine.UI.ContentSizeFitter fitter = content.gameObject.AddComponent<UnityEngine.UI.ContentSizeFitter>();
            fitter.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;
            return scroll;
        }

        /// <summary>
        /// 将矩形拉伸到父节点并留出内边距。
        /// </summary>
        /// <param name="rect">待设置矩形。</param>
        /// <param name="padding">四周边距。</param>
        public static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>
        /// 设置布局项建议高度。
        /// </summary>
        /// <param name="rect">布局项矩形。</param>
        /// <param name="height">建议高度。</param>
        public static void Height(RectTransform rect, float height)
        {
            UnityEngine.UI.LayoutElement element = rect.GetComponent<UnityEngine.UI.LayoutElement>() ?? rect.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            element.preferredHeight = height;
            element.minHeight = height;
        }

        /// <summary>
        /// 移除动态列表中旧的子节点。
        /// </summary>
        /// <param name="parent">列表内容节点。</param>
        public static void Clear(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                GameObject child = parent.GetChild(index).gameObject;
                child.SetActive(false);
                UnityEngine.Object.Destroy(child);
            }
        }
    }
}
