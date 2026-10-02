using GameLogic.Project;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证局外商店报价、购买去重和旧存档兼容。
    /// </summary>
    public sealed class ProjectHomeShopTests
    {
        /// <summary>
        /// 一次候选同时更新金币、唯一物品、库存与修订号，旧报价不能再次扣款。
        /// </summary>
        [Test]
        public void PurchaseCommitsWholeCandidateAndRejectsStaleQuote()
        {
            ProjectSaveData original = ProjectSaveData.CreateDefault();
            ProjectSaveData candidate = original.Copy();
            ProjectHomeShopOffer offer = Find(candidate, "basic_blade");
            Assert.IsTrue(offer.CanPurchase);
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, offer.Id, offer.PurchaseRevision, out string error), error);
            Assert.AreEqual(original.Gold - offer.PriceGold, candidate.Gold);
            Assert.AreEqual(original.Diamonds, candidate.Diamonds);
            Assert.AreEqual(original.Warehouse.Count + 1, candidate.Warehouse.Count);
            Assert.AreEqual(1, candidate.HomeShopPurchaseRevision);
            Assert.AreEqual(1, candidate.HomeShopPurchases[0].Count);
            Assert.IsFalse(candidate.Warehouse[candidate.Warehouse.Count - 1].Equipped);
            Assert.AreNotEqual(original.Warehouse[0].Id, candidate.Warehouse[candidate.Warehouse.Count - 1].Id);
            Assert.IsNull(ProjectSaveData.Validate(candidate));
            int gold = candidate.Gold;
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(candidate, offer.Id, offer.PurchaseRevision, out error));
            Assert.AreEqual(gold, candidate.Gold);
            Assert.AreEqual(1, candidate.HomeShopPurchases[0].Count);
            Assert.AreEqual(0, original.HomeShopPurchaseRevision);
            Assert.AreEqual(0, original.HomeShopPurchases.Count);
        }

        /// <summary>
        /// 同一固定商品重复购买属性相同，但每次物品身份不同。
        /// </summary>
        [Test]
        public void FixedOfferKeepsStatsAndCreatesDistinctInstances()
        {
            ProjectSaveData candidate = ProjectSaveData.CreateDefault();
            candidate.Gold = 200;
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, "basic_blade", 0, out string error), error);
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, "basic_blade", 1, out error), error);
            ProjectItem first = candidate.Warehouse[1];
            ProjectItem second = candidate.Warehouse[2];
            Assert.AreNotEqual(first.Id, second.Id);
            Assert.AreEqual(first.DefinitionId, second.DefinitionId);
            Assert.AreEqual(first.Damage, second.Damage);
            Assert.AreEqual(first.Range, second.Range);
            Assert.AreEqual(first.SellGold, second.SellGold);
            Assert.IsNull(ProjectSaveData.Validate(candidate));
        }

        /// <summary>
        /// 等级、余额、局内状态和有限库存均拒绝购买且不改动货币。
        /// </summary>
        [Test]
        public void GatesAndFiniteStockRejectWithoutMutatingWallet()
        {
            ProjectSaveData candidate = ProjectSaveData.CreateDefault();
            int diamonds = candidate.Diamonds;
            int gold = candidate.Gold;
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(candidate, "fine_blade", 0, out _));
            Assert.AreEqual(gold, candidate.Gold);
            candidate.Gold = 0;
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(candidate, "basic_blade", 0, out _));
            Assert.AreEqual(diamonds, candidate.Diamonds);
            candidate.Gold = 200;
            candidate.ActiveRunId = "running";
            candidate.Stage = ProjectRunStage.Running;
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(candidate, "basic_blade", 0, out _));
            candidate.ActiveRunId = "";
            candidate.Stage = ProjectRunStage.None;
            candidate.Level = 3;
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, "fine_blade", 0, out _));
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, "fine_blade", 1, out _));
            Assert.AreEqual(0, Find(candidate, "fine_blade").StockRemaining);
            int after = candidate.Gold;
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(candidate, "fine_blade", 2, out _));
            Assert.AreEqual(after, candidate.Gold);
            Assert.AreEqual(diamonds, candidate.Diamonds);
        }

        /// <summary>
        /// 保存并重读后购买次数仍受库存约束，旧修订号不会再次扣款。
        /// </summary>
        [Test]
        public void RoundTripPreservesPurchaseRevisionAndStock()
        {
            ProjectSaveData candidate = ProjectSaveData.CreateDefault();
            candidate.Level = 3;
            candidate.Gold = 200;
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, "fine_blade", 0, out _));
            string json = new ProjectSaveCodec().Encode(candidate);
            ProjectSaveData reloaded = new ProjectSaveCodec().Decode(json);
            Assert.IsNull(ProjectSaveData.Validate(reloaded));
            Assert.AreEqual(1, reloaded.HomeShopPurchaseRevision);
            Assert.AreEqual(1, reloaded.HomeShopPurchases[0].Count);
            Assert.AreEqual(1, Find(reloaded, "fine_blade").StockRemaining);
            int gold = reloaded.Gold;
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(reloaded, "fine_blade", 0, out _));
            Assert.AreEqual(gold, reloaded.Gold);
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(reloaded, "fine_blade", 1, out _));
            Assert.AreEqual(0, Find(reloaded, "fine_blade").StockRemaining);
        }

        /// <summary>
        /// 报价在购买修订号或累计次数溢出时显示不可购买。
        /// </summary>
        [Test]
        public void OverflowLimitsAreVisibleInOffer()
        {
            ProjectSaveData candidate = ProjectSaveData.CreateDefault();
            candidate.HomeShopPurchaseRevision = long.MaxValue;
            Assert.IsFalse(Find(candidate, "basic_blade").CanPurchase);
            candidate.HomeShopPurchaseRevision = 0;
            candidate.HomeShopPurchases.Add(new ProjectHomeShopPurchase { Id = "basic_blade", Count = int.MaxValue });
            Assert.IsFalse(Find(candidate, "basic_blade").CanPurchase);
            Assert.IsFalse(ProjectHomeShopRules.TryPurchase(candidate, "basic_blade", 0, out _));
            Assert.IsNull(ProjectSaveData.Validate(candidate));
        }

        /// <summary>
        /// 候选副本隔离购买记录，旧 JSON 缺少商店字段时按零次购买打开。
        /// </summary>
        [Test]
        public void SaveCopyAndLegacyJsonKeepIndependentPurchaseState()
        {
            ProjectSaveData original = ProjectSaveData.CreateDefault();
            ProjectSaveData candidate = original.Copy();
            Assert.IsTrue(ProjectHomeShopRules.TryPurchase(candidate, "basic_blade", 0, out _));
            ProjectSaveData frozen = candidate.Copy();
            candidate.HomeShopPurchases[0].Count = 3;
            Assert.AreEqual(1, frozen.HomeShopPurchases[0].Count);
            Assert.AreEqual(0, original.HomeShopPurchases.Count);

            string legacy = JsonUtility.ToJson(original)
                .Replace("\"HomeShopPurchaseRevision\":0,", "")
                .Replace("\"HomeShopPurchases\":[],", "");
            Assert.IsFalse(legacy.Contains("HomeShop"));
            ProjectSaveData decoded = new ProjectSaveCodec().Decode(legacy);
            Assert.IsNotNull(decoded);
            Assert.AreEqual(0, decoded.HomeShopPurchaseRevision);
            Assert.IsNotNull(decoded.HomeShopPurchases);
            Assert.AreEqual(0, decoded.HomeShopPurchases.Count);
            Assert.IsNull(ProjectSaveData.Validate(decoded));
            decoded.HomeShopPurchases.Add(new ProjectHomeShopPurchase { Id = "retired_shop", Count = 2 });
            Assert.IsNull(ProjectSaveData.Validate(decoded));
            Assert.AreEqual("retired_shop", decoded.Copy().HomeShopPurchases[0].Id);
        }

        /// <summary>
        /// 根据身份找到一个独立报价。
        /// </summary>
        /// <param name="profile">用于报价的存档。</param>
        /// <param name="id">商品身份。</param>
        /// <returns>指定报价。</returns>
        private static ProjectHomeShopOffer Find(ProjectSaveData profile, string id)
        {
            foreach (ProjectHomeShopOffer offer in ProjectHomeShopRules.CreateOffers(profile, true))
                if (offer.Id == id) return offer;
            Assert.Fail("测试商品缺失：" + id);
            return null;
        }
    }
}
