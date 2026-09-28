# AI 开发脚手架与 API 索引设计方案

状态：首版已实现并完成本轮验收。日期：2026-09-26。本文保留设计依据；实际操作以 [使用指南](AI开发脚手架使用指南.md) 为准，API 以源码与 [索引](api/README.md) 为准。

## 1. 目标与结论

建立“三类最小模板 + 项目级组合入口 + 可验证 API 索引”。生成后应当能够编译、加载内容、进入和退出；开发者及 AI 在生成的业务文件中继续编写游戏规则。

默认采用现有 GameplayRoot、GameWindow、SceneBase、LifetimeScope、YooAsset 和 GamePackage 构建流程。生成器位于框架 Editor 层，产物位于 GameLogic 和 GameResources。通用 Runtime 保持独立于具体游戏。

首版围绕新增玩法、窗口和场景形成闭环。游戏规则、关卡美术、复杂 UI 排版及专属存档结构由业务继续开发。

## 2. 已核实的现状

| 领域 | 现有事实 | 对设计的影响 |
| --- | --- | --- |
| 生成器 | RoundDemoAssetsBuilder、RoundUIBuilder、GameUISampleBuilder 主要生成演示资源 | 复用 Unity 资产创建方法，抽出参数化最小模板 |
| 热更入口 | GameMain 完成表、输入、存档等准备后加载首场景或创建 RoundGameBootstrap | 增加一次项目级接入点，后续新增内容通过生成注册表接入 |
| 玩法 | IGameplayModule 通过 StartAsync / StopAsync 管理实例，没有通用 Register API | 生成项目级显式工厂，每次启动创建新实例 |
| 场景 | ISceneModule.RegisterScene / ChangeSceneAsync 管理 SceneBase | 默认生成逻辑场景；内容载体与逻辑类分别管理 |
| UI | ConfigureWindows 为增量添加；配置 ID、注册类型重复会报错 | 可与现有样例共存，统一管理脚手架生成的登记和稳定 ID |
| 资源 | GameContentCollectorSetup 使用 AddressByFileName | 地址必须在整个 GameResources 收集范围内唯一 |
| 生命周期 | SceneScope、GameplayScope、OpenScope、SpawnScope 已存在 | 模板直接采用现有所有权和取消语义 |
| 文档 | AI开发入口.md 已有模块概览和使用约定 | 补按任务检索的索引、准确签名及可编译示例 |

事实入口：

- [GameMain](../DevProject/Assets/Scripts/GameLogic/Runtime/GameMain.cs)
- [IGameplayModule](../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/IGameplayModule.cs)
- [ISceneModule](../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Scene/ISceneModule.cs)
- [UIModule](../DevProject/Assets/Scripts/GameFramework/Runtime/UI/UIModule.cs)
- [GameContentCollectorSetup](../DevProject/Assets/Editor/GameContentCollectorSetup.cs)
- [现有 AI 开发入口](AI开发入口.md)

## 3. 总体结构

~~~text
Unity 菜单 / AI 命令行
    → 读取项目约定与模板描述
    → 校验并输出生成计划
    → 生成业务骨架、Unity 资产、地址常量及注册表
    → Unity 编译
    → 验证类型、组件路径、资源地址与注册关系
    → 现有 Prepare / Build GamePackage

框架 API 源码和编译程序集 + 人工维护的使用约定
    → Markdown 索引 + JSON 索引
    → 可编译示例与行为验证
~~~

### 3.1 生成核心

建议新增 GameFramework/Editor/Scaffolding，负责模板、命名与路径校验、生成计划、写入和结果校验。项目目录、命名空间、资源根和启动设置由项目配置提供。

Editor 菜单与命令行调用同一个核心。Unity 资产通过 AssetDatabase、PrefabUtility、EditorSceneManager 等现有 Editor API 创建，并保留已有 .meta。

### 3.2 项目描述

建议新增 config/scaffold/project.json，版本管理以下信息：

- schemaVersion、命名空间、源码及资源输出目录。
- 玩法、窗口、场景条目与相互引用。
- 窗口稳定 ID、逻辑场景名称、资源地址、资产路径。
- 窗口层级、模态、遮罩、关闭方式和缓存策略。
- 可选启动逻辑场景。
- 已有手写内容的保留 ID 与资源引用。

描述文件是生成内容的元数据来源；生成的注册表和常量从它派生。既有演示及其他手写注册仍可独立维护。首轮集成记录现有 ID，生成前做冲突检查。

## 4. 三类模板

### 4.1 新建玩法

以 Fishing 为例，生成：

