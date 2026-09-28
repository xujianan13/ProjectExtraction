using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework.Editor.Scaffolding
{
    /// <summary>
    /// 验证项目描述并生成业务脚手架与 Unity 内容资产。
    /// </summary>
    public static class ScaffoldGenerator
    {
        private static readonly StringComparer Comparer = StringComparer.OrdinalIgnoreCase;
        private const string ConfigRelativePath = "config/scaffold/project.json";

        /// <summary>
        /// 执行生成、验证或 API 索引命令。
        /// </summary>
        /// <param name="request">命令参数。</param>
        /// <returns>结构化执行结果。</returns>
        public static ScaffoldResult Run(ScaffoldRequest request)
        {
            if (request == null) return Failure("request", "请求不能为空。");
            string operation = request.operation ?? "generate";
            if (operation == "validate") return Validate();
            if (operation == "refresh-api" || operation == "check-api")
            {
                bool success = operation == "refresh-api" ? ApiIndex.FrameworkApiIndex.Refresh() : ApiIndex.FrameworkApiIndex.Check();
                return new ScaffoldResult
                {
                    success = success, phase = operation,
                    errors = success ? Array.Empty<string>() : new[] { "API 索引操作失败；请检查 Unity 日志。" },
                    nextStep = success ? "API 索引已就绪。" : "检查 Unity 日志并修正后重试。"
                };
            }
            if (operation != "generate") return Failure("request", $"未知操作：{operation}");
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                return Failure("editor-state", "Unity 正在运行、编译或更新资产；请等待 Editor 空闲后生成。");
            if (!TryLoad(out ScaffoldProject project, out string error)) return Failure("config", error);

            var errors = Inspect(project);
            if (errors.Count > 0) return Failure("config", errors);
            var additions = new List<ScaffoldEntry>();
            foreach (ScaffoldEntry incoming in request.entries ?? Array.Empty<ScaffoldEntry>())
            {
                if (incoming == null) { errors.Add("请求中存在空条目。"); continue; }
                var entry = Clone(incoming);
                FillNewDefaults(project, entry);
                string shape = BuildShape(project, entry);
                if (!string.IsNullOrEmpty(entry.createdShape) && entry.createdShape != shape)
                    errors.Add($"请求条目的首次骨架结构不匹配：{entry.kind}/{entry.name}");
                else entry.createdShape = shape;
                if (project.entries.Any(existing => existing != null && existing.kind == entry.kind && existing.name == entry.name))
                    errors.Add($"条目已经存在：{entry.kind}/{entry.name}；重复生成请使用空 entries 请求。");
                if (!string.IsNullOrEmpty(entry.assetPath) && IsAssetPath(entry.assetPath, project.resourceRoot) &&
                    (File.Exists(ToUnityFullPath(entry.assetPath)) || File.Exists(ToUnityFullPath(entry.assetPath) + ".meta")))
                    errors.Add($"新条目资产已存在，不能覆盖：{entry.assetPath}");
                if (ValidIdentifier(entry.name) && ExistingTypeConflict(entry))
                    errors.Add($"新条目类型已存在，不能覆盖：{entry.kind}/{entry.name}");
                additions.Add(entry);
                project.entries.Add(entry);
            }
            if (errors.Count > 0) return Failure("preflight", errors);

            if (request.setStartup) project.startupScene = request.startupScene;
            errors.AddRange(Inspect(project));
            if (errors.Count > 0) return Failure("preflight", errors);

            IReadOnlyList<ScaffoldTextFile> textFiles = ScaffoldCodeTemplates.Build(project);
            var planned = new List<string>();
            var writes = new List<ScaffoldTextFile>();
            foreach (ScaffoldTextFile file in textFiles)
            {
                if (file == null || !IsAssetPath(file.Path, project.sourceRoot) || file.Text == null)
                {
                    errors.Add($"模板给出了无效文本路径：{file?.Path}");
                    continue;
                }
                string fullPath = ToUnityFullPath(file.Path);
                if (File.Exists(fullPath))
                {
                    if (!file.Generated && additions.Any(entry => IsNewSourcePath(file.Path, project.sourceRoot, entry)))
                    {
                        errors.Add($"新条目源码已存在，不能覆盖：{file.Path}");
                        continue;
                    }
                    if (!file.Generated) continue;
                    if (File.ReadAllText(fullPath) == file.Text) continue;
                }
                else if (!file.Generated && project.entries.Any(entry => !additions.Contains(entry) &&
                    IsNewSourcePath(file.Path, project.sourceRoot, entry)))
                {
                    errors.Add($"已有条目的源码缺失，请恢复或手工迁移：{file.Path}");
                    continue;
                }
                writes.Add(file);
                planned.Add(file.Path);
            }
            foreach (ScaffoldEntry entry in project.entries)
                if (!File.Exists(ToUnityFullPath(entry.assetPath)))
                {
                    if (additions.Contains(entry)) planned.Add(entry.assetPath);
                    else errors.Add($"已有条目的资产缺失，请恢复或手工迁移：{entry.assetPath}");
                }
            if (errors.Count > 0) return Failure("preflight", errors);
            if (!Application.isBatchMode && additions.Any(entry => entry.kind == "Scene" && entry.contentMode == "UnityScene"))
                for (int index = 0; index < SceneManager.sceneCount; index++)
                    if (string.IsNullOrEmpty(SceneManager.GetSceneAt(index).path))
                        errors.Add("当前有未保存的 Unity 场景；请先保存场景，再创建 UnityScene 内容资产。");
            if (errors.Count > 0) return Failure("preflight", errors);
            if (additions.Count > 0 || request.setStartup) planned.Add(ConfigRelativePath);
            if (request.dryRun) return new ScaffoldResult { success = true, phase = "dry-run", files = planned.ToArray(),
                entries = project.entries.ToArray(), nextStep = "确认文件、窗口 ID、资源地址及关联后执行 Generate。" };

            var transaction = new ScaffoldWriteTransaction();
            try
            {
                foreach (ScaffoldTextFile file in writes)
                {
                    string fullPath = ToUnityFullPath(file.Path);
                    bool existed = File.Exists(fullPath);
                    transaction.TrackFile(fullPath);
                    if (!existed) transaction.TrackNewSourceMeta(fullPath);
                }
                foreach (ScaffoldEntry entry in additions)
                    transaction.TrackNewAsset(ToUnityFullPath(entry.assetPath));
                string configPath = Path.Combine(RepositoryRoot, ConfigRelativePath);
                if (additions.Count > 0 || request.setStartup) transaction.TrackFile(configPath);

                foreach (ScaffoldTextFile file in writes)
                {
                    string fullPath = ToUnityFullPath(file.Path);
                    Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                    File.WriteAllText(fullPath, file.Text, new UTF8Encoding(false));
                }
                foreach (ScaffoldEntry entry in additions) ScaffoldAssets.Create(entry);
                if (additions.Count > 0 || request.setStartup)
                    File.WriteAllText(configPath, JsonUtility.ToJson(project, true) + Environment.NewLine, new UTF8Encoding(false));
                AssetDatabase.Refresh();
                AssetDatabase.SaveAssets();
            }
            catch (Exception exception)
            {
                Debug.LogError($"脚手架写入失败：{exception}");
                try
                {
                    transaction.Rollback();
                    return Failure("write", exception.Message);
                }
                catch (Exception rollbackException)
                {
                    Debug.LogError($"脚手架回滚失败：{rollbackException}");
                    return Failure("rollback", new[] { exception.Message, rollbackException.Message });
                }
            }
            return new ScaffoldResult { success = true, phase = "generated", files = planned.ToArray(),
                entries = project.entries.ToArray(), nextStep = "等待 Unity 编译完成，再执行 Validate。" };
        }

        /// <summary>
        /// 验证配置、资产和已编译类型的对应关系。
        /// </summary>
        /// <returns>结构化验证结果。</returns>
        public static ScaffoldResult Validate()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                return Failure("editor-state", "Unity 正在编译或更新资产，无法确认脚手架验证结果。");
            if (!TryLoad(out ScaffoldProject project, out string error)) return Failure("config", error);
            var errors = Inspect(project);
            if (errors.Count > 0) return Failure("validate", errors);
            IReadOnlyList<ScaffoldTextFile> files = errors.Count == 0 ? ScaffoldCodeTemplates.Build(project) : Array.Empty<ScaffoldTextFile>();
            foreach (ScaffoldTextFile file in files)
            {
                string fullPath = ToUnityFullPath(file.Path);
                if (!File.Exists(fullPath)) errors.Add($"缺少源码：{file.Path}");
                else if (file.Generated && File.ReadAllText(fullPath).Replace("\r\n", "\n") != file.Text.Replace("\r\n", "\n")) errors.Add($"生成文件与配置不一致：{file.Path}");
            }
            foreach (ScaffoldEntry entry in project.entries)
            {
                if (!File.Exists(ToUnityFullPath(entry.assetPath))) { errors.Add($"缺少资产：{entry.assetPath}"); continue; }
                if (AssetDatabase.LoadMainAssetAtPath(entry.assetPath) == null) errors.Add($"Unity 无法加载资产：{entry.assetPath}");
                string typeName = project.rootNamespace + "." + (entry.kind == "Scene" ? "Scenes" : entry.kind == "Window" ? "UI" : "Gameplay") +
                    "." + entry.name + "." + entry.name + entry.kind;
                if (!AppDomain.CurrentDomain.GetAssemblies().Any(assembly => assembly.GetType(typeName, false) != null))
                    errors.Add($"编译程序集未找到类型：{typeName}");
                if (entry.kind == "Window")
                {
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(entry.assetPath);
                    if (prefab == null || prefab.GetComponent<RectTransform>() == null ||
                        prefab.transform.Find("Panel/Title")?.GetComponent<UnityEngine.UI.Text>() == null ||
                        prefab.transform.Find("Panel/CloseButton")?.GetComponent<UnityEngine.UI.Button>() == null)
                        errors.Add($"窗口 Prefab 组件绑定不完整：{entry.assetPath}");
                }
            }
            return new ScaffoldResult { success = errors.Count == 0, phase = "validated", files = files.Select(file => file.Path).ToArray(),
                entries = project.entries.ToArray(), errors = errors.ToArray(),
                nextStep = errors.Count == 0 ? "验证通过；可执行 Prepare GamePackage。" : "修正错误后重新执行 Validate。" };
        }

        /// <summary>
        /// 读取并严格核对项目配置。
        /// </summary>
        /// <param name="project">成功时返回项目配置。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>是否读取成功。</returns>
        private static bool TryLoad(out ScaffoldProject project, out string error)
        {
            project = null;
            error = null;
            string path = Path.Combine(RepositoryRoot, ConfigRelativePath);
            if (!File.Exists(path)) { error = $"缺少配置：{path}"; return false; }
            string json = File.ReadAllText(path);
            foreach (string field in new[] { "schemaVersion", "rootNamespace", "sourceRoot", "resourceRoot", "startupScene", "reservedWindowIds", "entries" })
                if (!Regex.IsMatch(json, "\"" + field + "\"\\s*:")) { error = $"配置缺少字段：{field}"; return false; }
            try { project = JsonUtility.FromJson<ScaffoldProject>(json); }
            catch (ArgumentException exception) { error = $"项目配置 JSON 无法解析：{exception.Message}"; return false; }
            if (project == null) { error = "项目配置无法解析。"; return false; }
            return true;
        }

        /// <summary>
        /// 验证项目配置与当前项目内容是否相容。
        /// </summary>
        /// <param name="project">待验证项目配置。</param>
        /// <returns>发现的错误。</returns>
        private static List<string> Inspect(ScaffoldProject project)
        {
            var errors = new List<string>();
            if (project.schemaVersion != 1) errors.Add($"不支持的配置版本：{project.schemaVersion}");
            if (!ValidNamespace(project.rootNamespace)) errors.Add($"无效命名空间：{project.rootNamespace}");
            if (project.sourceRoot != "Assets/Scripts/GameLogic/Runtime" || !IsAssetPath(project.resourceRoot, "Assets/GameResources"))
                errors.Add("sourceRoot 或 resourceRoot 必须位于各自允许的 Assets 目录。");
            if (project.entries == null || project.reservedWindowIds == null) { errors.Add("entries 和 reservedWindowIds 不能为空。"); return errors; }
            var names = new HashSet<string>(Comparer);
            var paths = new HashSet<string>(Comparer);
            var addresses = new HashSet<string>(Comparer);
            var windowIds = new HashSet<int>();
            foreach (int reserved in project.reservedWindowIds)
                if (reserved <= 0 || !windowIds.Add(reserved)) errors.Add($"无效或重复的保留窗口 ID：{reserved}");
            foreach (ScaffoldEntry entry in project.entries)
            {
                if (entry == null) { errors.Add("配置中存在空条目。"); continue; }
                if (entry.kind != "Gameplay" && entry.kind != "Window" && entry.kind != "Scene") errors.Add($"无效种类：{entry.kind}");
                if (!ValidIdentifier(entry.name)) errors.Add($"无效 C# 名称：{entry.name}");
                if (!names.Add(entry.kind + "/" + entry.name)) errors.Add($"重复条目：{entry.kind}/{entry.name}");
                if (entry.createdShape != BuildShape(project, entry))
                    errors.Add($"首次骨架结构已变化：{entry.kind}/{entry.name}；请人工迁移业务源码或恢复配置。");
                if (!IsAssetPath(entry.assetPath, project.resourceRoot) || entry.assetPath == project.resourceRoot) errors.Add($"资产路径超出资源根目录：{entry.assetPath}");
                if (!paths.Add(entry.assetPath ?? "")) errors.Add($"重复资产路径：{entry.assetPath}");
                if (string.IsNullOrWhiteSpace(entry.address) || entry.address != Path.GetFileNameWithoutExtension(entry.assetPath))
                    errors.Add($"地址必须与资产文件名一致：{entry.assetPath}，{entry.address}");
                if (!addresses.Add(entry.address ?? "")) errors.Add($"重复资源地址：{entry.address}");
                string extension = entry.kind == "Scene" && entry.contentMode == "UnityScene" ? ".unity" : ".prefab";
                if (!string.Equals(Path.GetExtension(entry.assetPath), extension, StringComparison.OrdinalIgnoreCase))
                    errors.Add($"资产扩展名与内容模式不符：{entry.assetPath}");
                if (entry.kind == "Scene")
                {
                    if (entry.contentMode != "Prefab" && entry.contentMode != "UnityScene") errors.Add($"无效场景内容模式：{entry.contentMode}");
                    if (!string.IsNullOrEmpty(entry.gameplay) && !project.entries.Any(candidate => candidate?.kind == "Gameplay" && candidate.name == entry.gameplay))
                        errors.Add($"场景关联的玩法不存在：{entry.name} -> {entry.gameplay}");
                    if (!string.IsNullOrEmpty(entry.window) && !project.entries.Any(candidate => candidate?.kind == "Window" && candidate.name == entry.window))
                        errors.Add($"场景关联的窗口不存在：{entry.name} -> {entry.window}");
                }
                if (entry.kind == "Window")
                {
                    if (entry.windowId <= 0 || !windowIds.Add(entry.windowId)) errors.Add($"无效或冲突的窗口 ID：{entry.windowId}");
                    if (entry.preset != "Page" && entry.preset != "Popup" && entry.preset != "Modal") errors.Add($"无效窗口预设：{entry.preset}");
                    if (entry.cacheMode != "Immediate" && entry.cacheMode != "Timed" && entry.cacheMode != "Resident") errors.Add($"无效缓存模式：{entry.cacheMode}");
                    if (entry.scope != "Global" && entry.scope != "Scene" && entry.scope != "Session") errors.Add($"无效窗口作用域：{entry.scope}");
                    if (entry.maskAlpha < 0 || entry.maskAlpha > 1 || float.IsNaN(entry.maskAlpha) || float.IsInfinity(entry.maskAlpha)) errors.Add($"无效遮罩透明度：{entry.maskAlpha}");
                    if (entry.destroyDelaySeconds < 0 || float.IsNaN(entry.destroyDelaySeconds) || float.IsInfinity(entry.destroyDelaySeconds)) errors.Add($"无效销毁延迟：{entry.destroyDelaySeconds}");
                }
            }
            if (!string.IsNullOrEmpty(project.startupScene) &&
                !project.entries.Any(entry => entry?.kind == "Scene" && entry.name == project.startupScene))
                errors.Add($"启动逻辑场景不存在：{project.startupScene}");
            string settingsPath = ToUnityFullPath("Assets/GameResources/Config/GamePackageSettings.json");
            if (!string.IsNullOrEmpty(project.startupScene) && File.Exists(settingsPath))
            {
                try
                {
                    if (!string.IsNullOrEmpty(PackageRuntimeSettings.FromJson(File.ReadAllText(settingsPath)).StartupSceneLocation))
                        errors.Add("逻辑启动场景与包级 StartupSceneLocation 同时配置；请保留一个首内容入口。");
                }
                catch (Exception exception)
                {
                    errors.Add($"无法核对 GamePackageSettings：{exception.Message}");
                }
            }

            if (!Directory.Exists(ToUnityFullPath("Assets/GameResources"))) errors.Add($"资源根目录不存在：{project.resourceRoot}");
            else
            {
                var existingAddresses = new Dictionary<string, string>(Comparer);
                foreach (string fullPath in Directory.GetFiles(ToUnityFullPath("Assets/GameResources"), "*", SearchOption.AllDirectories))
                {
                    if (fullPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    string relative = "Assets/" + Path.GetRelativePath(Application.dataPath, fullPath).Replace('\\', '/');
                    string address = Path.GetFileNameWithoutExtension(fullPath);
                    if (existingAddresses.TryGetValue(address, out string previous))
                        errors.Add($"GameResources 资源地址冲突：{address}，{previous} 与 {relative}");
                    else existingAddresses.Add(address, relative);
                    ScaffoldEntry owner = project.entries.FirstOrDefault(entry => entry != null && Comparer.Equals(entry.address, address));
                    if (owner != null && !Comparer.Equals(owner.assetPath, relative))
                        errors.Add($"资源地址冲突：{address}，{relative} 与 {owner.assetPath}");
                }
            }
            return errors;
        }

        /// <summary>
        /// 为新请求补全约定的地址、资产路径和窗口 ID。
        /// </summary>
        /// <param name="project">已有配置。</param>
        /// <param name="entry">新条目。</param>
        private static void FillNewDefaults(ScaffoldProject project, ScaffoldEntry entry)
        {
            string folder = entry.kind == "Window" ? "UI" : entry.kind == "Scene" ? "Scenes" : "Gameplay";
            string prefix = entry.kind == "Window" ? "UI_" : entry.kind == "Scene" ? "SC_" : "GP_";
            string extension = entry.kind == "Scene" && entry.contentMode == "UnityScene" ? ".unity" : ".prefab";
            if (string.IsNullOrEmpty(entry.address)) entry.address = prefix + entry.name;
            if (string.IsNullOrEmpty(entry.assetPath))
                entry.assetPath = project.resourceRoot + "/" + folder + "/" + entry.name + "/" + entry.address + extension;
            if (entry.kind != "Window" || entry.windowId != 0) return;
            var used = new HashSet<int>(project.reservedWindowIds ?? Array.Empty<int>());
            foreach (ScaffoldEntry other in project.entries.Where(item => item?.kind == "Window")) used.Add(other.windowId);
            int candidate = 2000;
            while (used.Contains(candidate)) candidate++;
            entry.windowId = candidate;
        }

        /// <summary>
        /// 固定首次创建的业务骨架结构字段，防止配置静默偏离保留文件。
        /// </summary>
        /// <param name="project">项目配置。</param>
        /// <param name="entry">业务条目。</param>
        /// <returns>可比较的结构快照。</returns>
        private static string BuildShape(ScaffoldProject project, ScaffoldEntry entry) =>
            string.Join("|", project.rootNamespace, project.sourceRoot, entry.kind, entry.name,
                entry.contentMode, entry.includeModel ? "model" : "no-model", entry.gameplay, entry.window);

        /// <summary>
        /// 判断源码路径是否属于本次新建的业务条目。
        /// </summary>
        /// <param name="path">模板文件路径。</param>
        /// <param name="sourceRoot">源码根目录。</param>
        /// <param name="entry">新条目。</param>
        /// <returns>是否属于该条目。</returns>
        private static bool IsNewSourcePath(string path, string sourceRoot, ScaffoldEntry entry)
        {
            string folder = entry.kind == "Window" ? "UI" : entry.kind == "Scene" ? "Scenes" : "Gameplay";
            return path.StartsWith(sourceRoot + "/" + folder + "/" + entry.name + "/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 查找与新条目同名的已有业务类型声明。
        /// </summary>
        /// <param name="entry">新条目。</param>
        /// <returns>是否存在类型冲突。</returns>
        private static bool ExistingTypeConflict(ScaffoldEntry entry)
        {
            string root = ToUnityFullPath("Assets/Scripts/GameLogic");
            if (!Directory.Exists(root)) return false;
            string suffix = entry.kind == "Window" ? "Window" : entry.kind == "Scene" ? "Scene" : "Gameplay";
            var names = new List<string> { entry.name + suffix };
            if (entry.kind == "Gameplay")
            {
                names.Add(entry.name + "Feature");
                if (entry.includeModel) names.Add(entry.name + "Model");
            }
            foreach (string path in Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories))
            {
                string contents = File.ReadAllText(path);
                if (names.Any(name => Regex.IsMatch(contents, @"\b(class|struct|interface)\s+" + Regex.Escape(name) + @"\b")))
                    return true;
            }
            return false;
        }
        /// <summary>
        /// 复制请求条目以免改变调用方的数据。
        /// </summary>
        /// <param name="source">请求条目。</param>
        /// <returns>独立副本。</returns>
        private static ScaffoldEntry Clone(ScaffoldEntry source) => JsonUtility.FromJson<ScaffoldEntry>(JsonUtility.ToJson(source));

        /// <summary>
        /// 检查命名空间各段是否合法。
        /// </summary>
        /// <param name="value">命名空间。</param>
        /// <returns>是否合法。</returns>
        private static bool ValidNamespace(string value) => !string.IsNullOrEmpty(value) && value.Split('.').All(ValidIdentifier);

        /// <summary>
        /// 检查 C# 标识符与关键字。
        /// </summary>
        /// <param name="value">名称。</param>
        /// <returns>是否合法。</returns>
        private static bool ValidIdentifier(string value)
        {
            if (string.IsNullOrEmpty(value) || !Regex.IsMatch(value, @"^[A-Za-z_][A-Za-z0-9_]*$")) return false;
            return !("abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while".Split(' ').Contains(value));
        }

        /// <summary>
        /// 检查路径在指定资产根内。
        /// </summary>
        /// <param name="path">待检查路径。</param>
        /// <param name="root">允许的根目录。</param>
        /// <returns>是否安全。</returns>
        private static bool IsAssetPath(string path, string root)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(root) || path.Contains("\\") || path.Contains(":") ||
                path.Split('/').Any(segment => segment == "." || segment == ".." || segment.Length == 0)) return false;
            if (path.Split('/').Any(segment => segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                segment.EndsWith(" ", StringComparison.Ordinal) || segment.EndsWith(".", StringComparison.Ordinal) ||
                Regex.IsMatch(segment, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\..*)?$", RegexOptions.IgnoreCase))) return false;
            return path == root || path.StartsWith(root + "/", StringComparison.Ordinal);
        }

        /// <summary>
        /// 获取仓库根路径。
        /// </summary>
        private static string RepositoryRoot => Path.GetFullPath(Path.Combine(Application.dataPath, "../.."));

        /// <summary>
        /// 将 Unity 资产路径转换为磁盘路径。
        /// </summary>
        /// <param name="path">Assets 起始的路径。</param>
        /// <returns>磁盘绝对路径。</returns>
        private static string ToUnityFullPath(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path ?? ""));

        /// <summary>
        /// 构造错误结果。
        /// </summary>
        /// <param name="phase">失败阶段。</param>
        /// <param name="error">错误内容。</param>
        /// <returns>错误结果。</returns>
        private static ScaffoldResult Failure(string phase, string error) => Failure(phase, new[] { error });

        /// <summary>
        /// 构造错误结果。
        /// </summary>
        /// <param name="phase">失败阶段。</param>
        /// <param name="errors">错误内容。</param>
        /// <returns>错误结果。</returns>
        private static ScaffoldResult Failure(string phase, IEnumerable<string> errors) =>
            new ScaffoldResult { success = false, phase = phase, errors = errors.ToArray(), nextStep = "修正错误后重试。" };
    }
}
