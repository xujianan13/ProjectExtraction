# Unity 小游戏框架设计方案

状态：分阶段设计；首版单 Unity 工程已实现，实际结果见[首版验收记录](首版验收记录.md)。日期：2026-09-23。

## 1. 结论与本次交付

建设不区分 2D/3D 的 Unity 小游戏框架。默认样例采用 3D 场景，框架运行时不绑定维度或渲染管线；2D 游戏可复用同一套模块、资源、UI 和生命周期。技术基线为 Unity 2022.3.62f2、UniTask、YooAsset、uGUI/TMP。Unity Editor 是首版运行与验收环境；WebGL、微信和抖音是后续平台阶段，未经实际构建与设备验证不标记通过。

首版以 `DevProject/Assets/Scripts/GameFramework` 作为唯一框架源码目录，放置从 ProjectTavern 迁入的 Runtime、必要 Editor 工具和 Tests；Unity 直接编译这些源码，当前不安装同名框架包。UniTask 与 YooAsset 仍作为 `DevProject/Packages` 下的内嵌依赖，uGUI、TMP 和 Test Framework 由开发工程声明。迁入时保留原有模块生命周期、显式注册、资源句柄和测试，只对测试运行环境做必要适配。

本次交付的可验收范围是：DevProject 能由 Unity 2022.3.62f2 打开且依赖解析正常；框架程序集编译；相关 PlayMode 测试运行；默认 3D 样例场景进入 PlayMode 后可看到内容，并完成一次显式模块初始化、逐帧更新和逆序关闭；README 或验收记录列出运行步骤、通过项与未完成项。首版不把独立消费工程、多平台导出、远端更新、Excel 生成、双槽存档或声明式 UI 编译器列为完成条件。

## 2. 参考项目取舍与迁移原则

| 领域 | ProjectTavern 已有能力 | 首版决定 | 后续目标 |
| --- | --- | --- | --- |
| 模块与启动 | GameEntry、IModule、ModuleManager、Bootstrap | 迁入可独立编译的框架部分，以手工场景入口和显式注册驱动 3D 样例 | 实例级应用作用域、可重试启动任务 |
| 时间与基础服务 | Event、Timer、Pool、Log 等模块 | 直接复用并保留原测试；依赖不完整的模块先裁剪 | 作用域自动释放、统一帧时间与 Tween |
| 场景 | 逻辑场景栈、预加载与切换 | 能无业务依赖迁入的部分复用；样例只要求可运行场景 | 准备成功才提交、失败保留旧场景 |
| 资源 | YooAsset 加载、句柄、资源组 | 迁入资源模块和 YooAsset 依赖；项目按实际内容初始化资源包后使用 | 本地包加载与释放样例、逻辑 ID、组租约、远端加载与缓存 |
| UI 与音频 | uGUI/TMP 基础控件、窗口及音频模块 | 只迁入无 GameLogic 业务表依赖的通用部分；样例可用原生 Canvas | 单向投影、描述生成 Prefab、完整音频租约 |
| 状态与存档 | Model/Event、PlayerPrefs Storage | 保留现有能力的真实语义，不宣称事务命令或正式存档已实现 | StateStore/CommandDispatcher、双槽存档 |
| 平台 | Editor/原生主路径、可选微信 | 首版只验收 Editor | WebGL、微信、抖音适配和真机验证 |

迁移来源限定为 ProjectTavern/client/GameClient/Assets/Scripts/GameFramework。以实际源码、asmdef、Tests 和 GameClient 文档核对可复用性；复制时保留对应 .meta 与许可信息，记录来源路径和必要改动。BootLogic、UpdateLogic、GameLogic 中的游戏启动、HybridCLR 双包更新、登录、网络会话、Luban 游戏配置及业务窗口表不进入基础包。静态 GameEntry 可以作为首版迁移过渡，后续引入实例作用域时再替换；不因未来目标阻塞首版运行。

## 3. 首版工程与依赖

首版以 Unity 工程中的源码为中心：

