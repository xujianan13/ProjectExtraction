using System;
using System.Collections.Generic;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
        /// 验证历史数据补偿、活动局保护与保存修订号屏障。
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
        /// V3 已运行局必须恢复原身份，不能通过中断补偿退体力或携入装备。
        /// </summary>
        [Test]
        public void V3RunningRunCannotRefundOrClearItsIdentity()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            int stamina = data.Stamina;
            var entry = new List<ProjectItem> { data.Warehouse[0].Copy() };
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "v3-running", 1, 19, entry, out string error), Is.True, error);
            Assert.That(data.ActiveRunVersion, Is.EqualTo(3));
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "v3-running", out error), Is.True, error);
            string running = JsonUtility.ToJson(data);
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(data, "运行日志恢复失败"), Is.False);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(running));
            Assert.That(data.Stamina, Is.EqualTo(stamina - PrototypeRules.EntryCost));
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "new-run", 1, 20, entry, out error), Is.False);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(running));
        }

        /// <summary>
        /// 尚未运行的新准入局可补偿一次，清理版本字段并保持原提交副本。
        /// </summary>
        [Test]
        public void V3AdmittedCancellationCompensatesExactlyOnce()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            int stamina = data.Stamina;
            string itemId = data.Warehouse[0].Id;
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "v3-admitted", 1, 21,
                new[] { data.Warehouse[0].Copy() }, out string error), Is.True, error);
            Assert.That(data.ActiveRunEntryCost, Is.EqualTo(10));
            Assert.That(data.Stamina, Is.EqualTo(stamina - 10));
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(data, "取消首次入场"), Is.True);
            Assert.That(data.Stamina, Is.EqualTo(stamina));
            Assert.That(data.Warehouse.FindAll(item => item.Id == itemId).Count, Is.EqualTo(1));
            Assert.That(data.ActiveRunVersion, Is.Zero);
            Assert.That(data.Stage, Is.EqualTo(ProjectRunStage.None));
            string compensated = JsonUtility.ToJson(data);
            Assert.That(ProjectProfileRules.TryRecoverInterruptedRun(data, "重复取消"), Is.False);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(compensated));
        }

        /// <summary>
        /// 装备生命随机值写入独立实例，负值不能进存档。
        /// </summary>
        [Test]
        public void EquipmentHealthBonusIsValidAndPersistedByCopy()
        {
            var random = new System.Random(7);
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
