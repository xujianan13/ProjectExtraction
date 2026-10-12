using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFramework.Editor.ApiIndex
{
    /// <summary>
    /// 从已编译接口与辅助类型生成任务索引，并核对已提交的索引快照。
    /// </summary>
    public static class FrameworkApiIndex
    {
        private const string IndexPath = "docs/framework/api/index.json";
        private const string ReadmePath = "docs/framework/api/README.md";
        private const string RecipePath = "DevProject/Assets/Tests/PlayMode/ApiRecipes.cs";
        private const string LifetimeGuide = "docs/framework/生命周期作用域使用指南.md";

        private sealed class Entry
        {
            internal readonly string Name;
            internal readonly string Task;
            internal readonly string Source;
            internal readonly string Guide;
            internal readonly string Owner;
            internal readonly string Cancellation;
            internal readonly string Failure;
            internal readonly string Thread;
            internal readonly string Paused;

            /// <summary>
            /// 创建一个经人工核对的模块约定。
            /// </summary>
            /// <param name="name">注册时使用的接口名。</param>
            /// <param name="task">用户要完成的任务。</param>
            /// <param name="source">接口源码相对路径。</param>
            /// <param name="guide">对应使用指南相对路径。</param>
            /// <param name="owner">资源或句柄所有者。</param>
            /// <param name="cancellation">取消规则。</param>
            /// <param name="failure">失败规则。</param>
            /// <param name="thread">线程规则。</param>
            /// <param name="paused">玩法暂停时的规则。</param>
            internal Entry(string name, string task, string source, string guide, string owner, string cancellation, string failure,
                string thread = "Unity 主线程", string paused = "按具体调用及模块文档")
            {
                Name = name;
                Task = task;
                Source = source;
                Guide = guide;
                Owner = owner;
                Cancellation = cancellation;
                Failure = failure;
                Thread = thread;
                Paused = paused;
            }
        }

        private static readonly Entry[] Entries =
        {
            Module("ILogModule", "记录运行日志", "Modules/Log/ILogModule.cs", "docs/framework/运行诊断.md", "模块持有日志 Sink", "同步调用无取消", "按日志等级与 Sink 配置输出"),
            Module("IDiagnosticsModule", "检查资源、输入与模块状态", "Modules/Diagnostics/IDiagnosticsModule.cs", "docs/framework/运行诊断.md", "调用方释放 RegisterProvider 返回的订阅", "同步快照无取消", "快照反映调用时状态"),
            Module("IEventModule", "发布和订阅业务事件", "Modules/Event/IEventModule.cs", LifetimeGuide, "订阅者在作用域结束时退订", "作用域结束后不再处理", "回调错误按事件模块实现记录"),
            Module("ITimerModule", "延迟、循环及下一帧任务", "Modules/Timer/ITimerModule.cs", LifetimeGuide, "作用域持有 TimerHandle", "取消句柄或传入 CancellationToken", "取消后不执行回调", paused: "缩放时间计时随玩法暂停；可选 unscaledTime"),
            Module("ITweenModule", "播放属性动画", "Modules/Tween/ITweenModule.cs", "docs/framework/通用Tween.md", "传入的 LifetimeScope 持有 TweenHandle", "作用域结束或令牌取消时停止", "目标消失时动画停止", paused: "默认缩放时间；UI 可指定 unscaledTime"),
            Module("IStorageModule", "保存轻量偏好", "Modules/Storage/IStorageModule.cs", "docs/framework/正式存档使用指南.md", "StorageModule 管理命名空间", "同步 API 无取消", "持久化依赖底层存储"),
            Module("ISaveModule", "保存正式进度", "Modules/Save/Contracts/ISaveModule.cs", "docs/framework/正式存档使用指南.md", "项目持有 SaveSession 并等待 CloseAsync", "保存操作使用独立清理令牌", "检查 SaveResult 而非假定成功"),
            Module("IPoolModule", "复用普通对象", "Modules/Pool/IPoolModule.cs", LifetimeGuide, "借用方归还对象", "同步归还无取消", "无效归还按池实现处理"),
            Module("IResourceModule", "加载资产、内容场景及下载资源包", "Modules/Resource/IResourceModule.cs", "docs/framework/按需资源加载.md", "调用方或作用域释放 ResourceLease；场景显式卸载", "异步调用传入本轮令牌", "检查租约、下载状态和错误"),
            Module("IPrefabPoolModule", "创建可复用 Unity 实例", "Modules/PrefabPool/IPrefabPoolModule.cs", "docs/framework/Prefab实例池.md", "调用方持有 PrefabPool 并等待 DisposeAsync", "创建池传入本轮令牌", "创建或 Spawn 可返回空"),
            Module("IInputModule", "读取动作与管理输入上下文", "Modules/Input/IInputModule.cs", "docs/framework/游戏输入系统使用指南.md", "作用域释放上下文与订阅", "重绑定使用本轮令牌", "检查加载及重绑定结果", paused: "玩法暂停时输入由上下文和业务控制"),
            Module("IFlowModule", "运行通用流程", "Modules/Flow/IFlowModule.cs", LifetimeGuide, "调用方保存 FlowHandle 并取消", "Cancel 结束运行", "检查句柄和状态"),
            Module("IHttpModule", "发送 HTTP 请求", "Modules/Http/IHttpModule.cs", "docs/framework/AI开发入口.md", "调用方持有请求任务", "传入请求令牌", "检查 HttpResponse"),
            Module("INetworkModule", "管理网络连接", "Modules/Network/INetworkModule.cs", "docs/framework/AI开发入口.md", "调用方 Destroy 对应连接", "连接自己的操作负责取消", "创建和查找可能返回空"),
            Module("IAudioModule", "播放音乐与音效", "Modules/Audio/IAudioModule.cs", "docs/framework/音频系统.md", "调用方保存 AudioHandle 并停止循环音频", "加载时传入令牌；退出停止句柄", "检查句柄及播放状态", paused: "模块响应应用后台；业务暂停策略由项目决定"),
            Module("IModelModule", "重置项目模型", "Modules/Model/IModelModule.cs", "docs/framework/AI开发入口.md", "模块管理模型生命周期", "同步操作无取消", "模型实现负责自身错误"),
            Module("ISceneModule", "切换逻辑场景", "Modules/Scene/ISceneModule.cs", "docs/framework/关卡与单局流程.md", "场景本轮内容由 SceneScope 持有", "切换传入进入令牌；退出使用清理令牌", "进入失败需清理部分内容"),
            Module("IGameplayModule", "启动及停止玩法", "Modules/Gameplay/IGameplayModule.cs", "docs/framework/关卡与单局流程.md", "调用方保存 GameplayHandle 并等待 StopAsync", "进入传本轮令牌；退出用独立令牌", "启动失败不可继续使用句柄"),
            Module("IServiceModule", "管理项目服务", "Modules/Service/IServiceModule.cs", "docs/framework/AI开发入口.md", "模块初始化和关闭服务", "同步操作无取消", "服务实现负责自身错误"),
            Module("IUIModule", "配置、打开和关闭窗口", "UI/IUIModule.cs", "docs/framework/UI系统.md", "UI 管理器持有窗口租约；OpenScope 持有本轮订阅", "打开和关闭传入对应令牌", "检查窗口或 bool 返回值", paused: "窗口交互和非缩放动画可继续")
        };

        private static readonly Entry[] CompanionEntries =
        {
            Module("AnimatorPlayback", "播放、暂停和采样 Animator 状态", "Utilities/Animation/AnimatorPlayback.cs", "docs/framework/通用动画播放.md", "调用方在模型回收前 Dispose；不销毁模型或资源", "Stop 停止播放；Dispose 恢复原 Animator 配置", "无效参数或缺失状态返回 false", paused: "本地模式由 SetPaused 控制；外部采样由调用方时钟决定"),
            Module("AnimationPlaybackState", "读取动画层播放进度", "Utilities/Animation/AnimatorPlayback.cs", "docs/framework/通用动画播放.md", "不可变快照，无需释放", "同步读取无取消", "TryGetState 返回 false 时输出默认值"),
            Module("LifetimeScope", "托管本轮生命周期", "Core/Lifetime/LifetimeScope.cs", LifetimeGuide, "创建者持有并释放作用域", "Stop 取消本轮；Dispose 完成同步清理", "失活后登记立即清理"),
            Module("LifetimeScopeExtensions", "将事件、输入、计时器和资源绑定到作用域", "Core/Lifetime/LifetimeScopeExtensions.cs", LifetimeGuide, "调用方的 LifetimeScope 持有登记", "作用域停止后回调失活", "迟到资源自动释放"),
            Module("SceneBase", "实现逻辑场景生命周期", "Modules/Scene/SceneBase.cs", "docs/framework/关卡与单局流程.md", "SceneScope 持有本轮工作", "退出使用独立令牌", "进入失败时释放本轮作用域"),
            Module("GameplayRoot", "实现玩法根节点", "Modules/Gameplay/GameplayRoot.cs", "docs/framework/关卡与单局流程.md", "GameplayScope 持有本轮工作", "退出令牌独立于 GameplayToken", "进入失败时清理本轮工作"),
            Module("GameplayFeature", "实现玩法功能组件", "Modules/Gameplay/GameplayFeature.cs", LifetimeGuide, "所属 GameplayRoot 管理 Feature", "Enter 后使用 GameplayScope", "由玩法根节点管理失败清理"),
            Module("IFixedUpdateModule", "接入固定步长模块更新", "Core/IFixedUpdateModule.cs", "docs/framework/玩法更新阶段.md", "ModuleManager 管理注册模块", "同步阶段无取消", "异常由模块派发边界处理"),
            Module("ILateUpdateModule", "接入帧末模块更新", "Core/ILateUpdateModule.cs", "docs/framework/玩法更新阶段.md", "ModuleManager 管理注册模块", "同步阶段无取消", "异常由模块派发边界处理"),
            Module("IFixedUpdateGameplay", "接入玩法固定步长更新", "Modules/Gameplay/IFixedUpdateGameplay.cs", "docs/framework/玩法更新阶段.md", "玩法生命周期控制派发", "暂停或退出停止后续回调", "旧玩法无需实现该接口"),
            Module("ILateUpdateGameplay", "接入玩法帧末更新", "Modules/Gameplay/ILateUpdateGameplay.cs", "docs/framework/玩法更新阶段.md", "玩法生命周期控制派发", "暂停或退出停止后续回调", "旧玩法无需实现该接口"),
            Module("GameVirtualList", "显示大量固定尺寸列表项", "UI/Controls/GameVirtualList.cs", "docs/framework/UI虚拟列表.md", "列表持有可见项及缓冲实例", "禁用或销毁结束条目作用域", "配置无效时拒绝初始化"),
            Module("GameVirtualListItem", "实现可复用列表项", "UI/Controls/GameVirtualListItem.cs", "docs/framework/UI虚拟列表.md", "BindingScope 持有本次绑定工作", "异步资源使用绑定令牌并检查绑定轮次", "回收后迟到结果不得写入"),
            Module("GameWindow", "实现窗口生命周期", "UI/Windows/GameWindow.cs", "docs/framework/UI系统.md", "OpenScope 持有本轮监听", "关闭使 OpenScope 失活", "重复 Show 只刷新显示"),
            Module("Tween", "创建串行与并行动画片段", "Modules/Tween/TweenClip.cs", "docs/framework/通用Tween.md", "ITweenModule.Play 返回播放句柄", "作用域或句柄结束播放", "目标销毁后不再写入"),
            Module("TweenTargets", "绑定动画目标属性", "Modules/Tween/TweenTargets.cs", "docs/framework/通用Tween.md", "目标对象由业务持有", "作用域取消动画", "目标销毁后停止写入"),
            Module("PrefabPool", "生成、回收并释放 Prefab 实例", "Modules/PrefabPool/PrefabPool.cs", "docs/framework/Prefab实例池.md", "场景或玩法持有池", "等待 DisposeAsync 完成租约释放", "Spawn 失败时返回空"),
            Module("PrefabInstance", "管理一次 Prefab 生成轮次", "Modules/PrefabPool/PrefabInstance.cs", "docs/framework/Prefab实例池.md", "PrefabPool 持有实例", "Despawn 结束 SpawnScope", "失效句柄不可继续使用"),
            Module("SaveSession`1", "激活、写入并关闭正式存档会话", "Modules/Save/Session/SaveSession.cs", "docs/framework/正式存档使用指南.md", "项目持有会话并等待 CloseAsync", "关闭用独立清理令牌", "检查 SaveResult 和会话状态"),
            Module("SaveStateJournal", "保存有序状态增量并恢复完整提交", "Modules/Save/Repository/SaveStateJournal.cs", "docs/framework/正式存档使用指南.md", "业务会话独占日志与 FIFO 提交队列", "在途写入完成后再关闭后端", "失败保留原候选并显式重试；检查 SaveResult", paused: "暂停时仍可完成已接纳的保存"),
            Module("GameRoundFlow", "管理单局进入、结算和返回菜单", "Modules/Gameplay/Flow/GameRoundFlow.cs", "docs/framework/关卡与单局流程.md", "项目持有流程并等待 CloseAsync", "CancelLoading 取消进入", "检查状态、错误和 bool 结果")
        };

        /// <summary>
        /// 刷新版本管理中的 Markdown 和 JSON 索引。
        /// </summary>
        /// <returns>全部条目和路径有效且写入成功时返回 true。</returns>
        public static bool Refresh()
        {
            if (!Build(out string markdown, out string json)) return false;
            string root = RepositoryRoot();
            WriteIfChanged(Path.Combine(root, ReadmePath), markdown);
            WriteIfChanged(Path.Combine(root, IndexPath), json);
            Debug.Log("Framework API 索引已刷新。");
            return true;
        }

        /// <summary>
        /// 对比当前程序集与已提交索引，检查签名和人工引用是否失效。
        /// </summary>
        /// <returns>所有条目有效且两个索引与当前程序集一致时返回 true。</returns>
        public static bool Check()
        {
            if (!Build(out string markdown, out string json)) return false;
            string root = RepositoryRoot();
            bool valid = Same(Path.Combine(root, ReadmePath), markdown) && Same(Path.Combine(root, IndexPath), json);
            if (!valid) Debug.LogError("Framework API 索引与已编译 API 不一致；请检查差异后执行 Refresh API Index。");
            return valid;
        }

        /// <summary>
        /// 从 Unity 菜单刷新索引。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Scaffolding/Refresh API Index")]
        private static void RefreshMenu() => Refresh();

        /// <summary>
        /// 从 Unity 菜单检查索引。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Scaffolding/Check API Index")]
        private static void CheckMenu() => Check();

        /// <summary>
        /// 创建统一源码根下的模块条目。
        /// </summary>
        /// <param name="name">接口名。</param>
        /// <param name="task">检索任务。</param>
        /// <param name="source">接口源码子路径。</param>
        /// <param name="guide">指南路径。</param>
        /// <param name="owner">所有权说明。</param>
        /// <param name="cancellation">取消语义。</param>
        /// <param name="failure">失败语义。</param>
        /// <param name="thread">线程约束。</param>
        /// <param name="paused">暂停语义。</param>
        /// <returns>模块元数据。</returns>
        private static Entry Module(string name, string task, string source, string guide, string owner, string cancellation, string failure,
            string thread = "Unity 主线程", string paused = "按具体调用及模块文档") =>
            new Entry(name, task, "DevProject/Assets/Scripts/GameFramework/Runtime/" + source, guide, owner, cancellation, failure, thread, paused);

        /// <summary>
        /// 核对条目后构造确定性的两个输出。
        /// </summary>
        /// <param name="markdown">Markdown 索引内容。</param>
        /// <param name="json">JSON 索引内容。</param>
        /// <returns>全部元数据有效时返回 true。</returns>
        private static bool Build(out string markdown, out string json)
        {
            markdown = null;
            json = null;
            string root = RepositoryRoot();
            if (!File.Exists(Path.Combine(root, RecipePath))) { Debug.LogError("API recipe 不存在：" + RecipePath); return false; }
            if (Entries.Select(entry => entry.Name).Distinct().Count() != Entries.Length) { Debug.LogError("API 索引包含重复模块。"); return false; }

            var md = new StringBuilder("# 框架 API 任务索引\n\n由 Unity Editor 中已编译的 `GameFramework` 接口与辅助类型生成；用途与生命周期说明由 `FrameworkApiIndex` 人工维护。运行 `FrameworkApiIndex.Check()` 可检查受版本管理的签名快照。\n\n");
            var js = new StringBuilder("{\n  \"schemaVersion\": 1,\n  \"modules\": [\n");
            for (int i = 0; i < Entries.Length; i++)
            {
                Entry entry = Entries[i];
                Type type = typeof(GameEntry).Assembly.GetType("GameFramework." + entry.Name);
                if (type == null || !type.IsInterface || !typeof(IModule).IsAssignableFrom(type) || !FilesExist(root, entry))
                {
                    Debug.LogError("API 索引条目失效：" + entry.Name);
                    return false;
                }
                Type[] contracts = new[] { type }.Concat(type.GetInterfaces().Where(inherited => inherited != typeof(IModule))).ToArray();
                string[] signatures = contracts.SelectMany(contract => contract.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                    .Where(method => !method.IsSpecialName).Select(Signature)
                    .Concat(contracts.SelectMany(contract => contract.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).Select(Signature))
                    .Concat(contracts.SelectMany(contract => contract.GetEvents(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)).Select(Signature))
                    .Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray();
                if (signatures.Length == 0) { Debug.LogError("API 接口无公开成员：" + entry.Name); return false; }
                md.Append("## ").Append(entry.Task).Append(" · ").Append(entry.Name).Append("\n\n");
                md.Append("取得：`GameEntry.Get<").Append(entry.Name).Append(">()` · 命名空间：`GameFramework`\n\n");
                md.Append("源码：[").Append(entry.Name).Append("](../../../").Append(entry.Source).Append(") · 指南：[使用约定](../../../")
                    .Append(entry.Guide).Append(") · [可编译示例](../../../").Append(RecipePath).Append(")\n\n");
                md.Append("所有者：").Append(entry.Owner).Append("；取消：").Append(entry.Cancellation).Append("；失败：")
                    .Append(entry.Failure).Append("；线程：").Append(entry.Thread).Append("；暂停：").Append(entry.Paused).Append("。\n\n");
                md.Append("```csharp\n");
                foreach (string signature in signatures) md.Append(signature).Append('\n');
                md.Append("```\n\n");

                if (i > 0) js.Append(",\n");
                js.Append("    {\n      \"task\": ").Append(Quote(entry.Task)).Append(",\n      \"symbol\": ").Append(Quote(type.FullName))
                    .Append(",\n      \"access\": ").Append(Quote("GameEntry.Get<" + entry.Name + ">()"))
                    .Append(",\n      \"source\": ").Append(Quote(entry.Source)).Append(",\n      \"guide\": ").Append(Quote(entry.Guide))
                    .Append(",\n      \"recipe\": ").Append(Quote(RecipePath)).Append(",\n      \"owner\": ").Append(Quote(entry.Owner))
                    .Append(",\n      \"cancellation\": ").Append(Quote(entry.Cancellation)).Append(",\n      \"failure\": ").Append(Quote(entry.Failure))
                    .Append(",\n      \"thread\": ").Append(Quote(entry.Thread)).Append(",\n      \"paused\": ").Append(Quote(entry.Paused))
                    .Append(",\n      \"signatures\": [");
                for (int j = 0; j < signatures.Length; j++) { if (j > 0) js.Append(", "); js.Append(Quote(signatures[j])); }
                js.Append("]\n    }");
            }
            js.Append("\n  ],\n  \"companionTypes\": [\n");
            for (int i = 0; i < CompanionEntries.Length; i++)
            {
                Entry entry = CompanionEntries[i];
                Type type = typeof(GameEntry).Assembly.GetType("GameFramework." + entry.Name);
                if (type == null || !FilesExist(root, entry)) { Debug.LogError("API 辅助类型条目失效：" + entry.Name); return false; }
                BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
                string[] signatures = type.GetMethods(flags).Where(method => !method.IsSpecialName && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly))
                    .Select(method => (method.IsPublic ? "public " : "protected ") + Signature(method))
                    .Concat(type.GetConstructors(flags).Where(constructor => constructor.IsPublic || constructor.IsFamily || constructor.IsFamilyOrAssembly)
                        .Select(constructor => (constructor.IsPublic ? "public " : "protected ") + TypeName(type) + "(" +
                            string.Join(", ", constructor.GetParameters().Select(Parameter)) + ");"))
                    .Concat(type.GetProperties(flags).Where(property => property.GetMethod?.IsPublic == true || property.GetMethod?.IsFamily == true ||
                        property.SetMethod?.IsPublic == true || property.SetMethod?.IsFamily == true).Select(Signature))
                    .Concat(type.GetEvents(flags).Where(eventInfo => eventInfo.AddMethod?.IsPublic == true || eventInfo.AddMethod?.IsFamily == true).Select(Signature))
                    .Distinct().OrderBy(value => value, StringComparer.Ordinal).ToArray();
                if (signatures.Length == 0) { Debug.LogError("API 辅助类型没有可检索成员：" + entry.Name); return false; }
                md.Append("## ").Append(entry.Task).Append(" · ").Append(TypeName(type)).Append("\n\n");
                md.Append("辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`\n\n");
                md.Append("源码：[").Append(TypeName(type)).Append("](../../../").Append(entry.Source).Append(") · 指南：[使用约定](../../../")
                    .Append(entry.Guide).Append(") · [可编译示例](../../../").Append(RecipePath).Append(")\n\n");
                md.Append("所有者：").Append(entry.Owner).Append("；取消：").Append(entry.Cancellation).Append("；失败：")
                    .Append(entry.Failure).Append("；线程：").Append(entry.Thread).Append("；暂停：").Append(entry.Paused).Append("。\n\n");
                md.Append("```csharp\n");
                foreach (string signature in signatures) md.Append(signature).Append('\n');
                md.Append("```\n\n");
                if (i > 0) js.Append(",\n");
                js.Append("    {\n      \"task\": ").Append(Quote(entry.Task)).Append(",\n      \"symbol\": ").Append(Quote(type.FullName))
                    .Append(",\n      \"source\": ").Append(Quote(entry.Source)).Append(",\n      \"guide\": ").Append(Quote(entry.Guide))
                    .Append(",\n      \"recipe\": ").Append(Quote(RecipePath)).Append(",\n      \"owner\": ").Append(Quote(entry.Owner))
                    .Append(",\n      \"cancellation\": ").Append(Quote(entry.Cancellation)).Append(",\n      \"failure\": ").Append(Quote(entry.Failure))
                    .Append(",\n      \"thread\": ").Append(Quote(entry.Thread)).Append(",\n      \"paused\": ").Append(Quote(entry.Paused))
                    .Append(",\n      \"signatures\": [");
                for (int j = 0; j < signatures.Length; j++) { if (j > 0) js.Append(", "); js.Append(Quote(signatures[j])); }
                js.Append("]\n    }");
            }
            js.Append("\n  ]\n}\n");
            markdown = md.ToString();
            json = js.ToString();
            return true;
        }

        /// <summary>
        /// 检查条目引用的受版本管理文件。
        /// </summary>
        /// <param name="root">仓库根目录。</param>
        /// <param name="entry">待检查条目。</param>
        /// <returns>引用文件全部存在时返回 true。</returns>
        private static bool FilesExist(string root, Entry entry) => File.Exists(Path.Combine(root, entry.Source)) &&
            File.Exists(Path.Combine(root, entry.Guide));

        /// <summary>
        /// 从 Unity Assets 目录定位仓库根目录。
        /// </summary>
        /// <returns>仓库绝对路径。</returns>
        private static string RepositoryRoot() => Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;

        /// <summary>
        /// 格式化包含重载、泛型和可选参数的公开方法。
        /// </summary>
        /// <param name="method">已编译方法。</param>
        /// <returns>稳定的人类可读签名。</returns>
        private static string Signature(MethodInfo method)
        {
            string generic = method.IsGenericMethod ? "<" + string.Join(", ", method.GetGenericArguments().Select(argument => argument.Name)) + ">" : "";
            string parameters = string.Join(", ", method.GetParameters().Select(Parameter));
            string constraints = string.Join(" ", method.GetGenericArguments().Select(Constraint).Where(value => value.Length > 0));
            return TypeName(method.ReturnType) + " " + method.Name + generic + "(" + parameters + ")" +
                (constraints.Length == 0 ? "" : " " + constraints) + ";";
        }

        /// <summary>
        /// 格式化泛型参数约束。
        /// </summary>
        /// <param name="argument">泛型参数。</param>
        /// <returns>约束文字；没有约束时为空。</returns>
        private static string Constraint(Type argument)
        {
            if (!argument.IsGenericParameter) return "";
            var values = new List<string>();
            GenericParameterAttributes attributes = argument.GenericParameterAttributes & GenericParameterAttributes.SpecialConstraintMask;
            if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0) values.Add("class");
            if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0) values.Add("struct");
            values.AddRange(argument.GetGenericParameterConstraints().Where(type => type != typeof(ValueType)).Select(TypeName));
            if ((attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0 && !values.Contains("struct")) values.Add("new()");
            return values.Count == 0 ? "" : "where " + argument.Name + " : " + string.Join(", ", values);
        }

        /// <summary>
        /// 格式化公开属性。
        /// </summary>
        /// <param name="property">已编译属性。</param>
        /// <returns>稳定的人类可读签名。</returns>
        private static string Signature(PropertyInfo property) => TypeName(property.PropertyType) + " " + property.Name + " { " +
            (property.CanRead ? "get; " : "") + (property.CanWrite ? "set; " : "") + "}";

        /// <summary>
        /// 格式化公开事件。
        /// </summary>
        /// <param name="eventInfo">已编译事件。</param>
        /// <returns>稳定的人类可读签名。</returns>
        private static string Signature(EventInfo eventInfo) => "event " + TypeName(eventInfo.EventHandlerType) + " " + eventInfo.Name + ";";

        /// <summary>
        /// 格式化参数修饰符与默认值。
        /// </summary>
        /// <param name="parameter">已编译参数。</param>
        /// <returns>参数声明。</returns>
        private static string Parameter(ParameterInfo parameter)
        {
            string modifier = parameter.IsOut ? "out " : parameter.ParameterType.IsByRef ? (parameter.IsIn ? "in " : "ref ") : "";
            string result = modifier + TypeName(parameter.ParameterType) + " " + parameter.Name;
            if (parameter.IsOptional) result += " = " + DefaultValue(parameter);
            return result;
        }

        /// <summary>
        /// 将编译时常量转为 C# 默认值文字。
        /// </summary>
        /// <param name="parameter">包含默认值的参数。</param>
        /// <returns>默认值文字。</returns>
        private static string DefaultValue(ParameterInfo parameter)
        {
            object value = parameter.DefaultValue;
            if (value == null) return parameter.ParameterType.IsValueType && Nullable.GetUnderlyingType(parameter.ParameterType) == null ? "default" : "null";
            if (value == DBNull.Value || value == Missing.Value) return "default";
            if (value is bool boolean) return boolean ? "true" : "false";
            if (value is string str) return Quote(str);
            if (parameter.ParameterType.IsEnum)
            {
                string name = Enum.GetName(parameter.ParameterType, value);
                return name == null ? "(" + TypeName(parameter.ParameterType) + ")" + Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) :
                    TypeName(parameter.ParameterType) + "." + name;
            }
            if (value is float number) return number.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "f";
            if (value is double real) return real.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// 将 CLR 类型转为稳定的 C# 类型名。
        /// </summary>
        /// <param name="type">已编译类型。</param>
        /// <returns>简洁类型名。</returns>
        private static string TypeName(Type type)
        {
            if (type.IsByRef) return TypeName(type.GetElementType());
            if (type.IsArray) return TypeName(type.GetElementType()) + "[]";
            if (type == typeof(void)) return "void";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(byte)) return "byte";
            if (type == typeof(sbyte)) return "sbyte";
            if (type == typeof(short)) return "short";
            if (type == typeof(ushort)) return "ushort";
            if (type == typeof(int)) return "int";
            if (type == typeof(uint)) return "uint";
            if (type == typeof(long)) return "long";
            if (type == typeof(ulong)) return "ulong";
            if (type == typeof(float)) return "float";
            if (type == typeof(double)) return "double";
            if (type == typeof(decimal)) return "decimal";
            if (type == typeof(char)) return "char";
            if (type == typeof(string)) return "string";
            if (type == typeof(object)) return "object";
            if (type.IsGenericParameter) return type.Name;
            string name = type.Name.Split('`')[0];
            if (type.IsGenericType) name += "<" + string.Join(", ", type.GetGenericArguments().Select(TypeName)) + ">";
            return name;
        }

        /// <summary>
        /// 将文本编码为 JSON 字符串。
        /// </summary>
        /// <param name="value">原始文本。</param>
        /// <returns>带引号且已转义的文本。</returns>
        private static string Quote(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r")
            .Replace("\n", "\\n").Replace("\t", "\\t") + "\"";

        /// <summary>
        /// 仅在内容变化时写入文件。
        /// </summary>
        /// <param name="path">绝对文件路径。</param>
        /// <param name="content">目标内容。</param>
        private static void WriteIfChanged(string path, string content)
        {
            if (Same(path, content)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        /// <summary>
        /// 对比现有文件与确定性生成内容。
        /// </summary>
        /// <param name="path">绝对文件路径。</param>
        /// <param name="content">期望内容。</param>
        /// <returns>内容相等时返回 true。</returns>
        private static bool Same(string path, string content) => File.Exists(path) && File.ReadAllText(path, Encoding.UTF8) == content;
    }
}
