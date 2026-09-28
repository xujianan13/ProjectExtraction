#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 在项目资源中按名称快速搜索、定位、打开或拖拽资源。
    /// </summary>
    internal sealed class QuickSearchWindow : EditorWindow
    {
        private const string EnabledPrefKey = "Enabled";
        private const string ShortcutKeyCodePrefKey = "Shortcut_KeyCode";
        private const string ShortcutModifiersPrefKey = "Shortcut_Modifiers";
        private const string FilterDebounceMsPrefKey = "Filter_DebounceMs";
        private const int DefaultFilterDebounceMs = 150;
        private const int MinFilterDebounceMs = 50;
        private const int MaxFilterDebounceMs = 1000;
        private const int MaxResults = 100;
        private const int IconLoadsPerFrame = 4;
        private const float ResultHeight = 60f;
        private const double DoubleEnterInterval = 0.35d;

        private static readonly KeyCombination DefaultShortcutCombination =
            new KeyCombination(KeyCode.P, ShortcutModifiers.Action);

        private static QuickSearchWindow _window;

        private readonly List<SearchResult> _scoredResults = new List<SearchResult>(128);
        private readonly List<QuickSearchCachedAsset> _filteredAssets = new List<QuickSearchCachedAsset>(128);

        private SearchField _searchField;
        private GUIStyle _pathStyle;
        private string _searchText = string.Empty;
        private string _appliedSearchText = string.Empty;
        private Vector2 _scrollPosition;
        private int _selectedIndex;
        private int _focusFrameCount;
        private int _iconLoadsThisFrame;
        private int _filterDebounceMs = DefaultFilterDebounceMs;
        private bool _filterPending;
        private bool _isCapturingShortcut;
        private double _filterRequestTime;
        private double _lastEnterTime = -1d;
        private int _lastEnterIndex = -1;
        private KeyCombination _shortcutCaptureBackup;
        private QuickSearchCachedAsset _draggedAsset;
        private Vector2 _dragStartPosition;

        private static class QuickSearchUserStorage
        {
            private const string ModuleName = "QuickSearch";

            internal static string GetPrefKey(string keyName)
            {
                return FrameworkEditorUserStorage.GetPrefKey(ModuleName, keyName);
            }
        }

        private readonly struct SearchResult
        {
            internal SearchResult(QuickSearchCachedAsset asset, int score)
            {
                Asset = asset;
                Score = score;
            }

            internal QuickSearchCachedAsset Asset { get; }

            internal int Score { get; }
        }

        [MenuItem("GameFramework/快速搜索", priority = 100)]
        private static void OpenFromMenu()
        {
            OpenWindow();
        }

        private static void OpenWindow()
        {
            if (_window == null)
            {
                _window = GetWindow<QuickSearchWindow>("快速搜索");
                _window.minSize = new Vector2(450f, 500f);
            }
            _window.Show();
            _window.Focus();
            _window._searchText = string.Empty;
            _window._appliedSearchText = string.Empty;
            _window._selectedIndex = 0;
            _window._filteredAssets.Clear();
            if (_window._isCapturingShortcut)
            {
                _window.EndShortcutCapture(false);
            }
            _window._focusFrameCount = 3;
            QuickSearchCacheService.EnsureLoaded();
        }

        private static void ToggleWindowViaShortcut()
        {
            if (_window != null)
            {
                _window.Close();
                return;
            }

            OpenWindow();
        }

        private void OnEnable()
        {
            _window = this;
            _searchField = new SearchField();
            _pathStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                clipping = TextClipping.Clip,
            };
            _pathStyle.normal.textColor = Color.gray;
            _filterDebounceMs = LoadFilterDebounceMs();
            QuickSearchCacheService.CacheUpdated += OnCacheUpdated;
            QuickSearchCacheService.EnsureLoaded();
        }

        private void OnDisable()
        {
            if (_window == this)
            {
                _window = null;
            }

            QuickSearchCacheService.CacheUpdated -= OnCacheUpdated;
            EditorApplication.update -= ApplyPendingFilter;
            if (_isCapturingShortcut)
            {
                EndShortcutCapture(false);
            }
        }

        private void OnGUI()
        {
            _iconLoadsThisFrame = 0;
            if (_isCapturingShortcut)
            {
                HandleShortcutCaptureEvent();
            }

            DrawSettings();
            DrawSearchField();
            HandleKeyboardEvents();

            if (QuickSearchCacheService.IsBuilding && QuickSearchCacheService.AssetCount == 0)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("正在建立资源索引，请稍候……", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }

            DrawResults();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("设置", EditorStyles.boldLabel);

            bool enabled = EditorPrefs.GetBool(QuickSearchUserStorage.GetPrefKey(EnabledPrefKey), true);
            EditorGUI.BeginChangeCheck();
            enabled = EditorGUILayout.ToggleLeft(
                new GUIContent("启用全局快捷键", "关闭后仅可通过菜单「GameFramework/快速搜索」打开本窗口"),
                enabled);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(QuickSearchUserStorage.GetPrefKey(EnabledPrefKey), enabled);
            }

            DrawShortcutSettings();
            DrawFilterDebounceSettings();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("刷新缓存", GUILayout.Width(80f)))
            {
                QuickSearchCacheService.RequestFullRebuild(true);
            }

            string cacheStatus = QuickSearchCacheService.IsBuilding
                ? "同步中……"
                : QuickSearchCacheService.IsReady
                    ? $"{QuickSearchCacheService.AssetCount} 个资源"
                    : "未就绪";
            EditorGUILayout.LabelField($"缓存：{cacheStatus}", EditorStyles.miniLabel);
            if (!string.IsNullOrEmpty(QuickSearchCacheService.LastUpdateTime))
            {
                EditorGUILayout.LabelField(
                    $"更新：{QuickSearchCacheService.LastUpdateTime}",
                    EditorStyles.miniLabel);
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5f);
        }

        private void DrawShortcutSettings()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("打开快捷键", GUILayout.Width(80f));
            EditorGUILayout.LabelField(
                FormatKeyCombination(LoadShortcutCombination()),
                EditorStyles.boldLabel,
                GUILayout.MinWidth(120f));

            EditorGUI.BeginDisabledGroup(_isCapturingShortcut);
            if (GUILayout.Button("修改", GUILayout.Width(56f)))
            {
                BeginShortcutCapture();
            }

            if (GUILayout.Button("恢复默认", GUILayout.Width(72f)))
            {
                ResetShortcutToDefault();
            }
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();

            if (_isCapturingShortcut)
            {
                EditorGUILayout.HelpBox("请按下新的快捷键组合（Esc 取消，默认 Ctrl+P）", MessageType.Info);
            }
        }

        private void DrawFilterDebounceSettings()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("搜索防抖", GUILayout.Width(80f));
            EditorGUI.BeginChangeCheck();
            int debounceMs = EditorGUILayout.DelayedIntField(_filterDebounceMs, GUILayout.Width(60f));
            EditorGUILayout.LabelField("毫秒", GUILayout.Width(32f));
            bool debounceChanged = EditorGUI.EndChangeCheck();

            if (GUILayout.Button("恢复默认", GUILayout.Width(72f)))
            {
                SaveFilterDebounceMs(DefaultFilterDebounceMs);
            }
            else if (debounceChanged)
            {
                SaveFilterDebounceMs(debounceMs);
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(
                $"停止输入后等待该时长再搜索（{MinFilterDebounceMs}~{MaxFilterDebounceMs}ms，默认 {DefaultFilterDebounceMs}ms）",
                EditorStyles.miniLabel);
        }

        private void BeginShortcutCapture()
        {
            _shortcutCaptureBackup = LoadShortcutCombination();
            _focusFrameCount = 0;
            _isCapturingShortcut = true;
            Repaint();
        }

        private void EndShortcutCapture(bool applyNew, KeyCombination newCombination = default)
        {
            _isCapturingShortcut = false;
            if (applyNew && newCombination.keyCode != KeyCode.None)
            {
                SaveShortcutCombination(newCombination);
            }
            else
            {
                SaveShortcutCombination(_shortcutCaptureBackup);
            }
            Repaint();
        }

        private void HandleShortcutCaptureEvent()
        {
            Event currentEvent = Event.current;
            if (currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            if (currentEvent.keyCode == KeyCode.Escape)
            {
                EndShortcutCapture(false);
                currentEvent.Use();
                return;
            }

            if (!IsValidShortcutKey(currentEvent.keyCode))
            {
                return;
            }

            KeyCombination combination = KeyCombinationFromEvent(currentEvent);
            EndShortcutCapture(true, combination);
            currentEvent.Use();
        }

        private static bool IsValidShortcutKey(KeyCode keyCode)
        {
            switch (keyCode)
            {
                case KeyCode.None:
                case KeyCode.LeftControl:
                case KeyCode.RightControl:
                case KeyCode.LeftShift:
                case KeyCode.RightShift:
                case KeyCode.LeftAlt:
                case KeyCode.RightAlt:
                case KeyCode.LeftCommand:
                case KeyCode.RightCommand:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                    return false;
                default:
                    return true;
            }
        }

        private static KeyCombination KeyCombinationFromEvent(Event editorEvent)
        {
            ShortcutModifiers modifiers = ShortcutModifiers.None;
            if (editorEvent.control || editorEvent.command)
            {
                modifiers |= ShortcutModifiers.Action;
            }
            if (editorEvent.shift)
            {
                modifiers |= ShortcutModifiers.Shift;
            }
            if (editorEvent.alt)
            {
                modifiers |= ShortcutModifiers.Alt;
            }
            return new KeyCombination(editorEvent.keyCode, modifiers);
        }

        private static KeyCombination LoadShortcutCombination()
        {
            string keyCodePrefKey = QuickSearchUserStorage.GetPrefKey(ShortcutKeyCodePrefKey);
            if (!EditorPrefs.HasKey(keyCodePrefKey))
            {
                return DefaultShortcutCombination;
            }

            KeyCode keyCode = (KeyCode)EditorPrefs.GetInt(keyCodePrefKey, (int)KeyCode.P);
            ShortcutModifiers modifiers = (ShortcutModifiers)EditorPrefs.GetInt(
                QuickSearchUserStorage.GetPrefKey(ShortcutModifiersPrefKey),
                (int)ShortcutModifiers.Action);
            return new KeyCombination(keyCode, modifiers);
        }

        private static void SaveShortcutCombination(KeyCombination combination)
        {
            EditorPrefs.SetInt(QuickSearchUserStorage.GetPrefKey(ShortcutKeyCodePrefKey), (int)combination.keyCode);
            EditorPrefs.SetInt(
                QuickSearchUserStorage.GetPrefKey(ShortcutModifiersPrefKey),
                (int)combination.modifiers);
        }

        private static void ResetShortcutToDefault()
        {
            EditorPrefs.DeleteKey(QuickSearchUserStorage.GetPrefKey(ShortcutKeyCodePrefKey));
            EditorPrefs.DeleteKey(QuickSearchUserStorage.GetPrefKey(ShortcutModifiersPrefKey));
        }

        private static bool IsSameKeyCombination(KeyCombination left, KeyCombination right)
        {
            return left.keyCode == right.keyCode && left.modifiers == right.modifiers;
        }

        private static string FormatKeyCombination(KeyCombination combination)
        {
            if (combination.keyCode == KeyCode.None)
            {
                return "未设置";
            }

            StringBuilder builder = new StringBuilder(32);
            if ((combination.modifiers & ShortcutModifiers.Action) != 0)
            {
                builder.Append(Application.platform == RuntimePlatform.OSXEditor ? "Cmd+" : "Ctrl+");
            }
            if ((combination.modifiers & ShortcutModifiers.Shift) != 0)
            {
                builder.Append("Shift+");
            }
            if ((combination.modifiers & ShortcutModifiers.Alt) != 0)
            {
                builder.Append("Alt+");
            }
            builder.Append(combination.keyCode);
            return builder.ToString();
        }

        private static int LoadFilterDebounceMs()
        {
            return Mathf.Clamp(
                EditorPrefs.GetInt(
                    QuickSearchUserStorage.GetPrefKey(FilterDebounceMsPrefKey),
                    DefaultFilterDebounceMs),
                MinFilterDebounceMs,
                MaxFilterDebounceMs);
        }

        private void SaveFilterDebounceMs(int debounceMs)
        {
            _filterDebounceMs = Mathf.Clamp(debounceMs, MinFilterDebounceMs, MaxFilterDebounceMs);
            EditorPrefs.SetInt(
                QuickSearchUserStorage.GetPrefKey(FilterDebounceMsPrefKey),
                _filterDebounceMs);
        }

        private void DrawSearchField()
        {
            if (_isCapturingShortcut)
            {
                EditorGUILayout.LabelField("正在监听新快捷键……", EditorStyles.helpBox);
                EditorGUILayout.Space(5f);
                return;
            }

            Rect searchRect = GUILayoutUtility.GetRect(0f, 30f, GUILayout.ExpandWidth(true));
            string newSearchText = _searchField.OnGUI(searchRect, _searchText);
            if (!string.Equals(newSearchText, _searchText, StringComparison.Ordinal))
            {
                _searchText = newSearchText;
                RequestFilter();
                _selectedIndex = 0;
            }

            if (_focusFrameCount > 0)
            {
                _searchField.SetFocus();
                _focusFrameCount--;
            }
            EditorGUILayout.Space(5f);
        }

        private void DrawResults()
        {
            if (_filteredAssets.Count == 0)
            {
                if (!string.IsNullOrWhiteSpace(_searchText))
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField(
                        _filterPending ? "正在搜索……" : "没有找到匹配的资源",
                        EditorStyles.centeredGreyMiniLabel);
                    GUILayout.FlexibleSpace();
                }
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            for (int i = 0; i < _filteredAssets.Count; i++)
            {
                DrawResult(_filteredAssets[i], i);
            }
            EditorGUILayout.EndScrollView();
            EditorGUILayout.LabelField(
                $"↑↓ 导航 | 回车定位(不关闭) | 双回车打开(关闭) | 单击定位(不关闭) | 双击打开(关闭) | 拖拽到场景 | Esc 关闭 | 共 {_filteredAssets.Count} 个结果",
                EditorStyles.miniLabel);
        }

        private void DrawResult(QuickSearchCachedAsset asset, int index)
        {
            Rect rowRect = EditorGUILayout.BeginHorizontal(GUILayout.Height(35f));
            if (index == _selectedIndex)
            {
                EditorGUI.DrawRect(rowRect, new Color(0.25f, 0.48f, 0.78f, 0.28f));
            }

            HandleMouseEvent(asset, index, rowRect);

            GUILayout.Space(10f);
            Texture2D icon = null;
            if (_iconLoadsThisFrame < IconLoadsPerFrame)
            {
                icon = asset.Icon;
                _iconLoadsThisFrame++;
            }
            if (icon != null)
            {
                GUILayout.Label(icon, GUILayout.Width(24f), GUILayout.Height(24f));
            }
            else
            {
                GUILayout.Label(
                    EditorGUIUtility.IconContent("d_UnityEditor.ConsoleWindow"),
                    GUILayout.Width(24f),
                    GUILayout.Height(24f));
            }

            GUILayout.Space(10f);
            EditorGUILayout.BeginVertical();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(asset.Name, EditorStyles.boldLabel, GUILayout.MaxWidth(300f));
            EditorGUILayout.LabelField($"({asset.AssetType})", EditorStyles.miniLabel);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField(asset.Path, _pathStyle);
            EditorGUILayout.EndVertical();
            EditorGUILayout.EndHorizontal();
        }

        private void HandleMouseEvent(QuickSearchCachedAsset asset, int index, Rect rowRect)
        {
            Event currentEvent = Event.current;
            if (!rowRect.Contains(currentEvent.mousePosition))
            {
                return;
            }

            if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
            {
                _selectedIndex = index;
                _draggedAsset = asset;
                _dragStartPosition = currentEvent.mousePosition;
                if (currentEvent.clickCount >= 2)
                {
                    OpenAsset(asset);
                }
                else
                {
                    PingAsset(asset);
                }
                currentEvent.Use();
                Repaint();
            }
            else if (currentEvent.type == EventType.MouseDrag && _draggedAsset == asset)
            {
                if ((currentEvent.mousePosition - _dragStartPosition).sqrMagnitude > 25f)
                {
                    UnityEngine.Object loadedAsset = asset.Asset;
                    if (loadedAsset != null)
                    {
                        DragAndDrop.PrepareStartDrag();
                        DragAndDrop.objectReferences = new[] { loadedAsset };
                        DragAndDrop.paths = new[] { asset.Path };
                        DragAndDrop.StartDrag(asset.Name);
                    }
                    _draggedAsset = null;
                    currentEvent.Use();
                }
            }
            else if (currentEvent.type == EventType.MouseUp)
            {
                _draggedAsset = null;
            }
        }

        private void HandleKeyboardEvents()
        {
            if (_isCapturingShortcut)
            {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent.type == EventType.KeyUp
                && (currentEvent.keyCode == KeyCode.Return || currentEvent.keyCode == KeyCode.KeypadEnter))
            {
                if (_selectedIndex >= 0 && _selectedIndex < _filteredAssets.Count)
                {
                    double currentTime = EditorApplication.timeSinceStartup;
                    bool isDoubleEnter = _lastEnterIndex == _selectedIndex
                                         && currentTime - _lastEnterTime <= DoubleEnterInterval;
                    if (isDoubleEnter)
                    {
                        OpenAsset(_filteredAssets[_selectedIndex]);
                        _lastEnterTime = -1d;
                        _lastEnterIndex = -1;
                    }
                    else
                    {
                        PingAsset(_filteredAssets[_selectedIndex]);
                        _lastEnterTime = currentTime;
                        _lastEnterIndex = _selectedIndex;
                    }
                    currentEvent.Use();
                }
                return;
            }

            if (currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            switch (currentEvent.keyCode)
            {
                case KeyCode.DownArrow:
                    if (_filteredAssets.Count > 0)
                    {
                        _selectedIndex = Math.Min(_selectedIndex + 1, _filteredAssets.Count - 1);
                        _lastEnterIndex = -1;
                        EnsureSelectionVisible();
                    }
                    currentEvent.Use();
                    break;
                case KeyCode.UpArrow:
                    if (_filteredAssets.Count > 0)
                    {
                        _selectedIndex = Math.Max(_selectedIndex - 1, 0);
                        _lastEnterIndex = -1;
                        EnsureSelectionVisible();
                    }
                    currentEvent.Use();
                    break;
                case KeyCode.Escape:
                    Close();
                    currentEvent.Use();
                    break;
            }
        }

        private void EnsureSelectionVisible()
        {
            float itemTop = _selectedIndex * ResultHeight;
            float visibleHeight = Math.Max(0f, position.height - 90f);
            if (itemTop < _scrollPosition.y)
            {
                _scrollPosition.y = itemTop;
            }
            else if (itemTop + ResultHeight > _scrollPosition.y + visibleHeight)
            {
                _scrollPosition.y = itemTop + ResultHeight - visibleHeight;
            }
            Repaint();
        }

        private void RequestFilter()
        {
            _filterRequestTime = EditorApplication.timeSinceStartup;
            if (_filterPending)
            {
                return;
            }

            _filterPending = true;
            EditorApplication.update += ApplyPendingFilter;
        }

        private void ApplyPendingFilter()
        {
            if (EditorApplication.timeSinceStartup - _filterRequestTime < _filterDebounceMs / 1000d)
            {
                return;
            }

            EditorApplication.update -= ApplyPendingFilter;
            _filterPending = false;
            ApplyFilter(_searchText);
        }

        private void ApplyFilter(string searchText)
        {
            _appliedSearchText = searchText ?? string.Empty;
            _filteredAssets.Clear();
            _scoredResults.Clear();
            _selectedIndex = 0;

            string trimmedSearch = _appliedSearchText.Trim();
            if (trimmedSearch.Length == 0)
            {
                Repaint();
                return;
            }

            string searchLower = trimmedSearch.ToLowerInvariant();
            string fuzzySearchLower = QuickSearchCacheService.NormalizeForFuzzySearch(trimmedSearch);
            IReadOnlyList<QuickSearchCachedAsset> assets = QuickSearchCacheService.Assets;
            for (int i = 0; i < assets.Count; i++)
            {
                QuickSearchCachedAsset asset = assets[i];
                int score = GetMatchScore(asset, searchLower, fuzzySearchLower);
                if (score != int.MaxValue)
                {
                    _scoredResults.Add(new SearchResult(asset, score));
                }
            }

            _scoredResults.Sort(CompareResults);
            int resultCount = Math.Min(MaxResults, _scoredResults.Count);
            for (int i = 0; i < resultCount; i++)
            {
                _filteredAssets.Add(_scoredResults[i].Asset);
            }
            Repaint();
        }

        private static int GetMatchScore(
            QuickSearchCachedAsset asset,
            string searchLower,
            string fuzzySearchLower)
        {
            int matchTier;
            if (asset.NameLower.StartsWith(searchLower, StringComparison.Ordinal))
            {
                matchTier = 0;
            }
            else if (asset.FuzzyNameLower.StartsWith(fuzzySearchLower, StringComparison.Ordinal))
            {
                matchTier = 1;
            }
            else if (asset.NameLower.IndexOf(searchLower, StringComparison.Ordinal) >= 0)
            {
                matchTier = 2;
            }
            else if (asset.FuzzyNameLower.IndexOf(fuzzySearchLower, StringComparison.Ordinal) >= 0)
            {
                matchTier = 3;
            }
            else
            {
                return int.MaxValue;
            }

            int prefabPriority = asset.AssetType == "Prefab" ? 0 : 10;
            return prefabPriority + matchTier;
        }

        private static int CompareResults(SearchResult left, SearchResult right)
        {
            int scoreComparison = left.Score.CompareTo(right.Score);
            return scoreComparison != 0
                ? scoreComparison
                : string.Compare(left.Asset.Name, right.Asset.Name, StringComparison.OrdinalIgnoreCase);
        }

        private static void PingAsset(QuickSearchCachedAsset asset)
        {
            UnityEngine.Object loadedAsset = asset.Asset;
            if (loadedAsset == null)
            {
                return;
            }

            Selection.activeObject = loadedAsset;
            EditorGUIUtility.PingObject(loadedAsset);
        }

        private void OpenAsset(QuickSearchCachedAsset asset)
        {
            UnityEngine.Object loadedAsset = asset.Asset;
            if (loadedAsset == null)
            {
                return;
            }

            Selection.activeObject = loadedAsset;
            EditorGUIUtility.PingObject(loadedAsset);
            AssetDatabase.OpenAsset(loadedAsset);
            Close();
        }

        private void OnCacheUpdated()
        {
            if (!string.IsNullOrWhiteSpace(_appliedSearchText))
            {
                ApplyFilter(_searchText);
            }
            else
            {
                Repaint();
            }
        }

        /// <summary>
        /// 在 Unity Shortcut 系统之前监听可配置的全局快捷键。
        /// </summary>
        [InitializeOnLoad]
        private static class QuickSearchShortcutHook
        {
            private static bool _hookRegistered;
            private static EditorApplication.CallbackFunction _chainedHandler;

            static QuickSearchShortcutHook()
            {
                EditorApplication.delayCall += TryRegisterHook;
            }

            private static void TryRegisterHook()
            {
                if (_hookRegistered)
                {
                    return;
                }

                try
                {
                    FieldInfo field = typeof(EditorApplication).GetField(
                        "globalEventHandler",
                        BindingFlags.Static | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        Debug.LogWarning("[QuickSearch] 无法注册快捷键监听，请使用菜单「GameFramework/快速搜索」打开。");
                        return;
                    }

                    _chainedHandler = field.GetValue(null) as EditorApplication.CallbackFunction;
                    field.SetValue(null, (EditorApplication.CallbackFunction)(() =>
                    {
                        if (!TryHandleQuickSearchShortcut())
                        {
                            _chainedHandler?.Invoke();
                        }
                    }));
                    _hookRegistered = true;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[QuickSearch] 注册快捷键监听失败，请使用菜单打开：{exception.Message}");
                }
            }

            private static bool TryHandleQuickSearchShortcut()
            {
                if (!EditorPrefs.GetBool(QuickSearchUserStorage.GetPrefKey(EnabledPrefKey), true))
                {
                    return false;
                }

                if (_window != null && _window._isCapturingShortcut)
                {
                    return false;
                }

                Event currentEvent = Event.current;
                if (currentEvent == null || currentEvent.type != EventType.KeyDown)
                {
                    return false;
                }

                if (!IsValidShortcutKey(currentEvent.keyCode))
                {
                    return false;
                }

                KeyCombination pressedCombination = KeyCombinationFromEvent(currentEvent);
                if (!IsSameKeyCombination(pressedCombination, LoadShortcutCombination()))
                {
                    return false;
                }

                ToggleWindowViaShortcut();
                currentEvent.Use();
                return true;
            }
        }
    }
}
#endif
