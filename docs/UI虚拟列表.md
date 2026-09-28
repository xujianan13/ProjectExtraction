# UGUI 虚拟列表

`GameVirtualList` 复用场景中已有的 `ScrollRect`，支持固定尺寸的纵向单列或横向单行数据。实例数量由视口大小和 `buffer` 决定，不随数据总量增长。

## 层级与配置

```text
ScrollRect + GameVirtualList
└── Viewport (RectTransform + Image + Mask/RectMask2D)
    └── Content (RectTransform，初始无子对象)
```

`ScrollRect.viewport` 和 `ScrollRect.content` 必须已引用对应对象。条目预制体根节点须为 `RectTransform + GameVirtualListItem`（通常派生业务类）。`Content` 不可带 `LayoutGroup` 或 `ContentSizeFitter`，因为虚拟列表直接控制尺寸和条目位置；`Viewport` 可由布局系统调整大小。列表轴尺寸固定，另一轴填满视口。列表自身可在 `ScrollRect` 同一对象，也可位于窗口其他对象上。

```csharp
// 在窗口初始化或打开时，使用已绑定的组件引用。
virtualList.Configure(scrollRect, itemPrefab, extent: 96f, gap: 8f, isHorizontal: false, buffer: 1);
virtualList.SetCount(rows.Count);
virtualList.ScrollTo(250);
virtualList.RefreshItem(250); // 该行可见时重绑
virtualList.Refresh();        // 全部可见行重绑
```

`SetCount` 缩减数据时会夹紧滚动偏移并卸载越界条目。关闭窗口若只是隐藏对象，列表禁用会释放全部绑定作用域；重新启用时会重建可见条目。最终销毁时释放全部实例和作用域。更新数据模型之后调用 `RefreshItem` 或 `Refresh`；`SetCount` 只更新数量和可见范围，不重绑索引未变化的可见行。若替换整个数据集合，请依次调用 `SetCount(rows.Count)` 和 `Refresh()`。

## 异步图标和复用

`Bind` 每次收到新的 `LifetimeScope`，必须用此作用域加载图标，并在写回前核对当前绑定。重绑、滚出视口、禁用及销毁会取消旧令牌并释放该作用域拥有的资源租约。以下示例中的 `rows`、`resources` 和 `icon` 是业务类已有字段：

```csharp
public override void Bind(int index, LifetimeScope scope)
{
    icon.sprite = null;
    LoadIconAsync(index, scope).Forget(error => GameEntry.Get<ILogModule>()?.Error("Inventory", error.ToString()));
}

private async UniTask LoadIconAsync(int index, LifetimeScope scope)
{
    var (cancelled, lease) = await scope.AcquireAsync<Sprite>(resources, rows[index].IconLocation).SuppressCancellationThrow();
    if (cancelled || lease?.Asset == null || !scope.IsActive || BindingScope != scope) return;
    icon.sprite = lease.Asset;
}

public override void Unbind() => icon.sprite = null;
```

`scope.AcquireAsync` 接管租约，并在加载迟到且作用域已结束时释放结果。不要把租约转交给其他条目或依赖旧 `index` 异步写回。业务数据集合应至少在列表绑定期间保持有效。调用其他异步 API 时，传入 `scope.Token` 作为取消令牌；即使对方忽略取消，完成后仍要检查 `scope.IsActive && BindingScope == scope` 再写入视图。
