# 通用 Tween

`GameEntry.RegisterDefaults()` 默认注册 `ITweenModule`。模块在框架更新循环内推进动画，无需安装第三方 Tween 库，也不需要新增场景组件。接口位于 `GameFramework/Runtime/Modules/Tween`。

## 能力

- 值类型：`float`、`Vector2`、`Vector3`、`Color`、`Quaternion`。
- 常用目标：Transform 世界/局部位置、局部旋转、缩放，RectTransform 锚点位置，CanvasGroup 透明度，UGUI Graphic 颜色，AudioSource 音量。
- 缓动：Linear、In/Out/InOut Quad、In/Out/InOut Cubic、SmoothStep。
- 组合：Delay、Sequence、Parallel、有限或无限 Loop，支持 Yoyo。
- 运行：暂停、恢复、取消、异步等待、缩放与非缩放时间，以及作用域和取消令牌绑定。

## 播放与生命周期

在窗口的 `Open(args)` 中：

```csharp
ITweenModule tweens = GameEntry.Get<ITweenModule>();
canvasGroup.alpha = 0f;
TweenHandle fade = tweens.Play(canvasGroup.FadeTo(1f, 0.2f, TweenEase.OutQuad),
    scope: OpenScope, unscaledTime: true);
```

`OpenScope.Stop()` 会立即取消本轮动画；窗口缓存后重新打开时使用新的作用域。普通玩法传入 `GameplayScope`，池对象必须传入**本次生成**的 `SpawnScope`。不要给池对象使用长期应用作用域，否则旧动画可能继续写入复用后的对象。

自然结束会解除作用域登记，已完成句柄不再持有目标或写入回调。框架关闭取消所有活动动画。绑定的 Unity 对象被销毁后，动画在下一次推进时结束；目标仍存在但被禁用不会自动取消，禁用即结束的组件可使用配置了 `DisposeOnDisable` 的 `LifetimeScopeBehaviour.Scope`。

## 串行、并行与循环

```csharp
var entrance = Tween.Sequence(
    Tween.Delay(0.1f),
    Tween.Parallel(
        panel.ScaleTo(Vector3.one, 0.25f, TweenEase.OutCubic),
        canvasGroup.FadeTo(1f, 0.2f)));

TweenHandle animation = tweens.Play(entrance, scope: OpenScope, unscaledTime: true);
TweenState result = await animation.WaitAsync();
if (result != TweenState.Completed) return;
```

循环次数表示**总轮数**；`-1` 表示无限。Yoyo 的第二轮反向播放，因此两轮代表一次往返：

```csharp
TweenClip pulse = Tween.To(1f, 1.1f, 0.5f,
    value => target.localScale = Vector3.one * value,
    TweenEase.SmoothStep, target);
TweenHandle pulsing = tweens.Play(Tween.Loop(pulse, count: -1, yoyo: true), scope: GameplayScope);
```

`Tween.Loop(Tween.Sequence(Tween.Delay(...), clip), ...)` 会每轮等待；只想在最开始延迟时，把 Delay 放在 Loop 外面的 Sequence 中。零时长有限片段可以立即推进到终值；零时长无限循环无意义，会被参数校验拒绝。

片段只是描述，**只有根片段 `Play` 一次**。它的子片段不会单独加入模块更新。相同描述可以多次 Play，每次返回独立的播放句柄。

## 起点、目标与控制

目标扩展在**创建片段时**读取起点，不在未来某段开始时重新读取。连续修改同一属性时，显式写出每段起止值更清楚：

```csharp
var path = Tween.Sequence(
    Tween.To(Vector3.zero, Vector3.right, 0.4f, value => target.localPosition = value, target: target),
    Tween.To(Vector3.right, Vector3.right * 2f, 0.4f, value => target.localPosition = value, target: target));
var movement = tweens.Play(path, scope: GameplayScope);
movement.Pause();
movement.Resume();
movement.Cancel();
```

`Tween.To` 的 `target` 用于检查 Unity 对象销毁；直接传入捕获组件的 setter 时应指定它。属性扩展已经自动绑定目标。取消保持最后一次写入值，不跳到终点、不恢复起点；对象池的初始状态仍由池的重置流程负责。多个同时写同一属性的 Tween 不会自动互斥，替换动画前取消旧句柄。

`Completed` 事件仅在自然结束时触发；`Stopped` 用于非正常完成。`WaitAsync()` 返回 `Completed`、`Canceled`、`TargetDestroyed` 或 `Faulted`，不把普通取消当作异常；setter 出错会结束动画并通过框架日志记录。控制与播放操作在 Unity 主线程进行；外部取消令牌可由后台发出，模块会在主线程收束。

默认使用缩放时间，玩法暂停或切后台时随玩法停止；UI 提示和暂停界面可以显式指定 `unscaledTime: true`，此类动画在 Unity 仍提供帧更新时继续推进。动画用于表现，Rigidbody 的物理运动仍应遵守物理更新流程。

## 诊断

[运行诊断](运行诊断.md)显示 `ITweenModule.ActiveCount`，其中包含暂停但尚未结束的动画。长时间停留在该计数中的对象，应检查无限循环、Pause 和所属作用域是否正确。

## 本次验收

编译、模块与 UI 回归及真实 Main 场景结果见[Tween 与运行诊断验收](Tween与运行诊断验收.md)。
