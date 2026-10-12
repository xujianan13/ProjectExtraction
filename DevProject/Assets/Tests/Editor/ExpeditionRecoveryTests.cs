using System;
using System.Collections.Generic;
using System.Reflection;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 使用真实配置验证准入基线、原身份恢复及终局优先结算边界。
    /// </summary>
    public sealed class ExpeditionRecoveryTests
    {
        /// <summary>
        /// 尚未绑定首区资源的基线可以冷恢复，保持计划、随机与玩家但不推进时间。
        /// </summary>
        [Test]
        public void AdmissionBaselineRestoresFrozenRequestsWithoutOfflineAdvancement()
        {
            ProjectSession session = CreateSession();
            Assert.That(ExpeditionRun.TryCreate(session, "baseline", 1, 713,
                new List<ProjectItem>(session.Warehouse), out ExpeditionRun run), Is.True);
            using (run)
            {
                Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot state, out string error), Is.True, error);
                Assert.That(state.RegionInitialized, Is.False);
                Assert.That(state.Regions, Is.Empty);
                string saved = JsonUtility.ToJson(state);
                ExpeditionRunSnapshot cold = JsonUtility.FromJson<ExpeditionRunSnapshot>(saved);
                Assert.That(ExpeditionRun.TryRestore(session, cold, out ExpeditionRun restored, out error), Is.True, error);
                using (restored)
                {
                    Assert.That(restored.IsRunning || restored.IsReady, Is.False);
                    Assert.That(restored.RunId, Is.EqualTo(run.RunId));
                    Assert.That(restored.Seed, Is.EqualTo(run.Seed));
                    Assert.That(restored.TryCaptureState(out ExpeditionRunSnapshot recaptured, out error), Is.True, error);
                    Assert.That(JsonUtility.ToJson(recaptured), Is.EqualTo(saved));
                    recaptured.Requests[0].MapVersion = "changed";
                    recaptured.Inventory[0].Name = "changed";
                    Assert.That(JsonUtility.ToJson(state), Is.EqualTo(saved));
                }
            }
        }

        /// <summary>
        /// 配置或已冻结地图身份损坏时拒绝恢复，不修改候选也不退原准入数据。
        /// </summary>
        [Test]
        public void IncompatibleSnapshotKeepsOriginalAdmissionAndCandidate()
        {
            ProjectSaveData profile = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(profile, "incompatible", 1, 17,
                new[] { profile.Warehouse[0].Copy() }, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(profile, "incompatible", out error), Is.True, error);
            ProjectSession session = CreateSession(profile);
            string committed = JsonUtility.ToJson(profile);
            Assert.That(ExpeditionRun.TryCreate(session, profile.ActiveRunId, profile.ActiveLevelId, profile.ActiveSeed,
                new List<ProjectItem>(profile.EntryItems), out ExpeditionRun run), Is.True);
            using (run)
            {
                Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot state, out error), Is.True, error);
                string version = state.ConfigVersion;
                state.ConfigVersion += "-obsolete";
                string candidate = JsonUtility.ToJson(state);
                Assert.That(ExpeditionRun.TryRestore(session, state, out ExpeditionRun rejected, out error), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(JsonUtility.ToJson(state), Is.EqualTo(candidate));
                Assert.That(session.ActiveRunId, Is.EqualTo(profile.ActiveRunId));
                Assert.That(session.RunStage, Is.EqualTo(ProjectRunStage.Running));
                Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(committed));
                state.ConfigVersion = version;
                state.Requests[0].MapVersion += "-obsolete";
                Assert.That(ExpeditionRun.TryRestore(session, state, out rejected, out error), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(session.ActiveRunId, Is.EqualTo(profile.ActiveRunId));
            }
        }

        /// <summary>
        /// 持久化终局不能再加载为活跃世界，必须按原身份记录并幂等结算。
        /// </summary>
        [Test]
        public void PendingOutcomeCannotRestoreLiveWorldAndSettlesExactlyOnce()
        {
            ProjectSaveData profile = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(profile, "terminal", 1, 31,
                new[] { profile.Warehouse[0].Copy() }, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(profile, "terminal", out error), Is.True, error);
            ProjectSession session = CreateSession(profile);
            Assert.That(ExpeditionRun.TryCreate(session, profile.ActiveRunId, profile.ActiveLevelId, profile.ActiveSeed,
                new List<ProjectItem>(profile.EntryItems), out ExpeditionRun run), Is.True);
            using (run)
            {
                InitializeFirstRegion(run);
                run.Stop();
                ProjectRunOutcome outcome = run.CreateOutcome(false, true);
                Assert.That(outcome, Is.Not.Null);
                Assert.That(outcome.Statistics.VisitedRegions, Is.EqualTo(1));
                Assert.That(ProjectRunStatistics.IsValid(outcome.Statistics), Is.True);
                Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot state, out error), Is.True, error);
                Assert.That(state.HasPendingOutcome, Is.True);
                ExpeditionRunSnapshot cold = JsonUtility.FromJson<ExpeditionRunSnapshot>(JsonUtility.ToJson(state));
                Assert.That(ExpeditionRun.TryRestore(session, cold, out ExpeditionRun rejected, out error), Is.False);
                Assert.That(rejected, Is.Null);
                Assert.That(JsonUtility.ToJson(cold.PendingOutcome), Is.EqualTo(JsonUtility.ToJson(outcome)));
                Assert.That(ProjectRunRecordRules.TryRecordOutcome(profile, cold.PendingOutcome, out error), Is.True, error);
                Assert.That(ProjectRunRecordRules.TrySettleOutcome(profile, out error), Is.True, error);
                string settled = JsonUtility.ToJson(profile);
                Assert.That(ProjectRunRecordRules.TrySettleOutcome(profile, out error), Is.True, error);
                Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(settled));
                Assert.That(profile.LastSettledRunId, Is.EqualTo("terminal"));
                Assert.That(profile.ActiveRunId, Is.Empty);
            }
        }

        /// <summary>
        /// 使用正式地图和内容生成首区，再通过实际战斗绑定创建完整敌人生命。
        /// </summary>
        /// <param name="run">尚未生成首区的真实运行对象。</param>
        private static void InitializeFirstRegion(ExpeditionRun run)
        {
            RegionGenerationRequest request = run.RegionRequests[0];
            CfgRegionMapsRow row = GameTables.CfgRegionMaps.GetById(request.MapId);
            TextAsset geometry = AssetDatabase.LoadAssetAtPath<TextAsset>(
                "Assets/GameResources/Scenes/Expedition/Regions/" + row.GeometryAddress + ".json");
            Assert.That(geometry, Is.Not.Null, row.GeometryAddress);
            RegionMapDefinition map = JsonUtility.FromJson<RegionMapDefinition>(geometry.text);
            Assert.That(RegionContentGenerator.TryGenerate(request, map, LevelContentConfig.CreateRegionContent(request),
                out RegionPlan plan, out string error), Is.True, error);
            Assert.That(RegionRuntimeState.TryCreate(plan, out RegionRuntimeState region, out error), Is.True, error);
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.NonPublic;
            ((List<RegionRuntimeState>)typeof(ExpeditionRun).GetField("regions", fields).GetValue(run)).Add(region);
            typeof(ExpeditionRun).GetField("<CurrentMap>k__BackingField", fields).SetValue(run, map);
            Assert.That((bool)typeof(ExpeditionRun).GetMethod("BindRegionWorld", fields).Invoke(run, new object[] { region, false }), Is.True);
            Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot initialized, out error), Is.True, error);
            Assert.That(ExpeditionRun.TryRestore(CreateSession(), initialized, out ExpeditionRun restored, out error), Is.True, error);
            restored.Dispose();
        }

        /// <summary>
        /// 旧活动统计缺少生命分类时保留准入和原候选，不能推算分类后续关。
        /// </summary>
        [Test]
        public void OldActiveStatisticsCannotResumeOrMutateAdmission()
        {
            ProjectSaveData profile = ProjectSaveData.CreateDefault();
            Assert.That(ProjectRunRecordRules.TryAdmit(profile, "old-active-stats", 1, 17,
                new[] { profile.Warehouse[0].Copy() }, out string error), Is.True, error);
            Assert.That(ProjectRunRecordRules.TryMarkRunning(profile, "old-active-stats", out error), Is.True, error);
            ProjectSession session = CreateSession(profile);
            string original = JsonUtility.ToJson(profile);
            Assert.That(ExpeditionRun.TryCreate(session, profile.ActiveRunId, 1, 17, profile.EntryItems, out ExpeditionRun run), Is.True);
            using (run)
            {
                Assert.That(run.TryCaptureState(out ExpeditionRunSnapshot state, out error), Is.True, error);
                state.Statistics = new ProjectRunStatistics { Version = 1, TotalRooms = 4, VisitedRooms = 1 };
                string frozen = JsonUtility.ToJson(state);
                Assert.That(ExpeditionRun.TryRestore(session, state, out ExpeditionRun restored, out error), Is.False);
                Assert.That(restored, Is.Null);
                Assert.That(JsonUtility.ToJson(state), Is.EqualTo(frozen));
                Assert.That(JsonUtility.ToJson(profile), Is.EqualTo(original));
            }
        }

        /// <summary>
        /// 创建不连接物理后端且由测试持有的账号会话。
        /// </summary>
        /// <param name="profile">可选已经准入的局外快照。</param>
        /// <returns>只提供真实配置和账号属性的会话。</returns>
        private static ProjectSession CreateSession(ProjectSaveData profile = null) =>
            (ProjectSession)Activator.CreateInstance(typeof(ProjectSession), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { null, profile ?? ProjectSaveData.CreateDefault() }, null);
    }
}
