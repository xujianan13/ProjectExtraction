using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Advertising;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 两次死亡均通过真实按钮模拟广告复活，保留本局身份、物品和强化，明确结算后才应用死亡损失。
        /// </summary>
        /// <returns>真实死亡、模态窗口、复活保存及最终死亡结算的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Revival_DeathWindowAllowsRepeatedRevivesAndOnlyExplicitSettlementLosesItems() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            var random = new System.Random(932);
            ProjectItem starter = ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Weapon, 1, ProjectItemQuality.Common);
            ProjectItem loot = ProjectEquipmentRules.CreateEquipment(random, ProjectItemType.Armor, 1, ProjectItemQuality.Common);
            ProjectItem collection = ProjectEquipmentRules.CreateConfiguredItem(random, ProjectRewardConfig.Collection("gate_sigil"));
            starter.Equipped = true;
            ExpeditionRun run = await StartWithEquipmentFixturesAsync(starter, new[] { loot, collection });
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 7f), Is.True);
            Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.MaxHealth, 25f), Is.True);
            string runId = run.RunId;
            int stamina = ProjectServices.Session.Stamina;
            for (int death = 0; death < 2; death++)
            {
                TriggerDeath(run);
                await WaitForDeathChoiceAsync(run);
                Assert.That(run.TryCaptureState(out var before, out string error), Is.True, error);
                CombatPlayerCheckpoint dead = before.PlayerState;
                Vector3 deathPosition = InputPersistencePlayer(run).Position;
                GameWindow choice = await WaitForWindowAsync(ProjectServices.DeathChoiceWindow);
                Button[] actions = choice.RootTransform.GetComponentsInChildren<Button>(false);
                Assert.That(actions.Length, Is.EqualTo(4));
                Assert.That(actions.Select(button => button.GetComponentInChildren<Text>().text),
                    Is.EquivalentTo(new[] { "看广告复活", "暂离主城", "看广告携物撤离", "结算返回" }));
                Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
                Assert.That(ProjectServices.Session.LastResult, Is.Null);
                long tick = run.World.TickIndex;
                run.SetTouchMove(Vector2.right);
                run.Advance(10f);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
                Assert.That(run.CanAcceptPlayerInput, Is.False);
                ClickButton(choice, "看广告复活");
                await UniTask.WaitUntil(() => !run.IsAwaitingDeathChoice).Timeout(TimeSpan.FromSeconds(5));
                await WaitRunSavedAsync(run);
                await UniTask.WaitUntil(() => run.ActivePanel == ExpeditionPanelKind.None).Timeout(TimeSpan.FromSeconds(5));
                Assert.That(run.World.TryGetActor(run.Player, out var alive), Is.True);
                Assert.That(alive.Position, Is.EqualTo(deathPosition));
                Assert.That(alive.Health, Is.EqualTo(alive.MaxHealth));
                Assert.That(alive.Dead, Is.False);
                Assert.That(run.RunId, Is.EqualTo(runId));
                Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
                Assert.That(run.Inventory.Select(item => item.Id), Is.EquivalentTo(new[] { loot.Id, collection.Id }));
                Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.Empty, "复活保留持有收藏，但没有入库便不登记。");
                Assert.That(run.CanAcceptPlayerInput, Is.True);
                Assert.That(run.TryCaptureState(out var after, out error), Is.True, error);
                CombatPlayerCheckpoint revived = after.PlayerState;
                Assert.That(revived.PermanentBonuses, Is.EqualTo(dead.PermanentBonuses));
                Assert.That(JsonUtility.ToJson(new RevivalInventoryProbe { Items = after.Inventory }),
                    Is.EqualTo(JsonUtility.ToJson(new RevivalInventoryProbe { Items = before.Inventory })));
                Assert.That(JsonUtility.ToJson(after.Statistics), Is.EqualTo(JsonUtility.ToJson(before.Statistics)));
                Assert.That(revived.SkillCooldowns.Select(skill => (skill.SkillId, skill.RemainingTicks)),
                    Is.EqualTo(dead.SkillCooldowns.Select(skill => (skill.SkillId, skill.RemainingTicks))));
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            }
            TriggerDeath(run);
            await WaitForDeathChoiceAsync(run);
            ClickButton(await WaitForWindowAsync(ProjectServices.DeathChoiceWindow), "结算返回");
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            Assert.That(ProjectServices.Session.LastResult.Outcome.ExitKind, Is.EqualTo(ProjectRunExitKind.Death));
            Assert.That(ProjectServices.Session.LastResult.RetainedItems.Select(item => item.Id), Is.EquivalentTo(new[] { starter.Id }));
            Assert.That(ProjectServices.Session.LastResult.LostItems.Select(item => item.Id),
                Is.EquivalentTo(new[] { loot.Id, collection.Id }));
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.Empty);
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(ProjectServices.Session.Warehouse.Single().Id, Is.EqualTo(starter.Id));
        });

        /// <summary>
        /// 死亡检查点往返和暂离恢复仍停在死亡选择，不能通过重建房间免费复活。
        /// </summary>
        /// <returns>死亡事实编码、同局恢复和广告复活验证任务。</returns>
        [UnityTest]
        public IEnumerator Revival_PendingDeathCheckpointResumesWithoutFreeRevive() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectRunCoordinator coordinator = ProjectServices.Coordinator;
            Assert.That(await coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            TriggerDeath(run);
            await WaitForDeathChoiceAsync(run);
            Assert.That(run.TryCaptureState(out var checkpoint, out string error), Is.True, error);
            Assert.That(checkpoint.PendingPlayerDeathId, Is.GreaterThan(0));
            Assert.That(ExpeditionSnapshotCodec.TryEncode(checkpoint, out var parts, out error), Is.True, error);
            Assert.That(ExpeditionSnapshotCodec.TryDecode(parts, out var decoded, out error), Is.True, error);
            Assert.That(decoded.PendingPlayerDeathId, Is.EqualTo(checkpoint.PendingPlayerDeathId));
            string runId = run.RunId;
            Assert.That(await coordinator.SuspendRunAsync(run), Is.True);
            int stamina = ProjectServices.Session.Stamina;
            long cost = coordinator.RequiredResumeStamina;
            Assert.That(await coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            await WaitForDeathChoiceAsync(run);
            Assert.That(run.RunId, Is.EqualTo(runId));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina - cost));
            Assert.That(InputPersistencePlayer(run).Dead, Is.True);
            Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
            Assert.That(await run.RequestAdReviveAsync(), Is.True);
            await WaitRunSavedAsync(run);
            Assert.That(InputPersistencePlayer(run).Dead, Is.False);
        });

        /// <summary>
        /// 未完成、失败和后台取消后的晚完成均不复活，重复点击只发起一次广告。
        /// </summary>
        /// <returns>可控广告结果、暂停授权和重复请求验证任务。</returns>
        [UnityTest]
        public IEnumerator Revival_AdIncompleteFailureAndPausedLateCompletionDoNotReviveOrDuplicate() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            TriggerDeath(run);
            await WaitForDeathChoiceAsync(run);
            RewardedAdService original = ProjectServices.RewardedAds;
            var provider = new HeldRewardedAdProvider("death_revive");
            using var replacement = new RewardedAdService(provider);
            SetRevivalAdService(replacement);
            try
            {
                long tick = run.World.TickIndex;
                UniTask<bool> first = run.RequestAdReviveAsync().Preserve();
                Assert.That(run.IsReviveAdPending, Is.True);
                Assert.That(await run.RequestAdReviveAsync(), Is.False);
                Assert.That(run.TryConfirmDeath(), Is.False);
                Assert.That(provider.Calls, Is.EqualTo(1));
                run.Advance(10f);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
                provider.Complete(RewardedAdStatus.Cancelled);
                Assert.That(await first, Is.False);
                Assert.That(InputPersistencePlayer(run).Dead, Is.True);
                Assert.That(run.CanChooseDeath, Is.True);
                LogAssert.ignoreFailingMessages = true;
                UniTask<bool> failed = run.RequestAdReviveAsync().Preserve();
                provider.Complete(RewardedAdStatus.Failed);
                Assert.That(await failed, Is.False);
                Assert.That(run.IsAwaitingDeathChoice, Is.True);
                Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
                UniTask<bool> late = run.RequestAdReviveAsync().Preserve();
                await ProjectServices.Coordinator.SetApplicationPausedAsync(true);
                Assert.That(provider.Token.IsCancellationRequested, Is.True);
                provider.Complete(RewardedAdStatus.Completed);
                Assert.That(await late, Is.False);
                Assert.That(InputPersistencePlayer(run).Dead, Is.True);
                await ProjectServices.Coordinator.SetApplicationPausedAsync(false);
                Assert.That(run.CanChooseDeath, Is.True);
                UniTask<bool> accepted = run.RequestAdReviveAsync().Preserve();
                provider.Complete(RewardedAdStatus.Completed);
                Assert.That(await accepted, Is.True);
                await WaitRunSavedAsync(run);
                Assert.That(InputPersistencePlayer(run).Dead, Is.False);
                Assert.That(provider.Calls, Is.EqualTo(4));
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
            }
            finally { SetRevivalAdService(original); }
        });

        /// <summary>
        /// 单局释放后忽略广告提供者的晚完成，旧运行对象不能重新激活角色。
        /// </summary>
        /// <returns>释放授权与晚结果验证任务。</returns>
        [UnityTest]
        public IEnumerator Revival_DisposedRunIgnoresLateCompletedAd() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            TriggerDeath(run);
            await WaitForDeathChoiceAsync(run);
            RewardedAdService original = ProjectServices.RewardedAds;
            var provider = new HeldRewardedAdProvider("death_revive");
            using var replacement = new RewardedAdService(provider);
            SetRevivalAdService(replacement);
            try
            {
                UniTask<bool> request = run.RequestAdReviveAsync().Preserve();
                run.Dispose();
                Assert.That(provider.Token.IsCancellationRequested, Is.True);
                provider.Complete(RewardedAdStatus.Completed);
                Assert.That(await request, Is.False);
                Assert.That(run.IsRunning, Is.False);
                Assert.That(run.World.TryGetActor(run.Player, out _), Is.False);
                Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
            }
            finally { SetRevivalAdService(original); }
        });

        /// <summary>
        /// 复活候选写入失败后保持输入门禁，重试确认同一候选，不重看广告或重跑战斗。
        /// </summary>
        /// <returns>保存延迟、明确写失败和原候选重试验证任务。</returns>
        [UnityTest]
        public IEnumerator Revival_SaveFailureRetriesOriginalRevivedCandidateWithoutAdvancingOrAnotherAd() => UniTask.ToCoroutine(async () =>
        {
            var backend = new HeldInputSaveBackend();
            try
            {
                await SetupAsync(backend);
                Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
                ExpeditionRun run = ProjectServices.Session.CurrentRun;
                TriggerDeath(run);
                await WaitForDeathChoiceAsync(run);
                Assert.That(run.TryCaptureState(out var dead, out string error), Is.True, error);
                long tick = run.World.TickIndex;
                backend.HoldWrites();
                Assert.That(await run.RequestAdReviveAsync(), Is.True);
                Assert.That(run.IsSavingRun, Is.True);
                Assert.That(run.CanAcceptPlayerInput, Is.False);
                Assert.That(InputPersistencePlayer(run).Dead, Is.False);
                run.Advance(10f);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
                Assert.That(run.TryCaptureState(out var candidate, out error), Is.True, error);
                backend.FailWrites = true;
                LogAssert.ignoreFailingMessages = true;
                backend.ReleaseWrites();
                await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
                Assert.That(run.IsSavingRun, Is.True);
                Assert.That(await run.RequestAdReviveAsync(), Is.False);
                Assert.That(run.TryConfirmDeath(), Is.False);
                backend.FailWrites = false;
                Assert.That(await ProjectServices.Coordinator.RetryAsync(), Is.True);
                await WaitRunSavedAsync(run);
                Assert.That(run.CanAcceptPlayerInput, Is.True);
                Assert.That(run.TryCaptureState(out var confirmed, out error), Is.True, error);
                Assert.That(JsonUtility.ToJson(confirmed), Is.EqualTo(JsonUtility.ToJson(candidate)));
                Assert.That(confirmed.RunId, Is.EqualTo(dead.RunId));
                Assert.That(confirmed.PendingPlayerDeathId, Is.Zero);
                Assert.That(run.World.TickIndex, Is.EqualTo(tick));
                Assert.That(ProjectServices.Session.RecordedOutcome, Is.Null);
            }
            finally
            {
                backend.FailWrites = false;
                backend.ReleaseWrites();
            }
        });

        /// <summary>
        /// 等待死亡候选持久确认，确保所有测试仅在选择门禁开放后操作。
        /// </summary>
        /// <param name="run">已通过真实伤害进入死亡选择的单局。</param>
        /// <returns>候选保存与选择门禁等待任务。</returns>
        private static async UniTask WaitForDeathChoiceAsync(ExpeditionRun run)
        {
            Assert.That(run.IsAwaitingDeathChoice, Is.True);
            await WaitRunSavedAsync(run);
            await UniTask.WaitUntil(() => run.CanChooseDeath).Timeout(TimeSpan.FromSeconds(10));
        }

        /// <summary>
        /// 旧死亡结算回归测试显式选择结算，保留真实伤害和既有损失规则。
        /// </summary>
        /// <param name="run">待确认死亡的单局。</param>
        /// <returns>选择保存完成并提交死亡结算的任务。</returns>
        private static async UniTask ConfirmPendingDeathAsync(ExpeditionRun run)
        {
            await WaitForDeathChoiceAsync(run);
            Assert.That(run.TryConfirmDeath(), Is.True);
        }

        /// <summary>
        /// 临时替换项目广告服务，测试完成时由调用方恢复原服务。
        /// </summary>
        /// <param name="service">本轮测试服务或待恢复的原服务。</param>
        private static void SetRevivalAdService(RewardedAdService service) =>
            typeof(ProjectServices).GetProperty(nameof(ProjectServices.RewardedAds), BindingFlags.Static | BindingFlags.Public).SetValue(null, service);

        /// <summary>
        /// 包装完整物品列表，用 Unity 序列化比较复活前后的持有状态。
        /// </summary>
        [Serializable]
        private sealed class RevivalInventoryProbe
        {
            public System.Collections.Generic.List<ProjectItem> Items;
        }

        /// <summary>
        /// 按业务广告位验证请求，显式控制完成且故意忽略取消，验证业务层拒绝晚授权。
        /// </summary>
        private sealed class HeldRewardedAdProvider : IRewardedAdProvider
        {
            private readonly string expectedPlacement;
            private UniTaskCompletionSource<RewardedAdResult> pending;
            public int Calls { get; private set; }
            public CancellationToken Token { get; private set; }

            /// <summary>
            /// 指定本测试唯一允许请求的业务广告位。
            /// </summary>
            /// <param name="expectedPlacement">应当精确匹配的业务广告位。</param>
            public HeldRewardedAdProvider(string expectedPlacement) => this.expectedPlacement = expectedPlacement;

            /// <summary>
            /// 建立本次可控展示并保留取消令牌。
            /// </summary>
            /// <param name="placementId">当前请求的业务广告位。</param>
            /// <param name="ct">展示授权令牌。</param>
            /// <returns>由测试手动完成的展示结果。</returns>
            public UniTask<RewardedAdResult> ShowRewardedAsync(string placementId, CancellationToken ct = default)
            {
                Assert.That(placementId, Is.EqualTo(expectedPlacement));
                Calls++;
                Token = ct;
                pending = new UniTaskCompletionSource<RewardedAdResult>();
                return pending.Task;
            }

            /// <summary>
            /// 用指定状态结束当前广告，不主动响应取消令牌。
            /// </summary>
            /// <param name="status">本次展示结果状态。</param>
            public void Complete(RewardedAdStatus status) => pending.TrySetResult(new RewardedAdResult(status, "注入广告测试结果。"));
        }
    }
}
