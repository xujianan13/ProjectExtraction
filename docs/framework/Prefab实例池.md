# Prefab 实例池

## 使用入口

`GameEntry.RegisterDefaults()` 自动注册 `IPrefabPoolModule`。它依赖 `IResourceModule`，在资源模块之后初始化、之前关闭。普通 C# 对象继续使用 `IPoolModule`；Unity Prefab 实例使用本模块管理实例销毁和资源租约。

资源仍遵守项目边界：游戏 Prefab 放在 `Assets/GameResources`，使用已经激活的 YooAsset 包及资源地址加载。`Resources/Framework.prefab` 仍是不可热更的启动资产。

```csharp
IPrefabPoolModule pools = GameEntry.Get<IPrefabPoolModule>();
PrefabPool bullets = await pools.CreatePoolAsync("Bullet", new PrefabPoolOptions(prewarmCount: 8, maxInactive: 32), ct: sceneToken);
if (bullets == null) return;

PrefabInstance bullet = bullets.Spawn(muzzle.position, muzzle.rotation, parent: null);
if (bullet == null) return;
GameObject instance = bullet.GameObject;

// 命中、超时或离开玩法时归还本轮实例。
bullet.Dispose();

// 场景的异步退出入口：销毁池中全部实例并归还资源租约。
await bullets.DisposeAsync();
```

示例的 `Bullet` 需由游戏提供对应 Prefab 地址；本模块不绑定任何特定弹丸玩法。所有接口在 Unity 主线程调用。

## 所有权与加载

- 每次 `CreatePoolAsync` 返回独立池，由调用场景或玩法持有。相同地址的资源加载由 ResourceModule 合并，每个池持有独立租约；释放一个池不会破坏另一个池。
- `packageName` 为空时固定使用调用当时的默认包，也可显式指定其他已经就绪的包。池存续期间不能通过替换包内容来改变其 Prefab。
- 池加载完成后，`Spawn` 是同步方法，不会在生成路径发起资源 IO。调用方取消仅取消自己的创建；模块关闭会取消所有未完成创建。
- 取消和加载失败沿用资源模块的 `OperationCanceledException` / `ResourceLoadException` 契约。模块未启动、已关闭或缺少资源模块时返回空并通过现有日志接口记录可用的错误信息。
- `PrefabInstance` 只代表一次使用。归还、外部销毁或池关闭后 `IsValid` 为 false，`GameObject` / `Transform` 返回空。保留旧句柄不会误回收后续使用同一个 Unity 对象的新句柄。

## 生成、预热与回收

| API | 行为 |
| --- | --- |
| `Spawn(parent)` | 使用 Prefab 初始局部 Transform，在指定父节点下生成并激活。 |
| `Spawn(position, rotation, parent)` | 按世界位置、世界旋转生成，缩放仍使用 Prefab 初值。 |
| `Prewarm(count)` | 将闲置实例补足到目标数量，受 `MaxInactive` 限制；返回本次新增数量。 |
| `Despawn(handle)` / `handle.Dispose()` | 归还本轮实例；重复或跨池归还不生效。 |
| `ClearInactive()` | 销毁所有闲置实例，保留活跃实例和 Prefab 资源租约。 |
| `Dispose()` | 同步关闭池、使所有句柄失效并安排销毁所有实例。 |
| `DisposeAsync()` | 关闭池，并等待实例实际销毁和本池资源租约归还完成。 |

预热实例在不可见的持久节点下创建，不提前触发 `OnEnable`。生成时先重置并摆放 Transform，再激活对象。未指定父节点的活跃实例归属当前活动场景；回收后移入池的持久节点。`MaxInactive` 默认 32，0 表示不缓存，活跃数量不受其限制；超出闲置容量的归还对象会销毁。

`CountAll` / `CountActive` / `CountInactive` 提供存活实例统计。对象被外部 `Destroy` 或所属场景卸载后，不再计入可复用实例；推荐始终通过句柄归还，关闭池时仍需显式释放池所有权。

