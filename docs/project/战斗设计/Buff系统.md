# Buff 系统

更新日期：2026-09-30。状态：战斗框架设计草案，尚未实现；下列类型、阶段与默认值是首版实现建议，具体 Buff 内容和数值仍由配置决定。

关联：[战斗框架设计](../战斗框架设计.md) · [技能时间轴与判定](技能时间轴与判定.md) · [飞弹系统](飞弹系统.md)。

## 1. 定位与边界

Buff 是 `M03` 的 `CombatWorld` 中附着在战斗 Actor 上的**局内临时状态**。它可以改变属性，也可以在添加、周期、层数变化、移除或自然到期时执行效果。首版默认按时间间隔触发周期效果，达到持续时长后销毁；特殊触发、永久状态和独立层计时以后扩展。

- `M03` 唯一持有 Buff 实例、战斗属性运行态、生命值和状态变更，并推进战斗时钟；场景 GameObject、表现组件和 UI 只消费结果。
- `M05` 提供不可变 `BuffDefinition`、效果及属性修正定义和纯计算；它不管理运行中实例、时钟、生命值或 ECS 结构变更。技能、武器附加效果、飞弹和命中只向 `CombatWorld` 提交加 Buff 意图。
- `M07` 的 `SkillProgressionState` 是局外技能成长。战斗技能用 `CombatSkillDefinition`/`CombatSkillId`；既有 `SkillDefinition`/`SkillId` 保留给局外成长定义。Buff 使用 `BuffDefinition`/`BuffId`，运行中另有唯一 `BuffInstanceId`。Buff 不产生物品实例，不进入仓库、安全箱或局外技能点存档。
- Buff 实例只属于当前 `RunId` 和对应 `CombatWorld`。跨图快照保存的是局内战斗状态，不能当作长期局外成长或可携出战利品。

## 2. ECS 数据与效果上下文

| 数据 | 首版建议字段与约束 |
| --- | --- |
| `BuffDefinition` | `BuffId`、配置版本、`DurationTicks`、`IntervalTicks`、`MaxStacks`、合并策略、效果列表、属性修正列表、驱散标签、来源死亡策略；定义不可变，实例持有适用版本。 |
| `BuffInstance` | `BuffInstanceId`、`TargetActorId`、`BuffId`、稳定来源身份、`AppliedTick`、`ExpireTick`、`NextTick`、`Stacks`、来源属性快照或其稳定引用、`ModifierHandle` 集合、已处理触发身份；为 ECS 中的权威运行态。 |
| `ModifierHandle` | 每个已注册属性修正的唯一句柄，绑定 `BuffInstanceId` 与目标属性容器。层数变化时更新该句柄的贡献；移除时按句柄撤销，不能靠重新计算一个相反值抵消。 |
| `BuffCommand` | 加入、驱散、指定移除等结构操作的命令及确定排序键；系统迭代 ECS 查询结果时先排队，提交阶段统一修改，避免重入改变当前遍历集合。 |
| `BuffEffectContext` | 至少携带 `RunId`、`RoomId`、`SourceEntity`、`InstigatorActorId`、`RootExecutionId`、`TriggerId`、`EffectInvocationId`、`ParentInvocationId`，另含目标、Buff 实例、触发类型、CombatTick 和层数快照。 |

`SourceEntity` 表示当次效果的直接触发实体：ApplyBuff 的入站上下文可以来自 Cast、飞弹或其他 Buff；本实例 OnApply/OnTick/OnRemove 的出站上下文以当前 Buff 实体为 SourceEntity。原施加实体另存 OriginSourceEntity 与参数快照，`InstigatorActorId` 始终是稳定责任主体。来源实体销毁、死亡或离图后仍保留实例中的 `InstigatorActorId` 和必要快照，用于剩余周期效果及归因；不能通过失效 `EntityRef` 读取可能已复用的实体或 GameObject。周期效果只能在持有者当前所在图寻找目标，不能跨图寻找新目标。玩家 Buff 跨图时更新运行 RoomId 和出站上下文，原始来源地图仅作归因记录，不拿旧图上下文在新图执行。每次效果调用分配新的 `EffectInvocationId`，同一因果链保留 `RootExecutionId`，子调用记录 `ParentInvocationId`；`TriggerId` 用于一次触发的幂等识别。实际 ID 生成、根链去重和预算遵循[战斗框架设计](../战斗框架设计.md)，Buff 不自创另一套无限递归处理。

