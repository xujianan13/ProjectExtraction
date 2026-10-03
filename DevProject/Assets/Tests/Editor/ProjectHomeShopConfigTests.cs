using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 局外商店配置、固定商品生成及买卖数值校验。
    /// </summary>
    public sealed class ProjectHomeShopConfigTests
    {
        /// <summary>
        /// 每项测试恢复完整导出配置。
        /// </summary>
        [TearDown]
        public void RestoreTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 正式商品均可生成且按配置顺序展示。
        /// </summary>
        [Test]
        public void ConfiguredOffersAreValidAndOrdered()
        {
            Assert.That(ProjectHomeShopConfig.Validate(out string error), Is.True, error);
            Assert.That(ProjectHomeShopConfig.All().Count, Is.EqualTo(9));
            Assert.That(ProjectHomeShopConfig.All()[0].ID, Is.EqualTo("basic_blade"));
            CfgHomeShopRow basic = ProjectHomeShopConfig.Get("basic_blade");
            Assert.That(basic.PriceGold, Is.LessThanOrEqualTo(ProjectRulesConfig.Profile.StartingGold));
            ProjectItem first = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(basic.ItemSeed), basic.ItemLevel,
                (ProjectItemQuality)basic.Quality, basic.DefinitionId);
            ProjectItem second = ProjectEquipmentRules.CreateConfiguredItem(new System.Random(basic.ItemSeed), basic.ItemLevel,
                (ProjectItemQuality)basic.Quality, basic.DefinitionId);
            Assert.That(first.DefinitionId, Is.EqualTo(basic.DefinitionId));
            Assert.That(second.SellGold, Is.EqualTo(first.SellGold));
            Assert.That(second.Damage, Is.EqualTo(first.Damage));
            Assert.That(second.Id, Is.Not.EqualTo(first.Id));
        }

        /// <summary>
        /// 缺失模板、等级倒挂和无效库存阻止商店启用。
        /// </summary>
        [Test]
        public void RejectsMissingDefinitionInvalidLevelAndStock()
        {
            CfgHomeShopRow row = ProjectHomeShopConfig.Get("basic_armor");
            row.DefinitionId = "missing_definition";
            Assert.That(ProjectHomeShopConfig.Validate(out string missing), Is.False);
            Assert.That(missing, Does.Contain("字段或引用"));
            row.DefinitionId = "ruin_armor";
            row.RequiredLevel = 0;
            Assert.That(ProjectHomeShopConfig.Validate(out _), Is.False);
            row.RequiredLevel = 1;
            row.StockLimit = 0;
            Assert.That(ProjectHomeShopConfig.Validate(out _), Is.False);
        }

        /// <summary>
        /// 商品价格必须高于对应模板任意随机售价。
        /// </summary>
        [Test]
        public void RejectsBuySellArbitrage()
        {
            CfgHomeShopRow row = ProjectHomeShopConfig.Get("fine_blade");
            row.PriceGold = 33;
            Assert.That(ProjectHomeShopConfig.Validate(out string error), Is.False);
            Assert.That(error, Does.Contain("最大回收价"));
        }
    }
}
