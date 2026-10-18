using System.Collections.Generic;
using GameFramework;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证终局持久记录和结算的重复执行边界。
    /// </summary>
    public sealed class ProjectRunSettlementTests
    {
        /// <summary>
        /// 死亡只损失本关所得物品，携入装备归还，永久经验、入场扣费和金币支出不退回。
        /// </summary>
        [Test]
        public void DeathPreservesEntryEquipmentAndDoesNotRefund()
        {
            ProjectSaveData data = Admitted("death");
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, "death", out _));
            ProjectRunOutcome outcome = Outcome(data, false);
            outcome.Items.Add(new ProjectItem { Id = "loot", Name = "战利品", RequiredLevel = 1 });
            outcome.KillExperience = 80;
            outcome.AwardedKillExperience = 80;
            outcome.GoldSpent = 20;
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            outcome.Items[0].Name = "外部修改";
            Assert.AreEqual("战利品", data.Outcome.Items[0].Name);
            Assert.IsFalse(ProjectProfileRules.TryRecoverInterruptedRun(data, "中断"));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.AreEqual(90, data.Stamina);
            Assert.AreEqual(30, data.Gold);
            Assert.AreEqual(80, data.Experience);
            Assert.AreEqual(1, data.Warehouse.Count);
            Assert.AreEqual("death", data.LastSettledRunId);
            Assert.AreEqual(1, data.LastResult.LostItems.Count);
            Assert.AreEqual(1, data.LastResult.RetainedItems.Count);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.AreEqual(1, data.Warehouse.Count);
            Assert.AreEqual(80, data.Experience);
            Assert.IsNull(ProjectSaveData.Validate(data));
        }

        /// <summary>
        /// 撤离携出全部持有物品，结果副本与保存结果互不影响。
        /// </summary>
        [Test]
        public void ExtractionRetainsAllItemsAndCopiesResult()
        {
            ProjectSaveData data = Admitted("extract");
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, "extract", out _));
            ProjectRunOutcome outcome = Outcome(data, true);
            outcome.Items.Add(new ProjectItem { Id = "loot", Name = "战利品", RequiredLevel = 1 });
            outcome.ExtractionExperience = 40;
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.AreEqual(2, data.Warehouse.Count);
            Assert.AreEqual(40, data.Experience);
            ProjectSaveData copy = data.Copy();
            copy.LastResult.RetainedItems[0].Name = "副本修改";
            copy.LastResult.Outcome.Items.Clear();
            Assert.AreEqual("练习短刃", data.LastResult.RetainedItems[0].Name);
            Assert.AreEqual(1, data.LastResult.Outcome.Items.Count);
            Assert.IsTrue(ProjectRunRecordRules.TryAcknowledgeResult(data, "extract", out _));
            Assert.IsTrue(ProjectRunRecordRules.TryAcknowledgeResult(data, "extract", out _));
            Assert.IsTrue(data.LastResult.Viewed);
        }

        /// <summary>
        /// 未知终局、旧局结果和重复物品身份均被拒绝且不改变候选。
        /// </summary>
        [Test]
        public void RejectsUnknownOutcomeAndDuplicateItems()
        {
            ProjectSaveData data = Admitted("current");
            Assert.IsFalse(ProjectRunRecordRules.TryRecordOutcome(data, Outcome(data, true), out _));
            Assert.IsFalse(ProjectRunRecordRules.TryAdmit(data, "current", 2, 123, new List<ProjectItem> { data.EntryItems[0].Copy() }, out _));
            Assert.AreEqual(90, data.Stamina);
            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, "current", out _));
            ProjectRunOutcome old = Outcome(data, true);
            old.RunId = "old";
            Assert.IsFalse(ProjectRunRecordRules.TryRecordOutcome(data, old, out _));
            ProjectRunOutcome duplicate = Outcome(data, true);
            duplicate.Items.Add(new ProjectItem { Id = "duplicate", Name = "重复物品", RequiredLevel = 1 });
            duplicate.Items.Add(duplicate.Items[0].Copy());
            Assert.IsFalse(ProjectRunRecordRules.TryRecordOutcome(data, duplicate, out _));
            Assert.AreEqual(ProjectRunStage.Running, data.Stage);
            Assert.IsNull(data.Outcome);
            Assert.IsFalse(ProjectRunRecordRules.TryAcknowledgeResult(data, "old", out _));
            ProjectRunOutcome accepted = Outcome(data, true);
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, accepted, out _));
            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, accepted.Copy(), out _));
            accepted.GoldSpent = 1;
            Assert.IsFalse(ProjectRunRecordRules.TryRecordOutcome(data, accepted, out _));
        }

        /// <summary>
        /// 旧版未结局入场可按旧策略恢复，旧摘要不生成新奖励。
        /// </summary>
        [Test]
        public void MigratesLegacyActiveRunWithoutInventingOutcome()
        {
            ProjectSaveData old = ProjectSaveData.CreateDefault();
            old.ActiveRunId = "legacy";
            old.ActiveRunEntryCost = 10;
            old.Stamina = 90;
            old.EntryItems.Add(old.Warehouse[0].Copy());
            old.Warehouse.Clear();
            old.LastSettledRunId = "older";
            old.LastRunSummary = "旧摘要";
            string source = JsonUtility.ToJson(old);
            Assert.IsNull(ProjectRunRecordRules.ValidateLegacyJson(source));
            string migrated = ProjectRunRecordRules.MigrateLegacyJson(source);
            ProjectSaveData data = RoundTrip(new ProjectSaveCodec(), new ProjectSaveCodec().Decode(migrated));
            Assert.AreEqual(ProjectRunStage.Running, data.Stage);
            Assert.IsNull(data.Outcome);
            Assert.IsNull(data.LastResult);
            Assert.AreEqual("旧摘要", data.LastRunSummary);
            Assert.IsFalse(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.IsTrue(ProjectProfileRules.TryRecoverInterruptedRun(data, "旧局中断"));
            Assert.AreEqual(100, data.Stamina);
            Assert.AreEqual(1, data.Warehouse.Count);
            Assert.IsNull(ProjectSaveData.Validate(data));
        }

        /// <summary>
        /// 全部持久阶段经历与仓库相同的编码、解码和再次校验。
        /// </summary>
        [Test]
        public void CodecRoundTripPreservesEveryRunStage()
        {
            var codec = new ProjectSaveCodec();
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            data = RoundTrip(codec, data);
            Assert.AreEqual(ProjectRunStage.None, data.Stage);
            Assert.IsNull(data.Outcome);
            Assert.IsNull(data.LastResult);

            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, "roundtrip", 1, 123,
                new List<ProjectItem> { data.Warehouse[0].Copy() }, out _));
            data = RoundTrip(codec, data);
            Assert.AreEqual(ProjectRunStage.Admitted, data.Stage);
            Assert.IsNull(data.Outcome);
            Assert.IsNull(data.LastResult);

            Assert.IsTrue(ProjectRunRecordRules.TryMarkRunning(data, "roundtrip", out _));
            data = RoundTrip(codec, data);
            Assert.AreEqual(ProjectRunStage.Running, data.Stage);
            Assert.IsNull(data.Outcome);

            Assert.IsTrue(ProjectRunRecordRules.TryRecordOutcome(data, Outcome(data, true), out _));
            data = RoundTrip(codec, data);
            Assert.AreEqual(ProjectRunStage.OutcomeRecorded, data.Stage);
            Assert.AreEqual("roundtrip", data.Outcome.RunId);
            Assert.IsNull(data.LastResult);

            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            data = RoundTrip(codec, data);
            Assert.AreEqual(ProjectRunStage.None, data.Stage);
            Assert.IsNull(data.Outcome);
            Assert.AreEqual("roundtrip", data.LastResult.Outcome.RunId);
            Assert.AreEqual(1, data.Warehouse.Count);
        }

        /// <summary>
        /// 旧版无活动局和已结算摘要迁移后不会生成虚假结果。
        /// </summary>
        [Test]
        public void CodecMigratesLegacySettledSummaryWithoutInventingResult()
        {
            ProjectSaveData legacy = ProjectSaveData.CreateDefault();
            legacy.LastSettledRunId = "prior";
            legacy.LastRunSummary = "旧结算摘要";
            string source = JsonUtility.ToJson(legacy);
            SaveMigration migration = ProjectRunRecordRules.CreateMigration();
            Assert.IsNull(migration.ValidateSource(source));
            ProjectSaveData migrated = RoundTrip(new ProjectSaveCodec(),
                new ProjectSaveCodec().Decode(migration.Migrate(source)));
            Assert.AreEqual(ProjectRunStage.None, migrated.Stage);
            Assert.IsNull(migrated.Outcome);
            Assert.IsNull(migrated.LastResult);
            Assert.AreEqual("prior", migrated.LastSettledRunId);
            Assert.AreEqual("旧结算摘要", migrated.LastRunSummary);
        }

        /// <summary>
        /// 广告撤离始终归还携入装备，只带回选中的本关实例，重复结算不重复入库。
        /// </summary>
        [Test]
        public void AdExtractionProtectsEntryAndRetainsOnlySelectedEarnedItems()
        {
            var data = Admitted("ad-extraction");
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, data.ActiveRunId, out _), Is.True);
            var outcome = Outcome(data, false);
            outcome.ExitKind = ProjectRunExitKind.AdExtraction;
            int limit = ProjectRulesConfig.Profile.AdExtractionItemLimit;
            for (int index = 0; index < limit + 2; index++)
            {
                outcome.Items.Add(new ProjectItem { Id = "earned-" + index, Name = "本关物品", RequiredLevel = 1 });
                if (index < limit) outcome.RetainedItemIds.Add("earned-" + index);
            }
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out string error), Is.True, error);
            outcome.RetainedItemIds.Clear();
            Assert.That(data.Outcome.RetainedItemIds.Count, Is.EqualTo(limit));
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(data.LastResult.RetainedItems.Count, Is.EqualTo(limit + 1));
            Assert.That(data.LastResult.LostItems.Count, Is.EqualTo(2));
            Assert.That(data.LastResult.EntryItems.Count, Is.EqualTo(1));
            Assert.That(data.RunHistory[0].Outcome, Is.EqualTo(ProjectRunHistoryOutcome.AdExtraction));
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(data.Warehouse.Count, Is.EqualTo(limit + 1));
            Assert.That(ProjectSaveData.Validate(data), Is.Null);
        }

        /// <summary>
        /// 四种终局统一以携入装备与关卡背包的并集分类，失败不会把装备当成丢失战利品。
        /// </summary>
        /// <param name="exitKind">本次终局类别。</param>
        [TestCase(ProjectRunExitKind.Completed)]
        [TestCase(ProjectRunExitKind.Death)]
        [TestCase(ProjectRunExitKind.Abandoned)]
        [TestCase(ProjectRunExitKind.AdExtraction)]
        public void ClassificationPartitionsEntryAndBag(ProjectRunExitKind exitKind)
        {
            var data = Admitted("partition");
            var outcome = Outcome(data, false);
            outcome.ExitKind = exitKind;
            outcome.Items.Add(new ProjectItem { Id = "earned", Name = "本关物品", RequiredLevel = 1 });
            Assert.That(ProjectRunRecordRules.TryClassifyItems(outcome, data.EntryItems, out var retained, out var lost, out string error), Is.True, error);
            Assert.That(retained.Exists(item => item.Id == data.EntryItems[0].Id), Is.True);
            Assert.That(retained.Count, Is.EqualTo(exitKind == ProjectRunExitKind.Completed ? 2 : 1));
            Assert.That(retained.Count + lost.Count, Is.EqualTo(2));
            retained[0].Name = "副本";
            Assert.That(data.EntryItems[0].Name, Is.EqualTo("练习短刃"));
        }

        /// <summary>
        /// 选取重复、外来、携入身份或超限物品，以及在非广告终局选取，均被原子拒绝。
        /// </summary>
        /// <param name="invalidSelection">非法选取场景。</param>
        [TestCase("duplicate")]
        [TestCase("foreign")]
        [TestCase("entry")]
        [TestCase("limit")]
        [TestCase("non-ad")]
        public void ClassificationRejectsInvalidSelection(string invalidSelection)
        {
            var data = Admitted("selection");
            var outcome = Outcome(data, false);
            outcome.ExitKind = invalidSelection == "non-ad" ? ProjectRunExitKind.Completed : ProjectRunExitKind.AdExtraction;
            int limit = ProjectRulesConfig.Profile.AdExtractionItemLimit;
            for (int index = 0; index <= limit; index++)
                outcome.Items.Add(new ProjectItem { Id = "earned-" + index, Name = "本关物品", RequiredLevel = 1 });
            if (invalidSelection == "duplicate") outcome.RetainedItemIds.AddRange(new[] { "earned-0", "earned-0" });
            if (invalidSelection == "foreign") outcome.RetainedItemIds.Add("foreign");
            if (invalidSelection == "entry") outcome.RetainedItemIds.Add(data.EntryItems[0].Id);
            if (invalidSelection == "non-ad") outcome.RetainedItemIds.Add("earned-0");
            if (invalidSelection == "limit") foreach (var item in outcome.Items) outcome.RetainedItemIds.Add(item.Id);
            Assert.That(ProjectRunRecordRules.TryClassifyItems(outcome, data.EntryItems, out var retained, out var lost, out string error), Is.False);
            Assert.That(retained, Is.Empty);
            Assert.That(lost, Is.Empty);
            Assert.That(error, Is.Not.Empty);
        }

        /// <summary>
        /// 关卡背包不得混入携入身份或穿戴物品，携入证据也不得包含重复实例。
        /// </summary>
        /// <param name="invalidItems">非法物品场景。</param>
        [TestCase("entry-in-bag")]
        [TestCase("equipped-bag")]
        [TestCase("duplicate-entry")]
        public void ClassificationRejectsOwnershipConflicts(string invalidItems)
        {
            var data = Admitted("ownership");
            var outcome = Outcome(data, true);
            if (invalidItems == "entry-in-bag")
            {
                var item = data.EntryItems[0].Copy();
                item.Equipped = false;
                outcome.Items.Add(item);
            }
            if (invalidItems == "equipped-bag")
                outcome.Items.Add(new ProjectItem { Id = "earned", Name = "本关装备", RequiredLevel = 1, Equipped = true });
            if (invalidItems == "duplicate-entry") data.EntryItems.Add(data.EntryItems[0].Copy());
            Assert.That(ProjectRunRecordRules.TryClassifyItems(outcome, data.EntryItems, out _, out _, out _), Is.False);
        }

        /// <summary>
        /// 永久击杀经验只补发水位之后的差额，战绩仍保留整局累计所得。
        /// </summary>
        [Test]
        public void SettlementAwardsOnlyUncommittedPermanentExperience()
        {
            var data = Admitted("experience-watermark");
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, data.ActiveRunId, out _), Is.True);
            ProjectProfileRules.AddExperience(data, 80);
            data.ActiveRunAwardedKillExperience = 80;
            var expected = data.Copy();
            ProjectProfileRules.AddExperience(expected, 80);
            var outcome = Outcome(data, true);
            outcome.KillExperience = 120;
            outcome.AwardedKillExperience = 120;
            outcome.ExtractionExperience = 40;
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(data.Level, Is.EqualTo(expected.Level));
            Assert.That(data.Experience, Is.EqualTo(expected.Experience));
            Assert.That(data.SkillPoints, Is.EqualTo(expected.SkillPoints));
            Assert.That(data.ActiveRunAwardedKillExperience, Is.Zero);
            Assert.That(data.RunHistory[0].KillExperience, Is.EqualTo(120));
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(data.Experience, Is.EqualTo(expected.Experience));
        }

        /// <summary>
        /// 经验水位超过本局累计或统计到账额不一致时，终局不能进入保存阶段。
        /// </summary>
        [Test]
        public void OutcomeRejectsInvalidExperienceWatermark()
        {
            var data = Admitted("invalid-watermark");
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, data.ActiveRunId, out _), Is.True);
            var outcome = Outcome(data, false);
            data.ActiveRunAwardedKillExperience = 1;
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _), Is.False);
            data.ActiveRunAwardedKillExperience = 0;
            outcome.KillExperience = 1;
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out _), Is.False);
            Assert.That(data.Stage, Is.EqualTo(ProjectRunStage.Running));
            Assert.That(data.Outcome, Is.Null);
        }

        /// <summary>
        /// 结算回执保存携入证据，篡改带回分区即使物品总数不变也会被拒绝。
        /// </summary>
        [Test]
        public void ResultValidationRechecksProtectedEntryAndEarnedPartition()
        {
            var data = Admitted("receipt-partition");
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, data.ActiveRunId, out _), Is.True);
            var outcome = Outcome(data, false);
            outcome.Items.Add(new ProjectItem { Id = "earned", Name = "本关物品", RequiredLevel = 1 });
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(data, outcome, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(data, out error), Is.True, error);
            Assert.That(ProjectSaveData.Validate(data), Is.Null);
            var copied = data.Copy();
            copied.LastResult.EntryItems[0].Name = "副本";
            Assert.That(data.LastResult.EntryItems[0].Name, Is.EqualTo("练习短刃"));
            data.LastResult.RetainedItems.Add(data.LastResult.LostItems[0]);
            data.LastResult.LostItems.Clear();
            Assert.That(ProjectSaveData.Validate(data), Is.Not.Null);
        }

        /// <summary>
        /// 准入只收关卡入场费，取消准入退回实际扣费，已开始的局不能按取消入口退款。
        /// </summary>
        [Test]
        public void AdmissionChargesOnlyEntryCostAndRefundsOnlyBeforeRunning()
        {
            var data = Admitted("admission-cost");
            Assert.That(data.ActiveRunVersion, Is.EqualTo(3));
            Assert.That(data.ActiveRunEntryCost, Is.EqualTo(10));
            Assert.That(data.Stamina, Is.EqualTo(90));
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(data, "取消准入"), Is.True);
            Assert.That(data.Stamina, Is.EqualTo(100));
            Assert.That(data.ActiveRunAwardedKillExperience, Is.Zero);
            data = Admitted("running-cost");
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, data.ActiveRunId, out _), Is.True);
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(data, "不能取消"), Is.False);
            Assert.That(data.Stamina, Is.EqualTo(90));
        }

        /// <summary>
        /// 余额恰好十点可首次入场，少一点原子拒绝，重复准入和账号重读均不重复收费。
        /// </summary>
        /// <param name="stamina">准入前实际体力余额。</param>
        /// <param name="accepted">本次余额是否应允许准入。</param>
        [TestCase(9, false)]
        [TestCase(10, true)]
        public void AdmissionUsesTenPointBoundaryAndRetriedIdentityIsFree(int stamina, bool accepted)
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            data.Stamina = stamina;
            var entry = data.Warehouse.FindAll(item => item.Equipped).ConvertAll(item => item.Copy());
            string original = JsonUtility.ToJson(data);
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "ten-point-entry", 1, 71, entry, out string error), Is.EqualTo(accepted), error);
            if (!accepted)
            {
                Assert.That(JsonUtility.ToJson(data), Is.EqualTo(original));
                return;
            }
            Assert.That(data.Stamina, Is.Zero);
            Assert.That(data.ActiveRunEntryCost, Is.EqualTo(10));
            Assert.That(data.ActiveRoomAdmissionSerial, Is.EqualTo(1));
            data = RoundTrip(new ProjectSaveCodec(), data);
            string admitted = JsonUtility.ToJson(data);
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "ten-point-entry", 1, 71, entry, out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(admitted));
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "ten-point-entry", out error), Is.True, error);
            string running = JsonUtility.ToJson(data);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "ten-point-entry", out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(running));
            Assert.That(data.Stamina, Is.Zero);
        }

        /// <summary>
        /// 旧活动记录可以原样保留，但不得按新规则结算或将旧结果猜测为通关。
        /// </summary>
        [Test]
        public void LegacyRecordsRemainBlockedUntilMigration()
        {
            var legacy = Admitted("legacy-v2");
            legacy.ActiveRunVersion = 2;
            legacy.ActiveRulesVersion = 1;
            legacy.Stage = ProjectRunStage.Running;
            Assert.That(ProjectSaveData.Validate(legacy), Is.Null);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(legacy, legacy.ActiveRunId, out _), Is.False);
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(legacy, Outcome(legacy, true), out _), Is.False);
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(legacy, "旧运行中"), Is.False);
            var current = Admitted("new-result");
            Assert.That(ProjectRunRecordRules.TryMarkRunning(current, current.ActiveRunId, out _), Is.True);
            Assert.That(ProjectRunRecordRules.TryRecordOutcome(current, Outcome(current, true), out _), Is.True);
            Assert.That(ProjectRunRecordRules.TrySettleOutcome(current, out _), Is.True);
            current.LastResult.Outcome.RulesVersion = 1;
            Assert.That(ProjectSaveData.Validate(current), Is.Not.Null);
        }

        /// <summary>
        /// 模拟框架写入前的 JSON 往返校验。
        /// </summary>
        /// <param name="codec">项目存档编解码器。</param>
        /// <param name="data">待写入存档。</param>
        /// <returns>通过再次校验的独立存档。</returns>
        private static ProjectSaveData RoundTrip(ProjectSaveCodec codec, ProjectSaveData data)
        {
            Assert.IsNull(ProjectSaveData.Validate(data));
            ProjectSaveData decoded = codec.Decode(codec.Encode(data));
            Assert.IsNull(ProjectSaveData.Validate(decoded));
            Assert.AreNotSame(data, decoded);
            return decoded;
        }

        /// <summary>
        /// 创建已扣费且携入唯一装备的候选。
        /// </summary>
        /// <param name="runId">单局身份。</param>
        /// <returns>入场阶段的候选。</returns>
        private static ProjectSaveData Admitted(string runId)
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.IsTrue(ProjectRunRecordRules.TryAdmit(data, runId, 1, 123, new List<ProjectItem> { data.Warehouse[0].Copy() }, out _));
            return data;
        }

        /// <summary>
        /// 创建当前单局的冻结事实输入。
        /// </summary>
        /// <param name="data">当前入场候选。</param>
        /// <param name="extracted">是否正常撤离。</param>
        /// <returns>独立终局事实。</returns>
        private static ProjectRunOutcome Outcome(ProjectSaveData data, bool extracted) => new()
        {
            RunId = data.ActiveRunId, LevelId = data.ActiveLevelId, Seed = data.ActiveSeed,
            RulesVersion = ProjectRunRecordRules.RulesVersion, Tick = 5, ElapsedSeconds = 1,
            ExitKind = extracted ? ProjectRunExitKind.Completed : ProjectRunExitKind.Death
        };
    }
}
