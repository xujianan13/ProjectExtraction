# UGUI 游戏 UI 系统

## 入口与资源边界

`Framework.prefab` 启动默认模块时注册 `IUIModule`。业务通过 `GameEntry.Get<IUIModule>()` 使用 UI；窗口管理器是模块内部组合的能力。模块创建常驻的 UGUI Canvas、输入用 `EventSystem`、窗口 Canvas 和反馈层。`Loading` 场景仍使用自己的 Canvas，但与游戏 UI 共用框架的事件系统。

框架固定代码与启动资源位于 `Assets/Scripts/GameFramework` 和 `Assets/Resources`。游戏窗口 Prefab 放在 `Assets/GameResources/UI`，由 GamePackage 打包与热更新。窗口打开时，管理器通过 `IResourceModule.AcquireAsync<GameObject>` 持有 Prefab 租约；真正销毁窗口时释放租约。样例资产是 `Assets/GameResources/UI/UI_DemoWindow.prefab`，资源地址为 `UI_DemoWindow`。

## 业务窗口

窗口逻辑继承 `GameWindow`。在 `BindComponents()` 中使用 `FindRequired<T>("相对路径")` 取得 Prefab 组件，随后由 `Init()` 注册事件；`Show(args)` 每次打开或重新聚焦都会执行，`Close(reason)` 在关闭时执行，`Destroy()` 在实例销毁前执行。窗口根节点须带 `RectTransform`。完整例子见 `Assets/Scripts/GameLogic/Runtime/UI/DemoWindow.cs`。

内容包激活后，在热更程序入口注册配置及窗口类型：

```csharp
IUIModule ui = GameEntry.Get<IUIModule>();
var config = new GameWindowConfig(2001, "Inventory", typeof(InventoryWindow).FullName,
    "UI_Inventory", GameUILayers.Screen, GameWindowScope.Scene,
    GameWindowCacheMode.Timed, 10f, false, false, Color.clear);
ui.ConfigureWindows(new[] { config }, registry => registry.Register<InventoryWindow>(2001));
InventoryWindow window = await ui.OpenWindowAsync<InventoryWindow>(ct: ct);
await ui.CloseWindowAsync(window.Id, ct: ct);
```

`GameUILayers` 提供 `Hud`、`Screen`、`Popup` 和 `Guide` 标准层级。窗口配置还支持打开/关闭音效、关联窗口、关闭后的 `Immediate` / `Timed` / `Resident` 缓存策略。`GameWindowScope.Global` 跨逻辑场景保留；场景或会话退出时调用 `DestroyWindowScopeAsync(GameWindowScope.Scene)` 或 `DestroyWindowScopeAsync(GameWindowScope.Session)`，统一关闭窗口并释放 Prefab 租约。`CloseTopWindowAsync()` 和 `CloseAllWindowsAsync()` 可用于返回键及流程退出。

模态窗口配置 `useMask: true` 与黑色半透明 `maskColor`。遮罩会拦截窗口下方的 UGUI 指针事件；`closeOnOutsideClick: true` 时，点击未被当前窗口 UGUI 图形命中的区域会关闭顶层窗口。Prefab 的面板区域应由 `raycastTarget = true` 的 `Graphic` 覆盖，样例使用 `Panel` 上的 `Image`。多个模态窗口叠加时，遮罩随最高层窗口移动，关闭顶层后恢复下层遮罩。

## 通用反馈与游戏 UI 组件

`IUIModule` 直接提供三类常用反馈：

```csharp
ui.ShowToast("已保存");
bool confirmed = await ui.ConfirmAsync("退出", "确定退出当前游戏吗？", ct: ct);
await ui.AlertAsync("提示", "资源加载完成", ct: ct);
using (IUIBusyHandle busy = ui.BeginBusy("正在加载…"))
{
    // 执行异步工作；需要时调用 busy.SetMessage("正在进入场景…")。
}
```

Toast 按请求顺序展示，确认框和提示框串行展示并使用全屏黑色模态遮罩。等待层可由多个操作同时持有，最后一个句柄释放后才隐藏。默认反馈样式由框架代码创建，游戏窗口的视觉资源保持在 GamePackage 中。返回键 `Escape` 优先处理等待层和确认框，再关闭最上层业务窗口。

已有 `GameButton`、`GameText`、`GameImage`、`GameInputField`、`GameScrollView`、`GameSlider`、`GameTab`、`GameProgressBar` 等控件。`GameSafeAreaFitter` 可挂在铺满屏幕的 `RectTransform` 上适配设备安全区。`GameWorldUIFollower` 可将 3D 世界目标跟随到 Screen Space Overlay Canvas，并在目标位于屏幕后方或屏幕外时隐藏 UI、关闭射线拦截。

## 编辑器工作流与验收

使用 Unity 2022.3.62f2 打开 `DevProject`，执行 **Tools → Unity Mini Game Framework → Set Up Game UI**，可重建样例 Prefab 并清理旧 `Loading` 场景的独立事件系统。新增业务窗口时，将 Prefab 放入 `Assets/GameResources/UI`，保留 `.meta`，在热更程序中显式注册窗口配置与类型。执行 **Prepare GamePackage** 后再构建离线或远端 GamePackage；具体发布步骤见 [热更启动流程](热更启动流程.md)。

`tools/verify.ps1` 执行 Unity 编译与 PlayMode 测试。UI 测试覆盖控件、安全区、世界目标跟随、确认框和等待层的输入阻断、双层模态遮罩恢复；启动测试验证热更后的样例 Prefab 实际打开。
