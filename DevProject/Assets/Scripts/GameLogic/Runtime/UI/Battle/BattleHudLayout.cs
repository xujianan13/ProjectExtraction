using UnityEngine;

namespace GameLogic.UI.Battle
{
    /// <summary>
    /// 在当前安全区内排列战斗信息、圆形摇杆与操作按钮。
    /// </summary>
    public sealed class BattleHudLayout : MonoBehaviour
    {
        private RectTransform area;
        private RectTransform header;
        private RectTransform minimap;
        private RectTransform stick;
        private RectTransform actions;

        /// <summary>
        /// 绑定现有控件并按安全区实际尺寸布局。
        /// </summary>
        /// <param name="header">顶部状态区。</param>
        /// <param name="minimap">右上小地图。</param>
        /// <param name="stick">左下圆形摇杆。</param>
        /// <param name="actions">右下操作区。</param>
        public void Configure(RectTransform header, RectTransform minimap, RectTransform stick, RectTransform actions)
        {
            area = (RectTransform)transform;
            this.header = header;
            this.minimap = minimap;
            this.stick = stick;
            this.actions = actions;
            Refresh();
        }

        /// <summary>
        /// 屏幕旋转、窗口缩放或安全区变化时重新排列控件。
        /// </summary>
        private void OnRectTransformDimensionsChange() => Refresh();

        /// <summary>
        /// 使用参考单位和实际可用尺寸保持控件可见且摇杆不变形。
        /// </summary>
        public void Refresh()
        {
            if (area == null || header == null || minimap == null || stick == null || actions == null) return;
            float width = area.rect.width;
            float height = area.rect.height;
            if (width <= 0f || height <= 0f) return;
            float padding = 24f;
            float diameter = Mathf.Clamp(Mathf.Min(width, height) * 0.25f, 180f, 280f);
            float mapSize = Mathf.Clamp(Mathf.Min(width * 0.29f, height * 0.25f), 160f, 300f);
            float actionWidth = Mathf.Min(330f, width * 0.38f);
            float controlsHeight = 324f;
            Pin(header, new Vector2(0f, 1f), new Vector2(padding, -padding), new Vector2(width - mapSize - padding * 3f, 214f));
            Pin(minimap, Vector2.one, new Vector2(-padding, -padding), Vector2.one * mapSize);
            Pin(stick, Vector2.zero, new Vector2(padding, padding * 2f), Vector2.one * diameter);
            Pin(actions, Vector2.right, new Vector2(-padding, padding * 2f), new Vector2(actionWidth, controlsHeight));
        }

        /// <summary>
        /// 将控件按其自然角点固定在安全区内。
        /// </summary>
        /// <param name="rect">控件矩形。</param>
        /// <param name="anchor">安全区锚点与控件轴心。</param>
        /// <param name="offset">相对锚点的偏移。</param>
        /// <param name="size">控件尺寸。</param>
        private static void Pin(RectTransform rect, Vector2 anchor, Vector2 offset, Vector2 size)
        {
            rect.anchorMin = anchor;
            rect.anchorMax = anchor;
            rect.pivot = anchor;
            rect.anchoredPosition = offset;
            rect.sizeDelta = size;
        }
    }
}
