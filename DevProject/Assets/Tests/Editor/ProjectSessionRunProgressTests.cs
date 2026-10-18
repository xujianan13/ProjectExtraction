using System;
using System.Collections.Generic;
using System.Reflection;
using GameLogic.Gameplay.Expedition;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using ProjectExtraction.Tests.Shared;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证永久击杀经验与房间体力账本的累计水位、幂等和原子拒绝。
    /// </summary>
    public sealed class ProjectSessionRunProgressTests
    {
        /// <summary>
        /// 每项测试加载实际配置。
        /// </summary>
        [SetUp]
        public void LoadTables() => TestGameTables.LoadAll();

        /// <summary>
        /// 首次开局只收入场费，首房等待和就绪检查点重复确认均不额外扣费。
        /// </summary>
        [Test]
        public void FirstAdmissionExcludesRoomCostAndProgressReplayIsFree()
        {
            int initialStamina = ProjectSaveData.CreateDefault().Stamina;
            ProjectSaveData data = AdmittedProfile();
            int cost = ProjectRulesConfig.Profile.EntryCost;
            Assert.That(cost, Is.EqualTo(10));
            Assert.That(data.Stamina, Is.EqualTo(initialStamina - cost));
            Assert.That(data.ActiveRoomAdmissionSerial, Is.EqualTo(1));
            ExpeditionRunSnapshot snapshot = Snapshot();
            Assert.That(Apply(data, snapshot, out string error), Is.True, error);
            snapshot.AdmissionPending = false;
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(data.Stamina, Is.EqualTo(initialStamina - cost));
        }

        /// <summary>
        /// 首房准入已包含在十点开关费用中，零余额可同步首房，后续新流水仍需一点且只能扣一次。
        /// </summary>
        [Test]
        public void ZeroBalanceFirstRoomReplayIsFreeButNextRoomNeedsOnePoint()
        {
            ProjectSaveData data = AdmittedProfile();
            data.Stamina = 0;
            ExpeditionRunSnapshot snapshot = Snapshot();
            Assert.That(Apply(data, snapshot, out string error), Is.True, error);
            snapshot.AdmissionPending = false;
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(data.Stamina, Is.Zero);
            snapshot.RoomAdmissionSerial = 2;
            snapshot.AdmissionPending = true;
            string firstRoom = JsonUtility.ToJson(data);
            Assert.That(Apply(data, snapshot, out error), Is.False);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(firstRoom));
            Assert.That(ProjectRulesConfig.Profile.RoomEntryCost, Is.EqualTo(1));
            data.Stamina = 1;
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(data.Stamina, Is.Zero);
            Assert.That(data.ActiveRoomAdmissionSerial, Is.EqualTo(2));
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(data.Stamina, Is.Zero);
        }

        /// <summary>
        /// 击杀经验立即永久升级并发放技能点，重复快照不会重复发奖。
        /// </summary>
        [Test]
        public void CumulativeExperienceAwardsLevelsAndSkillPointsExactlyOnce()
        {
            ProjectSaveData data = AdmittedProfile();
            int level = data.Level, points = data.SkillPoints, stamina = data.Stamina;
            ExpeditionRunSnapshot snapshot = Snapshot();
            snapshot.KillExperience = PrototypeRules.ExperienceRequired(level) - data.Experience +
                PrototypeRules.ExperienceRequired(level + 1) + 7;
            Assert.That(Apply(data, snapshot, out string error), Is.True, error);
            Assert.That(data.Level, Is.EqualTo(level + 2));
            Assert.That(data.Experience, Is.EqualTo(7));
            Assert.That(data.SkillPoints, Is.EqualTo(points + 2 * PrototypeRules.SkillPointsPerLevel));
            Assert.That(data.ActiveRunAwardedKillExperience, Is.EqualTo(snapshot.KillExperience));
            string confirmed = JsonUtility.ToJson(data);
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(confirmed));
            Assert.That(data.Stamina, Is.EqualTo(stamina));
        }

        /// <summary>
        /// 新房间准入序号只扣一次配置体力，同时确认累计经验增量。
        /// </summary>
        [Test]
        public void NextAdmissionChargesOnceAcrossPendingAndReadySnapshots()
        {
            ProjectSaveData data = AdmittedProfile();
            int stamina = data.Stamina;
            ExpeditionRunSnapshot snapshot = Snapshot();
            snapshot.KillExperience = 5;
            snapshot.RoomAdmissionSerial = 2;
            Assert.That(Apply(data, snapshot, out string error), Is.True, error);
            Assert.That(data.Stamina, Is.EqualTo(stamina - ProjectRulesConfig.Profile.RoomEntryCost));
            Assert.That(data.ActiveRoomAdmissionSerial, Is.EqualTo(2));
            Assert.That(data.ActiveRunAwardedKillExperience, Is.EqualTo(5));
            string confirmed = JsonUtility.ToJson(data);
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            snapshot.AdmissionPending = false;
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(confirmed));
        }

        /// <summary>
        /// 身份版本错误、账本回退和跨房跳号全部保留原账号。
        /// </summary>
        /// <param name="fault">待注入的故障类型。</param>
        [TestCase("version")]
        [TestCase("identity")]
        [TestCase("experience")]
        [TestCase("serial-backward")]
        [TestCase("serial-jump")]
        public void InvalidProgressNeverMutatesProfile(string fault)
        {
            ProjectSaveData data = AdmittedProfile();
            ExpeditionRunSnapshot snapshot = Snapshot();
            snapshot.KillExperience = 10;
            Assert.That(Apply(data, snapshot, out _), Is.True);
            if (fault == "version") snapshot.Version = 2;
            else if (fault == "identity") snapshot.RunId = "different";
            else if (fault == "experience") snapshot.KillExperience = 9;
            else if (fault == "serial-backward") snapshot.RoomAdmissionSerial = 0;
            else snapshot.RoomAdmissionSerial = 3;
            string before = JsonUtility.ToJson(data);
            Assert.That(Apply(data, snapshot, out string error), Is.False);
            Assert.That(error, Is.Not.Empty);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(before));
        }

        /// <summary>
        /// 日志已解除等待标记时仍按序号补齐账号水位，只扣一次房间体力。
        /// </summary>
        [Test]
        public void ReadyJournalSnapshotCanReconcileNewAdmissionSerial()
        {
            ProjectSaveData data = AdmittedProfile();
            int stamina = data.Stamina;
            ExpeditionRunSnapshot snapshot = Snapshot();
            snapshot.RoomAdmissionSerial = 2;
            snapshot.AdmissionPending = false;
            Assert.That(Apply(data, snapshot, out string error), Is.True, error);
            Assert.That(data.Stamina, Is.EqualTo(stamina - ProjectRulesConfig.Profile.RoomEntryCost));
            Assert.That(Apply(data, snapshot, out error), Is.True, error);
            Assert.That(data.Stamina, Is.EqualTo(stamina - ProjectRulesConfig.Profile.RoomEntryCost));
        }

        /// <summary>
        /// 房间体力不足时，经验增量和全部账本水位一起拒绝。
        /// </summary>
        [Test]
        public void InsufficientRoomStaminaRejectsExperienceAndAdmissionTogether()
        {
            ProjectSaveData data = AdmittedProfile();
            data.Stamina = 0;
            ExpeditionRunSnapshot snapshot = Snapshot();
            snapshot.RoomAdmissionSerial = 2;
            snapshot.KillExperience = PrototypeRules.ExperienceRequired(data.Level);
            string before = JsonUtility.ToJson(data);
            Assert.That(Apply(data, snapshot, out string error), Is.False);
            Assert.That(error, Does.Contain("体力"));
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(before));
        }

        /// <summary>
        /// 已结算账号和旧玩法活动身份不能接收当前关卡经验。
        /// </summary>
        [Test]
        public void InactiveAndLegacyRunCannotReceiveProgress()
        {
            Assert.That(Apply(ProjectSaveData.CreateDefault(), Snapshot(), out _), Is.False);
            ProjectSaveData data = AdmittedProfile();
            data.ActiveRunVersion = 2;
            string before = JsonUtility.ToJson(data);
            Assert.That(Apply(data, Snapshot(), out _), Is.False);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(before));
        }

        /// <summary>
        /// 局内外使用同一永久技能扣点规则，拒绝时不先扣点。
        /// </summary>
        [Test]
        public void PermanentSkillUpgradeUsesConfiguredCostAndRejectsWithoutMutation()
        {
            ProjectSaveData data = AdmittedProfile();
            CfgSkillsRow skill = ProjectRulesConfig.Skill("power");
            int rank = data.PowerRank;
            int cost = skill.CostBase + rank * skill.CostPerRank;
            data.SkillPoints = cost;
            Assert.That(Upgrade(data, "power", out string error), Is.True, error);
            Assert.That(data.PowerRank, Is.EqualTo(rank + 1));
            Assert.That(data.SkillPoints, Is.Zero);
            string before = JsonUtility.ToJson(data);
            Assert.That(Upgrade(data, "missing", out _), Is.False);
            Assert.That(Upgrade(data, "power", out _), Is.False);
            Assert.That(JsonUtility.ToJson(data), Is.EqualTo(before));
        }

        /// <summary>
        /// 会话公布首次入场体力，携入装备读取不会共享账号中的可写对象。
        /// </summary>
        [Test]
        public void SessionExposesEntryCostAndIndependentEntryItems()
        {
            ProjectSaveData data = AdmittedProfile();
            var session = (ProjectSession)Activator.CreateInstance(typeof(ProjectSession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { null, data }, null);
            Assert.That(session.RequiredEntryStamina, Is.EqualTo(10L));
            PropertyInfo entry = typeof(ProjectSession).GetProperty("EntryItems", BindingFlags.Instance | BindingFlags.NonPublic);
            var first = (IReadOnlyList<ProjectItem>)entry.GetValue(session);
            first[0].Name = "changed";
            var second = (IReadOnlyList<ProjectItem>)entry.GetValue(session);
            Assert.That(second[0].Name, Is.Not.EqualTo("changed"));
            Assert.That(data.EntryItems[0].Name, Is.Not.EqualTo("changed"));
        }

        /// <summary>
        /// 建立已支付关卡入场费且首房不额外收费的正式活动账号候选。
        /// </summary>
        /// <returns>已标记运行中的独立账号。</returns>
        private static ProjectSaveData AdmittedProfile()
        {
            ProjectSaveData data = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(data, "run", 1, 17, data.Warehouse.FindAll(item => item.Equipped), out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(data, "run", out error), Is.True, error);
            return data;
        }

        /// <summary>
        /// 创建用于账本边界的最小业务水位快照。
        /// </summary>
        /// <returns>首房等待准入确认的快照。</returns>
        private static ExpeditionRunSnapshot Snapshot() => new ExpeditionRunSnapshot
        { Version = 3, RunId = "run", RoomAdmissionSerial = 1, AdmissionPending = true };

        /// <summary>
        /// 调用会话内部的纯账本候选应用逻辑。
        /// </summary>
        /// <param name="data">独立账号候选。</param>
        /// <param name="snapshot">累计水位快照。</param>
        /// <param name="error">拒绝原因。</param>
        /// <returns>是否成功应用。</returns>
        private static bool Apply(ProjectSaveData data, ExpeditionRunSnapshot snapshot, out string error)
        {
            object[] args = { data, snapshot, null };
            bool applied = (bool)typeof(ProjectSession).GetMethod("TryApplyRunProgress", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            error = (string)args[2];
            return applied;
        }

        /// <summary>
        /// 调用局内外共用的永久技能候选规则。
        /// </summary>
        /// <param name="data">独立账号候选。</param>
        /// <param name="skillId">技能身份。</param>
        /// <param name="error">拒绝原因。</param>
        /// <returns>是否成功升级。</returns>
        private static bool Upgrade(ProjectSaveData data, string skillId, out string error)
        {
            object[] args = { data, skillId, null };
            bool upgraded = (bool)typeof(ProjectSession).GetMethod("TryApplySkillUpgrade", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, args);
            error = (string)args[2];
            return upgraded;
        }
    }
}
