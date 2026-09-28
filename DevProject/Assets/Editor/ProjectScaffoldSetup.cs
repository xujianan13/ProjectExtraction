using System.Collections.Generic;
using GameFramework.Editor.Scaffolding;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MiniGame.Editor
{
    /// <summary>
    /// 在 Editor 中创建和预览项目脚手架条目。
    /// </summary>
    public sealed class ProjectScaffoldSetup : EditorWindow
    {
        private string _kind = "Gameplay";
        private string _name = "";
        private string _preset = "Page";
        private string _contentMode = "Prefab";
        private string _gameplay = "";
        private string _window = "";
        private bool _setStartup;
        private bool _includeModel;
        private Label _output;

        /// <summary>
        /// 打开玩法创建页。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Scaffolding/New Gameplay")]
        public static void NewGameplay() => Open("Gameplay");

        /// <summary>
        /// 打开窗口创建页。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Scaffolding/New Window")]
        public static void NewWindow() => Open("Window");

        /// <summary>
        /// 打开场景创建页。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Scaffolding/New Scene")]
        public static void NewScene() => Open("Scene");

        /// <summary>
        /// 验证现有脚手架配置和产物。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Scaffolding/Validate")]
        public static void Validate() => Report(ScaffoldGenerator.Validate());

        /// <summary>
        /// 显示指定的创建页。
        /// </summary>
        /// <param name="kind">条目种类。</param>
        private static void Open(string kind)
        {
            ProjectScaffoldSetup window = GetWindow<ProjectScaffoldSetup>();
            window.titleContent = new GUIContent("Project Scaffolding");
            window._kind = kind;
            window.Show();
            window.Draw();
        }

        /// <summary>
        /// 构造 UI Toolkit 编辑器窗口。
        /// </summary>
        public void CreateGUI() => Draw();

        /// <summary>
        /// 刷新当前输入表单。
        /// </summary>
        private void Draw()
        {
            VisualElement root = rootVisualElement;
            root.Clear();
            root.style.paddingLeft = 14;
            root.style.paddingRight = 14;
            root.style.paddingTop = 12;
            root.style.paddingBottom = 12;
            root.Add(new Label("Project Scaffolding"));
            var kind = new DropdownField("Kind", new List<string> { "Gameplay", "Window", "Scene" }, _kind);
            kind.RegisterValueChangedCallback(change => { _kind = change.newValue; Draw(); });
            root.Add(kind);
            var name = new TextField("Name") { value = _name };
            name.RegisterValueChangedCallback(change => _name = change.newValue);
            root.Add(name);
            if (_kind == "Gameplay")
            {
                var model = new Toggle("Include model") { value = _includeModel };
                model.RegisterValueChangedCallback(change => _includeModel = change.newValue);
                root.Add(model);
            }
            if (_kind == "Window")
            {
                var preset = new DropdownField("Preset", new List<string> { "Page", "Popup", "Modal" }, _preset);
                preset.RegisterValueChangedCallback(change => _preset = change.newValue);
                root.Add(preset);
            }
            if (_kind == "Scene")
            {
                var mode = new DropdownField("Content mode", new List<string> { "Prefab", "UnityScene" }, _contentMode);
                mode.RegisterValueChangedCallback(change => _contentMode = change.newValue);
                root.Add(mode);
                var gameplay = new TextField("Gameplay") { value = _gameplay };
                gameplay.RegisterValueChangedCallback(change => _gameplay = change.newValue);
                root.Add(gameplay);
                var window = new TextField("Window") { value = _window };
                window.RegisterValueChangedCallback(change => _window = change.newValue);
                root.Add(window);
                var startup = new Toggle("Set startup scene") { value = _setStartup };
                startup.RegisterValueChangedCallback(change => _setStartup = change.newValue);
                root.Add(startup);
            }
            var dryRun = new Button(() => Execute(true)) { text = "Dry Run" };
            root.Add(dryRun);
            var generate = new Button(() => Execute(false)) { text = "Generate" };
            root.Add(generate);
            _output = new Label();
            _output.style.whiteSpace = WhiteSpace.Normal;
            root.Add(_output);
        }

        /// <summary>
        /// 提交预览或生成请求。
        /// </summary>
        /// <param name="dryRun">是否仅预览。</param>
        private void Execute(bool dryRun)
        {
            var entry = new ScaffoldEntry
            {
                kind = _kind, name = _name, preset = _preset, contentMode = _contentMode,
                gameplay = _gameplay, window = _window, includeModel = _includeModel
            };
            var request = new ScaffoldRequest
            {
                entries = new[] { entry }, setStartup = _kind == "Scene" && _setStartup,
                startupScene = _name, dryRun = dryRun
            };
            ScaffoldResult result = ScaffoldGenerator.Run(request);
            var lines = new List<string> { result.phase };
            foreach (ScaffoldEntry item in result.entries)
            {
                string detail = item.kind + "/" + item.name + "  address=" + item.address;
                if (item.kind == "Window") detail += "  windowId=" + item.windowId;
                if (item.kind == "Scene") detail += "  gameplay=" + item.gameplay + "  window=" + item.window;
                lines.Add(detail);
            }
            lines.AddRange(result.success ? result.files : result.errors);
            lines.Add(result.nextStep);
            _output.text = string.Join("\n", lines);
            Report(result);
        }

        /// <summary>
        /// 在 Unity 控制台记录执行结果。
        /// </summary>
        /// <param name="result">执行结果。</param>
        private static void Report(ScaffoldResult result)
        {
            if (result.success) Debug.Log($"脚手架 {result.phase}：{string.Join(", ", result.files)}");
            else Debug.LogError($"脚手架 {result.phase}：{string.Join("; ", result.errors)}");
        }
    }
}
