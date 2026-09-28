using System;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 仅供编辑器和开发构建主动打开的运行诊断面板。
    /// </summary>
    public sealed class RuntimeDiagnosticsPanel : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private const string InputBlockReason = "Runtime diagnostics panel";
        private static RuntimeDiagnosticsPanel _instance;
        private static Font _font;
        private IDisposable _inputBlock;
        private ScopeRegistration _scopeRegistration;
        private Text _body;
        private Text _status;
        private float _nextRefresh;
        private bool _closing;
#endif

        /// <summary>
        /// 诊断面板当前是否可见。
        /// </summary>
        public static bool IsVisible
        {
            get
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                return _instance != null && _instance.isActiveAndEnabled;
#else
                return false;
#endif
            }
        }

        /// <summary>
        /// 在运行中的框架上打开诊断面板。
        /// </summary>
        public static void Show()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_instance != null) return;
            if (!GameEntry.IsRunning || GameEntry.Get<IDiagnosticsModule>() == null) return;
            if (EventSystem.current == null)
            {
                GameEntry.Get<ILogModule>()?.Warn("Diagnostics", "Cannot open diagnostics panel: EventSystem is unavailable.");
                return;
            }

            var root = new GameObject("[RuntimeDiagnosticsPanel]", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(RuntimeDiagnosticsPanel));
            DontDestroyOnLoad(root);
            _instance = root.GetComponent<RuntimeDiagnosticsPanel>();
            _instance.Build();
            _instance._inputBlock = GameEntry.Get<IInputModule>()?.BlockGameplay(InputBlockReason);
            _instance._scopeRegistration = GameEntry.ApplicationScope?.Defer(Hide);
            _instance.Refresh();
