using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GameFramework.Editor.Scaffolding
{
    /// <summary>
    /// 将项目描述转换为可生成的 C# 文本。
    /// </summary>
    public static class ScaffoldCodeTemplates
    {
        /// <summary>
        /// 生成业务骨架与可重复生成的项目登记文件。
        /// </summary>
        /// <param name="project">已完成校验的项目描述。</param>
        /// <returns>按路径排序的文本文件计划。</returns>
        public static IReadOnlyList<ScaffoldTextFile> Build(ScaffoldProject project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            string root = project.sourceRoot.TrimEnd('/');
            string ns = project.rootNamespace;
            var entries = project.entries ?? new List<ScaffoldEntry>();
            var files = new List<ScaffoldTextFile>();
            string scaffold = root + "/Scaffold";
            files.Add(new ScaffoldTextFile(scaffold + "/Gen/ProjectContent.Gen.cs", BuildContent(ns, project.startupScene, entries), true));
            files.Add(new ScaffoldTextFile(scaffold + "/Gen/ContentIds.Gen.cs", BuildIds(entries), true));
            files.Add(new ScaffoldTextFile(scaffold + "/Gen/ContentAddresses.Gen.cs", BuildAddresses(entries), true));
            files.Add(new ScaffoldTextFile(scaffold + "/Gen/ProjectScaffoldBridge.Gen.cs", BuildBridge(), true));
            files.Add(new ScaffoldTextFile(scaffold + "/Ext/ProjectContent.Ext.cs", ContentExtTemplate, false));
            files.Add(new ScaffoldTextFile(scaffold + "/Ext/ProjectStartup.cs", StartupTemplate, false));
            foreach (ScaffoldEntry entry in entries)
            {
                string name = entry.name;
                if (entry.kind == "Gameplay")
                {
                    string path = root + "/Gameplay/" + name + "/";
                    files.Add(new ScaffoldTextFile(path + name + "Gameplay.cs", GameplayTemplate.Replace("$NS$", ns).Replace("$NAME$", name).Replace("$MODEL_ATTACH$", entry.includeModel ? "AddModel<" + name + "Model>();" : ""), false));
                    files.Add(new ScaffoldTextFile(path + name + "Feature.cs", FeatureTemplate.Replace("$NS$", ns).Replace("$NAME$", name), false));
                    if (entry.includeModel)
                        files.Add(new ScaffoldTextFile(path + name + "Model.cs", ModelTemplate.Replace("$NS$", ns).Replace("$NAME$", name), false));
                }
                else if (entry.kind == "Window")
                    files.Add(new ScaffoldTextFile(root + "/UI/" + name + "/" + name + "Window.cs", WindowTemplate.Replace("$NS$", ns).Replace("$NAME$", name), false));
                else if (entry.kind == "Scene")
                {
                    string contentStart = entry.contentMode == "UnityScene" ? UnityContentStart : PrefabContentStart;
                    string gameplay = string.IsNullOrEmpty(entry.gameplay) ? "" : GameplaySceneSnippet.Replace("$GAMEPLAY$", entry.gameplay);
                    string window = string.IsNullOrEmpty(entry.window) ? "" : WindowSceneSnippet.Replace("$WINDOW$", entry.window);
                    files.Add(new ScaffoldTextFile(root + "/Scenes/" + name + "/" + name + "Scene.cs",
                        SceneTemplate.Replace("$NS$", ns).Replace("$NAME$", name).Replace("$CONTENT_START$", contentStart).Replace("$GAMEPLAY_START$", gameplay)
                            .Replace("$WINDOW_OPEN$", window).Replace("$WINDOW_ID$", string.IsNullOrEmpty(entry.window) ? "0" : "GameLogic.Scaffold.ContentIds." + entry.window + "Window")
                            .Replace("$NS$", ns).Replace("$NAME$", name), false));
                }
            }
            return files.OrderBy(file => file.Path, StringComparer.Ordinal).ToArray();
        }

        /// <summary>
        /// 生成显式登记和玩法工厂。
        /// </summary>
        /// <param name="ns">项目根命名空间。</param>
        /// <param name="startupScene">项目逻辑首场景名称。</param>
        /// <param name="entries">项目条目。</param>
        /// <returns>项目登记源代码。</returns>
        private static string BuildContent(string ns, string startupScene, List<ScaffoldEntry> entries)
        {
            var code = new StringBuilder();
            code.AppendLine("using System;").AppendLine("using GameFramework;").AppendLine("using UnityEngine;").AppendLine();
            code.AppendLine("namespace GameLogic.Scaffold").AppendLine("{");
            code.AppendLine("    /// <summary>").AppendLine("    /// 脚手架内容的显式登记入口。").AppendLine("    /// </summary>");
            code.AppendLine("    public static partial class ProjectContent").AppendLine("    {");
            code.AppendLine("        /// <summary>").AppendLine("        /// 已配置的逻辑首场景。").AppendLine("        /// </summary>");
            code.AppendLine("        public const string StartupScene = \"" + startupScene + "\";").AppendLine();
            bool startupUsesUnityScene = entries.Any(item => item.kind == "Scene" && item.name == startupScene && item.contentMode == "UnityScene");
            code.AppendLine("        /// <summary>").AppendLine("        /// 首场景是否使用 Additive Unity 内容场景。").AppendLine("        /// </summary>");
            code.AppendLine("        public const bool StartupUsesUnityScene = " + Bool(startupUsesUnityScene) + ";").AppendLine();
            code.AppendLine("        private static LifetimeScope registeredScope;").AppendLine("        private static ISceneModule registeredScenes;")
                .AppendLine("        private static IUIModule registeredUi;").AppendLine();
            code.AppendLine("        /// <summary>").AppendLine("        /// 向当前框架模块实例登记场景和窗口。").AppendLine("        /// </summary>");
            code.AppendLine("        /// <param name=\"scenes\">场景模块。</param>").AppendLine("        /// <param name=\"ui\">窗口模块。</param>");
            code.AppendLine("        public static void Register(ISceneModule scenes, IUIModule ui)").AppendLine("        {");
            code.AppendLine("            if (scenes == null) throw new ArgumentNullException(nameof(scenes));");
            code.AppendLine("            if (ui == null) throw new ArgumentNullException(nameof(ui));");
            code.AppendLine("            LifetimeScope scope = GameEntry.ApplicationScope;");
            code.AppendLine("            if (scope == null || !scope.IsActive) throw new InvalidOperationException(\"框架应用作用域尚未就绪。\");");
            code.AppendLine("            if (!ReferenceEquals(registeredScope, scope))").AppendLine("            {");
            code.AppendLine("                registeredScenes = null;").AppendLine("                registeredUi = null;");
            code.AppendLine("                registeredScope = scope;");
            code.AppendLine("                scope.Defer(() =>").AppendLine("                {").AppendLine("                    if (!ReferenceEquals(registeredScope, scope)) return;")
                .AppendLine("                    registeredScenes = null;").AppendLine("                    registeredUi = null;").AppendLine("                    registeredScope = null;")
                .AppendLine("                });").AppendLine("            }");
            code.AppendLine("            if (ReferenceEquals(registeredScenes, scenes) && ReferenceEquals(registeredUi, ui)) return;");
            code.AppendLine("            if (!ReferenceEquals(registeredScenes, scenes))").AppendLine("            {");
            foreach (ScaffoldEntry entry in entries.Where(item => item.kind == "Scene"))
                code.AppendLine("                RegisterScene<" + ns + ".Scenes." + entry.name + "." + entry.name + "Scene>(scenes, ContentIds." + entry.name + "Scene);");
            code.AppendLine("                registeredScenes = scenes;").AppendLine("            }");
            code.AppendLine("            if (!ReferenceEquals(registeredUi, ui))").AppendLine("            {");
            var windows = entries.Where(item => item.kind == "Window").ToArray();
            if (windows.Length > 0)
            {
                code.AppendLine("                ui.ConfigureWindows(new GameWindowConfig[]").AppendLine("                {");
                foreach (ScaffoldEntry entry in windows)
                {
                    string name = entry.name;
                    string preset = entry.preset;
                    bool mask = preset == "Modal";
                    string layer = entry.layer >= 0 ? entry.layer.ToString(CultureInfo.InvariantCulture) : preset == "Page" ? "GameUILayers.Screen" : "GameUILayers.Popup";
                    string alpha = entry.maskAlpha.ToString("R", CultureInfo.InvariantCulture) + "f";
                    string delay = entry.destroyDelaySeconds.ToString("R", CultureInfo.InvariantCulture) + "f";
                    code.AppendLine("                    new GameWindowConfig(ContentIds." + name + "Window, \"" + name + "Window\", typeof(" + ns + ".UI." + name + "." + name + "Window).FullName,");
                    code.AppendLine("                        ContentAddresses." + name + "Window, " + layer + ", GameWindowScope." + entry.scope + ", GameWindowCacheMode." + entry.cacheMode + ",");
                    code.AppendLine("                        " + delay + ", " + Bool(entry.closeOnOutsideClick) + ", " + Bool(mask) + ", new Color(0f, 0f, 0f, " + alpha + ")),");
                }
                code.AppendLine("                }, registry =>").AppendLine("                {");
                foreach (ScaffoldEntry entry in windows)
                    code.AppendLine("                    registry.Register<" + ns + ".UI." + entry.name + "." + entry.name + "Window>(ContentIds." + entry.name + "Window);");
                code.AppendLine("                });");
            }
            code.AppendLine("                registeredUi = ui;").AppendLine("            }");
            code.AppendLine("            OnRegistered(scenes, ui);").AppendLine("        }");
            code.AppendLine();
            code.AppendLine("        /// <summary>").AppendLine("        /// 当前框架实例登记完成后的手写扩展点。").AppendLine("        /// </summary>");
            code.AppendLine("        /// <param name=\"scenes\">当前场景模块。</param>").AppendLine("        /// <param name=\"ui\">当前窗口模块。</param>");
            code.AppendLine("        static partial void OnRegistered(ISceneModule scenes, IUIModule ui);");
            code.AppendLine();
            code.AppendLine("        /// <summary>").AppendLine("        /// 在新框架轮次替换同名且闲置的旧场景实例。").AppendLine("        /// </summary>");
            code.AppendLine("        /// <typeparam name=\"TScene\">待登记的场景类型。</typeparam>");
            code.AppendLine("        /// <param name=\"scenes\">当前场景模块。</param>").AppendLine("        /// <param name=\"name\">稳定场景名称。</param>");
            code.AppendLine("        private static void RegisterScene<TScene>(ISceneModule scenes, string name) where TScene : IScene, new()");
            code.AppendLine("        {");
            code.AppendLine("            IScene previous = scenes.GetScene(name);");
            code.AppendLine("            if (previous != null)");
            code.AppendLine("            {");
            code.AppendLine("                if (previous.GetType() != typeof(TScene))");
            code.AppendLine("                    throw new InvalidOperationException($\"场景 {name} 已由其他类型 {previous.GetType().FullName} 登记。\");");
            code.AppendLine("                if (previous.IsEntering || previous.IsExiting || previous.IsActive || scenes.IsChanging");
            code.AppendLine("                    || ReferenceEquals(scenes.CurrentScene, previous))");
            code.AppendLine("                    throw new InvalidOperationException($\"场景 {name} 仍在使用，无法替换旧实例。\");");
            code.AppendLine("                scenes.UnregisterScene(previous);");
            code.AppendLine("                if (scenes.GetScene(name) != null)");
            code.AppendLine("                    throw new InvalidOperationException($\"场景 {name} 注销失败，可能仍位于场景栈。\");");
            code.AppendLine("            }");
            code.AppendLine("            var current = new TScene();");
            code.AppendLine("            scenes.RegisterScene(current);");
            code.AppendLine("            if (!ReferenceEquals(scenes.GetScene(name), current))");
            code.AppendLine("                throw new InvalidOperationException($\"场景 {name} 登记失败。\");");
            code.AppendLine("        }");
            foreach (ScaffoldEntry entry in entries.Where(item => item.kind == "Gameplay"))
            {
                code.AppendLine().AppendLine("        /// <summary>").AppendLine("        /// 创建全新的" + entry.name + "玩法实例。").AppendLine("        /// </summary>");
                code.AppendLine("        /// <returns>未启动的玩法实例。</returns>");
                code.AppendLine("        public static " + ns + ".Gameplay." + entry.name + "." + entry.name + "Gameplay Create" + entry.name + "Gameplay()");
                code.AppendLine("            => new " + ns + ".Gameplay." + entry.name + "." + entry.name + "Gameplay();");
            }
            code.AppendLine("    }").AppendLine("}");
            return code.ToString();
        }

        /// <summary>
        /// 生成窗口和逻辑场景 ID。
        /// </summary>
        /// <param name="entries">项目条目。</param>
        /// <returns>ID 源代码。</returns>
        private static string BuildIds(List<ScaffoldEntry> entries)
        {
            var code = new StringBuilder();
            code.AppendLine("namespace GameLogic.Scaffold").AppendLine("{").AppendLine("    /// <summary>")
                .AppendLine("    /// 脚手架内容的稳定标识。").AppendLine("    /// </summary>").AppendLine("    public static class ContentIds").AppendLine("    {");
            foreach (ScaffoldEntry entry in entries)
            {
                if (entry.kind == "Window")
                {
                    code.AppendLine("        /// <summary>").AppendLine("        /// " + entry.name + "窗口 ID。").AppendLine("        /// </summary>");
                    code.AppendLine("        public const int " + entry.name + "Window = " + entry.windowId + ";");
                }
                else if (entry.kind == "Scene")
                {
                    code.AppendLine("        /// <summary>").AppendLine("        /// " + entry.name + "逻辑场景名称。").AppendLine("        /// </summary>");
                    code.AppendLine("        public const string " + entry.name + "Scene = \"" + entry.name + "\";");
                }
            }
            code.AppendLine("    }").AppendLine("}");
            return code.ToString();
        }

        /// <summary>
        /// 生成资源地址常量。
        /// </summary>
        /// <param name="entries">项目条目。</param>
        /// <returns>地址源代码。</returns>
        private static string BuildAddresses(List<ScaffoldEntry> entries)
        {
            var code = new StringBuilder();
            code.AppendLine("namespace GameLogic.Scaffold").AppendLine("{").AppendLine("    /// <summary>")
                .AppendLine("    /// GamePackage 中的资源地址。").AppendLine("    /// </summary>").AppendLine("    public static class ContentAddresses").AppendLine("    {");
            foreach (ScaffoldEntry entry in entries)
            {
                string suffix = entry.kind == "Gameplay" ? "Gameplay" : entry.kind == "Window" ? "Window" : "Scene";
                code.AppendLine("        /// <summary>").AppendLine("        /// " + entry.name + "内容地址。").AppendLine("        /// </summary>");
                code.AppendLine("        public const string " + entry.name + suffix + " = \"" + entry.address + "\";");
            }
            code.AppendLine("    }").AppendLine("}");
            return code.ToString();
        }

        /// <summary>
        /// 将布尔值写为 C# 字面量。
        /// </summary>
        /// <param name="value">布尔值。</param>
        /// <returns>C# 字面量。</returns>
        private static string Bool(bool value) => value ? "true" : "false";

        /// <summary>
        /// 生成固定命名空间的项目接入桥。
        /// </summary>
        /// <returns>桥接源代码。</returns>
        private static string BuildBridge() => BridgeTemplate;

        private const string BridgeTemplate = @"using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Scaffold
{
    /// <summary>
    /// GameMain 的稳定脚手架接入点。
    /// </summary>
    public static class ProjectScaffoldBridge
    {
        /// <summary>
        /// 项目配置的逻辑首场景。
        /// </summary>
        public static string StartupScene => global::GameLogic.Scaffold.ProjectContent.StartupScene;

        /// <summary>
        /// 向框架登记项目内容。
        /// </summary>
        /// <param name=""scenes"">场景模块。</param>
        /// <param name=""ui"">窗口模块。</param>
        public static void Register(ISceneModule scenes, IUIModule ui)
            => global::GameLogic.Scaffold.ProjectContent.Register(scenes, ui);

        /// <summary>
        /// 进入项目逻辑首场景。
        /// </summary>
        /// <param name=""ct"">启动取消令牌。</param>
        /// <returns>场景进入任务。</returns>
        public static UniTask StartAsync(CancellationToken ct)
            => global::GameLogic.Scaffold.ProjectStartup.StartAsync(ct);
    }
}
";

        private const string ContentExtTemplate = @"using GameFramework;

namespace GameLogic.Scaffold
{
    /// <summary>
    /// 项目登记后的手写扩展。
    /// </summary>
    public static partial class ProjectContent
    {
        /// <summary>
        /// 当前框架实例登记完成后的业务扩展。
        /// </summary>
        /// <param name=""scenes"">当前场景模块。</param>
        /// <param name=""ui"">当前窗口模块。</param>
        static partial void OnRegistered(ISceneModule scenes, IUIModule ui)
        {
        }
    }
}
";
        private const string StartupTemplate = @"using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine.SceneManagement;

namespace GameLogic.Scaffold
{
    /// <summary>
    /// 项目手写启动入口；按需要在这里扩展业务准备。
    /// </summary>
    public static class ProjectStartup
    {
        /// <summary>
        /// 项目逻辑首场景；空值沿用现有包场景或三关演示。
        /// </summary>
        public static string StartupScene => ProjectContent.StartupScene;

        /// <summary>
        /// 进入项目逻辑首场景。
        /// </summary>
        /// <param name=""ct"">启动取消令牌。</param>
        /// <returns>场景进入任务。</returns>
        public static UniTask StartAsync(CancellationToken ct)
        {
            if (ProjectContent.StartupUsesUnityScene)
            {
                Scene host = SceneManager.GetSceneByName(""[ScaffoldHost]"");
                if (!host.IsValid() || !host.isLoaded) host = SceneManager.CreateScene(""[ScaffoldHost]"");
                SceneManager.SetActiveScene(host);
            }
            ISceneModule scenes = GameEntry.Get<ISceneModule>();
            return scenes.ChangeSceneAsync(StartupScene, ct: ct);
        }
    }
}
";

        private const string GameplayTemplate = @"using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace $NS$.Gameplay.$NAME$
{
    /// <summary>
    /// $NAME$玩法根；在这里扩展玩法规则。
    /// </summary>
    public sealed class $NAME$Gameplay : GameplayRoot<$NAME$Gameplay>
    {
        /// <summary>
        /// 创建玩法并连接本地功能模块。
        /// </summary>
        public $NAME$Gameplay()
        {
            AddFeature<$NAME$Feature>();
            $MODEL_ATTACH$
        }

        /// <summary>
        /// 初始化不属于单轮进入的玩法数据。
        /// </summary>
        /// <param name=""ct"">初始化取消令牌。</param>
        /// <returns>初始化任务。</returns>
        protected override UniTask OnInitGameplayAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 每帧推进玩法规则。
        /// </summary>
        /// <param name=""deltaTime"">本帧经过的秒数。</param>
        protected override void OnUpdateGameplay(float deltaTime) { }

        /// <summary>
        /// 在固定步长阶段更新物理玩法。
        /// </summary>
        /// <param name=""fixedDeltaTime"">本次固定步长秒数。</param>
        protected override void OnFixedUpdateGameplay(float fixedDeltaTime) { }

        /// <summary>
        /// 在帧末阶段更新跟随和表现。
        /// </summary>
        /// <param name=""deltaTime"">本帧经过的秒数。</param>
        protected override void OnLateUpdateGameplay(float deltaTime) { }

    }
}
";

        private const string FeatureTemplate = @"using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace $NS$.Gameplay.$NAME$
{
    /// <summary>
    /// $NAME$玩法内容；每轮进入持有独立 PrefabPool。
    /// </summary>
    public sealed class $NAME$Feature : GameplayFeature<$NAME$Gameplay>
    {
        private PrefabPool pool;
        private PrefabInstance instance;
        private LifetimeScope ownedScope;

        /// <summary>
        /// 加载并生成本轮玩法内容。
        /// </summary>
        /// <param name=""enterData"">玩法进入参数。</param>
        /// <param name=""ct"">本轮取消令牌。</param>
        /// <returns>内容进入任务。</returns>
        public override async UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
        {
            LifetimeScope scope = Root.GameplayScope;
            ownedScope = scope;
            bool entered = false;
            try
            {
                PrefabPool created = await GameEntry.Get<IPrefabPoolModule>().CreatePoolAsync(
                    GameLogic.Scaffold.ContentAddresses.$NAME$Gameplay, ct: ct);
                if (created == null) throw new InvalidOperationException(""无法创建$NAME$内容池。"");
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    await created.DisposeAsync();
                    return;
                }
                pool = created;
                instance = pool.Spawn();
                if (instance == null) throw new InvalidOperationException(""无法生成$NAME$内容实例。"");
                entered = true;
            }
            finally
            {
                if (!entered) await CleanupAsync(scope);
            }
        }

        /// <summary>
        /// 等待本轮实例和资源租约释放。
        /// </summary>
        /// <param name=""ct"">独立退出令牌。</param>
        /// <returns>资源释放任务。</returns>
        public override UniTask ExitAsync(CancellationToken ct = default) => CleanupAsync(ownedScope);

        /// <summary>
        /// 强制停止并安排释放；异步销毁由 PrefabPool 自行完成。
        /// </summary>
        public override void ForceExit()
        {
            instance?.Dispose();
            instance = null;
            pool?.Dispose();
            pool = null;
            ownedScope = null;
        }

        /// <summary>
        /// 清理指定轮次持有的池和实例。
        /// </summary>
        /// <param name=""scope"">进入时捕获的作用域。</param>
        /// <returns>资源释放任务。</returns>
        private async UniTask CleanupAsync(LifetimeScope scope)
        {
            if (!ReferenceEquals(ownedScope, scope)) return;
            PrefabInstance currentInstance = instance;
            PrefabPool currentPool = pool;
            instance = null;
            pool = null;
            ownedScope = null;
            currentInstance?.Dispose();
            if (currentPool != null) await currentPool.DisposeAsync();
        }
    }
}
";

        private const string ModelTemplate = @"using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace $NS$.Gameplay.$NAME$
{
    /// <summary>
    /// $NAME$玩法的可选数据模型。
    /// </summary>
    public sealed class $NAME$Model : GameplayModel<$NAME$Gameplay>
    {
        /// <summary>
        /// 初始化模型。
        /// </summary>
        /// <param name=""ct"">初始化取消令牌。</param>
        /// <returns>初始化任务。</returns>
        public override UniTask InitAsync(CancellationToken ct = default) => UniTask.CompletedTask;
    }
}
";

        private const string WindowTemplate = @"using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine.UI;

namespace $NS$.UI.$NAME$
{
    /// <summary>
    /// $NAME$窗口逻辑；显示组件保存在资源 Prefab 中。
    /// </summary>
    public sealed class $NAME$Window : GameWindow
    {
        private Text title;
        private Button closeButton;

        /// <summary>
        /// 绑定窗口必需组件。
        /// </summary>
        protected override void BindComponents()
        {
            title = FindRequired<Text>(""Panel/Title"");
            closeButton = FindRequired<Button>(""Panel/CloseButton"");
        }

        /// <summary>
        /// 每轮打开登记按钮监听。
        /// </summary>
        /// <param name=""args"">打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
            OpenScope.Defer(() => closeButton.onClick.RemoveListener(OnCloseClicked));
        }

        /// <summary>
        /// 刷新本次显示。
        /// </summary>
        /// <param name=""args"">显示参数。</param>
        protected override void Show(GameWindowOpenArgs args)
        {
            title.text = args.UserData as string ?? ""$NAME$"";
        }

        /// <summary>
        /// 用户点击关闭按钮。
        /// </summary>
        private void OnCloseClicked() => CloseSelfAsync().Forget();
    }
}
";

        private const string SceneTemplate = @"using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine.SceneManagement;
