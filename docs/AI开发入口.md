# AI 开发入口

按任务检索当前已注册模块及准确签名，从 [框架 API 索引](api/README.md) 开始；所有权与退出顺序见 [API 生命周期约定](api/生命周期约定.md)，可编译调用见 [ApiRecipes.cs](../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)。

新项目先按[新项目复用](新项目复用.md)选择 `-Template Minimal` 纯净起点或默认 `-Template Demo` 三关演示。`Minimal` 保留固定 Main/Loading、Boot 热更、输入与 GamePackage，首次导入生成空 `Main` 玩法、`Home` Page 窗口及 Prefab 模式的 `Home` 逻辑首场景；空 Excel 源目录可生成可编译入口，不携带三关演示或 PlayerSave 业务。

本工程以 `DevProject` 为可运行宿主。`DevProject/Assets/Scripts/GameFramework` 提供通用模块，`Boot` 是固定启动层，`GameLogic` 是热更程序源码，游戏资产放在 `GameResources`。默认演示是 3D；框架模块不区分 2D/3D。完整启动与构建步骤见[热更启动流程](热更启动流程.md)，运行时资源下载和内存预加载见[按需资源加载](按需资源加载.md)。

## 开始一个小游戏

1. 阅读 [`Main.unity`](../DevProject/Assets/Scenes/Main.unity)、[`Framework.prefab`](../DevProject/Assets/Resources/Framework.prefab)、[`HotUpdateBootstrap.cs`](../DevProject/Assets/Scripts/Boot/Runtime/HotUpdateBootstrap.cs) 和 [`GameMain.cs`](../DevProject/Assets/Scripts/GameLogic/Runtime/GameMain.cs)。主场景先初始化框架，显示 Loading 并更新 GamePackage，最后由 GameMain 进入游戏。`Demo` 的三关收集样例单局状态、会话资源、菜单和存档接入见[关卡与单局流程](关卡与单局流程.md)。`Minimal` 从生成的 Main/Home 内容扩展。
2. 在 `DevProject/Assets/Scripts/GameLogic` 增加可热更项目代码，并把游戏资源放入 `Assets/GameResources`。Excel 数据表的源文件放在仓库 `config/excel`，按[Excel 数据表](Excel数据表.md)导出到 `GameResources/Config/Tables`；源 `.xlsx` 不放进 `GameResources`。新场景继承 `SceneBase`，在 `OnEnterSceneAsync` 创建内容，将本轮订阅、计时器和业务租约登记到 `SceneScope`；`OnExitSceneAsync` 和 `OnForceExit` 处理业务收尾以及未托管对象。2D 内容同样可以放在逻辑场景里，按项目需要配置相机与渲染资产。
3. 使用 `GameEntry.Get<T>()` 访问已注册模块。现有 `GameEntry.RegisterDefaults()` 注册日志、诊断、事件、计时器、Tween、存储、对象池、Prefab 实例池、资源、流程、HTTP、网络、音频、输入、模型、逻辑场景、玩法、服务与 UI 模块。玩法动作、输入上下文、触屏虚拟控件与重绑定见[游戏输入系统使用指南](游戏输入系统使用指南.md)；业务窗口、模态弹窗和提示见[UI 系统](UI系统.md)，大量重复项见[UI 虚拟列表](UI虚拟列表.md)。玩法普通帧、固定帧及帧末钩子见[玩法更新阶段](玩法更新阶段.md)。可复用 Unity 实例的加载、生成、回收和释放见[Prefab 实例池](Prefab实例池.md)。接口和行为以 [`Runtime`](../DevProject/Assets/Scripts/GameFramework/Runtime/) 源码为准。
4. 使用 Unity Editor 创建或修改 `.unity`、`.prefab` 等资产，并保留 `.meta`。Demo 模板的 UI 样例可通过 **Set Up Game UI** 生成；Minimal 项目使用脚手架新增窗口，业务窗口 Prefab 放在 `GameResources/UI`。表格改动后先运行 **Export Excel Tables**，等待生成的 C# 编译完成；随后运行 **Prepare GamePackage**，发布内容时按目标运行 **Build Startup GamePackage**、**Build Offline GamePackage** 或 **Build Remote GamePackage**。关闭同一工程的 Unity Editor 后运行根目录 [`tools/verify.ps1`](../tools/verify.ps1)（默认 Quick，`-Mode Full` 验证纯净模板复用，`-Mode Full -WindowsPlayer` 增加独立 Player）；固定日志、JSON、隔离副本保护和范围见[统一验收](统一验收.md)。按需重新打开 Unity Editor，进入 PlayMode 检查 Loading、内容更新和游戏入口。验收范围及尚未实现的能力见[设计方案](Unity小游戏框架设计方案.md)。

## 按需内容的业务接入