~~~text
UnityMiniGameFramework/
├─ DevProject/
│  ├─ Assets/
│  │  ├─ Scripts/GameFramework/       唯一框架源码：Runtime、Editor、Tests
│  │  ├─ Scripts/MiniGame/            默认 3D 样例项目代码
│  │  └─ Scenes/Main.unity            默认启动场景
│  ├─ Packages/
│  │  ├─ UniTask/                     本地内嵌依赖
│  │  ├─ YooAsset/                    本地内嵌依赖
│  │  └─ manifest.json                uGUI、TMP、Test Framework 等
│  └─ ProjectSettings/
└─ docs/
   ├─ framework/                    框架设计、指南与历史验收
   └─ project/                      当前项目设计与验收
~~~

框架代码直接作为 Unity `Assets` 源码维护，`GameFramework.asmdef` 仍定义独立运行时程序集，`Editor/` 与 `Tests/` 保留各自代码和 `.meta`。`GameFramework/package.json` 当前只保存未来导出包的元数据；开发工程不引用这个包，避免同一源码被导入两次。外部 UniTask、YooAsset 的版本以其 `package.json` 为准，uGUI/TMP 与 Test Framework 由开发工程 manifest 显式声明。框架 Runtime 不引用样例游戏程序集；样例负责组合根与内容。

