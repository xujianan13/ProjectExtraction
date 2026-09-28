# 可编译 API 示例

示例源码：[ApiRecipes.cs](../../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)。这些方法位于测试程序集；使用时将相应方法接入自己的业务生命周期，并传入已初始化的模块与资源地址。

| 任务 | 示例方法或类型 |
| --- | --- |
| 注册并进入逻辑场景 | EnterSceneAsync |
| 启动和停止玩法 | RunGameplayAsync |
| 打开与关闭窗口 | ShowWindowAsync |
| 作用域持有 Sprite | LoadIconAsync |
| Prefab 加载、生成与回收 | SpawnOnceAsync |
| 输入上下文与动作订阅 | BindInput |
| 事件与计时器 | BindEventAndTimer |
| 作用域 Tween | Animate |
| 循环音频 | PlayLoopAsync |
| 加载项目 Excel 表 | LoadTablesAsync |
| 诊断快照 | Snapshot |
| 单局状态流程 | RunRoundFlowAsync、ApiRoundFactory、ApiRoundSession |
| 正式存档入口 | OpenSaveAsync |

进入与退出使用不同令牌；资源取得成功后及时确定所有者。打开窗口或加载内容的方法不应放在每帧 Update 中反复调用。取消通常以 OperationCanceledException 传播，具体返回值和失败规则以索引及模块指南为准。

测试程序集编译用于发现签名失效；`ScopeDisposalStopsEventDelivery` 测试用于验证事件订阅随作用域释放。其他方法需结合真实资源与游戏场景运行，不能把编译通过当作所有平台的行为验证。实际结果以本次验收记录为准。
固定尺寸列表通过 `ShowVirtualList` 配置；条目异步加载和回收约定见 [UI 虚拟列表](../../UI虚拟列表.md)。
