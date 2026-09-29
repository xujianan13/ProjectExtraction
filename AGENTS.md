# 项目工作指引

本文件只约束 `D:\Repositories\ProjectExtraction`。通用沟通、执行、测试、C# 中文 XML 文档注释及紧凑排版规则使用当前生效的全局 `AGENTS.md`；这里记录本项目事实和边界。

## 文档归属

- `docs/framework` 保存框架设计、使用指南、API 索引及框架历史验收；`docs/project` 是本项目文档根目录，保存需求、玩法设计、项目决策、平台接入与本项目验收。新增文档按归属放入对应目录，`docs` 根目录仅保留导航。
- [文档导航](docs/README.md) 汇总两个入口；API 索引生成与校验使用 `docs/framework/api`。

## 先读这些入口

- [README.md](README.md)：定位、工程打开方式、日常开发命令和源码位置。
- [核心玩法设计](docs/project/核心玩法设计.md)：当前已确认的玩法与结算规则；其中待定项不能当作已批准需求。
- [玩法模块与迭代计划](docs/project/玩法模块与迭代计划.md)：M01～M09 的职责、协作契约、开发顺序和待定事项；均为设计草案，不能当作已实现 API。
- [项目基础接入验收](docs/project/项目基础接入验收.md)：本项目首次导入与实测结果；源框架历史验收不得算作本项目验收。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：新增玩法、窗口和场景及查阅现有公开 API。
- [新项目复用](docs/framework/新项目复用.md)、[依赖清单](docs/framework/新项目资源与依赖清单.md)与[统一验收](docs/framework/统一验收.md)：Minimal 模板、固定资产和 Quick/Full 检查范围。
- [Unity 版本](DevProject/ProjectSettings/ProjectVersion.txt)、[Runtime 程序集定义](DevProject/Assets/Scripts/GameFramework/Runtime/GameFramework.asmdef)、[Packages 清单](DevProject/Packages/manifest.json)：版本与编译依赖的事实来源。

## 当前项目事实

- `DevProject` 使用 Unity **2022.3.62f2**，由源框架干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 的 `Minimal` 模板创建。公司名 `MiniGameStudio` 和应用标识 `com.minigamestudio.projectextraction` 是可修改的开发占位。
- 目标是 3D 搜打撤 + Roguelike，支持 PC、Mobile 与微信小游戏；优先 Mobile、微信小游戏，并适配 PC 输入和屏幕。默认竖屏，兼容横屏；UI 规划为主 UI 加模态弹窗。目标不等于已实现功能或通过的平台验收。
- 当前确认的玩法为单一基础攻击方式的近战自动攻击、程序化房间探索及撤离成长；正常撤离全部携出，死亡丢失安全箱外的入场装备和战利品，技能为可携出的特殊物品，安全箱默认容量 X 后续配置。新增体力规则为上限暂定 100、每局消耗 10、每 6 分钟恢复 1、每天 5 点重置到 100、12 点/18 点/24 点各可手动领取 40；打怪与成功撤离均产经验并自动升级，等级约束高级装备/技能、关卡和局外购买。补给领取窗口、溢出等边界以[核心玩法设计](docs/project/核心玩法设计.md)及模块待定项为准。
- 当前只有空 `Main` 玩法、`Home` 页面窗口及 Prefab 模式的 `Home` 逻辑首场景，玩法规则与内容待开发。尚未为本项目新增广告、付费、联网服务或微信 SDK。
- `DevProject/Assets/Scripts/GameFramework` 是框架唯一源码，当前不是已安装的 UPM 包；`DevProject/Packages` 保留 UniTask **2.5.11**、YooAsset **3.0.1-beta**、HybridCLR **8.12.0** 等模板依赖，渲染管线沿用模板。`GameFramework/package.json` 仅作未来包元数据。
- `Assets/Resources/Framework.prefab`、`Assets/Scenes/Main.unity` 和 `Loading.unity` 属于固定宿主资产。`Assets/Scripts/Boot` 为固定启动层；`Assets/Scripts/GameLogic` 为热更游戏代码；`Assets/GameResources` 是 YooAsset `GamePackage` 的热更内容源。通用框架不得依赖项目玩法。
- Excel 源表在 `config/excel`，当前为空；工具在 `tool`。生成 JSON 位于 `GameResources/Config/Tables`，可重生 C# 位于 `GameLogic/Runtime/Gen`，扩展 partial 位于 `GameLogic/Runtime/Ext`。

## 修改与验收

- 日常用 Unity 打开 `DevProject/Assets/Scenes/Main.unity` 并点击 Play。修改源码或内容后执行 **Tools → Unity Mini Game Framework → Prepare GamePackage**，再检查启动与对应行为。
- 修改 Excel 源表后先执行 `node tool/export-excel.mjs export` 与 `node tool/export-excel.mjs verify`，等待 Unity 导入生成物，再执行 Prepare GamePackage。
- 修改公开 API 时检查调用方、[API 索引](docs/framework/api/README.md)和相称的测试。代码改动后验证 Unity 编译、当前场景及受影响的 PlayMode 测试；同一工程的 Editor 关闭后可运行默认 Quick 的 [verify.ps1](tools/verify.ps1)。日常不运行 Full 或递归创建新工程。
- 只记录本项目实际执行过的结果与限制；源框架文档和旧验收属于来源资料，不能替代本项目验证。
## 显示与平台约束

- 项目以竖屏起步：PlayerSettings 为 Portrait；GameMain 在原生移动端启动完成后开放自动旋转，允许正竖屏和左右横屏。PC 为可调整的 720×1280 窗口。微信/WebGL 的宿主方向另行接入和验收。
- Canvas 保留 ScaleWithScreenSize、1920×1080 参考尺寸和 0.5 宽高匹配；Home 前景与虚拟控件通过 GameSafeAreaFitter 适配安全区。新增 UI 使用锚点和布局组件，主 UI 使用 Page，模态弹窗使用 Modal；检查窄屏边界，不以固定像素坐标绑定布局。
- 本机已安装 Windows、Android（SDK/NDK/JDK）与 WebGL 模块，缺 iOS 模块和微信 SDK。当前仅通过 Editor 验收，发布平台均需独立构建与设备验证。
- 显示默认值入口为 `Tools/ProjectExtraction/Apply Display Defaults`；首次 Git 检出须 `npm ci --prefix tool` 并等待 Unity 编译，再 Prepare GamePackage。生成的 DLL.bytes 被忽略，不沿用历史平台程序集。