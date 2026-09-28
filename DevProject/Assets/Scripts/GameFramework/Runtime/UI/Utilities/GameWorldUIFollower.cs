using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 将世界目标映射到 Screen Space Overlay 画布中的 UI 位置。
    /// </summary>
    [RequireComponent(typeof(RectTransform), typeof(CanvasGroup))]
    [AddComponentMenu("Game/UI/Game World UI Follower")]
    public sealed class GameWorldUIFollower : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField] private Camera _worldCamera;
        [SerializeField] private Vector3 _worldOffset;
        [SerializeField] private Vector2 _screenOffset;
        [SerializeField] private bool _hideWhenOffscreen = true;

        private RectTransform _rectTransform;
        private CanvasGroup _canvasGroup;
        private bool _visible;

        /// <summary>
        /// 被跟随的世界目标。
        /// </summary>
        public Transform Target
        {
            get => _target;
            set => _target = value;
        }

        /// <summary>
        /// 将目标投影到屏幕的世界相机；未设置时使用主相机。
        /// </summary>
        public Camera WorldCamera
        {
            get => _worldCamera;
            set => _worldCamera = value;
        }

        /// <summary>
        /// 世界坐标中的目标偏移。
        /// </summary>
        public Vector3 WorldOffset
        {
            get => _worldOffset;
            set => _worldOffset = value;
        }

        /// <summary>
        /// 屏幕像素坐标中的 UI 偏移。
        /// </summary>
        public Vector2 ScreenOffset
        {
            get => _screenOffset;
            set => _screenOffset = value;
        }

        /// <summary>
        /// 目标超出屏幕时是否隐藏 UI。
        /// </summary>
        public bool HideWhenOffscreen
        {
            get => _hideWhenOffscreen;
            set => _hideWhenOffscreen = value;
        }

        /// <summary>
        /// 当前 UI 是否可见。
        /// </summary>
        public bool IsVisible => _visible;

        /// <summary>
        /// 缓存所需组件并首次同步位置。
        /// </summary>
        private void OnEnable()
        {
            _rectTransform = GetComponent<RectTransform>();
            _canvasGroup = GetComponent<CanvasGroup>();
            Refresh();
        }

        /// <summary>
        /// 在相机和目标完成当帧移动后同步位置。
        /// </summary>
        private void LateUpdate()
        {
            Refresh();
        }

        /// <summary>
        /// 立即将目标位置同步到 UI，并更新可见性。
        /// </summary>
        /// <returns>目标在相机前方且满足屏幕可见条件时返回 true。</returns>
        public bool Refresh()
        {
            if (_rectTransform == null)
                _rectTransform = GetComponent<RectTransform>();
            if (_canvasGroup == null)
                _canvasGroup = GetComponent<CanvasGroup>();

            Camera camera = _worldCamera != null ? _worldCamera : Camera.main;
            Canvas canvas = GetComponentInParent<Canvas>();
            RectTransform parent = _rectTransform.parent as RectTransform;
            if (_target == null || camera == null || canvas == null || canvas.renderMode != RenderMode.ScreenSpaceOverlay || parent == null)
            {
                SetVisible(false);
                return false;
            }

            Vector3 point = camera.WorldToScreenPoint(_target.position + _worldOffset);
            Vector2 screenPoint = new(point.x + _screenOffset.x, point.y + _screenOffset.y);
            bool visible = point.z > 0f && (!_hideWhenOffscreen || screenPoint.x >= 0f && screenPoint.x <= Screen.width &&
                screenPoint.y >= 0f && screenPoint.y <= Screen.height);
            if (!visible)
            {
                SetVisible(false);
                return false;
            }

            if (!RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, screenPoint, null, out Vector3 uiPosition))
            {
                SetVisible(false);
                return false;
            }

            _rectTransform.position = uiPosition;
            SetVisible(true);
            return true;
        }

        /// <summary>
        /// 使用 CanvasGroup 同步显示和射线拦截状态，使组件在隐藏时仍可恢复更新。
        /// </summary>
        /// <param name="visible">是否显示 UI。</param>
        private void SetVisible(bool visible)
        {
            _visible = visible;
            _canvasGroup.alpha = visible ? 1f : 0f;
            _canvasGroup.blocksRaycasts = visible;
            _canvasGroup.interactable = visible;
        }
    }
}