属性修正可为固定值、百分比或其他由 M05 定义的纯计算项；实际组合顺序、快照取值还是动态取值必须在定义中明确。首版建议加 Buff 时取来源快照，`ResetDuration` 保留该快照；要重新取样须显式选择 `Replace` 或后续专门规则。Buff 实例变更只更新一次属性容器，再由 M03 读取有效值；表现事件不能反向写入权威属性。

### 属性与控制状态建议

属性修正按 `ModifierHandle` 归属 Buff 实例，不通过“结束时减去当时加上的数值”恢复。首版可用统一纯计算顺序 `clamp((Base + ΣFlat) × (1 + ΣAddPercent) × ΠMultiplier, Min, Max)`；覆盖值若有需求需单独配置优先级和稳定平局规则。定义规定每层是线性放大贡献还是只影响周期强度，层数变化更新原句柄，不再新增一份常驻修正。

眩晕、禁移动、禁施法、伤害免疫等状态以带来源句柄的标签集合表达，多个 Buff 同时提供标签时，移除一个不会释放其他来源的限制。时间轴的打断策略读取标签；生命扣减、控制判定和速度计算仍在 M03 的统一系统中执行。具体控制种类和免疫规则是内容配置，不默认所有控制都会打断所有技能。

移除时先锁存本次事件上下文与层数/来源快照，再使实例失活、撤销句柄并排队结束效果；允许的结束效果可读取锁存上下文，不能因实例已失活就全部被误判为过期回调。完全相同的 TriggerId 仍只执行一次；实体槽回收继续在安全末阶段进行。

## 3. 时钟、周期与到期

首版建议固定 **60 Hz `CombatTick`**；它是设计参数而非已实现工程事实。持续与间隔使用整数 tick，限时周期 Buff 要求 `DurationTicks > 0`、`IntervalTicks > 0`，并校验计算 `ExpireTick`、`NextTick` 不溢出。于 tick `T` 添加的实例，生命周期是半开区间 **`[T, T + DurationTicks)`**，初始 `ExpireTick = T + DurationTicks`、`NextTick = T + IntervalTicks`。

默认周期仅在 `T + k × IntervalTicks < T + DurationTicks`（`k >= 1`）时触发。到期 tick 先移除，因此恰好与 `ExpireTick` 相等的周期点不触发。例如 `T=100`、`DurationTicks=12`、`IntervalTicks=4` 时，仅在 104、108 触发；112 只到期。若 `DurationTicks <= IntervalTicks`，默认没有周期触发；`OnApply` 可以配置一次效果，但不能暗中算作一次 `OnTick`。配置需要加入时立即产生周期同类效果时，必须显式启用“立即触发”，使用独立触发身份去重，不改变后续 `NextTick`。

每个活跃 Buff 在每个 `CombatTick` 最多处理一次应到周期；处理后按配置间隔推进 `NextTick`。暂停或离图冻结时不靠墙钟补发积压周期。玩家 Buff 使用活跃单局战斗时间，在目标图恢复模拟后继续计时；非当前图敌方 Buff 跟随目标所在图的 `RoomClock` 冻结，回访从剩余 tick 继续。加载阶段双方暂停模拟。这与 M03 回访保留敌人状态的约定一致；切图快照需包含 `ExpireTick`/`NextTick` 相对所属时钟的剩余量、层数、来源归因与属性句柄可重建数据。

## 4. 效果事件与生命周期

