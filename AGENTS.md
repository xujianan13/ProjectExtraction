# 项目工作指引

本文件只约束 `D:\Repositories\ProjectExtraction`。通用沟通、执行、测试、C# 中文 XML 文档注释及紧凑排版规则使用当前生效的全局 `AGENTS.md`；这里记录本项目事实和边界。

## 当前版本与文档归属

- **当前玩法设计与后续开发采用 V3。** 以 [V3 当前版本](docs/project/v3/README.md)及[线性房间闯关整体设计](docs/project/v3/线性房间闯关整体设计.md)为准；用户五项方向已归入 V3，评估建议与待定项不能当作已批准规则。**当前代码仍为 V2 多区域实现，V3 尚未实施。**
- **当前先联调玩法，磁盘存档暂时关闭。** `Assets/Resources/HotUpdateBootSettings.asset` 显式选择 `SaveBackendMode.Memory`，每次启动创建独立内存会话，本次运行内保留进度，停止 Play 或重启后重新开始；原有磁盘档不读取、不写入、不删除。文件保存吞吐与跨启动恢复留到玩法确认后继续，现有存档设计及代码保留；不能将内存联调结果当作持久化验收。
- `docs/project/v3` 保存当前版本的需求、设计、决策及后续实现与验收；`docs/project/v2` 保留多区域设计与当前代码的实现基线，`docs/project/v1` 保留更早的历史资料。旧正文中的“当前”“已确认”不自动约束 V3；继续适用的范围见 [V3 文档承接清单](docs/project/v3/文档承接清单.md)。
- **工程只维护一套有效实现。** 按 V3 阶段原位改造现有代码，同批删除被替代的旧代码、配置、调用入口、测试及无引用资产。不恢复 V1 旧代码，不建立 V1/V2 备用副本、版本开关或双轨运行逻辑；本轮文档切版不执行代码删除。
- 仍适用的战斗、物品、交易和框架能力可原位改造，工程中只维护一份实现。历史数据是否迁移另行明确，不能将文档归档理解为删除实际玩家存档。
- V2 基线的 P1～P6 玩法与保存加固、本局总览、地图定位与弱点详情、收藏登记态、出口收益预览及分类统计已实现，旧房间与混合奖励已替换。地图首版选点导航已接入预览、绕障前往、手动取消、暂停恢复及堵路停止，参数由 `CfgNavigation` 配置。基础移速为 6.5，`CfgDisplay` 目标帧率为 60；扩大普攻范围并接入死亡广告复活的该轮 Prepare、43 表校验、定向 EditMode **27/27**、死亡流程 PlayMode **6/6**、Quick PlayMode **594/594**、横竖屏图形 **2/2**（十四张截图与两份布局）通过。此前导航 EditMode 47/47、导航 PlayMode 8/8、完整 EditMode 258/258、文件保存压力 1/1 及配置报告导出通过。文件后端实时诊断存在明显保存积压；本机短时内存对照约 60 FPS/60 tick/s，不代表 Mobile、长时间或发布验收达标。目标设备预算与全部进程终止矩阵仍未完成。实际源码、三图、资源清理、验证证据与限制见[V2 多区域玩法实现与验收](docs/project/v2/多区域玩法实现与验收.md)，V1/V2 结果不能冒充 V3 验收。
- V2 物品全量带回与死亡/放弃全损已完成，旧保护容器职责、配置和 UI 已移除；该轮 Prepare、43 表校验、定向 EditMode **30/30**、Quick PlayMode **594/594**、横竖屏图形 **2/2**（十六张截图与两份布局）通过，实际范围见 V2 实现记录第 6.7 节。
- V2 宝箱领取保存确认后自动展示本次获得物品，支持逐件详情，窗口暂停战斗、关闭继续探索；Prepare、Quick PlayMode **594/594**、横竖屏图形 **2/2**（十八张截图与两份布局）通过，范围及内存后端限制见 V2 实现记录第 6.8 节。
- V2 三类武器与技能首轮可玩版本已实现：刀剑/斧锤/长柄普攻、统一顺序轮询、裂地斩/追魂飞剑/环身剑阵/复苏、换装保留冷却及技能状态 UI。Prepare、43 表、EditMode **347/347**、横竖屏图形 **2/2** 通过；Quick 首轮 **594/596**，两项旧夹具修正后定向复验 **2/2**，组合覆盖 **596/596**，原始 Quick 保留失败状态。完整技能池、学习位置、进化与更多组合尚未实现，实际范围和证据见 V2 实现记录第 6.9 节。
- V3 已保存线性房间、击杀升级、独立关卡背包、暂离续关和广告三件撤离方向；经验归属、未完成房间回退、携入装备、撤离选择及磁盘恢复细则尚待明确。当前仅完成文档整理，后续实际结果写入 [V3 实施计划与验收](docs/project/v3/实施计划与验收.md)。
- `docs/framework` 保存通用框架设计、使用指南、API 索引及框架历史验收，不参与本项目玩法版本划分。`docs` 根目录只保留[文档导航](docs/README.md)，项目版本入口见[项目文档导航](docs/project/README.md)。

