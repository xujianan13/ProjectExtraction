using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.UI.Run;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证单局统计来自真实战斗与探索，并随终局保存到结算界面。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 尾王死亡统计由真实事件产生，领奖和读取快照不会重复累计击杀。
        /// </summary>
        /// <returns>战斗事件和统计隔离验证任务。</returns>
        [UnityTest]
        public IEnumerator Statistics_BossEventsAndRewardsCountExactlyOnce() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            var events = new List<CombatEvent>();
            run.CombatEventPublished += events.Add;
            await CompleteRegionBossAsync(run);
            ProjectRunStatistics snapshot = run.CaptureStatistics();
            Assert.That(snapshot.HasData, Is.True);
            Assert.That(snapshot.TotalRegions, Is.EqualTo(run.RegionCount + 1));
            Assert.That(snapshot.VisitedRegions, Is.EqualTo(1));
            Assert.That(snapshot.RegionBossKills, Is.EqualTo(1));
            Assert.That(snapshot.FinalBossKills, Is.Zero);
            Assert.That(snapshot.CompletedRegions, Is.EqualTo(1));
            int rounds = ProjectV2RewardConfig.BossReward(GameTables.CfgBosses.GetById(run.CurrentRegion.Plan.Request.BossDefinitionId).RewardID).Rounds;
            Assert.That(snapshot.BossLootRounds, Is.EqualTo(rounds));
            Assert.That(snapshot.DamageDealt, Is.EqualTo(events.Where(fact => fact.Kind == CombatEventKind.Damage &&
                fact.ActorId == 1 && fact.TargetActorId != 1).Sum(fact => (double)fact.Value)).Within(0.001));
            int kills = snapshot.EnemyKills;
            snapshot.EnemyKills = 999;
            Assert.That(run.CaptureStatistics().EnemyKills, Is.EqualTo(kills));
            Assert.That(run.PendingBossChoice, Is.Null);
            Assert.That(run.CurrentRegion.ExitEnabled, Is.True);
        });
        /// <summary>
        /// 首次和复活收益取自真实死亡确认，冷恢复保留分类、已选择次数与有效停留时钟。
        /// </summary>
        /// <returns>真实战斗、短计时测试夹具与冷恢复验证任务。</returns>
        [UnityTest]
        public IEnumerator Statistics_FirstAndRespawnGainsSurviveColdRestore() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var stationarySkills = new LifetimeScope("StatisticsStationaryEnemies");
            SetStatisticsEnemySkills(stationarySkills);
            Assert.That(CombatEnemySkillConfig.Validate(out string fixtureError), Is.True, fixtureError);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            FreezeStatisticsFixtureEnemyMovement(run, true);
            foreach (RegionEnemyKind kind in new[] { RegionEnemyKind.Normal, RegionEnemyKind.Elite })
            {
                RegionRespawnRule rule = kind == RegionEnemyKind.Normal ? run.CurrentRegion.Plan.NormalRespawn : run.CurrentRegion.Plan.EliteRespawn;
                rule.DelayTicks = 120;
                rule.WarningTicks = 2;
                RegionSpawnSlotPlan plan = run.CurrentRegion.Plan.SpawnSlots.Where(value => value.Kind == kind)
                    .OrderByDescending(value => (value.HomePosition - run.CurrentRegion.Plan.EntryPosition).sqrMagnitude).First();
                RegionSpawnSlotState slot = run.CurrentRegion.Slots.Single(value => value.SlotId == plan.SlotId);
                Assert.That(await DefeatSlotAsync(run, plan.SlotId), Is.True);
                await ChooseAllSkillsAsync(run);
                int firstActor = slot.CurrentActorId;
                Assert.That(run.World.TrySetPosition(run.Player, run.CurrentRegion.Plan.EntryPosition), Is.True);
                await UniTask.Yield();
                for (int tick = 0; tick < 600 && slot.LifeSequence == 1; tick++)
                {
                    await WaitRunSavedAsync(run);
                    FreezeStatisticsFixtureEnemyMovement(run);
                    run.Advance(CombatWorld.TickSeconds);
                    if (tick % 20 == 0) await UniTask.Yield();
                }
                Assert.That(slot.LifeSequence, Is.EqualTo(2), StatisticsSpawnDiagnostic(run, plan, slot));
                FreezeStatisticsFixtureEnemyMovement(run);
                Assert.That(slot.CurrentActorId, Is.GreaterThan(firstActor));
                Assert.That(run.World.TryFindActor(firstActor, out _), Is.False);
                Assert.That(await DefeatSlotAsync(run, plan.SlotId), Is.True);
                await ChooseAllSkillsAsync(run);
            }
            await WaitRunSavedAsync(run);
            Assert.That(run.TryOpenMap(), Is.True);
            AssertStatisticsMatchConfirmedLives(run);
            Assert.That(run.TryCaptureState(out var state, out string error), Is.True, error);
            Assert.That(state.Statistics.RogueChoices, Is.EqualTo(state.Progress.SelectedChoices));
            Assert.That(state.Progress.IsExhausted, Is.False);
            var corrupted = JsonUtility.FromJson<ExpeditionRunSnapshot>(JsonUtility.ToJson(state));
            corrupted.Statistics.NormalFirst.Progress++;
            string invalid = JsonUtility.ToJson(corrupted);
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, corrupted, out ExpeditionRun rejected, out error), Is.False);
            Assert.That(rejected, Is.Null);
            Assert.That(JsonUtility.ToJson(corrupted), Is.EqualTo(invalid));
            corrupted = JsonUtility.FromJson<ExpeditionRunSnapshot>(JsonUtility.ToJson(state));
            corrupted.Statistics.NormalFirst.Progress = long.MaxValue;
            corrupted.Statistics.EliteFirst.Progress = long.MaxValue;
            Assert.That(ExpeditionRun.TryRestore(ProjectServices.Session, corrupted, out rejected, out error), Is.False);
            string frozen = JsonUtility.ToJson(state.Statistics);
            long ticks = state.Statistics.Regions[0].EffectiveTicks;
            for (int frame = 0; frame < 3; frame++)
            {
                run.Advance(1f);
                await UniTask.Yield();
            }
            Assert.That(run.CaptureStatistics().Regions[0].EffectiveTicks, Is.EqualTo(ticks));
            Assert.That(await ProjectServices.Coordinator.CloseAsync(), Is.True);
            Assert.That(await ProjectServices.InitializeAsync(default), Is.True);
            SetStatisticsEnemySkills(stationarySkills);
            Assert.That(await ProjectServices.Coordinator.InitializeAsync(), Is.True);
            Assert.That(ProjectServices.Coordinator.CanResume, Is.True);
            Assert.That(await ProjectServices.Coordinator.ResumeRunAsync(), Is.True);
            run = ProjectServices.Session.CurrentRun;
            await WaitRunSavedAsync(run);
            Assert.That(run.TryOpenMap(), Is.True);
            ProjectRunStatistics restored = run.CaptureStatistics();
            Assert.That(restored.Regions[0].EffectiveTicks, Is.GreaterThanOrEqualTo(ticks));
            restored.Regions[0].EffectiveTicks = ticks;
            Assert.That(restored.DamageDealt, Is.EqualTo(state.Statistics.DamageDealt).Within(1e-12));
            Assert.That(restored.DamageTaken, Is.EqualTo(state.Statistics.DamageTaken).Within(1e-12));
            Assert.That(restored.HealingReceived, Is.EqualTo(state.Statistics.HealingReceived).Within(1e-12));
            restored.DamageDealt = state.Statistics.DamageDealt;
            restored.DamageTaken = state.Statistics.DamageTaken;
            restored.HealingReceived = state.Statistics.HealingReceived;
            Assert.That(JsonUtility.ToJson(restored), Is.EqualTo(frozen));
            AssertStatisticsMatchConfirmedLives(run);
        });

        /// <summary>
        /// 实际完成全部区域及最终领奖，战绩区分通关并保留全部成功结算收益。
        /// </summary>
        /// <returns>真实尾王、最终 Boss 与逐轮奖励确认的验证任务。</returns>
        [UnityTest]
        public IEnumerator Statistics_FinalBossRoundsProduceCompletedHistory() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            int expectedRounds = run.RegionRequests.Sum(request =>
                ProjectV2RewardConfig.BossReward(GameTables.CfgBosses.GetById(request.BossDefinitionId).RewardID).Rounds);
            for (int index = 0; index < run.RegionCount; index++)
            {
                await EnterExtractionConfirmationAsync(run);
                await ClickExtractionConfirmationAsync("继续下一区域");
                await WaitRunSavedAsync(run);
            }
            Assert.That(run.IsFinalRegion, Is.True);
            await CompleteRegionBossAsync(run);
            await WaitForPhaseAsync(ProjectRunPhase.ResultsReady);
            ProjectRunResult result = ProjectServices.Session.LastResult;
            ProjectRunStatistics statistics = result.Outcome.Statistics;
            Assert.That(statistics.RegionBossKills, Is.EqualTo(run.RegionCount));
            Assert.That(statistics.FinalBossKills, Is.EqualTo(1));
            Assert.That(statistics.BossLootRounds, Is.EqualTo(expectedRounds));
            Assert.That(statistics.CompletedRegions, Is.EqualTo(run.RegionCount + 1));
            Assert.That(statistics.IsFinalCompletion, Is.True);
            Assert.That(statistics.EarnedKillExperience, Is.EqualTo(result.Outcome.KillExperience));
            Assert.That(result.Outcome.Extracted, Is.True);
            Assert.That(result.LostItems, Is.Empty);
            Assert.That(result.Outcome.ExtractionExperience, Is.GreaterThan(0));
            Assert.That(ProjectServices.Session.RunHistory.Single(value => value.RunId == run.RunId).Outcome,
                Is.EqualTo(ProjectRunHistoryOutcome.Completed));
        });

        /// <summary>
        /// 在统计测试作用域内把敌人独立位移技能替换为现有普通攻击，结束还原原行，保留经验和身份。
        /// </summary>
        /// <param name="scope">无论测试成功或失败都会恢复配置的生命作用域。</param>
        private static void SetStatisticsEnemySkills(LifetimeScope scope)
        {
            var displacementEffects = new HashSet<int>(GameTables.CfgCombatEffects.GetAll()
                .Where(effect => effect.Kind == (int)CombatEffectKind.Displace).Select(effect => effect.ID));
            var displacementSkills = new HashSet<int>(GameTables.CfgCombatTimelineNodes.GetAll()
                .Where(node => node.EffectIDs.Any(displacementEffects.Contains)).Select(node => node.SkillID));
            CfgEnemiesRow ordinary = GameTables.CfgEnemies.GetAll().First(enemy =>
                enemy.Role == (int)RegionEnemyKind.Normal && !displacementSkills.Contains(enemy.SkillID));
            CfgCombatSkillsRow skill = GameTables.CfgCombatSkills.GetById(ordinary.SkillID);
            foreach (CfgEnemySkillsRow row in GameTables.CfgEnemySkills.GetAll().Where(row => displacementSkills.Contains(row.SkillID)))
            {
                int skillId = row.SkillID;
                double minDistance = row.MinDistance;
                double maxDistance = row.MaxDistance;
                scope.Defer(() =>
                {
                    row.SkillID = skillId;
                    row.MinDistance = minDistance;
                    row.MaxDistance = maxDistance;
                });
                row.SkillID = skill.ID;
                row.MinDistance = 0d;
                row.MaxDistance = Math.Min(row.MaxDistance, skill.StartRange);
            }
        }

        /// <summary>
        /// 固定统计夹具敌人的基础移动速度，保留普通攻击与真实生命，并初始化合法出生站位。
        /// </summary>
        /// <param name="run">已经完整绑定区域的真实单局。</param>
        /// <param name="restoreHomes">首次建立夹具时是否通过权威碰撞入口恢复各自出生点。</param>
        private static void FreezeStatisticsFixtureEnemyMovement(ExpeditionRun run, bool restoreHomes = false)
        {
            Assert.That(run.World.TryExportState(out CombatWorldSnapshot world, out string error), Is.True, error);
            foreach (RegionSpawnSlotState slot in run.CurrentRegion.Slots.Where(value => value.Phase == RegionSpawnPhase.Alive))
            {
                CombatActorState state = world.Actors.Single(value => value.Spec.ActorId == slot.CurrentActorId);
                Assert.That(run.World.TryFindActor(slot.CurrentActorId, out CombatEntityRef actor), Is.True);
                if (restoreHomes)
                {
                    RegionSpawnSlotPlan plan = run.CurrentRegion.Plan.SpawnSlots.Single(value => value.SlotId == slot.SlotId);
                    Assert.That(run.World.TrySetPosition(actor, plan.HomePosition), Is.True,
                        "测试出生点必须通过动态占位校验：" + StatisticsSpawnDiagnostic(run, plan, slot));
                }
                if (state.BaseSpec.MoveSpeed == 0f) continue;
                CombatActorSpec attributes = state.BaseSpec;
                attributes.MoveSpeed = 0f;
                Assert.That(run.World.TryReplaceBaseAttributes(actor, attributes), Is.True);
            }
        }

        /// <summary>
        /// 输出目标出生点、真实占位生命及独立位移状态，避免失败只显示等待原因。
        /// </summary>
        /// <param name="run">当前完整区域。</param>
        /// <param name="plan">目标冻结槽位。</param>
        /// <param name="slot">目标运行生命。</param>
        /// <returns>能定位具体占位来源的断言说明。</returns>
        private static string StatisticsSpawnDiagnostic(ExpeditionRun run, RegionSpawnSlotPlan plan, RegionSpawnSlotState slot)
        {
            Assert.That(run.World.TryExportState(out CombatWorldSnapshot world, out string error), Is.True, error);
            string actors = string.Join("; ", world.Actors.Where(actor => actor.Health > 0 && !actor.DeadPending &&
                actor.Spec.RoomId == run.CurrentRegion.Plan.Request.RegionId &&
                (actor.Position - plan.HomePosition).sqrMagnitude < (actor.Spec.Radius + plan.Radius) * (actor.Spec.Radius + plan.Radius))
                .Select(actor => $"actor={actor.Spec.ActorId}, position={actor.Position}, radius={actor.Spec.Radius}, speed={actor.Spec.MoveSpeed}"));
            return $"seed={run.Seed}, slot={plan.SlotId}, home={plan.HomePosition}, radius={plan.Radius}, " +
                $"phase={slot.Phase}, reason={slot.WaitingReason}, tick={run.CurrentRegion.EffectiveTick}, " +
                $"ready={slot.ReadyTick}, warning={slot.WarningStartTick}, displacements={world.Displacements.Count}, occupants=[{actors}]";
        }

        /// <summary>
        /// 依据已经确认的槽位生命独立计算分类击杀、配置贡献及首次和复活的真实经验。
        /// </summary>
        /// <param name="run">只生成首区且尚未耗尽构筑的真实单局。</param>
        private static void AssertStatisticsMatchConfirmedLives(ExpeditionRun run)
        {
            ProjectRunStatistics statistics = run.CaptureStatistics();
            CfgRogueProgressRow progress = ProjectV2RewardConfig.Progress(GameTables.CfgLevels.GetById(1).ProgressRuleID);
            foreach (RegionEnemyKind kind in new[] { RegionEnemyKind.Normal, RegionEnemyKind.Elite })
            {
                var first = new ProjectEnemyLifeStatistics();
                var respawn = new ProjectEnemyLifeStatistics();
                RegionRespawnRule rule = kind == RegionEnemyKind.Normal ? run.CurrentRegion.Plan.NormalRespawn : run.CurrentRegion.Plan.EliteRespawn;
                foreach (RegionSpawnSlotPlan plan in run.CurrentRegion.Plan.SpawnSlots.Where(value => value.Kind == kind))
                {
                    int processed = run.CurrentRegion.Slots.Single(value => value.SlotId == plan.SlotId).LastProcessedLife;
                    if (processed == 0) continue;
                    Assert.That(LevelContentConfig.TryGetEnemy(plan.EnemyId, out EnemyDefinition enemy), Is.True);
                    first.Kills++;
                    first.Experience += enemy.ExperienceAt(plan.EnemyLevel);
                    respawn.Kills += processed - 1;
                    respawn.Experience += (long)(processed - 1) * Mathf.FloorToInt(enemy.ExperienceAt(plan.EnemyLevel) * rule.ExperienceMultiplier);
                }
                int contribution = kind == RegionEnemyKind.Normal ? progress.NormalContribution : progress.EliteContribution;
                ProjectEnemyLifeStatistics recordedFirst = kind == RegionEnemyKind.Normal ? statistics.NormalFirst : statistics.EliteFirst;
                ProjectEnemyLifeStatistics recordedRespawn = kind == RegionEnemyKind.Normal ? statistics.NormalRespawn : statistics.EliteRespawn;
                Assert.That(recordedFirst.Kills, Is.EqualTo(first.Kills));
                Assert.That(recordedRespawn.Kills, Is.EqualTo(respawn.Kills));
                Assert.That(recordedFirst.Experience, Is.EqualTo(first.Experience));
                Assert.That(recordedRespawn.Experience, Is.EqualTo(respawn.Experience));
                Assert.That(recordedFirst.Progress, Is.EqualTo((long)first.Kills * contribution));
                Assert.That(recordedRespawn.Progress, Is.EqualTo((long)respawn.Kills * contribution));
                Assert.That(recordedFirst.Kills, Is.GreaterThan(0));
                Assert.That(recordedRespawn.Kills, Is.GreaterThan(0));
            }
            Assert.That(statistics.EarnedKillExperience, Is.EqualTo(run.KillExperience));
        }

        /// <summary>
        /// 撤离结果冻结入场估值与统计，并在真实滚动窗口完整显示收益明细。
        /// </summary>
        /// <returns>场景切换、终局保存和结果窗口布局的逐帧测试。</returns>
        [UnityTest]
        public IEnumerator Statistics_ExtractionFreezesEconomyAndDisplaysReadableDetails() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            ProjectRunStatistics entry = run.CaptureStatistics();
            Assert.That(entry.EntryItemCount, Is.EqualTo(run.Inventory.Count));
            Assert.That(entry.EntryItemSellValue, Is.EqualTo(run.Inventory.Sum(item => (long)item.SellGold)));
            await TriggerExtractionAsync(run);
            ProjectRunResult result = ProjectServices.Session.LastResult;
            ProjectRunStatistics frozen = result.Outcome.Statistics;
            Assert.That(frozen.HasData, Is.True);
            Assert.That(frozen.VisitedRegions, Is.GreaterThanOrEqualTo(entry.VisitedRegions));
            Assert.That(frozen.IsFinalCompletion, Is.False);
            Assert.That(ProjectServices.Session.RunHistory.Single(value => value.RunId == run.RunId).Outcome,
                Is.EqualTo(ProjectRunHistoryOutcome.Extracted));
            Assert.That(frozen.EntryItemSellValue, Is.EqualTo(entry.EntryItemSellValue));
            ProjectRunEconomySummary economy = ProjectRunEconomy.Calculate(result);
            Assert.That(economy.HasEntrySnapshot, Is.True);
            Assert.That(economy.EntrySellValue, Is.EqualTo(entry.EntryItemSellValue));
            Assert.That(economy.RetainedSellValue, Is.EqualTo(result.RetainedItems.Sum(item => (long)item.SellGold)));
            Assert.That(economy.NetAssetValue, Is.EqualTo(economy.RetainedSellValue - economy.EntrySellValue - result.Outcome.GoldSpent));
            long effectiveTicks = frozen.Regions[0].EffectiveTicks;
            ProjectRunResult copy = result.Copy();
            copy.Outcome.Statistics.Regions[0].EffectiveTicks = 999;
            Assert.That(result.Outcome.Statistics.Regions[0].EffectiveTicks, Is.EqualTo(effectiveTicks));
            RunResultWindow window = GameEntry.Get<IUIModule>().OpenedWindows.OfType<RunResultWindow>().Single();
            ScrollRect[] scrolls = window.Root.GetComponentsInChildren<ScrollRect>(true);
            Assert.That(scrolls.Length, Is.EqualTo(2));
            ScrollRect details = scrolls.Single(scroll => scroll.name == "ResultItems");
            Text[] labels = details.content.GetComponentsInChildren<Text>(true);
            foreach (string caption in new[] { "本局表现", "探索区域", "物品与支出", "入场装备估值", "携回物品估值", "资产变化" })
                Assert.That(labels.Any(label => label.text.Contains(caption)), Is.True, caption);
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            Assert.That(details.viewport.rect.height, Is.GreaterThan(0));
            Text[] detailBodies = labels.Where(value => value.text.Contains("探索区域") || value.text.Contains("入场装备估值")).ToArray();
            foreach (Text label in detailBodies)
                Assert.That(label.rectTransform.rect.height + 1f, Is.GreaterThanOrEqualTo(label.preferredHeight), label.text);
            Vector2 originalSize = details.content.sizeDelta;
            details.content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 360f);
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            foreach (Text label in detailBodies)
            {
                Assert.That(label.rectTransform.rect.width, Is.GreaterThan(0f), label.text);
                Assert.That(label.rectTransform.rect.height + 1f, Is.GreaterThanOrEqualTo(label.preferredHeight), label.text);
            }
            details.content.sizeDelta = originalSize;
            Canvas.ForceUpdateCanvases();
            await UniTask.Yield();
            Canvas.ForceUpdateCanvases();
            ClickButton(window, "返回主界面");
            await WaitForPhaseAsync(ProjectRunPhase.HomeReady);
            Assert.That(ProjectServices.Session.LastResult.Viewed, Is.True);
        });
    }
}