| 事件 | 触发条件 | 首版执行约束 |
| --- | --- | --- |
| `OnApply` | 新实例成功建立一次 | 可配置即时效果；不会隐式执行 `OnTick`。合并到旧实例不再次调用。 |
| `OnTick` | 活跃实例在阶段 3 到达 `NextTick` 且尚未到期 | 使用当时层数快照和唯一触发身份；同 tick 不重复。 |
| `OnStackChanged` | 成功改变旧实例层数 | 传入旧层数和新层数；不是 `OnApply` 或 `OnTick` 的别名，达到上限而层数未变则不调用。 |
| `OnRemove`（或实现名 `OnRemoved`） | 显式移除、驱散、替换、目标死亡、来源死亡策略移除或自然到期 | 先登记原因，只执行定义中允许该原因触发的效果；属性句柄无论原因都必须撤销。 |
| `OnExpire` | 存活到 `ExpireTick` 自然到期 | 可配置额外效果，仅 `Reason=Expired` 触发；被替换、驱散、死亡或销毁均不触发。 |

`OnRemove` 与 `OnExpire` 若都配置，必须是两个显式效果，不能把清理动作当作伤害或奖励。自然到期在阶段 1 产生两者时按定义顺序执行一次，先使 Buff 失活并撤销属性句柄，再执行许可的结束效果，避免结束效果再次读到旧属性。`WorldDispose`、`MapTransfer` 的表现清理、切图快照卸载及纯表现销毁只释放或迁移所需资源，不触发 `OnExpire`，也不触发任何额外奖励或伤害；有效 Buff 状态须随目标进入正确的快照或新图运行态。每次事件的效果仍走统一根链队列、预算和循环校验。

## 5. 合并键与策略

默认合并键为 **`(TargetActorId, BuffId, SourceActorId)`**；此处 `SourceActorId` 取稳定的 `InstigatorActorId`。同一种 Buff 来自不同施加者时默认是不同实例，各自计时、层数和伤害归因。一次加 Buff 请求须明确配置版本、来源和目标，先校验目标仍有效与合并键，再执行以下一种策略；`MaxStacks >= 1`。

| 策略 | 已有同键实例时 | 层数/属性 | 时钟与事件 |
| --- | --- | --- | --- |
| `AddStack`（建议默认） | 沿用旧实例 | `Stacks = min(MaxStacks, old + AddCount)`；只有实际变化才更新 modifier 并发 `OnStackChanged` | 默认**不刷新**剩余时长，**不重置** `NextTick`；不发 `OnApply`。满层请求返回未变化。 |
| `RefreshDuration` | 沿用旧实例 | 保持旧层数与来源快照 | `ExpireTick = max(old ExpireTick, now + DurationTicks)`；`NextTick` 不变，不补发 `OnTick`。用于只延长、不缩短有效期。 |
| `ResetDuration` | 沿用旧实例 | 保持旧层数、属性句柄和来源快照 | `ExpireTick = now + DurationTicks`；默认保持 `NextTick`，防止反复刷新跳过应到周期；不发 `OnApply`。允许新配置时长比原剩余更短，使用前须由内容配置确认。 |
| `Replace` | 旧实例以 `Reason=Replaced` 移除，再建新 `BuffInstanceId` | 撤销旧 modifier 后按新定义创建，不允许短暂叠加两套属性 | 旧实例不发 `OnExpire`；新实例按 `now` 初始化完整时长及下一周期，发一次 `OnApply`。 |
| `Ignore` | 保持旧实例 | 不变 | 不刷新、不触发效果，返回已存在。 |

没有旧实例时，上述策略都按新建处理，初始层数夹到 `[1, MaxStacks]`。若需要“加层并重置时长”，应声明显式组合策略并固定顺序：先计算层数及 `OnStackChanged`，再调整 `ExpireTick`；不得把它偷偷并入默认 `AddStack`。独立层独立计时、永久 Buff、按触发次数耗尽、事件驱动周期和跨来源共享层数属于后续扩展，不作为首版隐含行为。