## 自动重置与游戏扩展

回收与重新生成负责恢复原有节点的 Transform、清理动态 Rigidbody 和 Rigidbody2D 速度以及粒子和拖尾。血量、目标引用、事件订阅、AI 状态、运行时新建节点和其他组件参数由游戏组件实现 `IPrefabPoolCallbacks` 处理。池不会克隆一套通用游戏状态系统。

```csharp
public sealed class PooledProjectile : MonoBehaviour, IPrefabPoolCallbacks
{
    private PrefabInstance instance;
    private int remainingHits;

    /// <summary>
    /// 重置本轮弹丸状态。
    /// </summary>
    /// <param name="handle">本轮生成句柄。</param>
    public void OnSpawned(PrefabInstance handle)
    {
        instance = handle;
        remainingHits = 1;
    }

    /// <summary>
    /// 释放本轮目标、订阅及其他玩法状态。
    /// </summary>
    public void OnDespawned()
    {
        instance = null;
        remainingHits = 0;
    }
}
```

回调覆盖原有子节点中的实现组件。`OnSpawned` 在姿态设置并激活后执行；`OnDespawned` 在停用前执行，此时旧句柄已失效。回调失败的对象会记录错误并丢弃，避免脏状态进入闲置队列。`OnEnable` 适合只依赖摆放结果的 Unity 行为，本轮句柄和自定义生成参数在 `OnSpawned` 中处理。

## 资源释放与场景退出

场景或玩法的正常异步退出使用 `await pool.DisposeAsync()`；同步强制退出使用 `pool.Dispose()`。池销毁包含借出后挂在其他父节点下的对象、脱离根层级的原始子节点，也包含之前因清空或容量超限而等待 Unity 销毁的对象。原始子节点仍归池所有；新增节点的业务所有权由游戏回调管理。正常释放完成前，Prefab 租约持续有效。

池默认按 `ResourceLoadOptions.Transient` 获取 Prefab。最后一份引用释放后，底层句柄按 ResourceModule 策略释放；若其他系统曾要求缓存同一资源，空闲缓存仍遵守资源模块预算，可由资源模块的清理接口回收。AB 卸载继续由 YooAsset 资源模块负责。

完整框架停机采用同步契约：Prefab 模块先停用全部实例、提交销毁并归还租约，随后 ResourceModule 统一关闭；实际 Unity 对象销毁在本帧末完成。需要在框架关闭前等待对象彻底销毁时，先主动等待各池 `DisposeAsync()`。

## 本轮验收（2026-09-26）

- Unity **2022.3.62f2** 编译通过；`tools/verify.ps1` 全量 PlayMode **329/329 通过，无跳过**。
- 新增 10 个实例池边界用例，使用真实 ResourceModule 和可控加载后端，覆盖预热与 OnEnable 姿态、Transform/物理/特效重置、独立句柄和资源租约、并发创建与取消、失败重试、容量淘汰、跨帧销毁、外部销毁、脱离层级的原始子节点、回调异常和框架关闭。
- 全量回归同时验证 Main 场景的 Loading → GamePackage → 游戏逻辑及 UI 启动；修正一个既有场景测试未观察预期异常所导致的跨用例干扰。
- `Prepare GamePackage` 执行成功，HybridCLR 的 `StandaloneWindows64` 程序集编译通过，已更新 `GameLogic.dll.bytes`；准备日志位于 `verification/prepare-gamepackage.log`。
- 日志使用被忽略的 `verification/compile.log`、`verification/playmode.log` 与 `verification/playmode-results.xml`，覆盖固定文件名；未在项目根目录新增日志。
- 本轮未重新构建 Player、AssetBundle 或进行真机验收。新增框架 API 需要随宿主 Player 更新，游戏 Prefab 内容继续通过 GamePackage 热更。
