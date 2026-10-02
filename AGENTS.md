# 项目工作指引

本文件只约束 `D:\Repositories\ProjectExtraction`。通用沟通、执行、测试、C# 中文 XML 文档注释及紧凑排版规则使用当前生效的全局 `AGENTS.md`；这里记录本项目事实和边界。

## 文档归属

- `docs/framework` 保存框架设计、使用指南、API 索引及框架历史验收；`docs/project` 是本项目文档根目录，保存需求、玩法设计、项目决策、平台接入与本项目验收。新增文档按归属放入对应目录，`docs` 根目录仅保留导航。
- [文档导航](docs/README.md) 汇总两个入口；API 索引生成与校验使用 `docs/framework/api`。

## 先读这些入口

- [README.md](README.md)：定位、工程打开方式、日常开发命令和源码位置。
- [核心玩法设计](docs/project/核心玩法设计.md)：当前已确认的玩法与结算规则；其中待定项不能当作已批准需求。
- [玩家主要属性与货币设计](docs/project/玩家主要属性与货币设计.md)：等级、经验、体力、金币、钻石；M07 管成长和钱包，M09 管体力，买卖与物品变更统一保存。建议与待定项不能当作已批准规则。
- [角色属性与装备词条](docs/project/角色属性与装备词条.md)：HP、攻击、防御、暴击值/暴击伤害、闪避、增伤/减伤，武器/护甲和六档品质；公式与词条数量为首版可调数值。
- [Roguelike 三选一设计与实现](docs/project/Roguelike三选一设计与实现.md)：局内选择暂停、临时强化与物品奖励、默认关闭的刷新和第四项授权。
- [角色动画与战斗接入](docs/project/角色动画与战斗接入.md)：项目通用动画播放器、战斗适配与热更控制器；通用调用见[通用动画播放](docs/framework/通用动画播放.md)。
- [战斗反馈与角色碰撞](docs/project/战斗反馈与角色碰撞.md)：血条、伤害反馈、地面预警、音效、ECS 占位和死亡结果页时序；本轮验证状态以该文档为准。
- [测试资源与热更目录](docs/project/测试资源与热更目录.md)：关卡、胶囊体角色和障碍的热更 Prefab、生命周期与生成入口。
- [战斗摇杆与界面适配](docs/project/战斗摇杆与界面适配.md)：1080×1920 默认竖屏、触屏摇杆控制、安全区和不同尺寸布局。
- [技能成长设计](docs/project/技能成长设计.md)、[主界面设计](docs/project/主界面设计.md)：角色技能点的局外提升、Home 顶部五项属性与底部仓库/技能入口。
- [关卡内容首版实现](docs/project/关卡内容首版实现.md)：房间模板与固定内容计划、随机怪群、敌人多技能、特殊房间及可达性验证。
- [搜打撤交互首版实现](docs/project/搜打撤交互首版实现.md)：局内换装、背包安全箱、固定商品商店和模态暂停。
- [高级词条与局内流派首版实现](docs/project/高级词条与局内流派首版实现.md)：高级词条冻结、战斗增益、Rogue 升级与兼容边界；本轮验证状态以该文档为准。
- [关卡与地图设计](docs/project/关卡与地图设计.md)：随机连通地图、未清图自由往返、回访状态与撤离建议。
- [ECS 战斗框架设计](docs/project/战斗框架设计.md)：统一效果执行、技能时间轴、飞弹和 Buff，包含固定时序与跨图状态；首版实现与验证范围见[首版代码实现与验证](docs/project/首版代码实现与验证.md)。
- [主界面与关卡场景流程设计](docs/project/主界面与关卡场景流程设计.md)：出战、场景激活、终局记录、结果页和返场恢复的设计；首版接入已实现，范围及验证状态见[场景流程首版实现与验证](docs/project/场景流程首版实现与验证.md)。
- [玩法模块与迭代计划](docs/project/玩法模块与迭代计划.md)：M01～M09 的职责、协作契约、开发顺序和待定事项；设计名称不能当作已实现 API，实际首版范围以实现记录为准。
- [项目基础接入验收](docs/project/项目基础接入验收.md)：本项目首次导入与实测结果；源框架历史验收不得算作本项目验收。
- [AI 开发入口](docs/framework/AI开发入口.md)、[脚手架指南](docs/framework/AI开发脚手架使用指南.md)与 [API 索引](docs/framework/api/README.md)：新增玩法、窗口和场景及查阅现有公开 API。
- [新项目复用](docs/framework/新项目复用.md)、[依赖清单](docs/framework/新项目资源与依赖清单.md)与[统一验收](docs/framework/统一验收.md)：Minimal 模板、固定资产和 Quick/Full 检查范围。
- [Unity 版本](DevProject/ProjectSettings/ProjectVersion.txt)、[Runtime 程序集定义](DevProject/Assets/Scripts/GameFramework/Runtime/GameFramework.asmdef)、[Packages 清单](DevProject/Packages/manifest.json)：版本与编译依赖的事实来源。