跨来源合并必须单独配置聚合键、每层来源记录以及周期伤害、击杀、经验与表现的归因策略；不得仅把新来源覆盖在旧实例上。首版建议拒绝需要跨来源归因的合并配置，继续按默认键创建独立实例。来源死亡也不改变已有实例的稳定归因。

## 6. 单 tick 阶段与重入

同一 `CombatTick` 的建议阶段固定为：

1. **到期与状态更新：** 处理 `ExpireTick <= now` 的实例，先失活、撤销 modifier，再派发允许的 `OnRemove`/`OnExpire` 效果；刷新有效战斗属性。阶段 1 结束后，已到期 Buff 不能被本 tick 后续刷新复活。
2. **已有运动与飞弹命中效果：** 处理已在运行的运动、飞弹/命中及其效果，可排队添加、替换或驱散 Buff。
3. **Buff 周期效果：** 对阶段开始时仍活跃、且 `NextTick <= now < ExpireTick` 的旧实例各执行最多一次 `OnTick`。本 tick 新增的 Buff 不在本阶段周期触发；阶段 2 刷新旧实例若恰好应到，保持 `NextTick` 后仍触发一次。
4. **输入与技能时间轴效果：** 处理新输入和技能时间轴推进产生的效果；这里新添 Buff 也要等后续 tick 才可能周期触发。
5. **死亡清图、结构销毁与表现：** 已成立伤害即时写生命值并标记 `DeadPending`，后续命中/目标选择不能把待死者视为活目标；Buff 结构移除和目标死亡清理在本阶段收束；按总纲的有界死亡工作表处理显式允许 TargetDeath/SourceDeath 的移除效果与新产生的死亡，继承根链和 tick 预算，全部收束后再发布唯一死亡、清图和表现事件。不能让 GameObject 销毁替代权威清理。

各阶段内由 `CombatWorld` 串行消费命令，稳定排序并在安全点提交结构变更。效果中的效果可以排入同一 `RootExecutionId` 的队列，但受主文档的根链深度/总调用预算与循环配置拒绝规则约束；不得让 `OnApply → OnRemove → OnApply` 等重入链无限执行，也不得通过换 `EffectInvocationId` 绕过去重。超预算时记录完整因果链并进入 CombatFault，停止继续推进战斗，交由 M01 错误边界处理；保留已提交状态用于诊断，不静默丢弃后续伤害、不擅自算死亡或撤离。

`RefreshDuration` 或 `ResetDuration` 与应到周期同 tick：发生在阶段 2 时，旧实例阶段 3 仍按原 `NextTick` 触发一次；发生在阶段 4 时，本 tick 阶段 3 已结束，不补发第二次，下一次按原节拍继续。发生在阶段 1 到期移除之后的请求只能创建新实例，不能复活旧 `BuffInstanceId`。同 tick 的驱散先于周期时不再 Tick，周期之后驱散则已成立的该次效果不会回滚；命令实际处理顺序及触发身份必须可回放。

## 7. 移除、死亡和跨图

