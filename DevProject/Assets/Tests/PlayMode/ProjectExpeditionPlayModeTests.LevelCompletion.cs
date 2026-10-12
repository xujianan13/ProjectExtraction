using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 整关只扣一次体力，最终三轮领奖续局保持候选，通关入库及收藏成就只登记一次。
        /// </summary>
        /// <returns>真实全关卡、领奖冷恢复、永久收藏及再次出战的验证任务。</returns>
        [UnityTest]
        public IEnumerator LevelCompletion_FinalThreeRoundsResumeAndCollectionsSettleOnce() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            ProjectCharacterStats baseline = ProjectServices.Session.CharacterStats;
            int beforeEntry = ProjectServices.Session.Stamina;
            int entryCost = PrototypeRules.EntryCost;
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.Empty);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            string runId = run.RunId;
            int admittedStamina = beforeEntry - entryCost;
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(admittedStamina));
            Assert.That(run.RegionCount, Is.EqualTo(3));
            for (int region = 0; region < 3; region++)
            {
                Assert.That(run.CurrentRegionIndex, Is.EqualTo(region));
                Assert.That(run.IsFinalRegion, Is.False);
                await CompleteRegionBossAsync(run);
                await EnterExtractionConfirmationAsync(run);
                await ClickExtractionConfirmationAsync("继续下一区域");
                await WaitRunSavedAsync(run);
                Assert.That(run.CurrentRegionIndex, Is.EqualTo(region + 1));
                Assert.That(run.RunId, Is.EqualTo(runId));
                Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(admittedStamina));
            }

            Assert.That(run.IsFinalRegion, Is.True);
            string bossSlot = run.CurrentRegion.Plan.SpawnSlots.Single(value => value.Kind == RegionEnemyKind.Boss).SlotId;
            Assert.That(await DefeatSlotAsync(run, bossSlot), Is.True);
            await ChooseAllSkillsAsync(run);
            await WaitRunSavedAsync(run);
            BossLootChoiceSnapshot first = run.PendingBossChoice;
            Assert.That(first, Is.Not.Null);
            Assert.That(first.Round, Is.EqualTo(1));
            Assert.That(first.TotalRounds, Is.EqualTo(3));
            int firstIndex = CollectionChoiceIndex(first);
            ProjectItem firstCollection = first.Options[firstIndex];
            Assert.That(firstCollection.Type, Is.EqualTo(ProjectItemType.Collection));
            var selectedFinalItems = new List<ProjectItem> { firstCollection.Copy() };
            Assert.That(run.TryChooseBoss(first.OfferId, first.Revision, firstIndex), Is.True);
            Assert.That(run.TryChooseBoss(first.OfferId, first.Revision, firstIndex), Is.False);
            await WaitRunSavedAsync(run);
            Assert.That(run.Inventory.Count(value => value.Id == firstCollection.Id), Is.EqualTo(1));
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.Empty, "未入库的单局收藏不能提前登记。");
            BossLootChoiceSnapshot second = run.PendingBossChoice;
            Assert.That(second.Round, Is.EqualTo(2));
            string frozenRound = FrozenBossRoundJson(second);
            string frozenOffer = second.OfferId;
            string frozenBatch = second.BatchId;
            int frozenTotal = second.TotalRounds;
            Assert.That(run.TryCaptureState(out var saved, out string error), Is.True, error);
            string frozenBossState = JsonUtility.ToJson(saved.BossLoot);

            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.CanResume, Is.True);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            Assert.That(run.RunId, Is.EqualTo(runId));
            Assert.That(run.CurrentRegionIndex, Is.EqualTo(3));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(admittedStamina));
            BossLootChoiceSnapshot restored = run.PendingBossChoice;
            Assert.That(restored.OfferId, Is.EqualTo(frozenOffer));
            Assert.That(restored.BatchId, Is.EqualTo(frozenBatch));
            Assert.That(restored.Round, Is.EqualTo(2));
            Assert.That(restored.TotalRounds, Is.EqualTo(frozenTotal));
            Assert.That(FrozenBossRoundJson(restored), Is.EqualTo(frozenRound));
            Assert.That(run.TryCaptureState(out var resumed, out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(resumed.BossLoot), Is.EqualTo(frozenBossState));
            Assert.That(run.Inventory.Count(value => value.Id == firstCollection.Id), Is.EqualTo(1));

            string[] expectedRetained = Array.Empty<string>();
            for (int round = 2; round <= 3; round++)
            {
                BossLootChoiceSnapshot choice = run.PendingBossChoice;
                Assert.That(choice.Round, Is.EqualTo(round));
                Assert.That(choice.TotalRounds, Is.EqualTo(3));
                Assert.That(choice.Options.Count, Is.EqualTo(3));
                Assert.That(choice.Options.Any(value => value.Type == ProjectItemType.Collection), Is.True);
                Assert.That(choice.Options.Any(value => value.Type is ProjectItemType.Weapon or ProjectItemType.Armor), Is.True);
                Assert.That(run.CurrentRegion.ExitEnabled, Is.False);
                int index = CollectionChoiceIndex(choice);
                ProjectItem selected = choice.Options[index].Copy();
                selectedFinalItems.Add(selected);
                if (round == 3) expectedRetained = run.Inventory.Select(value => value.Id).Append(selected.Id).ToArray();
                Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, index), Is.True);
                Assert.That(run.TryChooseBoss(choice.OfferId, choice.Revision, index), Is.False);
                if (round < 3) await WaitRunSavedAsync(run);
            }

            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectSession settled = ProjectServices.Session;
            ProjectRunResult result = settled.LastResult;
            Assert.That(settled.CurrentRun, Is.Null);
            Assert.That(result.Outcome.RunId, Is.EqualTo(runId));
            Assert.That(result.Outcome.Extracted, Is.True);
            Assert.That(result.RetainedItems.Select(value => value.Id), Is.EquivalentTo(expectedRetained));
            Assert.That(result.LostItems, Is.Empty);
            Assert.That(result.RetainedItems.Select(value => value.Id).Distinct().Count(), Is.EqualTo(result.RetainedItems.Count));
            Assert.That(selectedFinalItems.All(item => result.RetainedItems.Count(value => value.Id == item.Id) == 1), Is.True);
            string[] collectionIds = result.RetainedItems.Where(value => value.Type == ProjectItemType.Collection)
                .Select(value => value.CollectionId).Distinct().ToArray();
            Assert.That(collectionIds.Length, Is.GreaterThanOrEqualTo(1));
            Assert.That(settled.CollectedCollectionIds, Is.EquivalentTo(collectionIds));
            var achievementDefinitions = ProjectV2RewardConfig.CollectionAchievements(out error);
            Assert.That(error, Is.Null);
            var earned = achievementDefinitions.Where(value => value.RequiredCollectionIds.All(collectionIds.Contains)).ToArray();
            Assert.That(settled.AchievedCollectionIds, Is.EquivalentTo(earned.Select(value => value.Id)));
            Assert.That(settled.AchievedCollectionIds.Distinct().Count(), Is.EqualTo(settled.AchievedCollectionIds.Count));
            Assert.That(settled.CollectionBonusSources.Select(value => value.AchievementId), Is.EquivalentTo(earned.Select(value => value.Id)));
            AssertCollectionAttributeDelta(baseline, settled.CharacterStats, earned);
            string settledStats = JsonUtility.ToJson(settled.CharacterStats);
            string settledResult = JsonUtility.ToJson(result);
            string[] warehouseIds = settled.Warehouse.Select(value => value.Id).ToArray();
            Assert.That(settled.RunHistory.Count(value => value.RunId == runId), Is.EqualTo(1));
            Assert.That(settled.Stamina, Is.EqualTo(admittedStamina));

            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.Phase, Is.EqualTo(ProjectRunPhase.ResultsReady));
            Assert.That(JsonUtility.ToJson(ProjectServices.Session.LastResult), Is.EqualTo(settledResult));
            Assert.That(ProjectServices.Session.Warehouse.Select(value => value.Id), Is.EquivalentTo(warehouseIds));
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.EquivalentTo(collectionIds));
            Assert.That(ProjectServices.Session.AchievedCollectionIds, Is.EquivalentTo(earned.Select(value => value.Id)));
            Assert.That(JsonUtility.ToJson(ProjectServices.Session.CharacterStats), Is.EqualTo(settledStats));
            Assert.That(ProjectServices.Session.RunHistory.Count(value => value.RunId == runId), Is.EqualTo(1));
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.True);
            Assert.That(await ProjectServices.Coordinator.ReturnHomeAsync(), Is.False);
            Assert.That(JsonUtility.ToJson(ProjectServices.Session.CharacterStats), Is.EqualTo(settledStats));
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            Assert.That(ProjectServices.Session.CurrentRun.RunId, Is.Not.EqualTo(runId));
            Assert.That(ProjectServices.Session.Stamina, Is.EqualTo(admittedStamina - entryCost));
            Assert.That(ProjectServices.Session.CollectedCollectionIds, Is.EquivalentTo(collectionIds));
            Assert.That(ProjectServices.Session.AchievedCollectionIds, Is.EquivalentTo(earned.Select(value => value.Id)));
            Assert.That(ProjectServices.Session.CollectionBonusSources.Select(value => value.AchievementId),
                Is.EquivalentTo(earned.Select(value => value.Id)));
        });

        /// <summary>
        /// 优先选择实际候选中的收藏，保留无收藏时的普通候选索引。
        /// </summary>
        /// <param name="choice">已经持久化的完整 Boss 候选。</param>
        /// <returns>实际存在的候选索引。</returns>
        private static int CollectionChoiceIndex(BossLootChoiceSnapshot choice)
        {
            for (int index = 0; index < choice.Options.Count; index++)
                if (choice.Options[index].Type == ProjectItemType.Collection) return index;
            return 0;
        }

        /// <summary>
        /// 序列化候选顺序、修订号与完整物品属性，供冷恢复逐字段比对。
        /// </summary>
        /// <param name="choice">已经持久化的轮次候选。</param>
        /// <returns>完整候选的稳定 JSON。</returns>
        private static string FrozenBossRoundJson(BossLootChoiceSnapshot choice) =>
            JsonUtility.ToJson(new BossLootRoundState { Revision = choice.Revision, Options = choice.Options.ToList() });

        /// <summary>
        /// 永久属性增量等于实际完成成就的配置奖励，每项来源只计一次。
        /// </summary>
        /// <param name="before">首次准入前的局外属性。</param>
        /// <param name="after">成功通关结算后的局外属性。</param>
        /// <param name="earned">按实际入库收藏满足条件的成就定义。</param>
        private static void AssertCollectionAttributeDelta(ProjectCharacterStats before, ProjectCharacterStats after,
            IReadOnlyList<ProjectCollectionAchievementDefinition> earned)
        {
            float[] initial = { before.MaxHealth, before.Attack, before.Defense, before.CriticalRating, before.CriticalDamage,
                before.DodgeChance, before.DamageIncrease, before.DamageReduction };
            float[] actual = { after.MaxHealth, after.Attack, after.Defense, after.CriticalRating, after.CriticalDamage,
                after.DodgeChance, after.DamageIncrease, after.DamageReduction };
            foreach (ProjectAttributeType attribute in Enum.GetValues(typeof(ProjectAttributeType)))
            {
                float bonus = earned.SelectMany(value => value.AttributeRewards).Where(value => value.Attribute == attribute).Sum(value => value.Value);
                Assert.That(actual[(int)attribute], Is.EqualTo(initial[(int)attribute] + bonus).Within(0.001f), attribute.ToString());
            }
            Assert.That(after.AttackRange, Is.EqualTo(before.AttackRange));
            Assert.That(after.MoveSpeed, Is.EqualTo(before.MoveSpeed));
        }
    }
}
