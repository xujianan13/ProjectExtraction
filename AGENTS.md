# 项目工作指引

本文件只约束 `D:\Repositories\ProjectExtraction`。通用沟通、执行、测试、C# 中文 XML 文档注释及紧凑排版规则使用当前生效的全局 `AGENTS.md`；这里记录本项目事实和边界。

## 当前版本与文档归属

- **当前开发采用 v2。** 玩法设计和实施计划以 [v2 当前版本](docs/project/v2/README.md)及[多区域探索与成长奖励整体设计](docs/project/v2/多区域探索与成长奖励整体设计.md)为准；建议、示例与待定项不能当作已批准规则。
- `docs/project/v2` 保存当前版本的需求、设计、决策、实现与验收；`docs/project/v1` 保存旧系统的设计、模块契约和历史验收，仅用于查阅历史。v1 正文中的“当前”“已确认”不自动约束 v2。
- **v1 代码不做保留。** 按 v2 阶段直接改造现有工程，并同批删除被替代的旧代码、配置、调用入口、测试及无引用资产。不建立 v1 代码目录、备用副本、版本开关或双轨运行逻辑。
- 仍适用的战斗、物品、交易和框架能力可原位改造，工程中只维护一份实现。历史数据是否迁移另行明确，不能将文档归档理解为删除实际玩家存档。
- 当前已建立 v2 总体设计、基础规范、模块契约与[文档承接清单](docs/project/v2/文档承接清单.md)，P1～P6 尚未实施；现有工程仍有待替换的旧原型代码。每阶段完成后更新 v2 的实现状态、清理范围和实际验证，不能引用 v1 测试结果冒充 v2 验收。
- `docs/framework` 保存通用框架设计、使用指南、API 索引及框架历史验收，不参与本项目玩法版本划分。`docs` 根目录只保留[文档导航](docs/README.md)，项目版本入口见[项目文档导航](docs/project/README.md)。

## 先读这些入口

- [README.md](README.md)：工程打开方式、日常命令和源码位置。
- [v2 当前版本](docs/project/v2/README.md)：已确认方向、沿用基础边界、实施顺序和版本状态。
- [多区域探索与成长奖励整体设计](docs/project/v2/多区域探索与成长奖励整体设计.md)：整关一次体力、单区域探索地图、刷怪技能三选一、Boss 装备收藏三选一、收藏成就及 P1～P6 计划。
- [基础玩法与局外成长](docs/project/v2/基础玩法与局外成长.md)：属性装备、背包安全箱、交易、技能与体力；区分当前约束、原型参数和未定政策。
- [战斗系统与表现契约](docs/project/v2/战斗系统与表现契约.md)：ECS、技能、飞弹、Buff、碰撞、动画与反馈；旧草案能力不等于现有 API。
- [场景流程与数据契约](docs/project/v2/场景流程与数据契约.md)：准入、区域转换、领奖、终局、保存恢复和战绩。
- [工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)、[模块职责与开发验收](docs/project/v2/模块职责与开发验收.md)：操作入口、模块交接、待定边界与各阶段完成条件。
- [v1 历史归档](docs/project/v1/README.md)：只在查阅旧实现、约束来源和历史证据时使用；新采用的规则需写入 v2。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：查阅现有 API 或新增玩法、窗口和场景。设计名称不代表 API 已存在。
- [新项目复用](docs/framework/新项目复用.md)、[依赖清单](docs/framework/新项目资源与依赖清单.md)与[统一验收](docs/framework/统一验收.md)：Minimal 模板、固定资产和 Quick/Full 检查范围。
- [Unity 版本](DevProject/ProjectSettings/ProjectVersion.txt)、[Runtime 程序集定义](DevProject/Assets/Scripts/GameFramework/Runtime/GameFramework.asmdef)、[Packages 清单](DevProject/Packages/manifest.json)：版本与编译依赖的事实来源。

## 工程事实与边界

