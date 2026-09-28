#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace GameFramework
{
    /// <summary>
    /// 在编辑器中查看运行诊断快照与近期日志。
    /// </summary>
    internal sealed class DiagnosticsWindow : EditorWindow
    {
        private readonly Dictionary<string, bool> _expanded = new();
        private DiagnosticSnapshot _snapshot;
        private VisualElement _content;
        private TextField _search;
        private Toggle _autoRefresh;
        private double _nextRefresh;

        /// <summary>
        /// 打开框架诊断窗口。
        /// </summary>
        [MenuItem("Game Framework/Diagnostics")]
        private static void Open() => GetWindow<DiagnosticsWindow>("Diagnostics");

        /// <summary>
        /// 注册编辑器更新回调。
        /// </summary>
        private void OnEnable() => EditorApplication.update += Tick;

        /// <summary>
        /// 注销编辑器更新回调。
        /// </summary>
        private void OnDisable() => EditorApplication.update -= Tick;

        /// <summary>
        /// 构建诊断窗口的 UI Toolkit 控件。
        /// </summary>
        public void CreateGUI()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            var style = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/Scripts/GameFramework/Editor/Diagnostics/DiagnosticsWindow.uss");
            if (style != null) root.styleSheets.Add(style);

            var toolbar = new Toolbar();
            toolbar.Add(new ToolbarButton(Refresh) { text = "Refresh" });
            _autoRefresh = new Toggle("Auto 0.5 s") { value = true };
            toolbar.Add(_autoRefresh);
            toolbar.Add(new ToolbarButton(CopyJson) { text = "Copy JSON" });
            root.Add(toolbar);

            _search = new TextField("Search");
            _search.RegisterValueChangedCallback(_ => Render());
            root.Add(_search);

            var scroll = new ScrollView();
            scroll.AddToClassList("diagnostics-scroll");
            _content = new VisualElement();
            scroll.Add(_content);
            root.Add(scroll);
            Refresh();
        }

        /// <summary>
        /// 自动刷新开启时每半秒采样一次。
        /// </summary>
        private void Tick()
        {
            if (_autoRefresh == null || !_autoRefresh.value || EditorApplication.timeSinceStartup < _nextRefresh) return;
            _nextRefresh = EditorApplication.timeSinceStartup + 0.5d;
            Refresh();
        }

        /// <summary>
        /// Play 模式下从诊断模块采集快照。
        /// </summary>
        private void Refresh()
        {
            _snapshot = EditorApplication.isPlaying && GameEntry.IsRunning ? GameEntry.Get<IDiagnosticsModule>()?.CaptureSnapshot() : null;
            Render();
        }

        /// <summary>
        /// 复制当前诊断模块输出的 JSON。
        /// </summary>
        private void CopyJson()
        {
            if (_snapshot == null) return;
            EditorGUIUtility.systemCopyBuffer = GameEntry.Get<IDiagnosticsModule>()?.ExportJson(true) ?? string.Empty;
        }

        /// <summary>
        /// 按搜索条件绘制分区和近期日志。
        /// </summary>
        private void Render()
        {
            if (_content == null) return;
            _content.Clear();
            if (_snapshot == null)
            {
                AddLabel(EditorApplication.isPlaying
                    ? "Diagnostics module unavailable. Start the framework and register IDiagnosticsModule."
                    : "Diagnostics are available only while playing.");
                return;
            }

            AddLabel("Captured (UTC): " + (_snapshot.CapturedAtUtc ?? string.Empty));
            if (_snapshot.Sections != null)
                foreach (DiagnosticSection section in _snapshot.Sections)
                {
                    if (section == null || !MatchesSection(section)) continue;
                    string sectionName = section.Name ?? "Unnamed section";
                    bool expanded = !_expanded.TryGetValue(sectionName, out bool saved) || saved;
                    var foldout = new Foldout { text = sectionName, value = expanded };
                    foldout.Query<Label>().ForEach(label => label.enableRichText = false);
                    foldout.RegisterValueChangedCallback(change => _expanded[sectionName] = change.newValue);
                    foldout.AddToClassList("diagnostics-section");
                    if (!string.IsNullOrEmpty(section.Error)) foldout.Add(new Label("Error: " + section.Error) { enableRichText = false });
                    if (section.Values != null)
                        foreach (DiagnosticValue value in section.Values)
                            if (value != null && (Matches(section.Name, section.Error) || Matches(value.Name, value.Value)))
                                foldout.Add(new Label((value.Name ?? string.Empty) + ": " + (value.Value ?? string.Empty)) { enableRichText = false });
                    _content.Add(foldout);
                }

            AddLabel("Recent logs");
            if (_snapshot.Logs == null || _snapshot.Logs.Count == 0) { AddLabel("No captured logs."); return; }
            foreach (DiagnosticLogEntry entry in _snapshot.Logs)
                if (entry != null && Matches(entry.Tag, entry.Message, entry.Level.ToString()))
                    AddLabel("[" + entry.TimestampUtc + "] " + entry.Level + " " + entry.Tag + ": " + entry.Message);
        }

        /// <summary>
        /// 向内容区域加入普通文字。
        /// </summary>
        /// <param name="value">显示文本。</param>
        private void AddLabel(string value)
        {
            var label = new Label(value) { enableRichText = false };
            label.AddToClassList("diagnostics-line");
            _content.Add(label);
        }

        /// <summary>
        /// 判断分区及其值是否匹配搜索条件。
        /// </summary>
        /// <param name="section">待检查分区。</param>
        /// <returns>是否匹配。</returns>
        private bool MatchesSection(DiagnosticSection section)
        {
            if (Matches(section.Name, section.Error)) return true;
            if (section.Values == null) return false;
            foreach (DiagnosticValue value in section.Values)
                if (value != null && Matches(value.Name, value.Value)) return true;
            return false;
        }

        /// <summary>
        /// 判断任一文本是否包含搜索内容。
        /// </summary>
        /// <param name="first">第一段文本。</param>
        /// <param name="second">第二段文本。</param>
        /// <param name="third">第三段文本。</param>
        /// <returns>是否匹配。</returns>
        private bool Matches(string first, string second, string third = null)
        {
            string query = _search?.value;
            if (string.IsNullOrWhiteSpace(query)) return true;
            return Contains(first, query) || Contains(second, query) || Contains(third, query);
        }

        /// <summary>
        /// 按不区分大小写方式搜索文本。
        /// </summary>
        /// <param name="value">待搜索文本。</param>
        /// <param name="query">搜索文本。</param>
        /// <returns>是否包含。</returns>
        private static bool Contains(string value, string query) =>
            !string.IsNullOrEmpty(value) && value.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
#endif