- FishingGameplay：继承 GameplayRoot<FishingGameplay>，包含初始化、进入、更新、退出与强制退出骨架。
- FishingFeature：继承 GameplayFeature<FishingGameplay>，在 Root 构造阶段挂接。
- GP_Fishing.prefab：最小内容根；默认使用原生 Unity 组件，便于编译前创建资产。
- 生成的资源地址与显式创建入口。

默认不生成没有用途的 Model；需要数据模型时可选择附加模板。

玩法工厂每次返回新实例，通过 IGameplayModule.StartAsync 启动，保存 GameplayHandle，退出时等待 StopAsync。场景可选关联已生成玩法，由场景承担启动、退出和进入失败时的清理。

内容实例复用现有 PrefabPool，模板明确保存池和实例句柄，正常退出等待池释放；避免把 Prefab 资产租约与实例销毁顺序交给业务猜测。

另外提供“接入一局游戏”的可编译示例，使用现有 IGameRoundFactory、IGameRoundSession 和 GameRoundFlow；规则、结算与关卡数据由项目实现。

### 4.2 新建窗口

以 Shop 为例，生成：

- ShopWindow：继承 GameWindow。
- UI_Shop.prefab：RectTransform 根、内容面板、标题、关闭按钮；屏幕页面与弹窗有不同默认布局。
- 最小组件绑定、按钮监听、打开参数处理与刷新方法。
- 窗口 ID、Prefab 地址、GameWindowConfig 和显式类型注册。

提供“页面 / 普通弹窗 / 模态弹窗”预设。模态遮罩和输入阻挡使用现有 UI 系统；遮罩颜色、点击外部关闭、层级与缓存可配置。

默认采用 Immediate 缓存，便于首次使用和验证资源释放；业务按需要改为 Timed 或 Resident。

生命周期：

- BindComponents / Init：实例级绑定和初始化。
- Open：每轮打开时登记按钮监听、订阅与任务，交给 OpenScope。
- Show：刷新显示，可多次调用。
- Close / Destroy：分别处理本轮结束与实例销毁。

窗口逻辑为普通 C# 类，Prefab 保存显示组件，由 UI 管理器加载并绑定；无须在固定宿主资产上挂热更窗口脚本。

### 4.3 新建场景

以 Lobby 为例，生成：

- LobbyScene：继承 SceneBase。
- 场景名称常量、内容地址和 RegisterScene 入口。
- 进入、退出、取消与失败清理骨架。
- 可选关联玩法及窗口。

内容载体有两个选项：

| 选项 | 产物 | 使用方式 |
| --- | --- | --- |
| 默认：Prefab 内容 | SC_Lobby.prefab | 逻辑场景异步准备并创建内容，适合大多数小游戏 |
| Unity 内容场景 | SC_Lobby.unity | 通过资源模块 Additive 加载、保存 SceneHandle，退出等待卸载 |

SceneBase 是逻辑生命周期；.unity 是 Unity 内容资产。内容场景不重复放置 Framework 或 HotUpdateBootstrap。

原生场景模板使用 SceneHandle.UnloadSceneAsync 并等待结束；卸载成功后 YooAsset 自动释放句柄引用。仅调用 Release 不构成已加载内容场景的完整卸载流程。

## 5. 目录与文件所有权

以下为建议新增布局，原有 Excel Gen / Ext 和其他业务目录保持各自职责：

~~~text
config/scaffold/project.json
tools/scaffold.ps1

DevProject/Assets/
  Scripts/GameFramework/Editor/Scaffolding/
  Editor/ProjectScaffoldSetup.cs
  Scripts/GameLogic/Runtime/
    Gameplay/Fishing/
      FishingGameplay.cs
      FishingFeature.cs
    UI/Shop/ShopWindow.cs
    Scenes/Lobby/LobbyScene.cs
    Scaffold/
      Gen/
        ProjectContent.Gen.cs
        ContentIds.Gen.cs
        ContentAddresses.Gen.cs
      Ext/
        ProjectContent.Ext.cs
        ProjectStartup.cs
  GameResources/
    Gameplay/Fishing/GP_Fishing.prefab
    UI/Shop/UI_Shop.prefab
    Scenes/Lobby/SC_Lobby.prefab

docs/api/
  README.md
  index.json
  生命周期约定.md
  recipes/
~~~

文件维护约定：