- **驱散：** 请求指定实例或按标签筛选，按稳定 `BuffInstanceId` 顺序移除，`Reason=Dispelled`。只撤销 modifier 和执行显式允许的 `OnRemove`；不发 `OnExpire`。重复驱散幂等。不可驱散标签和筛选优先级由配置决定。
- **目标死亡：** 标记 `DeadPending` 后立即停止该目标后续周期/目标选择；阶段 5 以 `Reason=TargetDeath` 清除其 Buff、撤销全部句柄。死亡不会伪装成自然到期；允许的死亡移除效果先按总纲收束，再以唯一 DeathId 对外提供经验/掉落事实。
- **来源死亡：** 默认建议已施加 Buff 依其快照持续到期，保留原 `InstigatorActorId` 归因；确需随来源死亡消失时，定义中显式启用 `RemoveOnSourceDeath`，以 `Reason=SourceDeath` 移除。来源实体被卸载但战斗 Actor 尚存活，不等于来源死亡。若需移除的实例在冻结地图，先记待移除并禁止后续周期，地图恢复后在阶段 1 清理和执行许可的 SourceDeath 效果，不在冻结图扣血。
- **离图与返回：** 非当前图敌方 Buff 随该图时钟冻结，回访重建实例和 modifier 句柄而不重新派发 `OnApply`；玩家 Buff 随玩家状态跨图保存，并在活跃单局时间连续计时；同图敌方 Buff 随所属 `RoomClock` 冻结。加载时双方暂停。旧图已成立的伤害/死亡按 M03 与 M04 的切图顺序收束，卸载不触发 Buff 到期效果。玩家未完成的 Cast 或战斗位移只在成功换图后取消，失败时保留原状态；这项取消不清除 Buff 或冷却。
- **终局与世界销毁：** M01 冻结终局后不接受新战斗效果；销毁 `CombatWorld` 时释放句柄、队列和快照资源，不派发自然到期收益。Buff 不进入物品结算，也不改变 M07 已保存技能等级和技能点。

## 8. 示例与可操作验收

以下 tick 数仅用于验证时序，不表示正式游戏平衡数值。

1. **周期/到期：** `T=100,D=12,I=4`，104/108 各一次 `OnTick`，112 先到期且没有第三次 Tick。只有显式配置 `OnApply` 才在 100 产生加入效果。
2. **默认叠层：** 同键实例在 100 建立、104 应到周期；103 加一层后到期 tick 和 `NextTick` 不变，104 以新层数触发一次。达到 `MaxStacks` 后重复申请不发 `OnStackChanged`。
3. **同 tick 刷新：** 旧实例下一周期在 104，阶段 2 `ResetDuration` 仍在阶段 3 Tick 一次；阶段 4 再重置不产生第二次 Tick。到期阶段 1 已移除的实例不能刷新复活。
4. **替换/驱散：** `Replace` 先撤旧 modifier，再加新 modifier；旧实例不执行 `OnExpire`。驱散后属性恢复且无到期伤害；自然到期只执行明示配置的结束效果。
5. **跨来源：** 两个施加者向同一目标施加同一 `BuffId`，默认各有独立 `BuffInstanceId`、周期和归因；一人死亡不会把另一人的来源覆盖。
6. **死亡/离图：** 周期伤害使目标生命值归零后即标记 `DeadPending`，同 tick 后续目标选择忽略它；阶段 5 清除 Buff 且不发自然到期收益。非当前图敌方 Buff 的剩余时间不变，玩家活跃战斗时间持续推进；加载时均暂停。
7. **重入与幂等：** 重复的 `TriggerId` 不重复执行效果；循环触发配置在校验时拒绝，运行时根链预算阻止漏网链。重复驱散或世界 Dispose 不多次撤 modifier，也不发奖励。
8. **快照重建：** 回访恢复层数、剩余周期/持续时间、来源归因和有效属性；不重跑 `OnApply`，已到期或已死亡实例不复活。

配置导入时至少校验唯一 `BuffId`、合法效果引用与事件名、`DurationTicks > 0`、`IntervalTicks > 0`、`MaxStacks >= 1`、合法合并策略、修正句柄可撤销、tick 加法不溢出、来源归因完整、驱散标签有效，以及触发图是否存在零等待循环。未满足的定义拒绝进入运行时；不能用静默修正配置掩盖错误。永久/次数型 Buff 若将来加入，使用不同的显式计时模式与校验规则。

## 9. 待定配置与扩展

正式 tick 频率、Buff 列表与数值、属性修正组合规则、具体效果、驱散标签与免疫、来源死亡是否移除、同一事件多个 Buff 的稳定优先级和表现补偿，均需随战斗原型确定。首版按本文件默认语义实现限时周期 Buff；独立层计时、跨来源合并、永久/次数/事件触发 Buff 在有明确内容需求后再扩展。