## 当前项目事实

- `DevProject` 使用 Unity **2022.3.62f2**，由源框架干净提交 `267f15313f997b703e90af351279e03bb82ea15e` 的 `Minimal` 模板创建。公司名 `MiniGameStudio` 和应用标识 `com.minigamestudio.projectextraction` 是可修改的开发占位。
- 目标是 3D 搜打撤 + Roguelike，支持 PC、Mobile 与微信小游戏；优先 Mobile、微信小游戏，并适配 PC 输入和屏幕。默认竖屏，兼容横屏；UI 规划为主 UI 加模态弹窗。目标不等于已实现功能或通过的平台验收。
- 当前确认的玩法为单一基础攻击方式的近战自动攻击、随机连通地图探索及撤离成长（暂定无需完成当前地图即可去相连地图）；正常撤离全部携出，死亡丢失安全箱外的入场装备和战利品，技能由升级获得技能点并在局外提升，不是物品；已保存技能等级和剩余点数不随物品死亡损失清除。安全箱默认容量 X 后续配置。新增体力规则为上限暂定 100、每局消耗 10、每 6 分钟恢复 1、每天 5 点重置到 100、12 点/18 点/24 点各可手动领取 40；打怪与成功撤离均产经验并自动升级，等级约束高级装备穿戴、关卡和局外购买；升级另获技能点，具体数量待定。补给领取窗口、溢出等边界以[核心玩法设计](docs/project/核心玩法设计.md)及模块待定项为准。
- 主要属性确定为等级、经验、体力、金币、钻石。金币用于常规消费、购买物品，战利品可出售换金币；钻石为独立的高级货币，产出、价格与高级消费项目单独定义。消费使用固定币种，余额不足即交易失败。钻石来源/具体用途、出售细节和货币死亡政策仍待定。
- 战斗属性包括 HP、攻击、防御、暴击值、暴击伤害、闪避、增伤、减伤；武器和护甲使用统一默认属性与随机词条。物品品质按普通/精良/优秀/史诗/传说/神器依次对应白/绿/蓝/紫/橙/红。公式、掉落权重与词条数为首版可调参数；第五阶段已接入七种高级词条战斗增益和八个局内流派奖励，具体范围与验证见[高级词条与局内流派首版实现](docs/project/高级词条与局内流派首版实现.md)。
- 已新增 Home 五属性、仓库和技能界面、随机连通地图、Expedition 关卡场景及轻量 ECS 战斗首版；场景流程现由 ProjectRunCoordinator 接入 GameRoundFlow，包含入场/终局持久阶段、全局恢复与结果界面和返场确认。当前仍使用占位表现和原型数值，定向自动化验证已通过，完整设计及设备验收未完成。原始能力与历史验证见[首版代码实现与验证](docs/project/首版代码实现与验证.md)，新增流程边界见[场景流程首版实现与验证](docs/project/场景流程首版实现与验证.md)。尚未为本项目新增广告、付费、联网服务或微信 SDK。
- Roguelike 三选一保留战利品房开箱入口，首次 Boss 清房必选，小怪房按配置概率抽取；暂停 ECS 模拟，选择一项临时属性、随机战利品或流派升级后恢复。刷新和第四项授权逻辑具备但默认关闭，尚无广告提供器。临时属性不写入局外技能或装备，物品沿用已有携出规则；中途选择不提供断点存档。
- `DevProject/Assets/Scripts/GameFramework` 是框架唯一源码，当前不是已安装的 UPM 包；`DevProject/Packages` 保留 UniTask **2.5.11**、YooAsset **3.0.1-beta**、HybridCLR **8.12.0** 等模板依赖，渲染管线沿用模板。`GameFramework/package.json` 仅作未来包元数据。
- `Assets/Resources/Framework.prefab`、`Assets/Scenes/Main.unity` 和 `Loading.unity` 属于固定宿主资产。`Assets/Scripts/Boot` 为固定启动层；`Assets/Scripts/GameLogic` 为热更游戏代码；`Assets/GameResources` 是 YooAsset `GamePackage` 的热更内容源。通用框架不得依赖项目玩法。
- 关卡基础测试表现保留 14 个热更 Prefab：场景/房间位于 `GameResources/Scenes/Expedition`，角色、障碍和交互物位于 `GameResources/Gameplay/Expedition`，13 个材质位于 `GameResources/Art/Prototype/Materials`。由 `ExpeditionVisualAssets` 加载并随场景释放租约；角色为胶囊体，碰撞仍由 ECS 管理。资源生成入口为 `Tools/ProjectExtraction/Create Test Prefabs`，只补齐缺失资产；另增 4 个反馈 Prefab、3 个合成测试 WAV 和 2 个材质，由 `Create Combat Feedback` 补齐。目录和本轮验证状态见[测试资源与热更目录](docs/project/测试资源与热更目录.md)、[战斗反馈与角色碰撞](docs/project/战斗反馈与角色碰撞.md)。
- 战斗反馈从 ECS 快照与事件驱动血条、伤害/暴击/闪避文字、命中闪光、下一伤害节点地面预警和短音效；预警飞弹只提示发射方向或出生目标范围，不表示完整弹道。角色 XZ 圆形占位、墙体扩半径保守矩形及移动扫掠由 ECS 负责；敌人使用障碍角点追击并在施法时停步，死者不阻挡。玩家死亡立即冻结并持久化结果，完成 0.6 秒倒下与 0.15 秒停留后进入结果页。本轮不含 NavMesh、动态拥挤避让或按墙裁剪预警；尚无真机与人工可视化验收。
- `GameFramework.AnimatorPlayback` 是不依赖 GameLogic 的通用动画播放器，支持本地时间播放与外部进度采样；项目通过 `CombatCharacterAnimation` 读取 ECS 快照控制 Idle/Move/Attack/Hit/Death。四个测试角色使用原生 Animator，5 个动画片段和控制器位于 `GameResources/Art/Prototype/Animations`。动画不参与战斗伤害判定或 ECS 位移。
- Excel 源表在 `config/excel`，当前包含 27 张项目内容表；工具在 `tool`。生成 JSON 位于 `GameResources/Config/Tables`，可重生 C# 位于 `GameLogic/Runtime/Gen`，扩展 partial 位于 `GameLogic/Runtime/Ext`。

