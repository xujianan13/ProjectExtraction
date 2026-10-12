using System;
using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.UI.Battle;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 真实触屏输入跨越单局保存积压及关键保存屏障的回归测试。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 保存预算满时保留同一触指和换向意图，排空后恢复移动，地图及暂停仍可操作并冻结世界。
        /// </summary>
        /// <returns>真实 HUD、延迟后端与战斗帧的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Joystick_SaveBackpressurePreservesPointerAndAllowsPausedPanels() => UniTask.ToCoroutine(async () =>
        {
            var backend = new HeldInputSaveBackend();
            try
            {
                await SetupAsync(backend);
                Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
                ProjectSession session = ProjectServices.Session;
                ExpeditionRun run = session.CurrentRun;
                Assert.That((await session.WaitForRunSavesAsync()).Success, Is.True);
                GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
                ExpeditionJoystick joystick = hud.RootTransform.GetComponentInChildren<ExpeditionJoystick>(false);
                Assert.That(joystick, Is.Not.Null);
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)joystick.transform;
                RectTransform handle = (RectTransform)rect.Find("Handle");
                Assert.That(handle, Is.Not.Null);
                float radius = Mathf.Min(rect.rect.width, rect.rect.height) * 0.5f;
                Assert.That(radius, Is.GreaterThan(0f));
                int budget = GameTables.CfgRunPersistence.GetById(1).MaxPendingTicks;
                PointerEventData pointer = Pointer(71, rect, Vector2.right * radius);
                ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                Vector2 direction = Vector2.right;
                for (int cycle = 0; cycle < 2; cycle++)
                {
                    Assert.That((await session.WaitForRunSavesAsync()).Success, Is.True);
                    backend.HoldWrites();
                    long startTick = run.World.TickIndex;
                    CombatActorSnapshot start = InputPersistencePlayer(run);
                    for (int tick = 0; tick < budget + 3; tick++) run.Advance(CombatWorld.TickSeconds);
                    Assert.That(backend.WaitingWrite, Is.True, "真实日志写入必须停在可控后端内。");
                    Assert.That(session.RunSaveBlocked, Is.True);
                    Assert.That(run.World.TickIndex, Is.EqualTo(startTick + budget), "积压只能停止在完整帧预算边界。");
                    CombatActorSnapshot full = InputPersistencePlayer(run);
                    Assert.That((full.Position.x - start.Position.x) * direction.x, Is.GreaterThan(0f));
                    Assert.That(joystick.enabled, Is.True, "普通帧积压不能禁用摇杆并丢失触指。");
                    Assert.That(handle.anchoredPosition.x * direction.x, Is.GreaterThan(0f));

                    direction = -direction;
                    pointer.position = ScreenPoint(rect, direction * radius);
                    ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, pointer, ExecuteEvents.dragHandler);
                    Assert.That(handle.anchoredPosition.x * direction.x, Is.GreaterThan(0f), "队满时同一触指仍应更新方向。");
                    run.Advance(1f);
                    Assert.That(run.World.TickIndex, Is.EqualTo(startTick + budget));
                    Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(full.Position));
                    backend.ReleaseWrites();
                    Assert.That((await session.WaitForRunSavesAsync().Timeout(TimeSpan.FromSeconds(10))).Success, Is.True);
                    run.Advance(CombatWorld.TickSeconds);
                    CombatActorSnapshot resumed = InputPersistencePlayer(run);
                    Assert.That(run.World.TickIndex, Is.EqualTo(startTick + budget + 1), "排空后不可补跑等待期间的积压时间。");
                    Assert.That((resumed.Position.x - full.Position.x) * direction.x, Is.GreaterThan(0f),
                        "没有第二次 PointerDown，原触指的最新方向必须驱动实际角色。");
                    Assert.That(resumed.Position.z, Is.EqualTo(full.Position.z).Within(0.0001f));
                }
                ExecuteEvents.Execute<IPointerUpHandler>(joystick.gameObject, pointer, ExecuteEvents.pointerUpHandler);
                CombatActorSnapshot released = InputPersistencePlayer(run);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(released.Position));
                Assert.That(handle.anchoredPosition, Is.EqualTo(Vector2.zero));

                Assert.That((await session.WaitForRunSavesAsync()).Success, Is.True);
                backend.HoldWrites();
                for (int tick = 0; tick < budget; tick++) run.Advance(CombatWorld.TickSeconds);
                Assert.That(session.RunSaveBlocked, Is.True);
                Button mapButton = hud.Root.GetComponentsInChildren<Button>(false).Single(value => value.name == "Minimap");
                Assert.That(mapButton.interactable, Is.True);
                mapButton.onClick.Invoke();
                GameWindow map = await WaitForWindowAsync(ProjectServices.ExpeditionMapWindow);
                Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Map));
                long mapTick = run.World.TickIndex;
                backend.ReleaseWrites();
                Assert.That((await session.WaitForRunSavesAsync().Timeout(TimeSpan.FromSeconds(10))).Success, Is.True);
                run.Advance(1f);
                Assert.That(run.World.TickIndex, Is.EqualTo(mapTick), "排空后地图面板仍须独立冻结世界。");
                ClickButton(map, "返回探索");
                await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);

                backend.HoldWrites();
                for (int tick = 0; tick < budget; tick++) run.Advance(CombatWorld.TickSeconds);
                Assert.That(session.RunSaveBlocked, Is.True);
                Assert.That(FindButton(hud, "暂停").interactable, Is.True);
                ClickButton(hud, "暂停");
                GameWindow pause = await WaitForWindowAsync(ProjectServices.PauseWindow);
                Assert.That(run.ActivePanel, Is.EqualTo(ExpeditionPanelKind.Pause));
                long pauseTick = run.World.TickIndex;
                backend.ReleaseWrites();
                Assert.That((await session.WaitForRunSavesAsync().Timeout(TimeSpan.FromSeconds(10))).Success, Is.True);
                run.Advance(1f);
                Assert.That(run.World.TickIndex, Is.EqualTo(pauseTick), "保存恢复后菜单暂停不得自动解除。");
                Assert.That(joystick.enabled, Is.False);
                ClickButton(pause, "继续游戏");
                await WaitForAsync(() => run.ActivePanel == ExpeditionPanelKind.None);
                CombatActorSnapshot afterPause = InputPersistencePlayer(run);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(afterPause.Position));
            }
            finally
            {
                backend.FailWrites = false;
                backend.ReleaseWrites();
            }
        });

        /// <summary>
        /// 真实拾取的关键保存及写入失败仍禁用摇杆并清触点，重试确认后旧拖动不得恢复移动。
        /// </summary>
        /// <returns>关键候选、明确写失败及恢复后的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Joystick_CriticalSaveAndWriteFailureReleaseOwnedPointer() => UniTask.ToCoroutine(async () =>
        {
            var backend = new HeldInputSaveBackend();
            try
            {
                await SetupAsync(backend);
                Assert.That(await ProjectServices.Coordinator.StartRunAsync(1).Timeout(TimeSpan.FromSeconds(20)), Is.True);
                ExpeditionRun run = ProjectServices.Session.CurrentRun;
                Assert.That((await ProjectServices.Session.WaitForRunSavesAsync()).Success, Is.True);
                GameWindow hud = await WaitForWindowAsync(ProjectServices.BattleHudWindow);
                ExpeditionJoystick joystick = hud.RootTransform.GetComponentInChildren<ExpeditionJoystick>(false);
                Assert.That(joystick, Is.Not.Null);
                Canvas.ForceUpdateCanvases();
                var rect = (RectTransform)joystick.transform;
                RectTransform handle = (RectTransform)rect.Find("Handle");
                Assert.That(handle, Is.Not.Null);
                float radius = Mathf.Min(rect.rect.width, rect.rect.height) * 0.5f;
                ExpeditionDrop drop = AddInteractionDrop(run, "input-save-barrier-loot", InputPersistencePlayer(run).Position);
                run.Advance(CombatWorld.TickSeconds);
                SelectInteractionLoot(run, drop.Item.Id);
                backend.HoldWrites();
                PointerEventData pointer = Pointer(72, rect, Vector2.right * radius);
                ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                Assert.That(handle.anchoredPosition.x, Is.GreaterThan(0f));
                run.RequestInteraction();
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(drop.Collected, Is.True, "必须经过真实拾取路径进入关键保存。");
                Assert.That(backend.WaitingWrite, Is.True);
                Assert.That(run.IsSavingRun, Is.True);
                Assert.That(joystick.enabled, Is.False);
                Assert.That(handle.anchoredPosition, Is.EqualTo(Vector2.zero));
                long frozenTick = run.World.TickIndex;
                CombatActorSnapshot frozen = InputPersistencePlayer(run);
                ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, pointer, ExecuteEvents.dragHandler);
                run.Advance(1f);
                Assert.That(run.World.TickIndex, Is.EqualTo(frozenTick));
                Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(frozen.Position));

                backend.FailWrites = true;
                LogAssert.ignoreFailingMessages = true;
                backend.ReleaseWrites();
                await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
                Assert.That(ProjectServices.Session.RunSaveMessage, Does.Contain("注入输入保存失败"));
                Assert.That(joystick.enabled, Is.False);
                Assert.That(handle.anchoredPosition, Is.EqualTo(Vector2.zero));
                run.Advance(1f);
                Assert.That(run.World.TickIndex, Is.EqualTo(frozenTick));
                backend.FailWrites = false;
                Assert.That(await ProjectServices.Coordinator.RetryAsync().Timeout(TimeSpan.FromSeconds(10)), Is.True);
                await WaitRunSavedAsync(run);
                Assert.That(run.Inventory.Count(value => value.Id == drop.Item.Id), Is.EqualTo(1));
                Assert.That(joystick.enabled, Is.True);
                ExecuteEvents.Execute<IDragHandler>(joystick.gameObject, pointer, ExecuteEvents.dragHandler);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(InputPersistencePlayer(run).Position, Is.EqualTo(frozen.Position), "关键屏障清除的触点不能被旧 Drag 复活。");
                Assert.That(handle.anchoredPosition, Is.EqualTo(Vector2.zero));
                ExecuteEvents.Execute<IPointerDownHandler>(joystick.gameObject, pointer, ExecuteEvents.pointerDownHandler);
                run.Advance(CombatWorld.TickSeconds);
                Assert.That(InputPersistencePlayer(run).Position.x, Is.GreaterThan(frozen.Position.x), "重试成功后新的按下必须恢复实际移动。");
                ExecuteEvents.Execute<IPointerUpHandler>(joystick.gameObject, pointer, ExecuteEvents.pointerUpHandler);
            }
            finally
            {
                backend.FailWrites = false;
                backend.ReleaseWrites();
            }
        });

        /// <summary>
        /// 从真实战斗世界读取玩家权威快照。
        /// </summary>
        /// <param name="run">当前关卡。</param>
        /// <returns>存在于战斗世界中的玩家快照。</returns>
        private static CombatActorSnapshot InputPersistencePlayer(ExpeditionRun run)
        {
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot player), Is.True);
            return player;
        }

        /// <summary>
        /// 延迟真实日志写入并可在放行时注入明确故障的测试后端。
        /// </summary>
        private sealed class HeldInputSaveBackend : ISaveBackend
        {
            /// <summary>
            /// 保存实际确认记录的内存后端。
            /// </summary>
            private readonly MemorySaveBackend inner = new();
            /// <summary>
            /// 当前一轮阻塞写入的放行信号。
            /// </summary>
            private UniTaskCompletionSource<bool> release;
            /// <summary>
            /// 是否在放行后返回明确写失败。
            /// </summary>
            public bool FailWrites;
            /// <summary>
            /// 是否已有物理写入等待放行。
            /// </summary>
            public bool WaitingWrite { get; private set; }
            /// <summary>
            /// 内存存储的稳定身份。
            /// </summary>
            public string StorageIdentity => inner.StorageIdentity;
            /// <summary>
            /// 单条记录的容量限制。
            /// </summary>
            public int MaxRecordBytes => inner.MaxRecordBytes;

            /// <summary>
            /// 建立下一轮写入延迟，已有等待必须先放行。
            /// </summary>
            public void HoldWrites()
            {
                Assert.That(release, Is.Null);
                release = new UniTaskCompletionSource<bool>();
            }

            /// <summary>
            /// 放行等待并恢复后续写入，清理阶段也可重复调用。
            /// </summary>
            public void ReleaseWrites()
            {
                UniTaskCompletionSource<bool> held = release;
                release = null;
                held?.TrySetResult(true);
            }

            /// <summary>
            /// 初始化实际内存存储。
            /// </summary>
            /// <returns>后端初始化结果。</returns>
            public UniTask<SaveResult> InitializeAsync() => inner.InitializeAsync();

            /// <summary>
            /// 读取已确认的实际记录。
            /// </summary>
            /// <param name="key">存储键。</param>
            /// <returns>记录读取结果。</returns>
            public UniTask<SaveReadResult> ReadAsync(string key) => inner.ReadAsync(key);

            /// <summary>
            /// 等待可控放行后写入完整记录，或返回明确的注入失败。
            /// </summary>
            /// <param name="key">存储键。</param>
            /// <param name="record">完整持久记录。</param>
            /// <returns>真实内存写入结果或注入失败。</returns>
            public async UniTask<SaveResult> WriteAsync(string key, string record)
            {
                UniTaskCompletionSource<bool> held = release;
                if (held != null)
                {
                    WaitingWrite = true;
                    await held.Task;
                    WaitingWrite = false;
                }
                return FailWrites ? SaveResult.Fail(SaveError.WriteFailed, "注入输入保存失败。") : await inner.WriteAsync(key, record);
            }
        }
    }
}