- `GameResources` 中的 `Code`、`Config`、`Input`、`Startup` 默认属于启动内容，其他直接子目录可按目录名标签与 `Optional` 分组。`GamePackageBuildSettings.asset` 可加入额外启动目录；初次使用按需模式要重建并发布带标签的内容版本。Startup 首包还要配置 CDN 并发布完整同版本内容。
- Boot 默认启动下载 `Startup` 与 `additionalStartupDownloadTags`；`downloadAllResourcesBeforeLaunch` 可恢复全量预下载。Boot 已为 `GamePackage` 配置下载支持；独立资源包须在激活后调用 `ConfigureDownloadSupport`。通过 `ResourceDownloadRequest.ForLocations/ForTags` 和 `DownloadAsync` 预下载 Bundle；检查 `Status`、`Error`、`CanRetry`。Web 默认不支持落盘预下载，按需加载不等于离线就绪。
- 使用 `AcquireGroupAsync` 将关卡资源加载到内存，并持有、释放 `ResourceGroupLease`。`OpenWindowAsync` 默认延迟 150 毫秒显示 busy，资源失败可重试或取消；`RunWithLoadingAsync` 可包裹 `PrefabPool` 创建，操作必须响应传入的取消令牌。业务保存创建的池并在场景退出调用 `DisposeAsync()`。
- 音频资源放入 GameResources/Audio。使用 IAudioModule.PlayAsync 播放 2D 音频，PlayAtAsync 或 PlayFollowAsync 播放 3D 音频；循环音效绑定本轮作用域，并为相机或玩家保留唯一启用的 AudioListener。选项、并发上限与 Mixer 释放顺序见[音频系统](音频系统.md)。

## 动画与运行诊断

- 通用属性动画使用 `ITweenModule`，通过 `Tween` 创建串并行片段并绑定 `OpenScope`、`GameplayScope` 或 `SpawnScope`；见[通用 Tween](通用Tween.md)。UI 在玩法暂停时仍需播放的动画显式使用非缩放时间。
- 遇到资源未释放、输入被阻挡或流程未推进时，先打开 **Game Framework → Diagnostics** 或读取 `IDiagnosticsModule.CaptureSnapshot()`；开发包可用 F8 打开面板。自定义游戏状态通过 `IDiagnosticProvider` 登记，见[统一运行诊断](运行诊断.md)。

## 生命周期与所有权

- 创建订阅、计时器、输入上下文或租约时立即确定所有者。场景使用 `SceneScope`，玩法每次进入使用 `GameplayScope`，窗口每次打开使用 `OpenScope`，池对象每次生成使用 `SpawnScope`；应用级业务使用 `GameEntry.ApplicationScope`。接口与示例见[生命周期作用域使用指南](生命周期作用域使用指南.md)。
- 玩法的 `GameplayScope` 在初始化完成后、Feature 进入前创建。Feature/Model 在 `Enter` 登记本轮行为，异步任务捕获当前 scope；场景退出时显式等待 `GameplayModule.StopAsync(handle)`，不要把同一作用域再次登记到 `SceneScope`。
- 窗口新增每轮仅一次的 `Open(args)`，本轮订阅放在这里；`Show(args)` 可能由重复聚焦调用，只刷新显示。持久组件绑定放在 `Init`，由 `InstanceScope` 管理。
- 异步任务捕获发起时的作用域，额外等待后检查该对象是否有效，避免关闭再开或对象池复用时旧任务写入新一轮。资源使用 `scope.AcquireAsync` / `scope.AcquireGroupAsync` 时会自动登记租约并释放迟到结果。
- UI 管理器和 PrefabPool 内部租约仍归各自管理器，不再登记一次。保存与异步销毁需要显式等待；同步作用域释放不能代替 `SaveSession.CloseAsync()` 或 `PrefabPool.DisposeAsync()`。

## 约束与边界

- `DevProject/Assets/Scripts/GameFramework` 保持通用，不引入单个游戏的玩法、关卡或表结构。优先复用 ProjectTavern 已迁入的代码，改框架 API 前先查调用方及现有测试。
- `Assets/Resources/Framework.prefab` 是不可热更的启动资产。`Assets/GameResources` 已由 YooAsset 收集为 `GamePackage`。框架 Prefab 只初始化通用模块；主场景的 HotUpdateBootstrap 负责具体包名、更新和热更入口。
- 主场景启动层已初始化并绑定 YooAsset `GamePackage`；业务通过 `ResourceModule` 按 Address 加载内容。默认三关样例由 `GameLogic/Runtime/Rounds` 的会话和 `GameResources/Rounds` 的 Prefab 组成；它是可替换的项目内容，详见[关卡与单局流程](关卡与单局流程.md)。
- `StorageModule` 用于 PlayerPrefs 偏好；`ISaveModule` 已提供正式存档的双槽恢复、迁移、自动保存和平台后端，接入以[正式存档使用指南](正式存档使用指南.md)为准。事务状态与声明式 UI 属于后续阶段。Excel 导表已由项目工具提供，表结构以[Excel 数据表](Excel数据表.md)和实际生成代码为准。不要把设计文档中的目标当作当前已存在的 API。
- 修改新 C# 代码遵守仓库 [`AGENTS.md`](../AGENTS.md) 中的中文 XML 注释规则。完成后记录实际运行的编译与 PlayMode 测试结果。