- 关卡内容已扩展为 7 种布局、11 组候选怪群、3 类小怪和多技能 Boss；每房模板/怪群在开局抽定，回访复用 ECS 状态。敌人按生命阶段、距离、优先级和权重选技能，各技能独立冷却。配置和最新验证见[关卡内容首版实现](docs/project/关卡内容首版实现.md)。

- 搜打撤交互首版已接入：局内背包与安全箱使用模态窗口，武器/护甲可穿脱并预览属性；换装保留局内强化及冷却，不因生命上限提升回血。NPC 商品开局冻结，明确购买并保留回访售出状态。所有商业数值沿用既有 Excel；窗口暂停独立于应用暂停和三选一。

## 修改与验收

- 日常用 Unity 打开 `DevProject/Assets/Scenes/Main.unity` 并点击 Play。修改源码或内容后执行 **Tools → Unity Mini Game Framework → Prepare GamePackage**，再检查启动与对应行为。
- 新增正式业务内容、可调数值与资源引用统一配置化，沿用 `config/excel` 为唯一源。运行时缺配置须明确失败，不得回退硬编码原型。临时测试配置只放测试程序集；代码保留算法与结构契约。配置装载和兼容边界见[内容配置化](docs/project/内容配置化.md)。
- 修改 Excel 源表后先执行 `node tool/export-excel.mjs export` 与 `node tool/export-excel.mjs verify`，等待 Unity 导入生成物，再执行 Prepare GamePackage。
- 修改公开 API 时检查调用方、[API 索引](docs/framework/api/README.md)和相称的测试。代码改动后验证 Unity 编译、当前场景及受影响的 PlayMode 测试；同一工程的 Editor 关闭后可运行默认 Quick 的 [verify.ps1](tools/verify.ps1)。日常不运行 Full 或递归创建新工程。
- 只记录本项目实际执行过的结果与限制；源框架文档和旧验收属于来源资料，不能替代本项目验证。
## 显示与平台约束

- 项目以竖屏起步：PlayerSettings 为 Portrait；GameMain 在原生移动端启动完成后开放自动旋转，允许正竖屏和左右横屏。PC/Web 默认可调整窗口为 1080×1920。微信/WebGL 的宿主方向另行接入和验收。
- 项目 Canvas 使用 ScaleWithScreenSize、1080×1920 参考尺寸和 0.5 宽高匹配，通过 IUIModule.TryConfigureCanvas 配置业务与反馈层；Home 前景与虚拟控件通过 GameSafeAreaFitter 适配安全区。新增 UI 使用锚点和布局组件，主 UI 使用 Page，模态弹窗使用 Modal；检查窄屏边界，不以固定像素坐标绑定布局。
- 本机已安装 Windows、Android（SDK/NDK/JDK）与 WebGL 模块，缺 iOS 模块和微信 SDK。当前仅通过 Editor 验收，发布平台均需独立构建与设备验证。
- 显示默认值入口为 `Tools/ProjectExtraction/Apply Display Defaults`；首次 Git 检出须 `npm ci --prefix tool` 并等待 Unity 编译，再 Prepare GamePackage。生成的 DLL.bytes 被忽略，不沿用历史平台程序集。