## 先读这些入口

- [README.md](README.md)：工程打开方式、日常命令和源码位置。
- [V3 当前版本](docs/project/v3/README.md)：已确认方向、版本状态、待定边界与文档导航。
- [线性房间闯关整体设计](docs/project/v3/线性房间闯关整体设计.md)：用户五项规则、房间与成长闭环、可行性及 D1～D7 待定决策。
- [房间进度与结算方案](docs/project/v3/房间进度与结算方案.md)：房间重开、暂离、领取交易、死亡与三件撤离的建议恢复契约。
- [V3 实施计划与验收](docs/project/v3/实施计划与验收.md)：源码基线、改造范围、阶段依赖和未来验证矩阵。
- [V3 文档承接清单](docs/project/v3/文档承接清单.md)：V2 战斗、装备、成长、属性分析和工程规范的采用范围，旧玩法规则的替换边界。
- [V2 实现基线](docs/project/v2/README.md)、[多区域玩法实现与验收](docs/project/v2/多区域玩法实现与验收.md)：查阅当前仍在运行的源码与历史验证，不能当作 V3 已完成。
- [工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)：已由 V3 承接的工程命令、Excel、热更、UI 和平台基础约束；表结构随 V3 实施更新。
- [V1 历史归档](docs/project/v1/README.md)：查阅更早的实现与来源；新采用的规则需写入 V3。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：查阅现有 API 或新增玩法、窗口和场景。设计名称不代表 API 已存在。
- [新项目复用](docs/framework/新项目复用.md)、[依赖清单](docs/framework/新项目资源与依赖清单.md)与[统一验收](docs/framework/统一验收.md)：Minimal 模板、固定资产和 Quick/Full 检查范围。
- [Unity 版本](DevProject/ProjectSettings/ProjectVersion.txt)、[Runtime 程序集定义](DevProject/Assets/Scripts/GameFramework/Runtime/GameFramework.asmdef)、[Packages 清单](DevProject/Packages/manifest.json)：版本与编译依赖的事实来源。

## 工程事实与边界

