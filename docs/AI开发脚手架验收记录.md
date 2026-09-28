# AI 开发脚手架与 API 索引验收记录

日期：2026-09-26。Unity：2022.3.62f2。验收平台：Windows Editor；资源包目标：StandaloneWindows64。

## 已实现

- Gameplay、Window、Scene 三类最小模板；可选 Model，Page / Popup / Modal 窗口预设，Prefab / UnityScene 内容载体。
- 项目清单、稳定窗口 ID、资源地址常量、显式注册和 GameMain 一次性接入。
- Unity Editor 向导及 PowerShell / JSON 批处理入口，生成预览、编译后校验、固定结果与日志路径。
- Gen 可刷新，业务源码、Ext、Prefab、Unity 场景及既有元数据保留。结构字段变化明确要求手动迁移；写入异常回滚本次产物。
- 20 个默认模块接口及 12 个辅助类型的 Markdown / JSON API 索引，真实编译签名、使用示例和生命周期约定。

## 实际执行结果

使用现有 `tools/new-project.ps1` 创建 `verification/scaffold-reuse` 隔离源码副本，完成依赖安装、首次导入和项目身份初始化。在副本使用 `ScaffoldVerification.Game` 业务命名空间应用批量请求；生成 Starter 玩法（含 Model）、三个窗口及 StarterLobby / StarterWorld 两种场景。

| 检查 | 实际结果 | 本地证据 |
| --- | --- | --- |
| 主工程 Unity 编译及空清单 Validate | 通过 | verification/scaffold-validate-result.json |
| API 索引 Refresh / Check | 主工程和独立副本通过 | verification/scaffold-check-api-result.json |
| 主工程定向 PlayMode 回归 | 3 / 3 通过：原三关演示、API 事件作用域、脚手架注册重启 | verification/scaffold-main-tests.xml |
| 副本 Dry Run、生成、二次编译及绑定 Validate | 通过，14 份源码计划和 6 个内容资产可加载 | verification/scaffold-reuse/verification/scaffold-validate-result.json |
| 生成保护及预检 | 7 项通过：保留手写修改、确定性重生成、名称冲突、窗口 ID 冲突、非有限数值、路径越界、结构变化 | verification/scaffold-reuse/verification/scaffold-generation-checks.json |
| 写入失败回滚故障注入 | 通过；源码、Gen、配置和内容文件摘要恢复，无新业务文件残留 | verification/scaffold-reuse/verification/scaffold-rollback-check.json |
| 副本 EditorSimulate PlayMode | 2 / 2 通过：生成内容生命周期、同模块实例重启注册 | verification/scaffold-reuse/verification/scaffold-editor-tests.xml |
| 真实离线 AB 构建及资源收集 | 两次通过；分别以 Prefab 和 UnityScene 为首场景 | verification/scaffold-reuse/verification/scaffold-build.log、scaffold-unity-startup-build.log |
| 副本 Offline AB PlayMode | 2 / 2 通过 | verification/scaffold-reuse/verification/scaffold-offline-tests.xml |
| UnityScene 作为逻辑首场景 | 1 / 1 通过；实际 Main 启动后仍可反复退出并重进内容场景 | verification/scaffold-reuse/verification/scaffold-unity-startup-tests.xml |

PlayMode 合计 8 项通过。内容集成验证覆盖：加载中取消、已取消令牌下的退出清理、两类场景各两轮进入退出、窗口重复打开/关闭/重开、三种预设、作用域释放、玩法与实例池清空、活动资源租约回到基线及 Unity 内容场景卸载。

回滚故障注入在副本用普通文件占据待建资产目录，使资产创建在源码写入后失败；比较失败前后的内容摘要。保留验证实际修改窗口业务文件并通过 PrefabUtility 添加手写节点，再重新生成，确认字节和元数据不变。

## 本轮修复的验收问题

- 批处理默认未保存空场景不支持直接 Additive 新建：批处理改为 Single 创建；交互入口在任何写入前检查未保存场景。
- 资产创建失败留下部分生成物：补文件事务回滚及临时 GameObject / 场景清理。
- 非默认业务命名空间与内部 Ext 不一致：内部入口统一固定 `GameLogic.Scaffold`。
- 复用 SceneModule 重启后仍保留旧登记：新运行轮次替换同名、同类型且闲置的场景实例。
- 场景退出取消或某一步失败跳过后续清理：模板使用独立退出令牌，并保证玩法、实例池和内容场景依次释放。
- UnityScene 首场景退出时不能卸载最后一个场景：使用可复用的 `[ScaffoldHost]` 中性宿主。

## 复验入口与范围

- 操作步骤见 [使用指南](AI开发脚手架使用指南.md)。向空清单工程应用 `config/scaffold/example-request.json` 后运行 `MiniGame.Tests.ScaffoldContentPlayModeTests`。
- `ScaffoldStartupTests` 可在空清单和已生成内容的工程执行；`ApiRecipesTests` 验证事件作用域。原三关回归在默认空清单主工程执行。
- 真实 AB 复验：先在副本设置 HotUpdateBootSettings 为 Offline，执行 Build Offline GamePackage，再运行内容测试。切换首场景可用 `-Regenerate -SetStartup -StartupScene StarterWorld`，随后重新构建。
- 本次为 Unity 2022 Editor 行为及 Windows AB 资源验证；未执行独立 Player、IL2CPP、WebGL 或小程序真机测试，未做 Editor 向导的人工视觉验收。
- 主工程内容清单保持为空；现有三关演示可继续运行。验证副本、构建缓存和固定日志位于忽略目录，未进入交付源码。
