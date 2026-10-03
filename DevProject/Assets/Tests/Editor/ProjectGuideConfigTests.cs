using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证指南配置完整性及运行时动态数值替换。
    /// </summary>
    public sealed class ProjectGuideConfigTests
    {
        /// <summary>
        /// 从实际导出表加载指南，确认页序与玩家规则插值。
        /// </summary>
        [Test]
        public void ExportedGuideUsesCurrentProfileValues()
        {
            TestGameTables.LoadAll();
            Assert.IsTrue(ProjectGuideConfig.Validate(out string error), error);
            var pages = ProjectGuideConfig.All();
            Assert.AreEqual(7, pages.Count);
            Assert.AreEqual("movement", pages[0].ID);
            Assert.AreEqual("stamina", pages[pages.Count - 1].ID);
            Assert.IsTrue(ProjectGuideConfig.TryFormat(GameTables.CfgGuide.GetById("loot").Body, out string loot, out error), error);
            Assert.IsTrue(ProjectGuideConfig.TryFormat(GameTables.CfgGuide.GetById("stamina").Body, out string stamina, out error), error);
            Assert.IsTrue(loot.Contains(GameTables.CfgProfile.GetById(1).SafeSlots.ToString()));
            Assert.IsTrue(stamina.Contains(GameTables.CfgProfile.GetById(1).EntryCost.ToString()));
            Assert.IsFalse(stamina.Contains("{"));
        }

        /// <summary>
        /// 拒绝未知、缺失闭合和多余括号的动态参数。
        /// </summary>
        [Test]
        public void UnknownAndMalformedPlaceholdersAreRejected()
        {
            Assert.IsTrue(ProjectGuideConfig.ValidatePlaceholders("{SafeSlots} 格", out _));
            Assert.IsFalse(ProjectGuideConfig.ValidatePlaceholders("{Unknown}", out _));
            Assert.IsFalse(ProjectGuideConfig.ValidatePlaceholders("{SafeSlots", out _));
            Assert.IsFalse(ProjectGuideConfig.ValidatePlaceholders("SafeSlots}", out _));
        }
    }
}