后续需要发布或独立消费时，再把 `Assets/Scripts/GameFramework` 整理为标准 UPM 包根，按 Unity 官方[包布局](https://docs.unity3d.com/2022.3/Documentation/Manual/cus-layout.html)建立独立消费工程、依赖配置和发布检查；这些工作不作为首版前置条件。

## 4. 长期运行时契约

以下是 AIMiniGameFramework 能力对齐方向。除第 1 节列明的首版验收项外，本节主要描述后续迭代目标；Excel 导表已在后续迭代单独实现，实际用法见[Excel 数据表](Excel数据表.md)。

### 4.1 启动、时间和所有权

- Bootstrap 场景手工挂载应用入口；Loading 从 Unity 首个可绘制帧开始呈现。项目提供有序启动任务、权重、必要性和超时；所有必要任务与首内容准备完成后才报告 Ready。
- 启动状态为 Created → Booting → Ready 或 Failed → Disposed。并发启动共享同一轮结果；失败重试创建新作用域，迟到结果不能修改新轮次。
- AppScope、SceneScope、ViewScope 逐级拥有取消源、订阅、定时器、Tween、资源租约与对象；退出逆序释放。
- Unity PlayerLoop 为唯一帧源。暂停、切后台和恢复时重设游戏帧差，不将后台间隔直接用于游戏更新。

### 4.2 场景、状态与事件

- 逻辑场景默认以 Prefab 和控制器承载 2D 或 3D 内容；大型项目可接 Unity additive Scene。新内容准备成功后才提交切换，失败或取消保留旧场景。
- 项目定义可序列化状态、初始值、校验器和同步命令处理器。命令产生候选状态、完整校验、一次提交 revision，然后发布通知；UnityEngine.Object 不进入业务状态。
- EventBus 是实例级强类型事后通知，订阅由作用域释放。异步副作用完成后重新校验当前状态再发命令。首版迁入的 Event/Model 不等同于上述状态事务契约。

### 4.3 资源、配置与存档

- 资源清单将稳定 ID 映射到 Unity 地址、类型和组。业务经 Acquire/AcquireGroup 获得独立租约；并发请求合并，调用者取消互不干扰，失败回滚本组新租约。
- YooAsset 负责包加载、下载校验与缓存；框架负责逻辑 ID、租约、作用域、进度和错误语义。资源组不等同于微信或抖音物理分包。
- 两行表头 Excel 输入生成 JSON 和强类型 C#；运行时读取 `GamePackage` 中的 JSON，不解析 Excel。当前项目实现及构建校验见[Excel 数据表](Excel数据表.md)；Luban 仍只是可选后端。
- 存档协调器由项目提供 schema、默认数据、持久字段与迁移。框架负责双槽序号、校验、损坏恢复、自动保存合并和 Flush/Close；PlayerPrefs 仅用于少量偏好。首版迁入的 Storage 不承诺这些能力。

### 4.4 UI、交互和媒体

- uGUI/TMP 提供窗口生命周期、模态层级、滚动、手势、安全区、Toast 与确认框；UI 先消费输入，再交给世界输入。Canvas 可覆盖默认 3D 场景，也可用于 2D 内容。
- AI 可编写纯界面描述，含稳定 ID、布局、样式 Token、模型字段绑定和命名动作。Editor 校验并生成 Prefab；项目 Presenter 将状态单向投影到 View，动作发命令。原生 Prefab 始终可手工扩展。
- SpriteAtlas、九宫格、序列帧与 3D 模型/动画使用 Unity 原生资产；框架补充生命周期和资源释放。音频按通道管理播放句柄；诊断使用有界快照，不成为业务全局状态。

## 5. 平台与 AI 开发路线

平台端口覆盖生命周期、触摸、安全区、文件与键值存储、资源下载、音频焦点和能力报告。Editor 先提供基础实现；WebGL、微信、抖音按各自 SDK 单独适配并验证。未接入能力明确返回不支持，不模拟成功。

首版 AI 工作循环是：阅读框架公开 API 和样例 → 修改样例规则、场景或 Prefab → Unity 编译与相关 PlayMode 测试 → PlayMode 观察 3D 样例。Excel 导表已作为项目内容能力加入；UI 自动生成、独立项目模板、WebGL 预览及平台导出仍待后续迭代。AI 编辑 Unity 资产时应使用 Editor API，并将资产和 .meta 一起纳入版本管理。

## 6. 实施顺序与完成判据

| 阶段 | 实现内容 | 可检查的完成条件 |
| --- | --- | --- |
| 1. 可运行首版 | 创建 DevProject；在 Assets 中维护框架源码，内嵌 UniTask、YooAsset；通过 UPM 安装官方包；迁入 ProjectTavern 通用 Runtime/Tests/必要 Editor；制作默认 3D 样例 | Unity 2022.3.62f2 打开并编译；相关 PlayMode 测试通过；PlayMode 可见 3D 内容，模块初始化、更新和关闭有可观察证据；记录来源和验收结果 |
| 2. 可独立消费 | 从首版中稳定公开 API、依赖和作用域；独立 BlankGame 模板及包消费检查 | 新路径工程只依赖公开包接口运行同一最小样例，迁移测试持续通过 |
| 3. AI 内容能力 | 资源 ID/租约、事务状态与命令、Excel 导出、存档协调、声明式 UI、音频及诊断逐项实现 | 每项有公开契约、样例和针对失败/取消/释放的验证；不以设计文档代替实现 |
| 4. 平台交付 | WebGL 浏览器回归、微信与抖音 SDK 适配、构建与真机验证 | 分别记录 Editor、浏览器、开发者工具和真机结果；未执行的目标明确标记未验收 |

验证只覆盖当阶段已经实现的能力。性能数据保留设备、Unity/SDK 版本、构建哈希和原始样本，并只与同环境基线比较。

## 7. 依据与待验证事项

- AIMiniGameFramework 当前事实入口：D:\Repositories\AIMiniGameFramework\doc\README.md、当前能力清单.md、AI开发规范.md、公共API索引.md；具体契约以运行时代码和 package.json 核对。
- ProjectTavern 客户端事实入口：D:\Repositories\ProjectTavern\client\GameClient\AGENTS.md、docs\Architecture.md、docs\Framework\Modules\Resource.md、docs\Framework\Modules\Storage.md、docs\Framework\UIComponents.md，以及 Assets\Scripts\GameFramework 的代码、asmdef 和 Tests。其微信适配不代表抖音支持。
- 微信与抖音 SDK 版本、WebGL 运行及真机能力需在平台阶段探针验证。当前方案不宣称这些平台已通过。

本文件描述目标和验收边界；实际完成状态以 DevProject 的编译、测试、PlayMode 证据和验收记录为准。
