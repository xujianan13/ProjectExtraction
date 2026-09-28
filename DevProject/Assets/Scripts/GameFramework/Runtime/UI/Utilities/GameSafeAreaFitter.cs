using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 将 RectTransform 约束到屏幕安全区，适用于铺满屏幕的父节点。
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(RectTransform))]
    [AddComponentMenu("Game/UI/Game Safe Area Fitter")]
    public sealed class GameSafeAreaFitter : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreenSize;

        /// <summary>
        /// 启用时立即应用安全区。
        /// </summary>
        private void OnEnable()
        {
            _rectTransform = GetComponent<RectTransform>();
            Refresh();
        }

        /// <summary>
        /// 在设备旋转、窗口缩放和编辑器预览变化时刷新安全区。
        /// </summary>
        private void Update()
        {
            Rect safeArea = Screen.safeArea;
            Vector2Int screenSize = new(Screen.width, Screen.height);
            if (safeArea != _lastSafeArea || screenSize != _lastScreenSize)
                Apply(safeArea, screenSize);
        }

        /// <summary>
        /// 使用当前屏幕尺寸和安全区重新布局。
        /// </summary>
        public void Refresh()
        {
            Apply(Screen.safeArea, new Vector2Int(Screen.width, Screen.height));
        }

        /// <summary>
        /// 使用指定安全区重新布局，便于适配预览和设备模拟。
        /// </summary>
        /// <param name="safeArea">屏幕像素坐标中的安全区。</param>
        /// <param name="screenSize">屏幕像素尺寸。</param>
        /// <returns>屏幕尺寸有效且已经应用布局时返回 true。</returns>
        public bool Apply(Rect safeArea, Vector2Int screenSize)
        {
            if (screenSize.x <= 0 || screenSize.y <= 0)
                return false;

            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();

            Vector2 minimum = new(Mathf.Clamp01(safeArea.xMin / screenSize.x), Mathf.Clamp01(safeArea.yMin / screenSize.y));
            Vector2 maximum = new(Mathf.Clamp01(safeArea.xMax / screenSize.x), Mathf.Clamp01(safeArea.yMax / screenSize.y));
            _rectTransform.anchorMin = minimum;
            _rectTransform.anchorMax = maximum;
            _rectTransform.offsetMin = Vector2.zero;
            _rectTransform.offsetMax = Vector2.zero;
            _lastSafeArea = safeArea;
            _lastScreenSize = screenSize;
            return true;
        }
    }
}
