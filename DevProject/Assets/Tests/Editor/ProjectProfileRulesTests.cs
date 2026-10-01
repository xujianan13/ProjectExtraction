using System;
using GameLogic.Project;
using NUnit.Framework;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证跨日补给、等级与独立物品等业务边界。
    /// </summary>
    public sealed class ProjectProfileRulesTests
    {
        /// <summary>
        /// 五点重置先于自然恢复，超过上限的体力也按规则重置。
        /// </summary>
        [Test]
        public void FiveAmResetThenRecoveryUsesGameTimezone()
        {
            var profile = ProjectSaveData.CreateDefault();
            profile.Stamina = 140;
            profile.ClockUtc = Utc(4, 59);
            profile.RecoveryUtc = profile.ClockUtc;
            ProjectProfileRules.AdvanceClock(profile, Utc(5, 0));
            Assert.AreEqual(100, profile.Stamina);
            profile.Stamina = 90;
            ProjectProfileRules.AdvanceClock(profile, Utc(5, 12));
            Assert.AreEqual(92, profile.Stamina);
        }

        /// <summary>
        /// 满体力不积累可在扣费后立即使用的恢复时间。
        /// </summary>
        [Test]
        public void FullStaminaDoesNotBankRecoveryTime()
        {
            var profile = ProjectSaveData.CreateDefault();
            profile.ClockUtc = Utc(6, 0);
            profile.RecoveryUtc = profile.ClockUtc;
            ProjectProfileRules.AdvanceClock(profile, Utc(10, 0));
            profile.Stamina -= 10;
            ProjectProfileRules.AdvanceClock(profile, Utc(10, 5));
            Assert.AreEqual(90, profile.Stamina);
            ProjectProfileRules.AdvanceClock(profile, Utc(10, 6));
            Assert.AreEqual(91, profile.Stamina);
        }

        /// <summary>
        /// 零点补给在五点重置后仍是同一个领取身份。
        /// </summary>
        [Test]
        public void SupplyIdentityDoesNotResetAtFiveAm()
        {
            Assert.AreEqual(ProjectProfileRules.SupplyKey(Utc(0, 10)), ProjectProfileRules.SupplyKey(Utc(6, 0)));
            Assert.AreNotEqual(ProjectProfileRules.SupplyKey(Utc(11, 59)), ProjectProfileRules.SupplyKey(Utc(12, 0)));
            Assert.AreNotEqual(ProjectProfileRules.SupplyKey(Utc(17, 59)), ProjectProfileRules.SupplyKey(Utc(18, 0)));
        }

        /// <summary>
        /// 一次跨越多个等级时逐级消耗经验并只发对应点数。
        /// </summary>
        [Test]
        public void MultipleLevelsAwardPointsPerCrossedThreshold()
        {
            var profile = ProjectSaveData.CreateDefault();
            ProjectProfileRules.AddExperience(profile, 270);
            Assert.AreEqual(3, profile.Level);
            Assert.AreEqual(20, profile.Experience);
            Assert.AreEqual(2, profile.SkillPoints);
        }

        /// <summary>
        /// 候选交易不会提前改变已公开物品与金币。
        /// </summary>
        [Test]
        public void TransactionCandidateHasIndependentItemsAndWallet()
        {
            var profile = ProjectSaveData.CreateDefault();
            ProjectSaveData candidate = profile.Copy();
            candidate.Gold += candidate.Warehouse[0].SellGold;
            candidate.Warehouse[0].Equipped = false;
            candidate.Warehouse.Clear();
            Assert.AreEqual(50, profile.Gold);
            Assert.AreEqual(1, profile.Warehouse.Count);
            Assert.IsTrue(profile.Warehouse[0].Equipped);
        }

        /// <summary>
        /// 跨容器的重复装备身份不能进入存档。
        /// </summary>
        [Test]
        public void ValidationRejectsDuplicateInventoryIdentity()
        {
            var profile = ProjectSaveData.CreateDefault();
            profile.ActiveRunId = "run";
            profile.Stage = ProjectRunStage.Running;
            profile.EntryItems.Add(profile.Warehouse[0].Copy());
            Assert.IsNotNull(ProjectSaveData.Validate(profile));
        }

        /// <summary>
        /// 创建固定游戏日期的 UTC 秒。
        /// </summary>
        /// <param name="hour">游戏时区小时。</param>
        /// <param name="minute">分钟。</param>
        /// <returns>UTC 秒。</returns>
        private static long Utc(int hour, int minute) => new DateTimeOffset(2026, 9, 30, hour, minute, 0, TimeSpan.FromHours(8)).ToUnixTimeSeconds();
    }
}
