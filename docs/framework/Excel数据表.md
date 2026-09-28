# Excel 数据表

Excel 源表放在仓库 `config/excel/`，通过 `tool/export-excel.mjs` 生成供 `GamePackage` 加载的 JSON 和 `GameLogic` 强类型代码。Unity 运行时读取 JSON 资源，不读取 `.xlsx`。源表不要放进 `DevProject/Assets/GameResources`。

## 目录与生成物

| 位置 | 用途 |
| --- | --- |
| `config/excel/**/*.xlsx` | 人工编辑的源表；可放在子目录。 |
| `config/excel/schema.json` | 可选的枚举与字段语义规则；无此文件时保持原有导表行为。 |
| `DevProject/Assets/GameResources/Config/Tables/Table_<工作表名>.json` | 导出的数据资源，随 `GamePackage` 发布并支持内容更新。 |
| `DevProject/Assets/Scripts/GameLogic/Runtime/Gen/<工作表名>.Gen.cs` | 可重复生成的行类型和表类型及 JSON 解析代码。 |
| `DevProject/Assets/Scripts/GameLogic/Runtime/Gen/GameTables.Gen.cs` | 全部表的加载入口；每次导出可更新。 |
| `DevProject/Assets/Scripts/GameLogic/Runtime/Ext/<工作表名>.Ext.cs`、`GameTables.Ext.cs` | 首次导出时创建的 `partial` 扩展文件；再次导出不覆盖。 |
| `tool/.excel-export-manifest.json` | 源表及生成物的校验清单，由工具维护。 |

每个 `.xlsx` 只读取**第一个工作表**，工作表名就是表名，并决定 `GameLogic.Tables.<表名>Row`、`<表名>Table` 等类型名。表名和字段名须为可用的 C# 标识符；重复的表名、字段名（忽略大小写）会报错。不要在 `Gen` 目录手工修改代码；行或表的自定义方法写入对应 `Ext` 文件中的 `partial` 类型。删除源表后再次导出，工具会移除对应的旧 JSON 和 `Gen` 文件，已有的 `Ext` 文件会保留，需自行检查是否还需要它们。

## 编写表格

第 1 行写字段名，**物理单元格 A1 必须是 `ID`**；第 2 行写字段类型；第 3 行起写数据。例如 Demo 模板中的 `config/excel/ExampleItems.xlsx` 工作表 `ExampleItems`（当前 Minimal 项目不包含该示例表）：

| ID | name | price | tags | weights |
| --- | --- | --- | --- | --- |
| string | string | number | string[] | number[] |
| item_001 | Potion | 2.5 | consumable,shop | 1,2.5 |

字段类型支持 `string`、`number`、`int`、`bool`、`enum:类型名` 及相应的 `[]` 数组；`enum:类型名` 须在 `schema.json` 中定义。生成的 C# `number` 是 `double`，`int` 是 32 位整数。`string` 单元格须为 Excel 文本，`number` 须为有限的 Excel 数字；数组以逗号分隔并写在文本单元格，`number[]` 也可在单个数字单元格填写一个元素。空 `string` 导出为空字符串，空数组导出为空数组，`number` 不允许为空。公式、富文本、错误值与合并单元格不能导出。

`ID` 的类型只能是 `string`、`number` 或 `int`，不能为空，同一张表内不得重复。数字 `ID` 必须是安全整数。A 列以 `#` 开头的行会跳过；第 1 行字段名以 `#` 开头的列会跳过。工作表名以 `#` 开头时，该工作簿的首个工作表会整体跳过。空数据行也会跳过。

## 可选的语义规则

在 `config/excel/schema.json` 中定义枚举和字段规则。表名、列名与表头精确匹配。例如：

```json
{
  "enums": {
    "Rarity": { "Common": 0, "Rare": 2 }
  },
  "tables": {
    "Items": {
      "fields": {
        "rarity": { "required": true },
        "price": { "min": 0, "max": 100 },
        "categoryId": { "ref": "Categories" },
        "icon": { "asset": true }
      }
    }
  }
}
```