#endif
        }

        /// <summary>
        /// 关闭面板并立即解除玩法输入阻断。
        /// </summary>
        public static void Hide()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_instance == null) return;
            _instance.Close();
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// 构建独立的右侧调试 Canvas。
        /// </summary>
        private void Build()
        {
            _font ??= Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas = GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = short.MaxValue;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            RectTransform panel = CreateRect("Panel", transform);
            panel.anchorMin = new Vector2(1f, 0.15f);
            panel.anchorMax = new Vector2(1f, 0.95f);
            panel.pivot = new Vector2(1f, 0.5f);
            panel.anchoredPosition = new Vector2(-16f, 0f);
            panel.sizeDelta = new Vector2(660f, 0f);
            AddImage(panel, new Color(0.06f, 0.08f, 0.11f, 0.94f), true);

            RectTransform header = CreateRect("Header", panel);
            SetTopBar(header, 46f);
            AddImage(header, new Color(0.12f, 0.17f, 0.22f, 1f), true);
            Text title = CreateText("Title", header, 20, TextAnchor.MiddleLeft);
            Stretch(title.rectTransform, 16f, 0f, -288f, 0f);
            title.text = "Framework Diagnostics";
            CreateButton("Close", header, "Close", 0f, Hide);
            CreateButton("Copy", header, "Copy JSON", 92f, CopyJson);
            CreateButton("Refresh", header, "Refresh", 184f, Refresh);

            RectTransform footer = CreateRect("Footer", panel);
            footer.anchorMin = Vector2.zero;
            footer.anchorMax = new Vector2(1f, 0f);
            footer.pivot = new Vector2(0.5f, 0f);
            footer.sizeDelta = new Vector2(0f, 32f);
            _status = CreateText("Status", footer, 14, TextAnchor.MiddleLeft);
            Stretch(_status.rectTransform, 14f, 0f, -14f, 0f);
            _status.text = "Updates every second";

            RectTransform scrollRoot = CreateRect("ScrollView", panel);
            scrollRoot.anchorMin = Vector2.zero;
            scrollRoot.anchorMax = Vector2.one;
            scrollRoot.offsetMin = new Vector2(12f, 36f);
            scrollRoot.offsetMax = new Vector2(-12f, -54f);
            var scroll = scrollRoot.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.scrollSensitivity = 30f;

            RectTransform viewport = CreateRect("Viewport", scrollRoot);
            Stretch(viewport, 0f, 0f, 0f, 0f);
            AddImage(viewport, new Color(1f, 1f, 1f, 0.001f), true);
            viewport.gameObject.AddComponent<RectMask2D>();
            RectTransform content = CreateRect("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = Vector2.one;
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var layout = content.gameObject.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(12, 12, 10, 10);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            _body = CreateText("Snapshot", content, 16, TextAnchor.UpperLeft);
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            scroll.viewport = viewport;
            scroll.content = content;
        }

        /// <summary>
        /// 使用不受时间缩放影响的间隔刷新可见内容。
        /// </summary>
        private void Update()
        {
            if (!GameEntry.IsRunning) { Hide(); return; }
            if (Time.unscaledTime < _nextRefresh) return;
            Refresh();
        }

        /// <summary>
        /// 采集一次快照并显示有界日志。
        /// </summary>
        private void Refresh()
        {
            IDiagnosticsModule diagnostics = GameEntry.Get<IDiagnosticsModule>();
            if (diagnostics == null) { _body.text = "Diagnostics module unavailable."; return; }
            DiagnosticSnapshot snapshot = diagnostics.CaptureSnapshot();
            var output = new StringBuilder(4096);
            bool truncated = !AppendDisplayLine(output, "Captured (UTC): " + snapshot.CapturedAtUtc);
            if (!truncated && snapshot.Sections != null)
                foreach (DiagnosticSection section in snapshot.Sections)
                {
                    if (section == null) continue;
                    if (!AppendDisplayLine(output, "[ " + section.Name + " ]")) { truncated = true; break; }
                    if (!string.IsNullOrEmpty(section.Error) && !AppendDisplayLine(output, "Error: " + section.Error))
                    { truncated = true; break; }
                    if (section.Values == null) continue;
                    foreach (DiagnosticValue value in section.Values)
                        if (value != null && !AppendDisplayLine(output, value.Name + ": " + value.Value))
                        { truncated = true; break; }
                    if (truncated) break;
                }
            if (!truncated) truncated = !AppendDisplayLine(output, "[ Recent logs ]");
            if (!truncated && snapshot.Logs != null)
            {
                int start = Mathf.Max(0, snapshot.Logs.Count - 20);
                for (int i = start; i < snapshot.Logs.Count; i++)
                {
                    DiagnosticLogEntry entry = snapshot.Logs[i];
                    if (entry == null) continue;
                    if (!AppendDisplayLine(output, "[" + entry.TimestampUtc + "] " + entry.Level + " " +
                        entry.Tag + ": " + entry.Message)) { truncated = true; break; }
                }
                if (start > 0) truncated = true;
            }
            if (truncated) output.AppendLine("… Copy JSON for full data.");
            _body.text = output.ToString();
            _nextRefresh = Time.unscaledTime + 1f;
        }

        /// <summary>
        /// 按单行及整体上限追加可见文本。
        /// </summary>
        /// <param name="output">显示文本缓冲区。</param>
        /// <param name="value">待追加内容。</param>
        /// <returns>文本是否完整加入。</returns>
        private static bool AppendDisplayLine(StringBuilder output, string value)
        {
            const int maxLineChars = 200;
            const int maxDisplayChars = 12000;
            int available = maxDisplayChars - output.Length - 48;
            if (available <= 0) return false;
            string line = value ?? string.Empty;
            int count = Mathf.Min(line.Length, maxLineChars, available);
            output.Append(line, 0, count);
            if (count < line.Length) output.Append('…');
            output.AppendLine();
            return true;
        }

        /// <summary>
        /// 将当前诊断快照复制到系统剪贴板。
        /// </summary>
        private void CopyJson()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            _status.text = "Clipboard unavailable on this platform.";
#else
            IDiagnosticsModule diagnostics = GameEntry.Get<IDiagnosticsModule>();
            if (diagnostics == null) { _status.text = "Diagnostics module unavailable."; return; }
            GUIUtility.systemCopyBuffer = diagnostics.ExportJson(true);
            _status.text = "JSON copied to clipboard.";
#endif
        }

        /// <summary>
        /// 立即释放阻断句柄与作用域注册，再销毁界面。
        /// </summary>
        private void Close()
        {
            if (_closing) return;
            _closing = true;
            _instance = null;
            _inputBlock?.Dispose();
            _inputBlock = null;
            ScopeRegistration registration = _scopeRegistration;
            _scopeRegistration = null;
            registration?.Dispose();
            Destroy(gameObject);
        }

        /// <summary>
        /// 面板被外部销毁时同样释放玩法输入。
        /// </summary>
        private void OnDestroy() => Close();

        /// <summary>
        /// 面板被停用时立即解除玩法输入阻断。
        /// </summary>
        private void OnDisable() => Close();

        /// <summary>
        /// 创建 RectTransform 子节点。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="parent">父节点。</param>
        /// <returns>新建的矩形变换。</returns>
        private static RectTransform CreateRect(string name, Transform parent)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            return rect;
        }

        /// <summary>
        /// 设置横向拉伸和边距。
        /// </summary>
        /// <param name="rect">目标矩形。</param>
        /// <param name="left">左边距。</param>
        /// <param name="bottom">下边距。</param>
        /// <param name="right">右边距。</param>
        /// <param name="top">上边距。</param>
        private static void Stretch(RectTransform rect, float left, float bottom, float right, float top)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(left, bottom);
            rect.offsetMax = new Vector2(right, top);
        }

        /// <summary>
        /// 设置顶部工具栏的锚点与高度。
        /// </summary>
        /// <param name="rect">工具栏矩形。</param>
        /// <param name="height">工具栏高度。</param>
        private static void SetTopBar(RectTransform rect, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 1f);
            rect.sizeDelta = new Vector2(0f, height);
        }

        /// <summary>
        /// 添加背景与射线目标。
        /// </summary>
        /// <param name="rect">目标节点。</param>
        /// <param name="color">背景颜色。</param>
        /// <param name="raycast">是否接收 UI 射线。</param>
        private static void AddImage(RectTransform rect, Color color, bool raycast)
        {
            var image = rect.gameObject.AddComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = raycast;
        }

        /// <summary>
        /// 创建无富文本解析的文本组件。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="size">字号。</param>
        /// <param name="alignment">文字对齐。</param>
        /// <returns>创建的文本组件。</returns>
        private static Text CreateText(string name, Transform parent, int size, TextAnchor alignment)
        {
            RectTransform rect = CreateRect(name, parent);
            var label = rect.gameObject.AddComponent<Text>();
            label.font = _font;
            label.fontSize = size;
            label.color = Color.white;
            label.alignment = alignment;
            label.supportRichText = false;
            label.raycastTarget = false;
            return label;
        }

        /// <summary>
        /// 创建工具栏按钮并绑定回调。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="caption">按钮文字。</param>
        /// <param name="rightOffset">距右侧的偏移。</param>
        /// <param name="action">点击回调。</param>
        private static void CreateButton(string name, RectTransform parent, string caption, float rightOffset, UnityEngine.Events.UnityAction action)
        {
            RectTransform rect = CreateRect(name, parent);
            rect.anchorMin = rect.anchorMax = new Vector2(1f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            rect.anchoredPosition = new Vector2(-8f - rightOffset, 0f);
            rect.sizeDelta = new Vector2(86f, 34f);
            AddImage(rect, new Color(0.23f, 0.32f, 0.4f, 1f), true);
            rect.gameObject.AddComponent<UnityEngine.UI.Button>().onClick.AddListener(action);
            Text label = CreateText("Label", rect, 15, TextAnchor.MiddleCenter);
            Stretch(label.rectTransform, 2f, 2f, -2f, -2f);
            label.text = caption;
        }
#endif
    }
}