- Gen 下的 .Gen.cs 可重复生成；结果稳定，无内容变化不重写。
- 业务类和 Ext 文件首次创建，后续保留用户修改。
- Prefab 和 .unity 首次创建，后续由 Unity Editor 编辑；重新生成注册代码不会重建它们。
- 模板升级通过版本差异审查和手动迁移处理；生成器首版仅创建和刷新生成物。
- 同名类型、路径、ID 或地址冲突时，在写入前返回明确错误。
- 新生成 C# 遵循仓库中文 XML 注释、参数说明与紧凑排版规则。

## 6. 注册、启动和热更接入

在 GameMain 中增加一次项目接入：

1. 完成现有热更准备及项目基础服务初始化。
2. 调用项目级 ProjectContent 登记脚手架生成的场景和窗口。
3. 通过 ProjectStartup 选择业务入口。
4. 未配置新的逻辑首场景时，沿用当前包级首场景或三关演示分支；显式设为启动场景后进入新逻辑场景。同时设置逻辑首场景与包级 StartupSceneLocation 时报告配置冲突，要求保留一个首内容入口。

项目入口沿用 GameStartupCompletion 的启动收尾顺序。GameMain.StartAsync 内只等待本阶段可完成的准备，不能反向等待 Loading 卸载或收尾后才就绪的 PlayerSave；需要这些条件的交互启用放到后续阶段，避免启动流程互相等待。

ProjectContent 属于 GameLogic，生成显式 new 和 Register 调用。玩法入口使用项目工厂，继续调用现有 IGameplayModule。通用框架不扫描热更类型来自动注册业务。

生成器只更新生成目录和描述文件，后续创建内容不反复文本替换 GameMain。注册以当前框架模块实例为边界，处理 Editor 重新进入 PlayMode 及框架重新初始化，防止重复登记或旧静态标记导致漏登记。

资源继续由 GameResources 自动收集，首版不新增包。生成的 UI、Gameplay、Scenes 内容默认属于按需资源；明确设为启动必需时才同步现有启动资源配置。所有引用使用生成地址常量，并校验实际收集结果。

## 7. Editor 与 AI 使用入口

已提供菜单：

- Tools/Unity Mini Game Framework/Scaffolding/New Gameplay
- Tools/Unity Mini Game Framework/Scaffolding/New Window
- Tools/Unity Mini Game Framework/Scaffolding/New Scene
- Tools/Unity Mini Game Framework/Scaffolding/Validate
- Tools/Unity Mini Game Framework/Scaffolding/Refresh API Index

已提供命令：

~~~powershell
./tools/scaffold.ps1 -Kind Gameplay -Name Fishing -DryRun
./tools/scaffold.ps1 -Kind Window -Name Shop -Preset Modal
./tools/scaffold.ps1 -Kind Scene -Name Lobby -Gameplay Fishing -Window Shop
./tools/scaffold.ps1 -Validate
~~~

还支持从 JSON 请求批量创建有关联的内容，一次校验全部名称、引用和写入范围。

DryRun 输出将创建与更新的文件、地址、登记项及冲突。执行输出结构化结果文件，包含阶段、文件清单、错误与下一步；临时状态放入忽略目录。

流程明确区分“已生成”“编译通过”“验证通过”。Unity 重新编译和 domain reload 后再验证，不在新类尚未编译时尝试绑定该类型。

交互使用当前 Editor；无人值守通过 Unity batchmode 调用。包装脚本检测同工程 Editor 占用，避免同时启动第二个 Editor。批处理可分为生成与重新打开验证两次执行。

## 8. API 索引与实际示例

### 8.1 索引结构

提供两种入口：

- docs/api/README.md：按“我要打开窗口、生成角色、切换场景、保存进度”等任务查找。
- docs/api/index.json：供 AI 按模块、符号及关键词检索。

每条索引包含：

- 用途、命名空间、模块取得方式和准确公开签名。
- 关联源码文件和可编译示例位置。
- 初始化条件、资源与返回句柄的所有者。
- 取消、失败和退出语义。
- 是否要求 Unity 主线程、是否允许玩法暂停时执行。
- 对应验证覆盖与平台限制。

默认覆盖所有已注册模块，详细示例优先覆盖：场景、玩法、UI、资源、PrefabPool、输入、事件、计时器、Tween、音频、存档、Excel 表、生命周期与诊断。项目级表结构和启动入口单独标识其所属 GameLogic。

### 8.2 以现有签名为依据

例如，下列调用使用当前已有 API。示例中的 root 为已创建的新玩法实例，ct 为进入令牌，exitToken 为独立退出令牌；这里展示签名，完整所有权在实现阶段的可编译示例中补齐：

~~~csharp
IGameplayModule gameplays = GameEntry.Get<IGameplayModule>();
GameplayHandle handle = await gameplays.StartAsync(root, ct: ct);
await gameplays.StopAsync(handle, ct: exitToken);
~~~

