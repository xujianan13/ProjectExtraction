using System;
using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证技能队列、候选恢复与 Boss 冻结物品的关键边界。
    /// </summary>
    public sealed class RoguePersistentRewardTests
    {
        /// <summary>
        /// 加载真实物品验证所需配置，测试数值仅在本测试程序集内创建。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 清理测试中配置变更，避免影响其他测试类。
        /// </summary>
        [TearDown]
        public void RestoreTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 有限技能构筑不再要求四个不限级保底项。
        /// </summary>
        [Test]
        public void RewardValidation_AllowsFiniteInitialSkills()
        {
            foreach (CfgRogueRewardsRow row in GameTables.CfgRogueRewards.GetAll())
                if (row.MaxRank == 0) row.MaxRank = 1;
            Assert.That(ProjectAugmentConfig.Validate(out string error), Is.True, error);
        }

        /// <summary>
        /// 技能池排除物品并展示实际剩余项，有限技能选完后正常耗尽。
        /// </summary>
        [Test]
        public void SkillPool_OffersRemainingItemsAndExhaustsWithoutLoot()
        {
            CfgRogueRewardsRow first = Attribute("first", 1);
            CfgRogueRewardsRow second = Attribute("second", 1);
            var session = new RogueChoiceSession("run", 11, new[] { first, second }, Policy());
            first.Amount = 999;
            Assert.That(session.TryBegin("kill:1"), Is.True);
            Assert.That(session.Active.Options.Count, Is.EqualTo(2));
            Assert.That(session.Active.Options.All(option => option.Reward.Kind == RogueRewardKind.AttributeBonus), Is.True);
            Assert.That(session.Active.Options.First(option => option.Reward.Id == "first").Reward.Amount, Is.EqualTo(1));
            RogueChoiceSnapshot choice = session.Active;
            Assert.That(session.TrySelect(choice.OfferId, choice.Revision, 0, reward => true), Is.True);
            Assert.That(session.TryBegin("kill:2"), Is.True);
            choice = session.Active;
            Assert.That(choice.Options.Count, Is.EqualTo(1));
            Assert.That(session.TrySelect(choice.OfferId, choice.Revision, 0, reward => true), Is.True);
            Assert.That(session.IsExhausted, Is.True);
            Assert.That(session.TryBegin("kill:3"), Is.False);
            Assert.That(session.HasPending, Is.False);
        }

        /// <summary>
        /// 保存并恢复已选等级与当前候选后，后续抽取、刷新和来源去重完全连续。
        /// </summary>
        [Test]
        public void SkillRestore_KeepsFrozenOfferAndFutureRandomSequence()
        {
            CfgRogueRewardsRow[] pool = Enumerable.Range(0, 6).Select(index => Attribute("reward:" + index, 4)).ToArray();
            var original = new RogueChoiceSession("run", 123, pool, Policy(true, true));
            Assert.That(original.TryBegin("kill:1"), Is.True);
            RogueChoiceSnapshot first = original.Active;
            Assert.That(original.TrySelect(first.OfferId, first.Revision, 0, reward => true), Is.True);
            Assert.That(original.TryBegin("kill:2"), Is.True);
            Assert.That(original.TryRefresh(original.Active.OfferId, original.Active.Revision), Is.True);
            RogueChoiceState saved = JsonUtility.FromJson<RogueChoiceState>(JsonUtility.ToJson(original.CaptureState()));
            var restored = new RogueChoiceSession("run", 999, pool, Policy());
            Assert.That(restored.TryRestore(saved, out string error), Is.True, error);
            AssertOffersEqual(original.Active, restored.Active);
            Assert.That(restored.Upgrades.Single().Rank, Is.EqualTo(1));
            Assert.That(restored.TryBegin("kill:2"), Is.False);
            RogueChoiceSnapshot current = original.Active;
            Assert.That(original.TrySelect(current.OfferId, current.Revision, 0, reward => true), Is.True);
            Assert.That(restored.TrySelect(current.OfferId, current.Revision, 0, reward => true), Is.True);
            Assert.That(restored.TrySelect(current.OfferId, current.Revision, 0, reward => true), Is.False);
            Assert.That(original.TryBegin("kill:3"), Is.True);
            Assert.That(restored.TryBegin("kill:3"), Is.True);
            AssertOffersEqual(original.Active, restored.Active);
            Assert.That(original.TryRefresh(original.Active.OfferId, original.Active.Revision), Is.True);
            Assert.That(restored.TryRefresh(restored.Active.OfferId, restored.Active.Revision), Is.True);
            AssertOffersEqual(original.Active, restored.Active);
            Assert.That(restored.CaptureState().State, Is.EqualTo(original.CaptureState().State));
        }

        /// <summary>
        /// 损坏候选或改变池顺序的恢复不会改变当前选择与随机位置。
        /// </summary>
        [Test]
        public void SkillRestore_RejectsInvalidFrozenRewardAndPoolOrderAtomically()
        {
            CfgRogueRewardsRow[] pool = { Attribute("a", 2), Attribute("b", 2), Attribute("c", 2) };
            var session = new RogueChoiceSession("run", 4, pool, Policy());
            Assert.That(session.TryBegin("source"), Is.True);
            RogueChoiceState original = session.CaptureState();
            RogueChoiceState invalid = original.Copy();
            invalid.Options[0].Amount = 99;
            Assert.That(session.TryRestore(invalid, out _), Is.False);
            Assert.That(session.CaptureState().State, Is.EqualTo(original.State));
            Assert.That(session.Active.Options[0].Reward.Amount, Is.EqualTo(original.Options[0].Amount));
            invalid = original.Copy();
            invalid.PoolIds.Reverse();
            Assert.That(session.TryRestore(invalid, out _), Is.False);
            Assert.That(session.Active.OfferId, Is.EqualTo(original.OfferId));
        }

        /// <summary>
        /// 同批贡献跨过多个阈值，资格顺序、余量与耗尽关闭记录经过保存保持一致。
        /// </summary>
        [Test]
        public void Progress_MultipleThresholdsQueueAndExhaustionPersist()
        {
            var original = new RogueProgressSession(20);
            Assert.That(original.TryAddProgress(18), Is.True);
            Assert.That(original.TryAddProgress(27), Is.True);
            Assert.That(original.RemainingProgress, Is.EqualTo(5));
            Assert.That(original.PendingQualifications, Is.EqualTo(new long[] { 1, 2 }));
            Assert.That(original.TryComplete(2), Is.False);
            Assert.That(original.TryComplete(1), Is.True);
            Assert.That(original.TryComplete(1), Is.False);
            RogueProgressState saved = JsonUtility.FromJson<RogueProgressState>(JsonUtility.ToJson(original.CaptureState()));
            var restored = new RogueProgressSession(20);
            Assert.That(restored.TryRestore(saved, out string error), Is.True, error);
            Assert.That(restored.TryPeek(out long qualification), Is.True);
            Assert.That(qualification, Is.EqualTo(2));
            Assert.That(restored.CloseExhausted(), Is.EqualTo(1));
            Assert.That(restored.SelectedChoices, Is.EqualTo(1));
            Assert.That(restored.ClosedChoices, Is.EqualTo(1));
            Assert.That(restored.RemainingProgress, Is.Zero);
            Assert.That(restored.PendingCount, Is.Zero);
            Assert.That(restored.TryAddProgress(100), Is.False);
            var exhausted = new RogueProgressSession(20);
            Assert.That(exhausted.TryRestore(restored.CaptureState(), out error), Is.True, error);
            Assert.That(exhausted.IsExhausted, Is.True);
            Assert.That(exhausted.TryPeek(out _), Is.False);
            Assert.That(exhausted.CaptureState().ClosedRanges.Single().FirstId, Is.EqualTo(2));
            Assert.That(exhausted.CloseExhausted(), Is.Zero);
        }

        /// <summary>
        /// 恢复拒绝缺失资格和计数不守恒，失败保留原余量。
        /// </summary>
        [Test]
        public void ProgressRestore_RejectsMissingQualificationWithoutMutation()
        {
            var session = new RogueProgressSession(10);
            Assert.That(session.TryAddProgress(23), Is.True);
            RogueProgressState invalid = session.CaptureState();
            invalid.PendingQualifications[0] = 2;
            Assert.That(session.TryRestore(invalid, out _), Is.False);
            Assert.That(session.RemainingProgress, Is.EqualTo(3));
            Assert.That(session.PendingQualifications, Is.EqualTo(new long[] { 1, 2 }));
        }

        /// <summary>
        /// Boss 候选、失败重试和已领结果都不共享物品，恢复后重复点击及重复批次不会发奖。
        /// </summary>
        [Test]
        public void BossLoot_FrozenItemsDeepCopyAndRoundsResumeExactlyOnce()
        {
            var session = new BossLootChoiceSession("run");
            List<ProjectItem> first = Items(1);
            string chosenId = first[0].Id;
            float originalValue = first[0].BaseAttributes[0].Value;
            Assert.That(session.TryBegin("region:1:boss", 2, first), Is.True);
            first[0].BaseAttributes[0].Value = 999;
            first[0].Id = "changed";
            BossLootChoiceSnapshot offer = session.Active;
            offer.Options[0].BaseAttributes[0].Value = 888;
            Assert.That(session.Active.Options[0].Id, Is.EqualTo(chosenId));
            Assert.That(session.Active.Options[0].BaseAttributes[0].Value, Is.EqualTo(originalValue));
            Assert.That(session.TrySelect(offer.OfferId, offer.Revision, 0, item =>
            {
                item.BaseAttributes[0].Value = 777;
                return false;
            }), Is.False);
            Assert.That(session.Active.Options[0].BaseAttributes[0].Value, Is.EqualTo(originalValue));
            int granted = 0;
            Assert.That(session.TrySelect(offer.OfferId, offer.Revision, 0, item => { granted++; return true; }), Is.True);
            Assert.That(session.HasPending, Is.True);
            Assert.That(session.NeedsCandidates, Is.True);
            Assert.That(session.TrySelect(offer.OfferId, offer.Revision, 0, item => { granted++; return true; }), Is.False);
            var restored = new BossLootChoiceSession("run");
            BossLootChoiceState saved = JsonUtility.FromJson<BossLootChoiceState>(JsonUtility.ToJson(session.CaptureState()));
            Assert.That(restored.TryRestore(saved, out string error), Is.True, error);
            Assert.That(restored.NeedsCandidates, Is.True);
            Assert.That(restored.ClaimedItems().Single().Id, Is.EqualTo(chosenId));
            Assert.That(restored.TryBeginNextRound(Items(2)), Is.True);
            var secondRoundRestored = new BossLootChoiceSession("run");
            saved = JsonUtility.FromJson<BossLootChoiceState>(JsonUtility.ToJson(restored.CaptureState()));
            Assert.That(secondRoundRestored.TryRestore(saved, out error), Is.True, error);
            Assert.That(secondRoundRestored.Active.OfferId, Is.EqualTo(restored.Active.OfferId));
            Assert.That(secondRoundRestored.Active.Options.Select(item => item.Id), Is.EqualTo(restored.Active.Options.Select(item => item.Id)));
            restored = secondRoundRestored;
            Assert.That(restored.Active.Round, Is.EqualTo(2));
            Assert.That(restored.Active.TotalRounds, Is.EqualTo(2));
            Assert.That(restored.TryBeginNextRound(Items(3)), Is.False);
            offer = restored.Active;
            Assert.That(restored.TrySelect(offer.OfferId, offer.Revision + 1, 1, item => true), Is.False);
            Assert.That(restored.TrySelect(offer.OfferId, offer.Revision, 1, item => { granted++; return true; }), Is.True);
            Assert.That(restored.IsComplete, Is.True);
            Assert.That(restored.HasPending, Is.False);
            Assert.That(granted, Is.EqualTo(2));
            Assert.That(restored.TryBegin("region:1:boss", 2, Items(4)), Is.False);
            Assert.That(restored.ClaimedItems().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// 重复候选实例或缺少前轮领取事实的保存状态不能恢复。
        /// </summary>
        [Test]
        public void BossRestore_RejectsDuplicateItemsAndUnclaimedPreviousRound()
        {
            var session = new BossLootChoiceSession("run");
            Assert.That(session.TryBegin("boss", 2, Items(10)), Is.True);
            BossLootChoiceSnapshot offer = session.Active;
            Assert.That(session.TrySelect(offer.OfferId, offer.Revision, 0, item => true), Is.True);
            Assert.That(session.TryBeginNextRound(Items(11)), Is.True);
            BossLootChoiceState original = session.CaptureState();
            BossLootChoiceState invalid = original.Copy();
            invalid.Batches[0].Rounds[1].Options[0].Id = invalid.Batches[0].Rounds[0].Options[0].Id;
            Assert.That(session.TryRestore(invalid, out _), Is.False);
            invalid = original.Copy();
            invalid.Batches[0].Rounds[0].SelectedIndex = -1;
            Assert.That(session.TryRestore(invalid, out _), Is.False);
            Assert.That(session.Active.Round, Is.EqualTo(2));
            Assert.That(session.ClaimedItems().Count, Is.EqualTo(1));
        }

        /// <summary>
        /// 创建无配置依赖的测试选择策略。
        /// </summary>
        /// <param name="refresh">是否启用刷新。</param>
        /// <param name="extra">是否启用第四项。</param>
        /// <returns>独立测试策略。</returns>
        private static RogueChoicePolicy Policy(bool refresh = false, bool extra = false) => new(refresh, extra, 2);

        /// <summary>
        /// 创建仅用于测试程序集的有限属性强化。
        /// </summary>
        /// <param name="id">候选身份。</param>
        /// <param name="maxRank">测试等级上限。</param>
        /// <returns>独立测试配置行。</returns>
        private static CfgRogueRewardsRow Attribute(string id, int maxRank) => new()
        {
            ID = id, Title = id, Description = "测试强化", Kind = (int)RogueRewardKind.AttributeBonus,
            Attribute = (int)ProjectAttributeType.Attack, Amount = 1, Weight = 1, MaxRank = maxRank
        };

        /// <summary>
        /// 比较冻结展示顺序、代次和升级等级。
        /// </summary>
        /// <param name="left">原会话候选。</param>
        /// <param name="right">恢复会话候选。</param>
        private static void AssertOffersEqual(RogueChoiceSnapshot left, RogueChoiceSnapshot right)
        {
            Assert.That(right.OfferId, Is.EqualTo(left.OfferId));
            Assert.That(right.Revision, Is.EqualTo(left.Revision));
            Assert.That(right.Options.Select(option => option.Reward.Id), Is.EqualTo(left.Options.Select(option => option.Reward.Id)));
            Assert.That(right.Options.Select(option => option.Reward.Rank), Is.EqualTo(left.Options.Select(option => option.Reward.Rank)));
        }

        /// <summary>
        /// 从正式定义生成三件完整测试装备并稳定指定测试实例身份。
        /// </summary>
        /// <param name="round">用于区分测试轮次的序号。</param>
        /// <returns>三件已冻结且互不共享属性的装备。</returns>
        private static List<ProjectItem> Items(int round)
        {
            var result = new List<ProjectItem>();
            for (int index = 0; index < 3; index++)
            {
                ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(round * 7 + index), 1,
                    ProjectItemQuality.Common, "ruin_weapon");
                Assert.That(item, Is.Not.Null);
                item.Id = "test:" + round + ":" + index;
                result.Add(item);
            }
            return result;
        }
    }
}
