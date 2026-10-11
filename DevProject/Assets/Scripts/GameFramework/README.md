# GameFramework 源码

此目录是当前 Unity 工程中唯一的框架源码。`Runtime/` 放通用运行时，`Editor/` 放编辑器工具，`Tests/` 放框架测试；有效源码与对应 `.meta` 配套维护。ProjectExtraction 的热更玩法代码放在 `Assets/Scripts/GameLogic`，热更资源放在 `Assets/GameResources`，通用框架不得依赖项目玩法。

`package.json` 保留为后续导出 UPM 包的元数据。当前目录位于 `Assets` 下，**不是**已安装的 UPM 包；不要再通过 `Packages/manifest.json` 引用同一目录，否则 Unity 可能重复导入。正式打包时应先在独立消费工程验证依赖与安装流程。

本项目当前采用 [v2 开发文档](../../../../docs/project/v2/README.md)，工程操作见[仓库说明](../../../../README.md)。框架机制见[框架文档](../../../../docs/framework/README.md)；框架历史验收只覆盖原记录范围，不能替代 v2 验证。
