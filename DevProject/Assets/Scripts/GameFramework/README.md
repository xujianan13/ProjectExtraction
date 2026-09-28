# GameFramework 源码

此目录是当前 Unity 工程中唯一的框架源码。`Runtime/` 放通用运行时，`Editor/` 放编辑器工具，`Tests/` 放迁入测试；相应 `.meta` 与原 ProjectTavern 代码一起保留。游戏玩法与内容放在 `Assets/Scripts/MiniGame` 等包外目录。

`package.json` 保留为后续导出 UPM 包的元数据。当前目录位于 `Assets` 下，**不是**已安装的 UPM 包；不要再通过 `Packages/manifest.json` 引用同一目录，否则 Unity 可能重复导入。正式打包时应先在独立消费工程验证依赖与安装流程。

仓库根目录的 `README.md`、`docs/Unity小游戏框架设计方案.md` 和 `docs/首版验收记录.md` 记录工程入口、设计与实际验证结果。