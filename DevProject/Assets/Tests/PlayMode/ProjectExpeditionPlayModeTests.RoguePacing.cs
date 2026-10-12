using System.Collections;
using System.Linq;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace MiniGame.Tests
{
    /// <summary>
    /// 验证加权击杀、跨阈值余量及有限技能池耗尽。
    /// </summary>
    public sealed partial class ProjectExpeditionPlayModeTests
    {
        /// <summary>
        /// 实际区域死亡按普通与精英贡献产生资格，跨越阈值后保存准确余量。
        /// </summary>
        /// <returns>真实槽位死亡和完整进度快照验证任务。</returns>
        [UnityTest]
        public IEnumerator RogueProgress_WeightsDeathsAndPreservesMultiThresholdRemainder() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("WeightedRogueProgress");
            SetProgressThreshold(scope, 2);
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            CfgRogueProgressRow rule = ProjectV2RewardConfig.Progress(GameTables.CfgLevels.GetById(1).ProgressRuleID);
            string elite = run.CurrentRegion.Plan.SpawnSlots.First(value => value.Kind == RegionEnemyKind.Elite).SlotId;
            Assert.That(await DefeatSlotAsync(run, elite), Is.True);
            Assert.That(run.TryCaptureState(out var captured, out string error), Is.True, error);
            int contribution = run.CurrentRegion.Plan.SpawnSlots.Where(plan => run.CurrentRegion.Slots.Single(slot =>
                slot.SlotId == plan.SlotId).Phase != RegionSpawnPhase.Alive).Sum(plan => plan.Kind == RegionEnemyKind.Normal ?
                rule.NormalContribution : plan.Kind == RegionEnemyKind.Elite ? rule.EliteContribution : 0);
            Assert.That(contribution, Is.GreaterThanOrEqualTo(rule.EliteContribution));
            Assert.That(captured.Progress.QualificationSerial, Is.EqualTo(contribution / rule.Threshold));
            Assert.That(captured.Progress.RemainingProgress, Is.EqualTo(contribution % rule.Threshold));
            long pending = captured.Progress.QualificationSerial - captured.Progress.SelectedChoices - captured.Progress.ClosedChoices;
            Assert.That(captured.Progress.PendingQualifications.Count, Is.EqualTo(pending));
            Assert.That(pending, Is.GreaterThan(0));
            Assert.That(run.PendingRogueChoice.SourceId, Is.EqualTo(run.RunId + ":progress:" + captured.Progress.PendingQualifications[0]));
            await ChooseAllSkillsAsync(run);
            Assert.That(run.PendingRogueQualifications, Is.Zero);
        });

        /// <summary>
        /// 单技能配置可正常展示一个候选，全部等级选完后关闭剩余资格而不生成物品。
        /// </summary>
        /// <returns>有限独立池及耗尽边界的逐帧验证任务。</returns>
        [UnityTest]
        public IEnumerator RogueFinitePool_OneOptionRanksThenClosesExcessQualifications() => UniTask.ToCoroutine(async () =>
        {
            await SetupAsync(new MemorySaveBackend());
            using var scope = new LifetimeScope("FiniteRoguePool");
            SetProgressThreshold(scope, 1);
            RestrictSkillPool(scope, "wide_blade");
            Assert.That(await ProjectServices.Coordinator.StartRunAsync(1), Is.True);
            ExpeditionRun run = ProjectServices.Session.CurrentRun;
            int inventory = run.Inventory.Count;
            int maximum = GameTables.CfgRogueRewards.GetById("wide_blade").MaxRank;
            for (int rank = 1; rank <= maximum; rank++)
            {
                if (run.PendingRogueChoice == null) await EarnRogueChoiceAsync(run);
                var choice = run.PendingRogueChoice;
                Assert.That(choice.Options.Count, Is.EqualTo(1));
                Assert.That(choice.Options[0].Reward.Rank, Is.EqualTo(rank));
                Assert.That(run.TryChooseRogue(choice.OfferId, choice.Revision, 0), Is.True);
                await WaitRunSavedAsync(run);
            }
            Assert.That(run.IsRogueBuildComplete, Is.True);
            Assert.That(run.IsChoosingReward, Is.False);
            Assert.That(run.Inventory.Count, Is.EqualTo(inventory));
            Assert.That(run.TryCaptureState(out var saved, out string error), Is.True, error);
            Assert.That(saved.Progress.IsExhausted, Is.True);
            Assert.That(saved.Progress.PendingQualifications, Is.Empty);
            Assert.That(saved.Progress.SelectedChoices, Is.EqualTo(maximum));
        });

        /// <summary>
        /// 限定测试单局冻结的技能成员，保留真实奖励定义及等级规则。
        /// </summary>
        /// <param name="scope">负责恢复源配置成员的测试作用域。</param>
        /// <param name="rewardId">本项测试保留的唯一技能。</param>
        private static void RestrictSkillPool(LifetimeScope scope, string rewardId)
        {
            string pool = GameTables.CfgLevels.GetById(1).SkillPoolID;
            foreach (CfgRoguePoolsRow row in GameTables.CfgRoguePools.GetAll().Where(value => value.PoolID == pool && value.RewardID != rewardId))
            {
                string previous = row.PoolID;
                scope.Defer(() => row.PoolID = previous);
                row.PoolID = "test-filter:" + row.RewardID;
            }
        }
    }
}
