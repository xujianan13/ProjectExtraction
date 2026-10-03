using System.Collections.Generic;
using System.Linq;
using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证仓库锁定、出售整批拒绝及排序和旧档兼容。
    /// </summary>
    public sealed class ProjectWarehouseRulesTests
    {
        /// <summary>
        /// 锁定标记随存档往返，解锁后允许出售，已锁定仍可标记穿戴。
        /// </summary>
        [Test]
        public void LockRoundTripAndSelling()
        {
            ProjectSaveData data = CreateSaleData();
            string id = data.Warehouse[0].Id;
            Assert.IsTrue(ProjectWarehouseRules.TrySetLocked(data, id, true, out string error), error);
            data.Warehouse[0].Equipped = true;
            ProjectSaveData loaded = new ProjectSaveCodec().Decode(new ProjectSaveCodec().Encode(data));
            Assert.IsTrue(loaded.Warehouse[0].Locked);
            Assert.IsTrue(loaded.Warehouse[0].Equipped);
            loaded.Warehouse[0].Equipped = false;
            Assert.IsFalse(ProjectWarehouseRules.TrySell(loaded, new[] { id }, out _, out _));
            Assert.IsTrue(ProjectWarehouseRules.TrySetLocked(loaded, id, false, out error), error);
            Assert.IsTrue(ProjectWarehouseRules.TrySell(loaded, new[] { id }, out int gold, out error), error);
            Assert.AreEqual(11, gold);
        }

        /// <summary>
        /// 旧物品 JSON 缺少 Locked 时保持默认未锁定。
        /// </summary>
        [Test]
        public void LegacyJsonDefaultsToUnlocked()
        {
            ProjectSaveData data = CreateSaleData();
            string json = new ProjectSaveCodec().Encode(data);
            Assert.IsTrue(json.Contains("\"Locked\":false"));
            ProjectSaveData loaded = new ProjectSaveCodec().Decode(json.Replace("\"Locked\":false,", ""));
            Assert.IsFalse(loaded.Warehouse[0].Locked);
            Assert.IsNull(ProjectSaveData.Validate(loaded));
        }

        /// <summary>
        /// 重复、缺失、穿戴、锁定及溢出一律整批拒绝，不改变原候选。
        /// </summary>
        [Test]
        public void InvalidBatchNeverPartiallyMutatesCandidate()
        {
            ProjectSaveData data = CreateSaleData();
            string first = data.Warehouse[0].Id;
            string second = data.Warehouse[1].Id;
            int gold = data.Gold;
            AssertRejected(data, new[] { first, first }, gold);
            AssertRejected(data, new[] { first, "missing" }, gold);
            AssertRejected(data, new[] { first, "" }, gold);
            data.Warehouse[1].Equipped = true;
            AssertRejected(data, new[] { first, second }, gold);
            data.Warehouse[1].Equipped = false;
            data.Warehouse[1].Locked = true;
            AssertRejected(data, new[] { first, second }, gold);
            data.Warehouse[1].Locked = false;
            data.Gold = int.MaxValue - 10;
            AssertRejected(data, new[] { first, second }, data.Gold);
            data.Gold = gold;
            Assert.IsTrue(ProjectWarehouseRules.TrySell(data, new[] { second, first }, out int total, out string error), error);
            Assert.AreEqual(24, total);
            Assert.AreEqual(gold + total, data.Gold);
            Assert.IsFalse(data.Warehouse.Any(item => item.Id == first || item.Id == second));
            Assert.IsFalse(ProjectWarehouseRules.TrySell(data, new[] { second, first }, out _, out _));
            Assert.AreEqual(gold + total, data.Gold);
        }

        /// <summary>
        /// 四种顺序均保留库存原顺序作为相同数值的稳定次序。
        /// </summary>
        [Test]
        public void SortingUsesRequestedFieldAndStableTies()
        {
            ProjectSaveData data = CreateSaleData();
            ProjectItem first = data.Warehouse[0];
            ProjectItem second = data.Warehouse[1];
            first.Quality = ProjectItemQuality.Common;
            second.Quality = ProjectItemQuality.Epic;
            first.RequiredLevel = 4;
            second.RequiredLevel = 1;
            Assert.AreEqual(first.Id, ProjectWarehouseRules.Sort(data.Warehouse, ProjectWarehouseSort.Default)[0].Id);
            Assert.AreEqual(second.Id, ProjectWarehouseRules.Sort(data.Warehouse, ProjectWarehouseSort.Quality)[0].Id);
            Assert.AreEqual(first.Id, ProjectWarehouseRules.Sort(data.Warehouse, ProjectWarehouseSort.Level)[0].Id);
            Assert.AreEqual(second.Id, ProjectWarehouseRules.Sort(data.Warehouse, ProjectWarehouseSort.SellGold)[0].Id);
            second.SellGold = first.SellGold;
            Assert.AreEqual(first.Id, ProjectWarehouseRules.Sort(data.Warehouse, ProjectWarehouseSort.SellGold)[0].Id);
        }

        /// <summary>
        /// 创建含两件可出售物品的独立存档。
        /// </summary>
        /// <returns>待测存档。</returns>
        private static ProjectSaveData CreateSaleData()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            ProjectItem first = data.Warehouse[0];
            first.Id = "sale-a";
            first.Equipped = false;
            first.SellGold = 11;
            ProjectItem second = first.Copy();
            second.Id = "sale-b";
            second.SellGold = 13;
            data.Warehouse.Add(second);
            return data;
        }

        /// <summary>
        /// 确认整批拒绝时库存与金币不变。
        /// </summary>
        /// <param name="data">待测存档。</param>
        /// <param name="ids">出售实例身份。</param>
        /// <param name="gold">交易前金币。</param>
        private static void AssertRejected(ProjectSaveData data, IReadOnlyList<string> ids, int gold)
        {
            string[] before = data.Warehouse.Select(item => item.Id).ToArray();
            Assert.IsFalse(ProjectWarehouseRules.TrySell(data, ids, out _, out _));
            Assert.AreEqual(gold, data.Gold);
            CollectionAssert.AreEqual(before, data.Warehouse.Select(item => item.Id).ToArray());
        }
    }
}
