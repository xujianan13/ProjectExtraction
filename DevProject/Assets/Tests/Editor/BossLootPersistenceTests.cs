using System;
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
    /// 验证 Boss 整批掉落的冻结、原子领取、保存恢复与重复回调边界。
    /// </summary>
    public sealed class BossLootPersistenceTests
    {
        /// <summary>
        /// 加载正式物品定义。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 非三件数量的整批掉落可冻结并恢复，输入、展示和保存副本均不能修改会话内物品。
        /// </summary>
        [Test]
        public void PendingLootRoundTripPreservesAllItemsAndIndependentCopies()
        {
            var session = new BossLootSession("run");
            List<ProjectItem> items = Items(1, 2);
            string firstId = items[0].Id;
            float firstValue = items[0].BaseAttributes[0].Value;
            Assert.That(session.TryBegin("boss-a", items), Is.True);
            items[0].Id = "changed-source";
            items[0].BaseAttributes[0].Value = 999;
            BossLootSnapshot view = session.Active;
            Assert.That(view.BatchId, Is.EqualTo("boss-a"));
            Assert.That(view.Revision, Is.EqualTo(1));
            view.Items[0].Id = "changed-view";
            view.Items[0].BaseAttributes[0].Value = 888;
            BossLootState captured = session.CaptureState();
            BossLootState copy = captured.Copy();
            copy.Batches[0].Items.Clear();
            Assert.That(captured.Batches[0].Items.Count, Is.EqualTo(2));
            var restored = new BossLootSession("run");
            Assert.That(restored.TryRestore(RoundTrip(captured), out string error), Is.True, error);
            captured.Batches[0].Items[0].Id = "changed-state";
            foreach (BossLootSession current in new[] { session, restored })
            {
                Assert.That(current.HasPending, Is.True);
                Assert.That(current.IsComplete, Is.False);
                Assert.That(current.Active.Items.Count, Is.EqualTo(2));
                Assert.That(current.Active.Items[0].Id, Is.EqualTo(firstId));
                Assert.That(current.Active.Items[0].BaseAttributes[0].Value, Is.EqualTo(firstValue));
                Assert.That(current.ClaimedItems(), Is.Empty);
            }
        }

        /// <summary>
        /// 失败回调不消费掉落，成功回调只收到一次整批只读副本，回调期间的重入不能开始、恢复、取消或再次领取。
        /// </summary>
        [Test]
        public void ClaimFailureAndReentrancyCannotConsumeOrDuplicateBatch()
        {
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("boss-a", Items(2, 4)), Is.True);
            BossLootSnapshot view = session.Active;
            string original = JsonUtility.ToJson(session.CaptureState());
            Assert.That(session.TryClaim(view.BatchId, view.Revision, items =>
            {
                Assert.That(((IList<ProjectItem>)items).IsReadOnly, Is.True);
                items[0].Id = "failed-copy";
                items[0].BaseAttributes[0].Value = 999;
                return false;
            }), Is.False);
            Assert.That(JsonUtility.ToJson(session.CaptureState()), Is.EqualTo(original));
            int applications = 0;
            var received = new List<string>();
            BossLootState pending = session.CaptureState();
            Assert.That(session.TryClaim(view.BatchId, view.Revision, items =>
            {
                applications++;
                received.AddRange(items.Select(item => item.Id));
                Assert.That(session.TryClaim(view.BatchId, view.Revision, _ => true), Is.False);
                Assert.That(session.TryBegin("boss-b", Items(3, 1)), Is.False);
                Assert.That(session.TryRestore(pending, out _), Is.False);
                session.Cancel();
                Assert.That(session.HasPending, Is.True);
                items[0].Name = "callback-copy";
                return true;
            }), Is.True);
            Assert.That(applications, Is.EqualTo(1));
            Assert.That(received, Is.EqualTo(view.Items.Select(item => item.Id)));
            Assert.That(session.IsComplete, Is.True);
            Assert.That(session.HasPending, Is.False);
            Assert.That(session.Active, Is.Null);
            Assert.That(session.TryClaim(view.BatchId, view.Revision, _ => { applications++; return true; }), Is.False);
            Assert.That(applications, Is.EqualTo(1));
            Assert.That(session.ClaimedItems().Count, Is.EqualTo(4));
            Assert.That(session.ClaimedItems()[0].Name, Is.Not.EqualTo("callback-copy"));
            session.ClaimedItems()[0].BaseAttributes[0].Value = 777;
            Assert.That(session.ClaimedItems(view.BatchId)[0].BaseAttributes[0].Value, Is.Not.EqualTo(777));
            Assert.That(session.ClaimedItems("unknown"), Is.Empty);
        }

        /// <summary>
        /// 已领取事实跨恢复保留，旧批次和跨批重复实例不能再次发奖，新批次仍按完整列表领取。
        /// </summary>
        [Test]
        public void ClaimedBatchRestoresWithoutRegrantingAndLaterBatchKeepsUniqueItems()
        {
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("boss-a", Items(4, 1)), Is.True);
            BossLootSnapshot first = session.Active;
            Assert.That(session.TryClaim(first.BatchId, first.Revision, _ => true), Is.True);
            var restored = new BossLootSession("run");
            Assert.That(restored.TryRestore(RoundTrip(session.CaptureState()), out string error), Is.True, error);
            Assert.That(restored.IsComplete, Is.True);
            Assert.That(restored.TryClaim(first.BatchId, first.Revision, _ => true), Is.False);
            Assert.That(restored.TryBegin(first.BatchId, Items(5, 2)), Is.False);
            Assert.That(restored.TryBegin("boss-b", Items(4, 1)), Is.False);
            Assert.That(restored.TryBegin("boss-b", Items(5, 2)), Is.True);
            BossLootState secondPending = RoundTrip(restored.CaptureState());
            var current = new BossLootSession("run");
            Assert.That(current.TryRestore(secondPending, out error), Is.True, error);
            BossLootSnapshot second = current.Active;
            int granted = 0;
            Assert.That(current.TryClaim(second.BatchId, second.Revision, items => { granted += items.Count; return true; }), Is.True);
            Assert.That(granted, Is.EqualTo(2));
            Assert.That(current.ClaimedItems().Select(item => item.Id).Distinct().Count(), Is.EqualTo(3));
            Assert.That(current.ClaimedItems(first.BatchId).Count, Is.EqualTo(1));
            Assert.That(current.ClaimedItems(second.BatchId).Count, Is.EqualTo(2));
        }

        /// <summary>
        /// 回调异常向调用方传播，但不会留下永久重入锁或提前消费整批掉落。
        /// </summary>
        [Test]
        public void CallbackExceptionReleasesGuardWithoutClaimingLoot()
        {
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("boss", Items(6, 2)), Is.True);
            BossLootSnapshot view = session.Active;
            string pending = JsonUtility.ToJson(session.CaptureState());
            Assert.Throws<InvalidOperationException>(() => session.TryClaim(view.BatchId, view.Revision,
                _ => throw new InvalidOperationException("测试回调异常")));
            Assert.That(JsonUtility.ToJson(session.CaptureState()), Is.EqualTo(pending));
            Assert.That(session.TryClaim(view.BatchId, view.Revision, items => items.Count == 2), Is.True);
            Assert.That(session.IsComplete, Is.True);
        }

        /// <summary>
        /// 取消状态可以保存恢复，尚未领取的物品不发放，既有领取事实仍保留。
        /// </summary>
        /// <param name="claimed">取消前是否已经领取整批掉落。</param>
        [TestCase(false)]
        [TestCase(true)]
        public void CancellationRestoresWithoutOpeningOrGrantingLoot(bool claimed)
        {
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("boss", Items(7, 2)), Is.True);
            BossLootSnapshot view = session.Active;
            if (claimed) Assert.That(session.TryClaim(view.BatchId, view.Revision, _ => true), Is.True);
            session.Cancel();
            var restored = new BossLootSession("run");
            Assert.That(restored.TryRestore(RoundTrip(session.CaptureState()), out string error), Is.True, error);
            Assert.That(restored.HasPending, Is.False);
            Assert.That(restored.Active, Is.Null);
            Assert.That(restored.IsComplete, Is.EqualTo(claimed));
            Assert.That(restored.ClaimedItems().Count, Is.EqualTo(claimed ? 2 : 0));
            Assert.That(restored.TryClaim(view.BatchId, view.Revision, _ => true), Is.False);
            Assert.That(restored.TryBegin("other", Items(8, 1)), Is.False);
            Assert.That(restored.CaptureState().Cancelled, Is.True);
        }

        /// <summary>
        /// 错误版本、活动身份、批次顺序和物品归属均原子拒绝，原会话和随后合法恢复不受影响。
        /// </summary>
        /// <param name="fault">待破坏的保存字段。</param>
        [TestCase("old-version")]
        [TestCase("run")]
        [TestCase("active")]
        [TestCase("revision")]
        [TestCase("duplicate-batch")]
        [TestCase("duplicate-item")]
        [TestCase("unclaimed-previous")]
        [TestCase("null-batches")]
        [TestCase("null-batch")]
        [TestCase("null-items")]
        [TestCase("null-item")]
        [TestCase("empty-items")]
        [TestCase("equipped-item")]
        public void InvalidSavedStateDoesNotReplaceExistingLoot(string fault)
        {
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("boss-a", Items(9, 2)), Is.True);
            BossLootSnapshot first = session.Active;
            Assert.That(session.TryClaim(first.BatchId, first.Revision, _ => true), Is.True);
            Assert.That(session.TryBegin("boss-b", Items(10, 2)), Is.True);
            BossLootState valid = session.CaptureState();
            BossLootState invalid = valid.Copy();
            if (fault == "old-version") invalid.Version = 1;
            if (fault == "run") invalid.RunId = "other";
            if (fault == "active") invalid.ActiveBatchId = "boss-a";
            if (fault == "revision") invalid.Batches[1].Revision = 0;
            if (fault == "duplicate-batch") invalid.Batches[1].BatchId = "boss-a";
            if (fault == "duplicate-item") invalid.Batches[1].Items[0].Id = invalid.Batches[0].Items[0].Id;
            if (fault == "unclaimed-previous") invalid.Batches[0].Claimed = false;
            if (fault == "null-batches") invalid.Batches = null;
            if (fault == "null-batch") invalid.Batches[1] = null;
            if (fault == "null-items") invalid.Batches[1].Items = null;
            if (fault == "null-item") invalid.Batches[1].Items[0] = null;
            if (fault == "empty-items") invalid.Batches[1].Items.Clear();
            if (fault == "equipped-item") invalid.Batches[1].Items[0].Equipped = true;
            string before = JsonUtility.ToJson(valid);
            Assert.That(session.TryRestore(invalid, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(session.CaptureState()), Is.EqualTo(before));
            var fresh = new BossLootSession("run");
            Assert.That(fresh.TryRestore(invalid, out error), Is.False);
            Assert.That(fresh.TryRestore(valid, out error), Is.True, error);
            Assert.That(fresh.Active.BatchId, Is.EqualTo("boss-b"));
            Assert.That(fresh.ClaimedItems().Count, Is.EqualTo(2));
        }

        /// <summary>
        /// 无效新批次、错误领取身份与空回调拒绝时不会修改当前冻结物品。
        /// </summary>
        [Test]
        public void InvalidBeginAndClaimKeepPendingBatchUnchanged()
        {
            var session = new BossLootSession("run");
            Assert.That(session.TryBegin("boss", null), Is.False);
            Assert.That(session.TryBegin("boss", Items(11, 0)), Is.False);
            List<ProjectItem> duplicate = Items(11, 2);
            duplicate[1].Id = duplicate[0].Id;
            Assert.That(session.TryBegin("boss", duplicate), Is.False);
            Assert.That(session.TryBegin(" ", Items(11, 1)), Is.False);
            Assert.That(new BossLootSession(" ").TryBegin("boss", Items(11, 1)), Is.False);
            Assert.That(session.TryBegin("boss", Items(11, 2)), Is.True);
            BossLootSnapshot view = session.Active;
            string pending = JsonUtility.ToJson(session.CaptureState());
            Assert.That(session.TryClaim("other", view.Revision, _ => true), Is.False);
            Assert.That(session.TryClaim(view.BatchId, view.Revision + 1, _ => true), Is.False);
            Assert.That(session.TryClaim(view.BatchId, view.Revision, null), Is.False);
            Assert.That(JsonUtility.ToJson(session.CaptureState()), Is.EqualTo(pending));
        }

        /// <summary>
        /// 尚未生成任何掉落的会话也可以往返保存，不凭空出现待领物品。
        /// </summary>
        [Test]
        public void EmptySessionRoundTripHasNoActiveLoot()
        {
            var source = new BossLootSession("run");
            var restored = new BossLootSession("run");
            Assert.That(restored.TryRestore(RoundTrip(source.CaptureState()), out string error), Is.True, error);
            Assert.That(restored.HasPending, Is.False);
            Assert.That(restored.IsComplete, Is.False);
            Assert.That(restored.Active, Is.Null);
            BossLootState invalid = source.CaptureState();
            invalid.ActiveBatchId = "unknown";
            Assert.That(restored.TryRestore(invalid, out _), Is.False);
        }

        /// <summary>
        /// 通过正式 Unity JSON 载荷复制整批掉落状态。
        /// </summary>
        /// <param name="state">需要保存和读取的独立会话状态。</param>
        /// <returns>不共享原有物品和批次列表的读取结果。</returns>
        private static BossLootState RoundTrip(BossLootState state) => JsonUtility.FromJson<BossLootState>(JsonUtility.ToJson(state));

        /// <summary>
        /// 从正式定义生成指定数量的完整测试装备，并冻结不同批次的实例身份。
        /// </summary>
        /// <param name="batch">用于区分实例的测试批次序号。</param>
        /// <param name="count">需要生成的物品数量。</param>
        /// <returns>已冻结且互不共享属性的未装备物品列表。</returns>
        private static List<ProjectItem> Items(int batch, int count)
        {
            var result = new List<ProjectItem>();
            for (int index = 0; index < count; index++)
            {
                ProjectItem item = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(batch * 7 + index), 1,
                    ProjectItemQuality.Common, "ruin_weapon");
                Assert.That(item, Is.Not.Null);
                item.Id = "test:" + batch + ":" + index;
                result.Add(item);
            }
            return result;
        }
    }
}