using YooAsset;

namespace $NS$.Scenes.$NAME$
{
    /// <summary>
    /// $NAME$逻辑场景；内容和玩法均由本轮进入持有。
    /// </summary>
    public sealed class $NAME$Scene : SceneBase
    {
        private LifetimeScope ownedScope;
        private IGameplayModule gameplays;
        private GameplayHandle gameplayHandle;
        private IUIModule ui;
        private bool openedWindow;
        private PrefabPool pool;
        private PrefabInstance instance;
        private SceneHandle sceneHandle;

        /// <summary>
        /// 创建逻辑场景。
        /// </summary>
        public $NAME$Scene() : base(GameLogic.Scaffold.ContentIds.$NAME$Scene) { }

        /// <summary>
        /// 加载内容并进入关联玩法。
        /// </summary>
        /// <param name=""enterData"">场景进入参数。</param>
        /// <param name=""ct"">本轮取消令牌。</param>
        /// <returns>场景进入任务。</returns>
        protected override async UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct)
        {
            LifetimeScope scope = SceneScope;
            ownedScope = scope;
            bool entered = false;
            try
            {
                $CONTENT_START$
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive) return;
                $GAMEPLAY_START$
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive) return;
                $WINDOW_OPEN$
                entered = true;
            }
            finally
            {
                if (!entered) await CleanupAsync(scope);
            }
        }

        /// <summary>
        /// 等待本轮关联玩法和内容退出。
        /// </summary>
        /// <param name=""ct"">独立退出令牌。</param>
        /// <returns>退出任务。</returns>
        protected override UniTask OnExitSceneAsync(CancellationToken ct) => CleanupAsync(ownedScope);

        /// <summary>
        /// 强制停止当前轮次并安排异步资源清理。
        /// </summary>
        protected override void OnForceExit()
        {
            GameplayHandle handle = gameplayHandle;
            IGameplayModule currentGameplays = gameplays;
            PrefabInstance currentInstance = instance;
            PrefabPool currentPool = pool;
            SceneHandle currentScene = sceneHandle;
            IUIModule currentUi = ui;
            bool closeWindow = openedWindow;
            ownedScope = null;
            gameplayHandle = GameplayHandle.Invalid;
            gameplays = null;
            instance = null;
            pool = null;
            sceneHandle = null;
            ui = null;
            openedWindow = false;
            try
            {
                if (closeWindow && currentUi != null)
                    currentUi.CloseWindowAsync($WINDOW_ID$, GameWindowCloseReason.ScopeExit, CancellationToken.None).Forget();
            }
            finally
            {
                try
                {
                    if (handle.IsValid && currentGameplays != null) currentGameplays.Stop(handle);
                }
                finally
                {
                    try
                    {
                        currentInstance?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            currentPool?.Dispose();
                        }
                        finally
                        {
                            if (currentScene != null) currentScene.UnloadSceneAsync();
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 清理指定轮次拥有的对象和租约。
        /// </summary>
        /// <param name=""scope"">进入时捕获的作用域。</param>
        /// <returns>完成释放后的任务。</returns>
        private async UniTask CleanupAsync(LifetimeScope scope)
        {
            if (!ReferenceEquals(ownedScope, scope)) return;
            GameplayHandle handle = gameplayHandle;
            IGameplayModule currentGameplays = gameplays;
            PrefabInstance currentInstance = instance;
            PrefabPool currentPool = pool;
            SceneHandle currentScene = sceneHandle;
            IUIModule currentUi = ui;
            bool closeWindow = openedWindow;
            ownedScope = null;
            gameplayHandle = GameplayHandle.Invalid;
            gameplays = null;
            instance = null;
            pool = null;
            sceneHandle = null;
            ui = null;
            openedWindow = false;
            try
            {
                if (closeWindow && currentUi != null)
                    await currentUi.CloseWindowAsync($WINDOW_ID$, GameWindowCloseReason.ScopeExit, CancellationToken.None);
            }
            finally
            {
                try
                {
                    if (handle.IsValid && currentGameplays != null)
                        await currentGameplays.StopAsync(handle, CancellationToken.None);
                }
                finally
                {
                    try
                    {
                        currentInstance?.Dispose();
                    }
                    finally
                    {
                        try
                        {
                            if (currentPool != null) await currentPool.DisposeAsync();
                        }
                        finally
                        {
                            if (currentScene != null)
                            {
                                var operation = currentScene.UnloadSceneAsync();
                                await UniTask.WaitUntil(() => operation.IsDone);
                            }
                        }
                    }
                }
            }
        }
    }
}
";

        private const string PrefabContentStart = @"PrefabPool created = await GameEntry.Get<IPrefabPoolModule>().CreatePoolAsync(
                    GameLogic.Scaffold.ContentAddresses.$NAME$Scene, ct: ct);
                if (created == null) throw new InvalidOperationException(""无法创建$NAME$场景内容池。"");
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    await created.DisposeAsync();
                    return;
                }
                pool = created;
                instance = pool.Spawn();
                if (instance == null) throw new InvalidOperationException(""无法生成$NAME$场景内容。"");";

        private const string UnityContentStart = @"SceneHandle loaded = await GameEntry.Get<IResourceModule>().LoadSceneAsync(
                    GameLogic.Scaffold.ContentAddresses.$NAME$Scene, LoadSceneMode.Additive, ct);
                if (loaded.Status != EOperationStatus.Succeeded)
                {
                    string error = loaded.Error;
                    loaded.Release();
                    throw new InvalidOperationException(""无法加载$NAME$内容场景："" + error);
                }
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    var operation = loaded.UnloadSceneAsync();
                    await UniTask.WaitUntil(() => operation.IsDone);
                    return;
                }
                sceneHandle = loaded;";

        private const string GameplaySceneSnippet = @"gameplays = GameEntry.Get<IGameplayModule>();
                GameplayHandle started = await gameplays.StartAsync(
                    GameLogic.Scaffold.ProjectContent.Create$GAMEPLAY$Gameplay(), ct: ct);
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    if (started.IsValid) await gameplays.StopAsync(started);
                    return;
                }
                gameplayHandle = started;";

        private const string WindowSceneSnippet = @"ui = GameEntry.Get<IUIModule>();
                await ui.OpenWindowAsync(GameLogic.Scaffold.ContentIds.$WINDOW$Window, ct: ct);
                openedWindow = true;";
    }
}