作用域资源示例中，scope 是发起操作时捕获的活跃作用域，iconAddress 指向已存在的 Sprite：

~~~csharp
IResourceModule resources = GameEntry.Get<IResourceModule>();
ResourceLease<Sprite> icon = await scope.AcquireAsync<Sprite>(resources, iconAddress, ct: ct);
~~~

该租约由 scope 托管。窗口或角色 Prefab 的内部租约由 UI / PrefabPool 管理。

### 8.3 防止文档失效

- API 签名从 Unity 编译后的程序集读取，并关联源码路径；用途和生命周期由人工维护的条目补充。
- 仅在 Editor 检查程序集，不向 Player 引入反射注册。
- 示例保存在实际 .cs 文件中，由专用测试程序集编译；Markdown 引用或提取这些示例。
- 索引检查发现符号消失、签名变化、示例失效或源码路径缺失时返回失败。
- 更新 AI开发入口.md 和项目 AGENTS.md 的入口链接，形成“任务索引 → 示例 → 源码 → 验证”的固定查找顺序。

## 9. 模板必须落实的生命周期约定

| 对象 | 所有者 | 模板要求 |
| --- | --- | --- |
| 逻辑场景本轮工作 | SceneScope | 每次进入重新取得，退出先停止再收尾 |
| 玩法本轮工作 | GameplayScope | Feature / Model 在 Enter 登记；Init 阶段不使用本轮 scope |
| 窗口本轮工作 | OpenScope | Open 登记，Show 刷新，Close 结束 |
| 窗口实例绑定 | InstanceScope | 随窗口真正销毁释放 |
| 池对象本轮工作 | SpawnScope | 每次 Spawn 独立，回收后旧任务失效 |
| 项目长生命周期服务 | ApplicationScope 或其项目子 scope | 由项目入口明确拥有并关闭 |

额外落实：

1. 异步任务捕获发起时的 scope；额外 await 后检查本轮仍有效，避免旧结果写入重开的对象。
2. 退出方法使用独立清理令牌；SceneToken、GameplayToken 此时可能已取消。
3. SceneScope.Dispose 不能代替 await gameplays.StopAsync(handle)；需要显式等待玩法退出。
4. PrefabPool.DisposeAsync、存档 CloseAsync、场景卸载由业务退出顺序显式等待。强制退出与进入失败提供可重复执行的兜底清理。
5. GameWindowScope.Scene / Session 是分类，当前不会自动与某个 SceneBase 绑定。标准场景/会话退出入口明确调用 DestroyWindowScopeAsync；该调用会清理整个分类，独立并存玩法只关闭自己拥有的窗口，由共同所有者统一处理分类销毁。
6. 正常关闭 Resident 窗口会保留缓存及其租约。模板及验证应区分“关闭”和“销毁”，按预期缓存策略判断释放。
7. 订阅、Timer、输入上下文、Tween、音频与资源租约只由一个明确所有者托管。

## 10. 实施顺序与验收

### 第一阶段：描述与注册入口

实现项目配置、稳定 ID / 地址、DryRun、生成与手写文件边界，以及 GameMain 的一次性项目接入。首先验证现有三关样例仍可启动。

### 第二阶段：三类模板

先完成玩法、窗口、默认 Prefab 场景，再接入 .unity 内容载体。验证分别创建与组合使用；支持选择启动入口。

### 第三阶段：API 索引

补模块索引、生命周期约定和常用任务示例；加入签名、示例编译与路径检查。

### 第四阶段：完整闭环

验收至少包含：

- 从源码模板副本生成一套新玩法、窗口和场景，Unity 2022.3.62f2 编译通过。
- 新入口异步加载内容，玩法启动，窗口可打开和关闭，场景可反复进入与退出。
- 验证加载中退出、取消后迟到结果、重复打开、重复注册及框架重启。
- 验证重复生成不覆盖业务 C#、Prefab、场景和 .meta；冲突在写入前报告。
- 检查本轮玩法句柄、订阅、输入上下文、计时器和资源所有权正确结束，缓存符合配置。
- 验证 .unity 内容场景实际 Additive 加载与卸载。
- 运行现有 Prepare / Build GamePackage，确认生成资源被正确收集并通过真实 AB 路径加载。
- 运行 API 索引检查和示例编译，记录实际结果。
- 新项目复制流程自动携带脚手架、描述文件和文档；验证副本不依赖原仓库绝对路径。

本轮实际执行的结果与平台范围见 [验收记录](AI开发脚手架验收记录.md)。
