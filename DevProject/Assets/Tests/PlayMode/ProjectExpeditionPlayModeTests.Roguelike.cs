using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.UI.Battle;
using GameLogic.UI.Roguelike;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 检查真实击杀技能奖励、冻结保存和 Boss 多轮恢复。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 击杀阈值触发纯技能选择，关闭窗口不丢候选，持久选择完成后解除暂停。
        /// </summary>
        /// <returns>真实技能奖励和窗口生命周期的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator ProgressChoice_ReopensFrozenSkillsAndAppliesOnlyOnce() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("RogueProgressChoice");
            SetProgressThreshold(scope, 1);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            int itemCount = run.Inventory.Count;
            await EarnRogueChoiceAsync(run);
            RogueChoiceSnapshot offer = run.PendingRogueChoice;
            Assert.That(offer.Options.Count, Is.InRange(1, 3));
            Assert.That(offer.Options.All(value => value.Reward.Kind == RogueRewardKind.AttributeBonus ||
                value.Reward.Kind == RogueRewardKind.CombatAugment), Is.True);
            Assert.That(run.Inventory.Count, Is.EqualTo(itemCount));
            Assert.That(run.TryBeginRogueChoice("chest:forged"), Is.False);
            long pausedTick = run.World.TickIndex;
            run.Advance(10);
            Assert.That(run.World.TickIndex, Is.EqualTo(pausedTick));
            IUIModule ui = GameEntry.Get<IUIModule>();
            await UniTask.WaitUntil(() => ui.OpenedWindows.Any(value => value.Id == ProjectServices.RogueChoiceWindow))
                .Timeout(TimeSpan.FromSeconds(10));
            Assert.That(await ui.CloseWindowAsync(ProjectServices.RogueChoiceWindow), Is.True);
            Assert.That(run.PendingRogueChoice.OfferId, Is.EqualTo(offer.OfferId));
            ClickButton(ui.OpenedWindows.OfType<BattleHudWindow>().Single(), "继续选择强化");
            await UniTask.WaitUntil(() => ui.OpenedWindows.Any(value => value.Id == ProjectServices.RogueChoiceWindow))
                .Timeout(TimeSpan.FromSeconds(10));
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot before), Is.True);
            RogueRewardDefinition reward = offer.Options[0].Reward;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.False);
            await WaitRunSavedAsync(run);
            AssertRewardApplied(reward, before, run, itemCount);
            await ChooseAllSkillsAsync(run);
            Assert.That(run.IsChoosingReward, Is.False);
            run.Advance(CombatWorld.TickSeconds);
            Assert.That(run.World.TickIndex, Is.EqualTo(pausedTick + 1));
        });

        /// <summary>
        /// 选择提交失败保持同一冻结候选，重试仅兑现一次并保留随机游标。
        /// </summary>
        /// <returns>保存故障和统一重试的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator SkillSaveFailure_RetriesOriginalCandidateWithoutReroll() => UniTask.ToCoroutine(async () =>
        {
            var backend = new FaultSaveBackend();
            await SetupAsync(backend);
            using var scope = new LifetimeScope("RogueChoiceRetry");
            SetProgressThreshold(scope, 1);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            await EarnRogueChoiceAsync(run);
            RogueChoiceSnapshot offer = run.PendingRogueChoice;
            Assert.That(run.TryCaptureState(out var before, out _), Is.True);
            backend.FailWrites = true;
            LogAssert.ignoreFailingMessages = true;
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
            await WaitForPhaseAsync(ProjectRunPhase.RecoveryRequired);
            Assert.That(run.PendingRogueChoice, Is.Null, "保存未确认时不能展示新候选。");
            Assert.That(run.IsSavingRun, Is.True);
            Assert.That(run.TryCaptureState(out var failed, out _), Is.True);
            string frozen = JsonUtility.ToJson(failed);
            Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.False);
            backend.FailWrites = false;
            Assert.That(await ProjectServices.Coordinator.RetryAsync(), Is.True);
            await WaitRunSavedAsync(run);
            Assert.That(run.TryCaptureState(out var confirmed, out _), Is.True);
            Assert.That(confirmed.Progress.SelectedChoices, Is.EqualTo(before.Progress.SelectedChoices + 1));
            Assert.That(confirmed.Rogue.SelectedRewards.Sum(value => value.Rank), Is.EqualTo(failed.Rogue.SelectedRewards.Sum(value => value.Rank)));
            if (run.PendingRogueChoice == null) Assert.That(JsonUtility.ToJson(confirmed), Is.EqualTo(frozen));
            else
            {
                Assert.That(confirmed.Progress.PendingQualifications, Is.EqualTo(failed.Progress.PendingQualifications));
                Assert.That(confirmed.Rogue.OfferSerial, Is.EqualTo(failed.Rogue.OfferSerial + 1));
            }
        });

        /// <summary>
        /// Boss 首轮候选冷恢复保持完整物品身份，各轮领取一次后才开放区域出口。
        /// </summary>
        /// <returns>真实尾王、存档续局及多轮领取测试。</returns>
        [UnityTest]
        public IEnumerator BossChoices_ResumeFrozenRoundAndUnlockAfterAllRounds() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            string bossSlot = run.CurrentRegion.Plan.SpawnSlots.Single(value => value.Kind == RegionEnemyKind.Boss).SlotId;
            await DefeatSlotAsync(run, bossSlot);
            await ChooseAllSkillsAsync(run);
            await WaitRunSavedAsync(run);
            BossLootChoiceSnapshot first = run.PendingBossChoice;
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Options.Count, Is.EqualTo(3));
            string offer = first.OfferId;
            string values = JsonUtility.ToJson(new BossLootRoundState { Revision = first.Revision, Options = first.Options.ToList() });
            string runId = run.RunId;
            int stamina = ProjectServices.Session.Stamina;
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.CanResume, Is.True);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            Assert.That(run.RunId, Is.EqualTo(runId));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(stamina));
            Assert.That(run.PendingBossChoice.OfferId, Is.EqualTo(offer));
            Assert.That(JsonUtility.ToJson(new BossLootRoundState { Revision = run.PendingBossChoice.Revision,
                Options = run.PendingBossChoice.Options.ToList() }), Is.EqualTo(values));
            int initialItems = run.Inventory.Count;
            int rounds = run.PendingBossChoice.TotalRounds;
            for (int round = 1; round <= rounds; round++)
            {
                BossLootChoiceSnapshot choice = run.PendingBossChoice;
                Assert.That(choice.Round, Is.EqualTo(round));
                Assert.That(run.CurrentRegion.ExitEnabled, Is.False);
                string id = choice.Options[0].Id;
                Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, 0), Is.True);
                Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, 0), Is.False);
                await WaitRunSavedAsync(run);
                Assert.That(run.Inventory.Count(value => value.Id == id), Is.EqualTo(1));
            }
            Assert.That(run.Inventory.Count, Is.EqualTo(initialItems + rounds));
            Assert.That(run.CurrentRegion.BossRewardsCompleted && run.CurrentRegion.ExitEnabled, Is.True);
        });

        /// <summary>
        /// 将阈值修改限制在单项测试作用域，其他业务规则继续使用真实配置。
        /// </summary>
        /// <param name="scope">负责恢复配置的测试作用域。</param>
        /// <param name="threshold">本项测试的有限击杀阈值。</param>
        private static void SetProgressThreshold(LifetimeScope scope, int threshold)
        {
            CfgRogueProgressRow rule = ProjectV2RewardConfig.Progress(GameTables.CfgLevels.GetById(1).ProgressRuleID);
            int previous = rule.Threshold;
            scope.Defer(() => rule.Threshold = previous);
            rule.Threshold = threshold;
        }

        /// <summary>
        /// 击杀实际普通或精英生命直至资格进入持久选择。
        /// </summary>
        /// <param name="run">已经正式准入并激活的单局。</param>
        /// <returns>候选完成保存后的任务。</returns>
        private static async UniTask EarnRogueChoiceAsync(ExpeditionRun run)
        {
            for (int attempt = 0; attempt < run.CurrentRegion.Plan.SpawnSlots.Count && run.PendingRogueChoice == null; attempt++)
            {
                await WaitRunSavedAsync(run);
                RegionSpawnSlotPlan target = run.CurrentRegion.Plan.SpawnSlots.FirstOrDefault(value => value.Kind != RegionEnemyKind.Boss &&
                    run.CurrentRegion.Slots.Single(state => state.SlotId == value.SlotId).Phase == RegionSpawnPhase.Alive);
                Assert.That(target, Is.Not.Null, "本测试的阈值必须可由当前区域冻结生命达到。");
                await DefeatSlotAsync(run, target.SlotId, true);
            }
            await WaitRunSavedAsync(run);
            Assert.That(run.PendingRogueChoice, Is.Not.Null);
        }

        /// <summary>
        /// 用真实基础攻击击败一个已经生成的槽位，所有业务保存完成后再返回。
        /// </summary>
        /// <param name="run">正式激活的单局。</param>
        /// <param name="slotId">计划内稳定槽位身份。</param>
        /// <param name="preserveChoice">遇到技能选择时保留资格并停止攻击。</param>
        /// <returns>目标死亡并保存则为真；保留选择但目标仍存活则为假。</returns>
        private static async UniTask<bool> DefeatSlotAsync(ExpeditionRun run, string slotId, bool preserveChoice = false)
        {
            RegionSpawnSlotState slot = run.CurrentRegion.Slots.Single(value => value.SlotId == slotId);
            Assert.That(run.World.TryFindActor(slot.CurrentActorId, out CombatEntityRef enemy), Is.True);
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot player), Is.True);
            if (player.Attack < 100000) Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Attack, 100000), Is.True);
            if (player.Defense < 100000) Assert.That(run.World.TryAddAttributeBonus(run.Player, CombatAttributeKind.Defense, 100000), Is.True);
            for (int tick = 0; tick < 600 && slot.Phase == RegionSpawnPhase.Alive; tick++)
            {
                await WaitRunSavedAsync(run);
                if (slot.Phase != RegionSpawnPhase.Alive) break;
                if (run.PendingRogueChoice != null)
                {
                    if (preserveChoice) return false;
                    await ChooseAllSkillsAsync(run);
                    continue;
                }
                TryPlacePlayerNearActor(run, enemy);
                run.Advance(CombatWorld.TickSeconds);
            }
            await WaitRunSavedAsync(run);
            Assert.That(slot.Phase, Is.Not.EqualTo(RegionSpawnPhase.Alive));
            Assert.That(slot.LastProcessedLife, Is.EqualTo(slot.LifeSequence), "目标死亡收益必须已经确认。");
            return true;
        }

        /// <summary>
        /// 使用共享几何采样在目标的实际近战距离内尝试合法玩家站位。
        /// </summary>
        /// <param name="run">处于完整战斗边界的测试单局。</param>
        /// <param name="target">同地图存活目标。</param>
        /// <returns>权威碰撞入口接受某个候选站位时为真。</returns>
        private static bool TryPlacePlayerNearActor(ExpeditionRun run, CombatEntityRef target) =>
            CombatTestPositioning.TryPlaceAdjacent(run.World, run.Player, target);

        /// <summary>
        /// 逐项消费已经保存的技能资格，每次选择后等待保存及下一候选生成。
        /// </summary>
        /// <param name="run">持有技能队列的正式单局。</param>
        /// <returns>技能队列清空任务。</returns>
        private static async UniTask ChooseAllSkillsAsync(ExpeditionRun run)
        {
            for (int attempt = 0; attempt < 100; attempt++)
            {
                await WaitRunSavedAsync(run);
                RogueChoiceSnapshot offer = run.PendingRogueChoice;
                if (offer == null) return;
                Assert.That(run.TryChooseRogue(offer.OfferId, offer.Revision, 0), Is.True);
            }
            Assert.Fail("技能队列未在有限候选池边界内收束。");
        }

        /// <summary>
        /// 触发真实窗口中指定文字的按钮。
        /// </summary>
        /// <param name="window">已打开的窗口。</param>
        /// <param name="caption">按钮文字。</param>
        private static void ClickButton(GameWindow window, string caption)
        {
            Button button = window.RootTransform.GetComponentsInChildren<Button>(false).Single(item =>
                item.GetComponentInChildren<Text>().text == caption);
            Assert.That(button.interactable, Is.True);
            button.onClick.Invoke();
        }

        /// <summary>
        /// 确认技能只兑现一次战斗属性或词条，不向背包生成物品。
        /// </summary>
        /// <param name="reward">刚刚选择的奖励。</param>
        /// <param name="before">选择前的角色属性。</param>
        /// <param name="run">已兑现奖励的单局。</param>
        /// <param name="inventoryBefore">选择前背包数量。</param>
        private static void AssertRewardApplied(RogueRewardDefinition reward, CombatActorSnapshot before, ExpeditionRun run, int inventoryBefore)
        {
            Assert.That(run.World.TryGetActor(run.Player, out CombatActorSnapshot after), Is.True);
            Assert.That(run.Inventory.Count, Is.EqualTo(inventoryBefore));
            if (reward.Kind == RogueRewardKind.CombatAugment)
                Assert.That(run.RogueUpgrades.Single(item => item.Id == reward.Id).Rank, Is.EqualTo(reward.Rank));
            else Assert.That(AttributeValue(after, reward.Attribute), Is.GreaterThan(AttributeValue(before, reward.Attribute)));
        }

        /// <summary>
        /// 从公开快照读取指定局内属性。
        /// </summary>
        /// <param name="actor">角色快照。</param>
        /// <param name="attribute">奖励属性类型。</param>
        /// <returns>对应的最终战斗属性。</returns>
        private static float AttributeValue(CombatActorSnapshot actor, ProjectAttributeType attribute) => attribute switch
        {
            ProjectAttributeType.MaxHealth => actor.MaxHealth,
            ProjectAttributeType.Attack => actor.Attack,
            ProjectAttributeType.Defense => actor.Defense,
            ProjectAttributeType.CriticalRating => actor.CriticalRating,
            ProjectAttributeType.CriticalDamage => actor.CriticalDamage,
            ProjectAttributeType.DodgeChance => actor.DodgeChance,
            ProjectAttributeType.DamageIncrease => actor.DamageIncrease,
            ProjectAttributeType.DamageReduction => actor.DamageReduction,
            _ => float.NaN
        };

    }
}
