using System.Collections.Generic;
using System.Linq;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证 Boss 冻结战利品在保存恢复后的独占归属和逐轮领取。
    /// </summary>
    public sealed class BossLootPersistenceTests
    {
        /// <summary>
        /// 加载正式物品定义。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

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
