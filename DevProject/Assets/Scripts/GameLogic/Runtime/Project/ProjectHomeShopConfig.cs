using System;
using System.Collections.Generic;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 局外金币商店的固定商品访问与启动校验。
    /// </summary>
    public static class ProjectHomeShopConfig
    {
        /// <summary>
        /// 按商品身份取得配置。
        /// </summary>
        /// <param name="id">商品身份。</param>
        /// <returns>商品配置；不存在时为空。</returns>
        public static CfgHomeShopRow Get(string id) => string.IsNullOrWhiteSpace(id) ? null : GameTables.CfgHomeShop?.GetById(id);

        /// <summary>
        /// 按界面排序值取得商品配置。
        /// </summary>
        /// <returns>已排序的商品配置列表。</returns>
        public static IReadOnlyList<CfgHomeShopRow> All()
        {
            if (GameTables.CfgHomeShop == null) return Array.Empty<CfgHomeShopRow>();
            var rows = new List<CfgHomeShopRow>(GameTables.CfgHomeShop.GetAll());
            rows.Sort((left, right) =>
            {
                int order = left.SortOrder.CompareTo(right.SortOrder);
                return order != 0 ? order : string.CompareOrdinal(left.ID, right.ID);
            });
            return rows;
        }

        /// <summary>
        /// 校验商品模板、门槛、库存、生成物和买卖价格。
        /// </summary>
        /// <param name="error">首个配置错误，成功时为空。</param>
        /// <returns>全部商品可以启用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgHomeShop == null || GameTables.CfgHomeShop.GetAll().Count == 0 ||
                GameTables.CfgItemDefinitions == null || GameTables.CfgQualities == null || ProjectRulesConfig.Profile == null)
            { error = "局外商店配置未全部加载。"; return false; }
            foreach (CfgHomeShopRow row in GameTables.CfgHomeShop.GetAll())
            {
                CfgItemDefinitionsRow definition = ProjectRulesConfig.Item(row.DefinitionId);
                if (string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.Name) || definition == null ||
                    !Enum.IsDefined(typeof(ProjectItemQuality), row.Quality) ||
                    row.ItemLevel < 1 || row.ItemLevel > ProjectRulesConfig.Profile.MaxLevel ||
                    row.RequiredLevel < row.ItemLevel || row.RequiredLevel > ProjectRulesConfig.Profile.MaxLevel ||
                    row.PriceGold < 1 || row.StockLimit == 0 || row.StockLimit < -1 || row.ItemSeed < 0 || row.SortOrder < 0)
                { error = "局外商店商品字段或引用无效：" + row.ID; return false; }
                long highestSale = (long)definition.SellBase + (long)row.ItemLevel * definition.SellPerLevel +
                    Math.Max(0, definition.SellRandomExclusive - 1) + (long)row.Quality * definition.SellPerQuality;
                if (row.PriceGold <= highestSale)
                { error = "局外商店售价不高于最大回收价：" + row.ID; return false; }
                ProjectItem preview = ProjectEquipmentRules.CreateConfiguredItem(new Random(row.ItemSeed), row.ItemLevel,
                    (ProjectItemQuality)row.Quality, row.DefinitionId);
                if (preview == null)
                { error = "局外商店无法生成商品：" + row.ID; return false; }
                preview.Equipped = false;
                if (preview.DefinitionId != row.DefinitionId || preview.RequiredLevel != row.ItemLevel ||
                    ProjectEquipmentRules.ValidateItem(preview) != null)
                { error = "局外商店生成物无效：" + row.ID; return false; }
            }
            return true;
        }
    }
}
