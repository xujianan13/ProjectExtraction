# ProjectExtraction

3D 自动近战、线性房间闯关与装备带出的小游戏。目标平台为 Mobile、微信小游戏和 PC，优先 Mobile、微信小游戏，同时适配 PC 输入与屏幕。默认竖屏 1080×1920，兼容横屏；界面采用 Page 与 Modal。平台目标仍须单独验收。

**当前采用 V3；本轮 Boss 宝箱整批领取已通过本机 Memory 验收，前轮已提交 `a5222ef`。** 原型配置为 `Combat → Chest → Combat → Npc → Combat → Boss` 六房。最终 Boss 击败后地图宝箱出现，触碰时一次冻结 3 件实际掉落，点击“领取”全部收入关卡背包；全部领奖只解锁出口，走到出口弹出“带回结束 / 留在本房”，确认带回才通关。击杀经验和技能点归永久成长，当前升级 `power`、`vitality`、`agility`，旧 Rogue 随机三选一已移除。已定规则见 [V3 当前版本](docs/project/v3/README.md)与[整体设计](docs/project/v3/线性房间闯关整体设计.md)。

历史首轮和触碰调整的验收记录保留于[实施计划与验收 6.1、6.3](docs/project/v3/实施计划与验收.md)；已提交 `a5222ef` 的 6.4 历史轮次 Prepare、38 表、EditMode **364/364**、定向 **2/2**、Quick PlayMode **586/586**、横竖屏 **2/2**（40 张截图、两份布局）通过，失败修复和实际证据见[6.4](docs/project/v3/实施计划与验收.md#64-摇杆速度boss-奖励宝箱与出口结算)。本轮一键全领的实际结果另见[6.5](docs/project/v3/实施计划与验收.md#65-boss-宝箱一次领取全部物品)，不沿用历史通过数量。

携入装备固定，局内不可换装，任何终局均归还；所得和购入仅进入独立关卡背包。死亡确认/主动放弃只丢关卡背包。暂离主城保留同一活动局，挂起期间锁仓库、局外交易与换装；未完成战斗续关保留玩家检查点和已获/已耗，重建敌人并按稳定槽位去重发奖。首次只扣 10 体力，首房不另扣费，后续新房 1；未清 Combat/未击败 Boss 续关 1、Chest 续关 0、Npc 续关推进下一房并 1。Boss 已击败后，未开箱、已开未领、全领未退出的存活检查点都回到当前 Boss 房，零体力恢复、不重生 Boss、不自动弹窗；死亡待选仍收 1 且不回满血。

摇杆超过死区后的有效输入只决定方向，统一使用角色配置移动速度，不随偏移幅度减速；松开或回到死区内停止。触碰 NPC、未领取宝箱或已开放出口自动打开窗口并暂停，主界面无互动按钮。NPC/出口及尚未领取的 Boss 宝箱窗口关闭后，须离开触碰范围再进入才能重开，整批掉落和领取事实保留；原地不反复弹窗，已领完宝箱不再触发。

死亡可无限次广告原地满血复活；暂停/死亡待选可请求广告撤离，完成后从关卡背包选择 0..3 件实例，不足三件可全选并允许取消勾选，出战装备不占名额。当前广告为模拟提供者，正式广告、付费、联网服务及微信 SDK 尚未接入。

Boss 宝箱窗口 `BossLootWindow` 只读展示全部物品详情，提供“领取 / 关闭”；不再选中单件或分轮领取。当前 `CfgBossRewards` 为 `ItemCount = 3`、`MinEquipmentItems = 1`、`MinCollectionItems = 1`，Boss 奖励状态 `BossLootState.Version = 2`。当前配置版本为 `v3-room-prototype-3`，使用 `SaveBackendMode.Memory`，本次运行内保留进度，停止 Play 或重启后重新开始；原有磁盘档不读、不写、不删，旧档迁移独立决定。当前恢复规则不代表跨进程磁盘恢复已验收。38 表、占位表现与原型数值不代表完整内容量或平衡验收。实际范围和验证状态见[V3 实施计划与验收](docs/project/v3/实施计划与验收.md)。V2 结果保留在[V2 历史实现记录](docs/project/v2/多区域玩法实现与验收.md)，不计为 V3 通过。

工程由 `UnityMiniGameFramework` 的 `Minimal` 模板创建，只维护一套有效实现。公司名 `MiniGameStudio`、应用标识 `com.minigamestudio.projectextraction` 为可修改的开发占位。

## 打开与运行

用 Unity **2022.3.62f2** 打开 [DevProject](DevProject/)，等待首次导入、依赖解析和编译。打开 [Assets/Scenes/Main.unity](DevProject/Assets/Scenes/Main.unity) 后点击 Play，检查固定启动、Loading、`GamePackage` 和 `Home` 入口。首次导入的历史实测结果见 [V1 项目基础接入验收](docs/project/v1/项目基础接入验收.md)；旧结果不代表 V3 已通过验收。

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

文档总入口为 [docs/README.md](docs/README.md)：通用框架文档位于 `docs/framework`，当前项目文档位于 `docs/project/v3`，V2 多区域设计与实现基线保留在 `docs/project/v2`，更早资料保留在 `docs/project/v1`。

- [项目工作指引](AGENTS.md)：当前项目事实、修改边界与验收原则。
- [V3 当前版本](docs/project/v3/README.md)：当前玩法方向、版本状态、文档导航和代码替换要求。
- [线性房间闯关整体设计](docs/project/v3/线性房间闯关整体设计.md)：已确认规则、原型费用与剩余平台及迁移边界。
- [房间进度与结算方案](docs/project/v3/房间进度与结算方案.md)、[实施计划与验收](docs/project/v3/实施计划与验收.md)：恢复结算契约、实际改造范围和验证矩阵。
- [V3 文档承接清单](docs/project/v3/文档承接清单.md)：稳定技术规范、属性分析及旧规则的采用或替换边界。
- [项目版本导航](docs/project/README.md)、[V2 基线](docs/project/v2/README.md)、[V1 历史归档](docs/project/v1/README.md)：版本约定、旧设计与实际实现证据。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架使用指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：新增玩法、窗口和场景的入口；使用现有 API 时以实际源码和签名为准。
- [新项目复用](docs/framework/新项目复用.md)与[资源依赖清单](docs/framework/新项目资源与依赖清单.md)：Minimal 模板的生成流程、固定资产和依赖；其中源框架的历史验证记录仅供参考。
- [热更启动流程](docs/framework/热更启动流程.md)、[Excel 数据表](docs/framework/Excel数据表.md)与[UI 系统](docs/framework/UI系统.md)：对应系统的配置和开发说明。

当前项目由 `UnityMiniGameFramework` 的干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 经 `tools/new-project.ps1 -Template Minimal` 创建。后续业务实现与平台验收以本仓库代码及 V3 新增的实际记录为准，V1/V2 结果按各自记录范围解释。
