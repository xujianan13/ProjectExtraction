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
        /// 死亡损失全部持有物品，已确认的经验、入场扣费和金币支出不退回。
        /// </summary>
        [Test]
        public void DeathLosesAllItemsAndDoesNotRefund()
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
            Assert.AreEqual("练习短刃", data.Outcome.Items[0].Name);
            Assert.IsFalse(ProjectProfileRules.TryRecoverInterruptedRun(data, "中断"));
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.AreEqual(90, data.Stamina);
            Assert.AreEqual(30, data.Gold);
            Assert.AreEqual(80, data.Experience);
            Assert.IsEmpty(data.Warehouse);
            Assert.AreEqual("death", data.LastSettledRunId);
            Assert.AreEqual(2, data.LastResult.LostItems.Count);
            Assert.IsEmpty(data.LastResult.RetainedItems);
            Assert.IsTrue(ProjectRunRecordRules.TrySettleOutcome(data, out _));
            Assert.IsEmpty(data.Warehouse);
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
            Assert.AreEqual(2, data.LastResult.Outcome.Items.Count);
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
            Extracted = extracted, Items = new List<ProjectItem> { data.EntryItems[0].Copy() }
        };
    }
}
