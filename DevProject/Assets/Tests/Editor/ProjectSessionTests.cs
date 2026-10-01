using System;
using GameLogic.Project;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证单局托管回退与保存修订号屏障。
    /// </summary>
    public sealed class ProjectSessionTests
    {
        /// <summary>
        /// 未成功标脏时，旧修订号的成功回执不能确认新候选。
        /// </summary>
        [Test]
        public void ReceiptRequiresMarkedCandidateRevision()
        {
            Assert.IsFalse(ProjectProfileRules.CanAcceptSaveReceipt(false, 0, 8, 8, true));
            Assert.IsFalse(ProjectProfileRules.CanAcceptSaveReceipt(true, 9, 8, 8, true));
            Assert.IsFalse(ProjectProfileRules.CanAcceptSaveReceipt(true, 9, 9, 8, true));
            Assert.IsFalse(ProjectProfileRules.CanAcceptSaveReceipt(true, 9, 9, 9, false));
            Assert.IsTrue(ProjectProfileRules.CanAcceptSaveReceipt(true, 9, 9, 9, true));
        }

        /// <summary>
        /// 异常回退只退原入场资源，不能重复退款，也不把未结算战利品写入仓库。
        /// </summary>
        [Test]
        public void InterruptedRunRefundsOriginalEntryOnlyOnce()
        {
            ProjectSaveData committed = ProjectSaveData.CreateDefault();
            ProjectItem original = committed.Warehouse[0].Copy();
            committed.Warehouse.Clear();
            committed.Stamina = 90;
            committed.ActiveRunId = "run-1";
            committed.Stage = ProjectRunStage.Running;
            committed.ActiveRunEntryCost = 10;
            committed.EntryItems.Add(original);
            Assert.IsNull(ProjectSaveData.Validate(committed));

            ProjectSaveData candidate = committed.Copy();
            var unsettledLoot = new ProjectItem { Id = "loot", Name = "临时战利品", Type = ProjectItemType.Item, RequiredLevel = 1 };
            Assert.IsTrue(ProjectProfileRules.TryRecoverInterruptedRun(candidate, "战斗故障"));
            Assert.AreEqual(100, candidate.Stamina);
            Assert.AreEqual(1, candidate.Warehouse.Count);
            Assert.AreEqual(original.Id, candidate.Warehouse[0].Id);
            Assert.IsFalse(candidate.Warehouse.Exists(item => item.Id == unsettledLoot.Id));
            Assert.AreEqual(0, candidate.EntryItems.Count);
            Assert.AreEqual("", candidate.ActiveRunId);
            Assert.AreEqual(0, candidate.ActiveRunEntryCost);
            Assert.AreEqual("run-1", candidate.LastSettledRunId);
            Assert.IsNull(ProjectSaveData.Validate(candidate));
            Assert.IsFalse(ProjectProfileRules.TryRecoverInterruptedRun(candidate, "重复调用"));
            Assert.AreEqual(100, candidate.Stamina);

            Assert.AreEqual(90, committed.Stamina);
            Assert.AreEqual(0, committed.Warehouse.Count);
            Assert.AreEqual("run-1", committed.ActiveRunId);
        }

        /// <summary>
        /// 旧存档没有记录扣费字段时，按原型入场费用回退。
        /// </summary>
        [Test]
        public void LegacyActiveRunUsesPrototypeEntryCost()
        {
            ProjectSaveData candidate = ProjectSaveData.CreateDefault();
            candidate.Warehouse.Clear();
            candidate.EntryItems.Add(new ProjectItem { Id = "entry", Name = "旧入场装备", Type = ProjectItemType.Weapon, RequiredLevel = 1 });
            candidate.ActiveRunId = "legacy";
            candidate.Stage = ProjectRunStage.Running;
            candidate.Stamina = 80;
            Assert.IsTrue(ProjectProfileRules.TryRecoverInterruptedRun(candidate, ""));
            Assert.AreEqual(80 + PrototypeRules.EntryCost, candidate.Stamina);
            Assert.IsNull(ProjectSaveData.Validate(candidate));
        }

        /// <summary>
        /// 装备生命随机值写入独立实例，负值不能进存档。
        /// </summary>
        [Test]
        public void EquipmentHealthBonusIsValidAndPersistedByCopy()
        {
            var random = new Random(7);
            ProjectItem equipment = null;
            for (int i = 0; i < 20 && equipment == null; i++)
            {
                ProjectItem item = PrototypeRules.CreateLoot(random, 2);
                if (ProjectEquipmentRules.IsArmor(item.Type)) equipment = item;
            }
            Assert.IsNotNull(equipment);
            Assert.That(equipment.BonusHealth, Is.InRange(11, 16));
            var data = ProjectSaveData.CreateDefault();
            data.Warehouse.Add(equipment);
            Assert.AreEqual(equipment.BonusHealth, data.Copy().Warehouse[1].BonusHealth);
            Assert.AreEqual(equipment.BonusHealth, data.Copy().Warehouse[1].BaseAttributes[0].Value);
            equipment.BonusHealth = -1;
            Assert.IsNotNull(ProjectSaveData.Validate(data));
        }
    }
}
