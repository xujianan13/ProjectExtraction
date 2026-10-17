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
        private float[] baseActionHeights;
        private UnityEngine.UI.Text[] actionLabels;
        private int[] baseFontSizes;

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
            baseActionHeights = new float[actions.childCount];
            for (int index = 0; index < actions.childCount; index++)
            {
                UnityEngine.UI.LayoutElement element = actions.GetChild(index).GetComponent<UnityEngine.UI.LayoutElement>();
                baseActionHeights[index] = element == null ? 0f : element.preferredHeight;
            }
            actionLabels = actions.GetComponentsInChildren<UnityEngine.UI.Text>(true);
            baseFontSizes = new int[actionLabels.Length];
            for (int index = 0; index < actionLabels.Length; index++) baseFontSizes[index] = actionLabels[index].fontSize;
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
            bool landscape = width > height;
            bool besideMap = false;
            float controlsHeight = FitActions(height, mapSize, padding, besideMap);
            float headerWidth = width - mapSize - padding * 3f;
            Pin(header, new Vector2(0f, 1f), new Vector2(padding, -padding), new Vector2(headerWidth, 430f));
            Pin(minimap, Vector2.one, new Vector2(-padding, -padding), Vector2.one * mapSize);
            Pin(stick, Vector2.zero, new Vector2(padding, padding * 2f), Vector2.one * diameter);
            Pin(actions, Vector2.right, new Vector2(-padding, padding * 2f), new Vector2(actionWidth, controlsHeight));
        }

        /// <summary>
        /// 按可见按钮的原始首选高度缩放操作区，避免小屏幕上压住小地图。
        /// </summary>
        /// <param name="height">安全区高度。</param>
        /// <param name="mapSize">小地图边长。</param>
        /// <param name="padding">边缘留白。</param>
        /// <param name="besideMap">操作区是否可在地图右侧独立排布。</param>
        /// <returns>操作区实际高度。</returns>
        private float FitActions(float height, float mapSize, float padding, bool besideMap)
        {
            float sum = 0f;
            int count = 0;
            for (int index = 0; index < actions.childCount; index++)
                if (actions.GetChild(index).gameObject.activeSelf) { sum += baseActionHeights[index]; count++; }
            UnityEngine.UI.VerticalLayoutGroup group = actions.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            float spacing = (group == null ? 0f : group.spacing) * Mathf.Max(0, count - 1);
            float available = besideMap ? height - padding * 3f : height - mapSize - padding * 4f;
            float scale = sum > 0f ? Mathf.Clamp01((available - spacing) / sum) : 1f;
            for (int index = 0; index < actions.childCount; index++)
            {
                UnityEngine.UI.LayoutElement element = actions.GetChild(index).GetComponent<UnityEngine.UI.LayoutElement>();
                if (element == null) continue;
                element.preferredHeight = baseActionHeights[index] * scale;
                element.minHeight = element.preferredHeight;
            }
            for (int index = 0; index < actionLabels.Length; index++)
            {
                UnityEngine.UI.Text label = actionLabels[index];
                label.fontSize = Mathf.Max(10, Mathf.RoundToInt(baseFontSizes[index] * scale));
                label.resizeTextForBestFit = true;
                label.resizeTextMinSize = 9;
                label.resizeTextMaxSize = baseFontSizes[index];
            }
            return sum * scale + spacing;
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
