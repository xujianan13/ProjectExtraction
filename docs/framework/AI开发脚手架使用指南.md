# AI 开发脚手架使用指南

脚手架在现有 GameLogic 热更程序集内创建玩法、窗口和逻辑场景，内容资产放入 GameResources。默认项目清单为空，启动入口继续使用原有三关演示。

## 创建内容

Unity 菜单位于 **Tools → Unity Mini Game Framework → Scaffolding**，提供 New Gameplay、New Window、New Scene 和 Validate。先使用 Dry Run 检查生成清单，再执行 Generate，等待 Unity 编译完成后验证。交互创建 UnityScene 时，应先保存 Editor 中尚无文件路径的场景。

命令行入口在仓库根目录执行。同一工程的 Unity Editor 需要先关闭；Editor 打开时使用菜单。

~~~powershell
./tools/scaffold.ps1 -Kind Gameplay -Name Fishing -IncludeModel -DryRun
./tools/scaffold.ps1 -Kind Gameplay -Name Fishing -IncludeModel
./tools/scaffold.ps1 -Kind Window -Name Shop -Preset Modal
./tools/scaffold.ps1 -Kind Scene -Name Lobby -Gameplay Fishing -Window Shop -SetStartup
~~~

Name 使用 C# 标识符，不带 Gameplay、Window 或 Scene 类型后缀。窗口预设可选 Page、Popup、Modal；场景载体可选 Prefab、UnityScene。其他 Unity 安装位置可通过 -EditorPath 指定。

完整批量示例：

~~~powershell
./tools/scaffold.ps1 -RequestFile ./config/scaffold/example-request.json -DryRun
./tools/scaffold.ps1 -RequestFile ./config/scaffold/example-request.json
~~~

示例创建 Starter 玩法、三种窗口以及 Prefab / UnityScene 两种场景，并把 StarterLobby 设为逻辑首场景。原有三关业务源码和资产继续保留。

## 生成结果在哪里

| 内容 | 路径 |
| --- | --- |
| 当前内容清单 | config/scaffold/project.json |
| 玩法代码 | DevProject/Assets/Scripts/GameLogic/Runtime/Gameplay/名称 |
| 窗口代码 | DevProject/Assets/Scripts/GameLogic/Runtime/UI/名称 |
| 场景代码 | DevProject/Assets/Scripts/GameLogic/Runtime/Scenes/名称 |
| 注册、ID 和地址常量 | DevProject/Assets/Scripts/GameLogic/Runtime/Scaffold/Gen |
| 项目启动与手写扩展 | DevProject/Assets/Scripts/GameLogic/Runtime/Scaffold/Ext |
| 玩法资产 | DevProject/Assets/GameResources/Gameplay/名称 |
| 窗口资产 | DevProject/Assets/GameResources/UI/名称 |
| 场景内容 | DevProject/Assets/GameResources/Scenes/名称 |

业务类、Prefab 和场景只首次创建，可以直接修改。Gen 下的 .Gen.cs 由生成器维护；手写逻辑放业务类或 Ext。已有 .meta 保留，生成器不重建用户编辑过的内容。写入阶段异常会回滚本次新增文件并还原修改过的生成文件；回滚若受磁盘错误等影响失败，会独立报告 rollback 阶段。

`sourceRoot` 当前固定为 `Assets/Scripts/GameLogic/Runtime`，不能改到其他目录。`rootNamespace` 仅用于生成的玩法、窗口和场景业务类型；内部 `ProjectContent`、`ContentIds`、`ContentAddresses`、`ProjectScaffoldBridge` 和 `ProjectStartup` 始终位于 `GameLogic.Scaffold`。更改业务根命名空间时，需要迁移已有业务类及引用，并处理下述 `createdShape` 校验。

窗口默认关闭后立即释放。可在项目描述中配置 scope、cacheMode、layer、closeOnOutsideClick、maskAlpha 和 destroyDelaySeconds，再重新生成注册表。

## 更新清单与验证

同名内容再次创建会报错。刷新已有清单派生的注册表和常量使用：

~~~powershell
./tools/scaffold.ps1 -Regenerate -DryRun
./tools/scaffold.ps1 -Regenerate
./tools/scaffold.ps1 -Validate
~~~