- `DevProject` 使用 Unity **2022.3.62f2**，由源框架干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 的 `Minimal` 模板创建。公司名 `MiniGameStudio` 和应用标识 `com.minigamestudio.projectextraction` 是可修改的开发占位。
- 目标是 3D 自动近战、装备带出与成长的小游戏，优先 Mobile、微信小游戏并适配 PC，默认竖屏、兼容横屏。V3 采用线性房间，Rogue 随机选择程度与经验归属仍待定。当前 V2 代码成功结束全量带回、死亡确认结算或主动放弃全损，含携入装备和本局全部物品；仓库锁定仅防出售，收藏实际入库后登记、成就属性只奖励一次。V3 携入装备政策、暂离与广告部分带出以新契约为准，不能把旧全损范围自动视为新决定。平台目标不等于已完成接入或设备验收。
- `DevProject/Assets/Scripts/GameFramework` 是框架唯一源码，当前不是已安装的 UPM 包；`GameFramework/package.json` 仅作未来包元数据。`DevProject/Packages` 保留 UniTask **2.5.11**、YooAsset **3.0.1-beta**、HybridCLR **8.12.0** 等模板依赖，渲染管线沿用模板。
- `Assets/Resources/Framework.prefab`、`Assets/Scenes/Main.unity` 和 `Loading.unity` 属于固定宿主资产。`Assets/Scripts/Boot` 为固定启动层；`Assets/Scripts/GameLogic` 为热更游戏代码；`Assets/GameResources` 是 YooAsset `GamePackage` 热更内容源。通用框架不得依赖项目玩法。
- 现有工程已接入 v2 多区域地图、刷怪技能成长、Boss 装备/收藏选择、收藏成就和完整运行保存恢复，仍使用占位表现和可调原型数值。旧房间地图、清房奖励、混合候选池及其无引用专属资产已替换删除；历史实现范围见 [v1 归档](docs/project/v1/README.md)。旧 `ActiveRunVersion = 0` 活动记录不清档、不自动退款并阻塞续关，等待独立迁移决策。
- ECS 负责伤害、Buff、位移和碰撞等战斗事实，表现从快照与事件读取；通用 `GameFramework.AnimatorPlayback` 不依赖 GameLogic，动画不参与伤害判定或 ECS 位移。稳定契约沿用 [V2 战斗系统与表现契约](docs/project/v2/战斗系统与表现契约.md)中由 V3 承接清单明确采用的部分，房间生命周期与恢复按 V3 改造；通用调用见[通用动画播放](docs/framework/通用动画播放.md)。
- Excel 源表在 `config/excel`，工具在 `tool`；生成 JSON 位于 `GameResources/Config/Tables`，可重生 C# 位于 `GameLogic/Runtime/Gen`，扩展 partial 位于 `GameLogic/Runtime/Ext`。当前表仍服务 V2 基线，随 V3 阶段原位调整，不维护并行的旧版配置集。
- V2 当前已有激励广告接口与立即完成的模拟提供者，用于每次死亡后原地满血复活，不限次数并保留同局进度和全部物品；选择结算返回才执行全部持有物品损失。V3 三件撤离尚未实现。尚未接入正式广告、付费、联网服务或微信 SDK；历史 Editor 和 Android 构建证据不能代表 V3 已验证。

## 修改与验收

- 日常用 Unity 打开 `DevProject/Assets/Scenes/Main.unity` 并点击 Play。修改源码或内容后执行 **Tools → Unity Mini Game Framework → Prepare GamePackage**，再检查启动与对应行为。
- 新增正式业务内容、可调数值与资源引用统一配置化，沿用 `config/excel` 为唯一源。运行时缺配置须明确失败，不得回退硬编码原型。临时测试配置只放测试程序集；代码保留算法与结构契约。工具链操作见 [v2 工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)，表结构和数据契约随对应阶段记录。
- 修改 Excel 源表后先执行 `node tool/export-excel.mjs export` 与 `node tool/export-excel.mjs verify`，等待 Unity 导入生成物，再执行 Prepare GamePackage。
- 修改公开 API 时检查调用方、[API 索引](docs/framework/api/README.md)和相称的测试。代码改动后验证 Unity 编译、当前场景及受影响的 PlayMode 测试；同一工程的 Editor 关闭后可运行默认 Quick 的 [verify.ps1](tools/verify.ps1)。日常不运行 Full 或递归创建新工程。
- 新的实现与验收写入 `docs/project/v3`，只记录实际执行过的结果与限制；源框架、V1 和 V2 验收属于相应版本的历史资料，不能替代 V3 验证。

## 显示与平台约束

- 项目以竖屏起步：PlayerSettings 为 Portrait；GameMain 在原生移动端启动完成后开放自动旋转，允许正竖屏和左右横屏。PC/Web 默认可调整窗口为 1080×1920。微信/WebGL 的宿主方向另行接入和验收。
- 项目 Canvas 使用 ScaleWithScreenSize、1080×1920 参考尺寸和 0.5 宽高匹配，通过 IUIModule.TryConfigureCanvas 配置业务与反馈层；Home 前景与虚拟控件通过 GameSafeAreaFitter 适配安全区。新增 UI 使用锚点和布局组件，主 UI 使用 Page，模态弹窗使用 Modal；检查窄屏边界，不以固定像素坐标绑定布局。
- 本机已安装 Windows、Android（SDK/NDK/JDK）与 WebGL 模块，缺 iOS 模块和微信 SDK。V1 曾通过 Editor 验收及 Android ARM64 离线 APK 构建/静态校验，尚无设备实测；操作与平台基础限制沿用 [工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)。V3 各目标平台仍需对应构建与设备验证。
- 显示默认值入口为 `Tools/ProjectExtraction/Apply Display Defaults`；首次 Git 检出须 `npm ci --prefix tool` 并等待 Unity 编译，再 Prepare GamePackage。生成的 DLL.bytes 被忽略，不沿用历史平台程序集。