`enum:Rarity` 单元格填写成员名（如 `Rare`），数组写成 `Common,Rare`；导出 JSON 使用枚举对应的整数值，并生成显式赋值的 C# 枚举。枚举名、成员名须为 C# 标识符；枚举名不得与生成代码使用的类型名冲突，例如 `Array`、`Dictionary`、`UniTask`、`JsonUtility`、`Serializable`。枚举值必须是互不重复的 32 位整数。布尔值单元格使用 Excel 布尔值，布尔数组使用 `true,false` 文本。空 `int` 默认为 0，空 `bool` 默认为 `false`；枚举空单元格仅在该枚举定义了值为 0 的成员时导出为 0，否则报错。需要强制填写时设置 `required`。

`required` 拒绝空单元格、空字符串或空数组；`min`/`max` 对数字及数字数组中的每个元素应用闭区间限制；`ref` 检查对应表的实际 ID 值及类型，也支持数组；`asset` 只用于字符串及字符串数组，检查 `GameResources` 中资源的 YooAsset 文件名地址（不含扩展名，大小写必须与资源文件一致）。`.meta`、生成的表 JSON 不作为可引用资源。资源地址重名按忽略大小写判断并报错，以避免跨平台冲突。规则引用不存在的表或列、非有限边界、类型不匹配都会阻止导出。

`verify` 会对照清单检查规则文件和被引用资源的哈希；修改规则或资源后须重新导出。原有项目可省略 `schema.json`，空 `config/excel` 仍生成零表的 `GameTables` 加载入口。

## 导出与校验

先安装 Node.js，在仓库根目录执行一次：

```powershell
npm ci --prefix tool
```

修改表格后导出；提交或构建前可单独校验：

```powershell
node tool/export-excel.mjs export
node tool/export-excel.mjs verify
```

也可在 Unity 菜单选择 **Tools → Unity Mini Game Framework → Export Excel Tables**。Unity 会刷新资产；等待资源导入和 C# 编译完成，再执行 **Prepare GamePackage**。该准备操作会运行导表校验，若源表与清单不一致、生成文件缺失或内容变化，会要求重新导出；Unity 正在刷新或编译时也会拒绝准备。命令行的 `verify` 只校验文件，不生成新文件。

生成的 `GameTables.LoadAsync` 在 `GameMain.StartAsync` 进入游戏场景前加载全部表，按 `Table_<工作表名>.json` 地址从 `GamePackage` 获取资源，并检查 JSON 的结构哈希。业务代码可通过 `GameTables.<表名>.GetAll()` 与 `GetById(id)` 读取数据。改动字段类型或名称时，需要同时发布新的 `GameLogic.dll` 与对应 JSON；导出后运行 **Prepare GamePackage**，再通过 **Build Offline GamePackage** 制作新的内置首包，或通过 **Build Remote GamePackage** 制作新的远端内容版本。具体版本和发布步骤见[热更启动流程](热更启动流程.md)。

## 本次验证（2026-09-25）

- `node tool/export-excel.mjs export` 与 `verify` 通过；重复工作表名和非法旧清单路径会拒绝导出，重新导出不会覆盖 `Ext` 文件。
- Unity 2022.3.62f2 编译通过；275 个 PlayMode 测试全部通过，启动测试直接核对了 `ExampleItems` 的 ID 查询、数字与数组字段。
- `Prepare GamePackage` 成功更新热更 DLL；临时远端 `GamePackage` 构建成功，清单同时包含 `GameLogic.dll` 与 `Table_ExampleItems`。
- Windows IL2CPP Player 构建和离线启动通过：运行日志显示从内置包加载 `GameLogic`，表加载完成后进入 3D 演示场景。此次表更新尚未在真实 CDN 验证。
