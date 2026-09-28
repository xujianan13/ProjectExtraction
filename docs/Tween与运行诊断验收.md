# Tween 与运行诊断验收

验收日期：2026-09-26。工程：`DevProject`；Unity：2022.3.62f2；本次使用 Windows 上的 Unity Editor。

## 交付内容

- 默认注册 `ITweenModule` 与 `IDiagnosticsModule`。
- Tween：基础值与常用 Unity 属性插值、缓动、串并行、延迟、循环、双时间源、暂停恢复、终态等待、取消和作用域清理。
- 诊断：按需独立快照、线程安全的有界日志、默认模块统计、自定义提供者、热更及单局状态、Editor UI Toolkit 窗口和开发运行时 UGUI 面板。
- 输入阻挡现在保留原因；旧生命周期句柄不会误释放新登记。
- 图形验收复现了退出时窗口根对象先被 Unity 销毁的旧问题：关闭路径现在释放作用域并移除失效缓存，展示根已销毁时停止刷新。

用法见[通用 Tween](通用Tween.md)与[统一运行诊断](运行诊断.md)。

## 实际执行结果

| 验证 | 结果 |
| --- | --- |
| Unity 批处理编译 | 退出码 0 |
| 完整 PlayMode 首轮 | 479 项：477 通过、2 失败；失败是新增面板测试错误地将 EventSystem.current 设为 null |
| 面板定向复测 | 修正测试隔离并修复滚动区遮罩后，2/2 通过；包括布局、可见区域、输入释放、时间缩放和框架关闭 |
| UI、单局与诊断面板回归 | 新增窗口根已销毁回归后，34/34 通过 |
| 真实 Main 场景 | Loading 后进入菜单；诊断显示 13 个分区，所有分区 Error 为空；快照包含 Hot Update 与 Game Round |
| 实际图形检查 | 面板正文可见；Editor Diagnostics 窗口已创建 Foldout 分区；最新图形验收退出码 0，退出时无先前的 MissingReferenceException |

Tween 回归覆盖大步长、串并行/往返组合、共用属性、零时长、延迟、双时间源、暂停恢复、取消令牌、作用域 Stop、目标销毁、回调重入、异常终态、无限循环与片段复用。诊断回归覆盖快照独立性、缺失模块、提供者异常和重入、释放、有界并发日志以及输入阻挡来源。

完整首轮报告保留原始失败事实，后续以定向报告记录修复通过结果；没有把定向结果改写成重新执行过的完整测试结果。

## 本机证据

以下产物位于被 Git 忽略的 `verification` 目录，不随源码模板分发：

- `tween-diagnostics-compile.log`
- `tween-diagnostics-playmode.xml`、`tween-diagnostics-playmode.log`
- `diagnostics-panel-tests.xml`、`diagnostics-panel-tests.log`
- `diagnostics-ui-regression.xml`、`diagnostics-ui-regression.log`
- `tween-diagnostics-visual.log`、`diagnostics-runtime.json`、`diagnostics-runtime.png`、`diagnostics-geometry.txt`

临时 Editor 验收脚本已移除。诊断模块本身不自动写文件，面板默认关闭。

## 本次边界

本次没有重新构建或运行 Windows IL2CPP、WebGL 和小游戏平台包；上述新增能力的实际验收范围是 Unity Editor。生产构建的开发面板按条件编译关闭，诊断快照 API 保留。框架源码和宿主 API 改动随新宿主构建发布，不能只发热更 GameLogic DLL 来获得新的框架模块。
