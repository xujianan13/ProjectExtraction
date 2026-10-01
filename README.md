# ProjectExtraction

3D 搜打撤 + Roguelike 小游戏项目。目标平台为 Mobile、微信小游戏和 PC，优先设计与验证 Mobile、微信小游戏体验，同时适配 PC 输入与屏幕。默认竖屏 1080×1920，兼容横屏；界面采用主 UI 与模态弹窗结构。平台目标仍须单独验收。

当前工程由 `UnityMiniGameFramework` 的 `Minimal` 模板创建，已接入主界面、仓库/技能、随机地图、ECS 自动近战和出入场保存的首版代码。使用运行时生成的占位界面与场景内容，原始能力和原型参数见[首版代码实现与验证](docs/project/首版代码实现与验证.md)；新增的出战、结算、结果页与返场流程见[场景流程首版实现与验证](docs/project/场景流程首版实现与验证.md)。项目公司名 `MiniGameStudio`、应用标识 `com.minigamestudio.projectextraction` 是可修改的开发占位。当前不接入广告、付费、联网服务或微信 SDK。

当前玩法方向为近战自动攻击与随机连通地图探索，暂定无需完成当前地图即可前往相连地图。正常撤离带回全部持有物品；死亡丢失安全箱外的入场装备及战利品，安全箱容量由配置决定。技能是局外成长：角色升级获得技能点，在局外提升，已保存技能成长不随物品死亡损失清除。局外增加体力与定时补给，打怪/成功撤离经验推动自动升级，等级约束高级装备穿戴、关卡及局外购买。玩家主要属性为等级、经验、体力、金币、钻石；金币用于常规消费并可通过出售战利品获得，钻石为独立的高级货币，具体产出与高级消费项目单独定义。已确认规则和待定事项见[核心玩法设计](docs/project/核心玩法设计.md)；各项落地范围分别以历史[首版实现记录](docs/project/首版代码实现与验证.md)和[场景流程首版实现与验证](docs/project/场景流程首版实现与验证.md)为准。

## 打开与运行

用 Unity **2022.3.62f2** 打开 [DevProject](DevProject/)，等待首次导入、依赖解析和编译。打开 [Assets/Scenes/Main.unity](DevProject/Assets/Scenes/Main.unity) 后点击 Play，检查固定启动、Loading、`GamePackage` 和 `Home` 入口。首次导入与本项目实测结果记录在[项目基础接入验收](docs/project/项目基础接入验收.md)；源框架文档中的历史验收数字不代表本项目结果。

首次 Git 检出先运行 `npm ci --prefix tool`；等待 Unity 编译完成后，或以后修改源码、内容后，在 Unity 菜单运行 **Tools → Unity Mini Game Framework → Prepare GamePackage**，使当前编译的 `GameLogic` 进入热更包，再运行场景。修改 `config/excel/*.xlsx` 时，先在仓库根目录导出并校验，等待 Unity 导入生成的 JSON 与 C#，然后执行 Prepare GamePackage；当前源表为空，导出与校验也应保持可运行。

```powershell
node tool/export-excel.mjs export
node tool/export-excel.mjs verify
```

关闭同一工程的 Unity Editor 后，可在仓库根目录运行默认 Quick 验收：

```powershell
& .\tools\verify.ps1
```

Quick 的检查范围和结果文件见[统一验收](docs/framework/统一验收.md)。日常开发无需运行 `-Mode Full`；Full 会创建隔离的新工程并执行额外验证。仅将本项目实际执行过的检查写入验收记录。

## 源码与资产边界

| 位置 | 用途 |
| --- | --- |
| [GameFramework](DevProject/Assets/Scripts/GameFramework/) | 通用框架唯一源码；`package.json` 只是未来导出包的元数据，当前不是已安装的 UPM 框架包。 |
| [Boot](DevProject/Assets/Scripts/Boot/) | 固定启动代码，包括 `HotUpdateBootstrap`。 |
| [GameLogic](DevProject/Assets/Scripts/GameLogic/) | 游戏热更程序与后续玩法代码；[GameMain.cs](DevProject/Assets/Scripts/GameLogic/Runtime/GameMain.cs) 是当前入口。 |
| [GameResources](DevProject/Assets/GameResources/) | YooAsset `GamePackage` 热更内容源；保留 `Main`、`Home` 模板资源，战斗场景和新增界面由项目代码构建。 |
| [Framework.prefab](DevProject/Assets/Resources/Framework.prefab) | 固定且不可热更的通用框架启动资产。 |
| [config/excel](config/excel/) 与 [tool](tool/) | Excel 源表及导表工具；生成数据和代码分别进入 `GameResources/Config/Tables` 与 `GameLogic/Runtime/Gen`。 |

`DevProject/Packages` 保留 UniTask **2.5.11**、YooAsset **3.0.1-beta**、HybridCLR **8.12.0** 等依赖；Unity 版本、依赖和渲染管线沿用源模板。固定的 `Main.unity`、`Loading.unity` 与 `Resources/Framework.prefab` 构成宿主启动部分；可热更内容留在 `GameResources`，项目业务代码留在 `GameLogic`。不要复制第二份框架源码。

## 开发资料

文档总入口为 [docs/README.md](docs/README.md)：通用框架文档位于 `docs/framework`，本项目需求、设计与验收位于 `docs/project`。

- [项目工作指引](AGENTS.md)：当前项目事实、修改边界与验收原则。
- [玩家主要属性与货币设计](docs/project/玩家主要属性与货币设计.md)：五项主要属性、金币/钻石定位、买卖与存档边界。
- [技能成长设计](docs/project/技能成长设计.md)：升级技能点与局外提升规则。
- [关卡与地图设计](docs/project/关卡与地图设计.md)：随机连通地图、未清图自由往返、回访状态与撤离建议。
- [ECS 战斗框架设计](docs/project/战斗框架设计.md)：统一效果执行、技能时间轴、飞弹和 Buff，包含固定时序与跨图状态；首版实现与验证范围见[首版代码实现与验证](docs/project/首版代码实现与验证.md)。
- [主界面设计](docs/project/主界面设计.md)：顶部五项属性、底部仓库与技能入口。
- [角色属性与装备词条](docs/project/角色属性与装备词条.md)：八项战斗属性、武器与护甲、六档品质、随机词条及迁移。
- [主界面与关卡场景流程设计](docs/project/主界面与关卡场景流程设计.md)：Home → Expedition → 结算 → Home 的流程设计；首版实现范围及验证状态见[场景流程首版实现与验证](docs/project/场景流程首版实现与验证.md)。
- [玩法模块与迭代计划](docs/project/玩法模块与迭代计划.md)：9 个玩法模块的设计入口、职责边界、验收条件和迭代顺序，当前按模块逐步实现。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架使用指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：新增玩法、窗口和场景的入口；使用现有 API 时以实际源码和签名为准。
- [新项目复用](docs/framework/新项目复用.md)与[资源依赖清单](docs/framework/新项目资源与依赖清单.md)：Minimal 模板的生成流程、固定资产和依赖；其中源框架的历史验证记录仅供参考。
- [热更启动流程](docs/framework/热更启动流程.md)、[Excel 数据表](docs/framework/Excel数据表.md)与[UI 系统](docs/framework/UI系统.md)：对应系统的配置和开发说明。

当前项目由 `UnityMiniGameFramework` 的干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 经 `tools/new-project.ps1 -Template Minimal` 创建。后续业务实现与平台验收以本仓库的代码和[项目基础接入验收](docs/project/项目基础接入验收.md)为准。
