using System;
using System.Collections.Generic;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 单个商品在当前存档中已购买的总数；下架商品的记录仍保留。
    /// </summary>
    [Serializable]
    public sealed class ProjectHomeShopPurchase
    {
        public string Id;
        public int Count;

        /// <summary>
        /// 创建独立购买记录。
        /// </summary>
        /// <returns>独立购买记录。</returns>
        public ProjectHomeShopPurchase Copy() => (ProjectHomeShopPurchase)MemberwiseClone();
    }

    /// <summary>
    /// 局外商店向界面提供的独立报价快照。
    /// </summary>
    public sealed class ProjectHomeShopOffer
    {
        private readonly ProjectItem item;

        public string Id { get; }
        public string Name { get; }
        public int RequiredLevel { get; }
        public int PriceGold { get; }
        public int StockRemaining { get; }
        public ProjectItem Item => item?.Copy();
        public long PurchaseRevision { get; }
        public string UnavailableReason { get; }
        public bool CanPurchase => string.IsNullOrEmpty(UnavailableReason);

        /// <summary>
        /// 创建不可回写存档的商店报价。
        /// </summary>
        /// <param name="id">商品身份。</param>
        /// <param name="name">商品名称。</param>
        /// <param name="requiredLevel">购买等级门槛。</param>
        /// <param name="priceGold">金币价格。</param>
        /// <param name="stockRemaining">剩余库存，负一表示不限量。</param>
        /// <param name="item">独立物品预览。</param>
        /// <param name="purchaseRevision">当前商店购买修订号。</param>
        /// <param name="unavailableReason">不可购买原因。</param>
        public ProjectHomeShopOffer(string id, string name, int requiredLevel, int priceGold, int stockRemaining,
            ProjectItem item, long purchaseRevision, string unavailableReason)
        {
            Id = id;
            Name = name;
            RequiredLevel = requiredLevel;
            PriceGold = priceGold;
            StockRemaining = stockRemaining;
            this.item = item?.Copy();
            PurchaseRevision = purchaseRevision;
            UnavailableReason = unavailableReason;
        }
    }

    /// <summary>
    /// 局外商店的报价计算和存档候选变更规则。
    /// </summary>
    public static class ProjectHomeShopRules
    {
        /// <summary>
        /// 按当前已提交存档生成全部报价。
        /// </summary>
        /// <param name="profile">当前存档。</param>
        /// <param name="canEdit">当前是否允许局外事务。</param>
        /// <returns>商品报价独立快照。</returns>
        public static IReadOnlyList<ProjectHomeShopOffer> CreateOffers(ProjectSaveData profile, bool canEdit)
        {
            var offers = new List<ProjectHomeShopOffer>();
            if (profile == null) return offers;
            foreach (CfgHomeShopRow row in ProjectHomeShopConfig.All()) offers.Add(CreateOffer(profile, row, canEdit));
            return offers;
        }

        /// <summary>
        /// 在独立候选中一次性扣金币、增加物品并写入购买版本和累计库存。
        /// </summary>
        /// <param name="candidate">尚未公开的事务候选。</param>
        /// <param name="offerId">商品身份。</param>
        /// <param name="expectedRevision">界面展示的购买修订号。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>候选是否已完成购买变更。</returns>
        public static bool TryPurchase(ProjectSaveData candidate, string offerId, long expectedRevision, out string error)
        {
            error = "商店商品或报价无效。";
            if (candidate == null || string.IsNullOrWhiteSpace(offerId) || candidate.HomeShopPurchases == null) return false;
            if (!string.IsNullOrEmpty(candidate.ActiveRunId) || candidate.Stage != ProjectRunStage.None)
            { error = "局内不能购买。"; return false; }
            if (expectedRevision != candidate.HomeShopPurchaseRevision || expectedRevision < 0 || expectedRevision == long.MaxValue)
            { error = "商品报价已变化，请刷新后重试。"; return false; }
            CfgHomeShopRow row = ProjectHomeShopConfig.Get(offerId);
            if (row == null) return false;
            if (candidate.Level < row.RequiredLevel) { error = "角色等级不足。"; return false; }
            int purchased = Purchased(candidate, row.ID);
            if (row.StockLimit != -1 && purchased >= row.StockLimit) { error = "商品已售罄。"; return false; }
            if (purchased == int.MaxValue) { error = "商品购买次数已达到上限。"; return false; }
            if (candidate.Gold < row.PriceGold) { error = "金币不足。"; return false; }
            ProjectItem item = Generate(row);
            if (item == null || ProjectEquipmentRules.ValidateItem(item) != null ||
                candidate.Warehouse.Exists(existing => existing.Id == item.Id))
            { error = "商品配置或物品生成失败。"; return false; }
            item.Equipped = false;
            ProjectHomeShopPurchase record = candidate.HomeShopPurchases.Find(value => value.Id == row.ID);
            if (record == null)
            {
                record = new ProjectHomeShopPurchase { Id = row.ID };
                candidate.HomeShopPurchases.Add(record);
            }
            candidate.Gold -= row.PriceGold;
            candidate.Warehouse.Add(item);
            record.Count++;
            candidate.HomeShopPurchaseRevision++;
            error = null;
            return true;
        }

        /// <summary>
        /// 根据当前存档创建一件商品的可展示报价。
        /// </summary>
        /// <param name="profile">当前存档。</param>
        /// <param name="row">商品配置。</param>
        /// <param name="canEdit">局外事务是否开放。</param>
        /// <returns>独立报价。</returns>
        private static ProjectHomeShopOffer CreateOffer(ProjectSaveData profile, CfgHomeShopRow row, bool canEdit)
        {
            int purchased = Purchased(profile, row.ID);
            int remaining = row.StockLimit == -1 ? -1 : Math.Max(0, row.StockLimit - purchased);
            ProjectItem preview = Generate(row);
            if (preview != null)
            {
                preview.Id = "home-shop-preview:" + row.ID;
                preview.Equipped = false;
            }
            string reason = !canEdit ? "当前不能购买。" : preview == null ? "商品配置无效。" :
                profile.HomeShopPurchaseRevision == long.MaxValue ? "购买记录已达到上限。" :
                profile.Level < row.RequiredLevel ? "角色等级不足。" : remaining == 0 ? "商品已售罄。" :
                purchased == int.MaxValue ? "商品购买次数已达到上限。" :
                profile.Gold < row.PriceGold ? "金币不足。" : null;
            return new ProjectHomeShopOffer(row.ID, row.Name, row.RequiredLevel, row.PriceGold, remaining,
                preview, profile.HomeShopPurchaseRevision, reason);
        }

        /// <summary>
        /// 生成属性稳定但实例身份独立的商品物品。
        /// </summary>
        /// <param name="row">商品配置。</param>
        /// <returns>新物品实例。</returns>
        private static ProjectItem Generate(CfgHomeShopRow row) =>
            ProjectEquipmentRules.CreateConfiguredItem(new Random(row.ItemSeed), row.ItemLevel,
                (ProjectItemQuality)row.Quality, row.DefinitionId);

        /// <summary>
        /// 查询商品累计购买次数，独立于商品当前是否上架。
        /// </summary>
        /// <param name="profile">当前存档。</param>
        /// <param name="id">商品身份。</param>
        /// <returns>累计购买次数。</returns>
        private static int Purchased(ProjectSaveData profile, string id) =>
            profile.HomeShopPurchases?.Find(value => value.Id == id)?.Count ?? 0;
    }
}
