# ProjectExtraction

3D 搜打撤 + Roguelike 小游戏项目。目标平台为 Mobile、微信小游戏和 PC，优先设计与验证 Mobile、微信小游戏体验，同时适配 PC 输入与屏幕。默认竖屏 1080×1920，兼容横屏；界面采用主 UI 与模态弹窗结构。平台目标仍须单独验收。

**当前采用 v2。** 新版方向是多区域连续探索：整关消耗一次体力，普通怪和精英推动技能三选一，区域尾王与关卡 Boss 提供独立的装备收藏三选一，并通过收藏成就获得长期成长。已确认范围、待定事项和实施顺序见 [v2 当前版本](docs/project/v2/README.md)及[整体设计](docs/project/v2/多区域探索与成长奖励整体设计.md)。

v2 的 P1～P6 玩法与保存加固代码已实现，源码入口、43 张配置表、三张区域地图、旧实现清理及实际验证集中见[多区域玩法实现与验收](docs/project/v2/多区域玩法实现与验收.md)；本轮物品全量带回与死亡/放弃全损改造已验证，目标设备、发布与长时间验收仍需完成。工程由 `UnityMiniGameFramework` 的 `Minimal` 模板创建，当前仍使用占位表现和可调原型参数。**v1 代码不做保留**：被替代的代码、配置、入口、测试和无引用资产已同批清理，工程只维护一份当前实现。旧设计和实际验收保存在 [v1 历史归档](docs/project/v1/README.md)，不能作为 v2 验收证明。

项目公司名 `MiniGameStudio`、应用标识 `com.minigamestudio.projectextraction` 是可修改的开发占位。激励广告已有可替换的项目接口与立即完成的模拟提供者；每次死亡均可广告原地满血复活，不限次数并保留本局全部物品。成功结束带回全部当前持有物，死亡确认结算或主动放弃损失全部持有物；本次不改变钱与已保存成长。尚未接入正式广告、付费、联网服务或微信 SDK。

## 打开与运行

用 Unity **2022.3.62f2** 打开 [DevProject](DevProject/)，等待首次导入、依赖解析和编译。打开 [Assets/Scenes/Main.unity](DevProject/Assets/Scenes/Main.unity) 后点击 Play，检查固定启动、Loading、`GamePackage` 和 `Home` 入口。首次导入的历史实测结果见 [v1 项目基础接入验收](docs/project/v1/项目基础接入验收.md)；旧结果不代表 v2 已通过验收。

首次 Git 检出先运行 `npm ci --prefix tool`；等待 Unity 编译完成后，或以后修改源码、内容后，在 Unity 菜单运行 **Tools → Unity Mini Game Framework → Prepare GamePackage**，使当前编译的 `GameLogic` 进入热更包，再运行场景。修改 `config/excel/*.xlsx` 时，先在仓库根目录导出并校验，等待 Unity 导入生成的 JSON 与 C#，然后执行 Prepare GamePackage。新增正式内容统一走配置；操作说明见 [v2 工程配置与平台接入](docs/project/v2/工程配置与平台接入.md)，表结构和数据契约随实施阶段更新。

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

文档总入口为 [docs/README.md](docs/README.md)：通用框架文档位于 `docs/framework`，当前项目文档位于 `docs/project/v2`，旧系统资料归档在 `docs/project/v1`。

- [项目工作指引](AGENTS.md)：当前项目事实、修改边界与验收原则。
- [v2 当前版本](docs/project/v2/README.md)：当前玩法方向、沿用基础边界、实施状态和代码替换要求。
- [多区域探索与成长奖励整体设计](docs/project/v2/多区域探索与成长奖励整体设计.md)：当前开发总入口，包含 P1～P6 阶段与实施前待定项。
- [项目版本导航](docs/project/README.md)、[v1 历史归档](docs/project/v1/README.md)：文档版本约定及旧设计、实现和验收证据。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架使用指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：新增玩法、窗口和场景的入口；使用现有 API 时以实际源码和签名为准。
- [新项目复用](docs/framework/新项目复用.md)与[资源依赖清单](docs/framework/新项目资源与依赖清单.md)：Minimal 模板的生成流程、固定资产和依赖；其中源框架的历史验证记录仅供参考。
- [热更启动流程](docs/framework/热更启动流程.md)、[Excel 数据表](docs/framework/Excel数据表.md)与[UI 系统](docs/framework/UI系统.md)：对应系统的配置和开发说明。

当前项目由 `UnityMiniGameFramework` 的干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 经 `tools/new-project.ps1 -Template Minimal` 创建。后续业务实现与平台验收以本仓库代码及 v2 新增的实际记录为准。