首次生成时，每个条目会记录 `createdShape`，锁定 `rootNamespace`、`sourceRoot`、种类、名称、内容模式、是否包含 Model，以及场景关联的玩法和窗口。更改其中任一项时，生成和 Validate 会报告“首次骨架结构已变化”；此检查防止保留的手写代码与配置静默分离。窗口预设、缓存和展示参数可更新后运行 `-Regenerate`；预设还影响首次创建的 Prefab 布局，修改预设时需自行调整已保留的 Prefab。

需要迁移已创建条目时，先手动迁移对应业务类及 Unity 资产，并修正全部调用方；然后同步项目清单中的结构字段和 `createdShape`，执行 `-Regenerate`、等待编译，再执行 `-Validate`。仅修改 `createdShape` 不能完成迁移。场景关联和 Prefab / UnityScene 载体的切换尤其需要同步修改保留的场景业务类；重新生成注册表不会覆盖它。首版主要通过创建向导添加内容，重命名和删除也需要按普通源码与 Unity 资产编辑处理。

资源地址采用 GameResources 中文件名去掉扩展名的规则，必须在整个收集范围内唯一。窗口编号从 2000 起分配；1001、1100 为现有样例保留 ID。编号保存到描述文件后保持稳定。

脚本区分生成和编译后验证两个阶段；最终结果在 verification/scaffold-validate-result.json。每类操作覆盖自己的固定日志与结果文件，避免累计日志。DryRun 的结果在 verification/scaffold-plan-result.json，包含完整规范化 entries（资源地址、窗口 ID、场景关联）、将写入的 files 及 nextStep。

## 运行与打包

1. 创建内容并通过 Validate。
2. 执行 Prepare GamePackage；该入口包含脚手架源码、注册、资产与配置一致性检查。
3. 打开 Main 场景运行，仍沿用 Framework → Loading → 热更 → GameMain。
4. 构建真实 AB 时，执行 Build Offline GamePackage、Build Startup GamePackage 或 Build Remote GamePackage。
5. 配置了脚手架逻辑首场景时，包级 GamePackageSettings.StartupSceneLocation 应留空，确保只有一个首内容入口。

Prefab 内容场景适合多数小游戏；UnityScene 模式会创建独立 .unity 资产并通过 YooAsset Additive 加载。将 UnityScene 类型设为逻辑首场景时，`ProjectStartup` 会先取得名为 `[ScaffoldHost]` 的中性 Unity 场景；尚未加载时创建，已加载时复用，并将其设为活动场景，然后进入逻辑场景。该宿主只承载 Unity 场景切换，不是额外生成的内容资产。生成的内容资产中不放第二个 Framework 或 HotUpdateBootstrap。

GameMain 的接入是一次性的；后续新条目由生成注册表连接。常见业务启动调整放在 ProjectStartup。启动准备阶段不能等待 Loading 已卸载或启动收尾后才就绪的存档状态，相关交互应在后续阶段启用。

## 生命周期与 API

- SceneScope、GameplayScope、OpenScope、SpawnScope 分别对应场景进入、玩法进入、窗口打开与池对象生成的一轮使用。
- 同一窗口重复 Show 时只刷新显示；本轮监听在 Open 登记。
- 异步工作捕获当前 scope，额外等待后检查该轮仍有效。
- 退出时显式等待 GameplayModule.StopAsync、PrefabPool.DisposeAsync 和内容场景卸载，使用独立清理令牌。
- 窗口管理器持有内部资源租约。Scene / Session 是分类，独立内容先关闭自己的窗口，分类缓存由共同所有者显式销毁。
- 进入失败、取消和强制退出也必须释放已经取得的内容。

按任务查阅 [API 索引](api/README.md)、[可编译示例说明](api/recipes/README.md) 和 [生命周期约定](api/生命周期约定.md)。

~~~powershell
./tools/scaffold.ps1 -RefreshApiIndex
./tools/scaffold.ps1 -CheckApiIndex
~~~

Refresh 从当前 Unity 编译程序集更新签名快照；Check 对比受版本管理的索引及引用路径。示例位于 DevProject/Assets/Tests/PlayMode/ApiRecipes.cs，由测试程序集编译。

## 验收入口

ScaffoldContentPlayModeTests 用于已应用批量示例并配置逻辑首场景的验证副本。它检查真实 Main 启动、场景加载中取消、重复进入退出、三种窗口预设、玩法和实例池清理、资源租约恢复基线及 Unity 内容场景卸载。空清单项目会跳过这项集成测试。

实际执行的编译、PlayMode 和打包结果见 [验收记录](AI开发脚手架验收记录.md)。