- `DevProject` 使用 Unity **2022.3.62f2**，由源框架干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 的 `Minimal` 模板创建。公司名 `MiniGameStudio` 和应用标识 `com.minigamestudio.projectextraction` 是可修改的开发占位。
- 目标是 3D 搜打撤 + Roguelike，优先 Mobile、微信小游戏并适配 PC。默认竖屏、兼容横屏；近战自动基础攻击、局外成长、装备和安全箱等沿用方向见 v2。平台目标不等于已完成接入或设备验收。
- `DevProject/Assets/Scripts/GameFramework` 是框架唯一源码，当前不是已安装的 UPM 包；`GameFramework/package.json` 仅作未来包元数据。`DevProject/Packages` 保留 UniTask **2.5.11**、YooAsset **3.0.1-beta**、HybridCLR **8.12.0** 等模板依赖，渲染管线沿用模板。
- `Assets/Resources/Framework.prefab`、`Assets/Scenes/Main.unity` 和 `Loading.unity` 属于固定宿主资产。`Assets/Scripts/Boot` 为固定启动层；`Assets/Scripts/GameLogic` 为热更游戏代码；`Assets/GameResources` 是 YooAsset `GamePackage` 热更内容源。通用框架不得依赖项目玩法。
- 现有工程已具有 Home、仓库/技能、Expedition 场景、轻量 ECS 战斗、物品与交易、出入场保存等原型基础，仍使用占位表现和原型数值。旧房间地图、清房奖励和混合候选池将按 v2 阶段替换删除；历史实现范围见 [v1 归档](docs/project/v1/README.md)。
- ECS 负责伤害、Buff、位移和碰撞等战斗事实，表现从快照与事件读取；通用 `GameFramework.AnimatorPlayback` 不依赖 GameLogic，动画不参与伤害判定或 ECS 位移。当前项目契约见 [v2 战斗系统与表现契约](docs/project/v2/战斗系统与表现契约.md)，通用调用见[通用动画播放](docs/framework/通用动画播放.md)。
- Excel 源表在 `config/excel`，工具在 `tool`；生成 JSON 位于 `GameResources/Config/Tables`，可重生 C# 位于 `GameLogic/Runtime/Gen`，扩展 partial 位于 `GameLogic/Runtime/Ext`。现有表结构随 v2 阶段调整，不维护并行的 v1 配置集。
- 尚未为本项目新增广告、付费、联网服务或微信 SDK。历史 Editor 和 Android 构建证据只覆盖当时原型，不能代表 v2 已验证。

## 修改与验收

- 日常用 Unity 打开 `DevProject/Assets/Scenes/Main.unity` 并点击 Play。修改源码或内容后执行 **Tools → Unity Mini Game Framework → Prepare GamePackage**，再检查启动与对应行为。
- 新增正式业务内容、可调数值与资源引用统一配置化，沿用 `config/excel` 为唯一源。运行时缺配置须明确失败，不得回退硬编码原型。临时测试配置只放测试程序集；代码保留算法与结构契约。工具链操作见 [v2 工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)，表结构和数据契约随对应阶段记录。
- 修改 Excel 源表后先执行 `node tool/export-excel.mjs export` 与 `node tool/export-excel.mjs verify`，等待 Unity 导入生成物，再执行 Prepare GamePackage。
- 修改公开 API 时检查调用方、[API 索引](docs/framework/api/README.md)和相称的测试。代码改动后验证 Unity 编译、当前场景及受影响的 PlayMode 测试；同一工程的 Editor 关闭后可运行默认 Quick 的 [verify.ps1](tools/verify.ps1)。日常不运行 Full 或递归创建新工程。
- 新的实现与验收写入 `docs/project/v2`，只记录实际执行过的结果与限制；源框架和 v1 验收属于历史资料，不能替代 v2 验证。

## 显示与平台约束

- 项目以竖屏起步：PlayerSettings 为 Portrait；GameMain 在原生移动端启动完成后开放自动旋转，允许正竖屏和左右横屏。PC/Web 默认可调整窗口为 1080×1920。微信/WebGL 的宿主方向另行接入和验收。
- 项目 Canvas 使用 ScaleWithScreenSize、1080×1920 参考尺寸和 0.5 宽高匹配，通过 IUIModule.TryConfigureCanvas 配置业务与反馈层；Home 前景与虚拟控件通过 GameSafeAreaFitter 适配安全区。新增 UI 使用锚点和布局组件，主 UI 使用 Page，模态弹窗使用 Modal；检查窄屏边界，不以固定像素坐标绑定布局。
- 本机已安装 Windows、Android（SDK/NDK/JDK）与 WebGL 模块，缺 iOS 模块和微信 SDK。v1 曾通过 Editor 验收及 Android ARM64 离线 APK 构建/静态校验，尚无设备实测；当前操作与平台限制见 [v2 工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)。v2 及其他发布平台仍需各自构建与设备验证。
- 显示默认值入口为 `Tools/ProjectExtraction/Apply Display Defaults`；首次 Git 检出须 `npm ci --prefix tool` 并等待 Unity 编译，再 Prepare GamePackage。生成的 DLL.bytes 被忽略，不沿用历史平台程序集。
