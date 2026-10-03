using System;
using System.IO;
using System.Linq;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
using GameLogic.Project;
using GameLogic.Tables;
using NUnit.Framework;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证清房保底边界，并使用真实选择会话统计流派形成节奏。
    /// </summary>
    public sealed class RoguePacingTests
    {
        private static readonly LevelRoomType[] FullRoute = { LevelRoomType.Mob, LevelRoomType.Mob, LevelRoomType.Loot,
            LevelRoomType.Mob, LevelRoomType.Npc, LevelRoomType.Mob, LevelRoomType.Mob, LevelRoomType.Loot,
            LevelRoomType.Mob, LevelRoomType.Boss };
        private static readonly LevelRoomType[] EarlyRoute = FullRoute.Take(5).ToArray();

        [Serializable]
        private sealed class Metrics
        {
            public string phase;
            public string route;
            public string strategy;
            public int runs;
            public int selections;
            public int rank2Runs;
            public int rank3Runs;
            public int bleedRank2Runs;
            public int bleedRank3Runs;
            public int bloodlustUnlockedRuns;
            public int bloodlustOfferedRuns;
            public int bloodlustSelectedRuns;
            public float meanSelections;
            public float rank2Rate;
            public float rank3Rate;
            public float bloodlustUnlockRate;
            public float bloodlustSelectionRate;
        }

        [Serializable]
        private sealed class Report
        {
            public string interpretation;
            public string seedFormula;
            public string fullRoute;
            public string earlyRoute;
            public string strategyDefinitions;
            public string baselinePolicy;
            public string revisedPolicy;
            public Metrics[] results;
        }

        /// <summary>
        /// 首次小怪房必奖，随后连续两次未中奖后第三次必奖，非小怪房不影响计数。
        /// </summary>
        [Test]
        public void FirstMobAndMissCapRespectRoomTypes()
        {
            var row = Policy(true, 2, 1, 1);
            row.MobClearChance = 0;
            var schedule = new RogueRoomRewardSchedule(row, new System.Random(7));
            Assert.IsTrue(schedule.ShouldReward(LevelRoomType.Mob));
            Assert.IsFalse(schedule.ShouldReward(LevelRoomType.Mob));
            row.FirstMobClearReward = false;
            row.MaxConsecutiveMobMisses = 0;
            row.MobClearChance = 1;
            row.BossClearReward = false;
            Assert.IsTrue(schedule.ShouldReward(LevelRoomType.Boss));
            Assert.IsFalse(schedule.ShouldReward(LevelRoomType.Loot));
            Assert.IsFalse(schedule.ShouldReward(LevelRoomType.Mob));
            Assert.IsFalse(schedule.ShouldReward(LevelRoomType.Npc));
            Assert.IsTrue(schedule.ShouldReward(LevelRoomType.Mob));
            Assert.IsFalse(schedule.ShouldReward(LevelRoomType.Mob));
        }

        /// <summary>
        /// 关闭保底时概率零保持无奖，非法会话倍率拒绝开启且不占用来源。
        /// </summary>
        [Test]
        public void DisabledPityAndInvalidWeightRejectWithoutConsumingSource()
        {
            var row = Policy(false, 0, 1, 1);
            row.MobClearChance = 0;
            var schedule = new RogueRoomRewardSchedule(row, new System.Random(8));
            for (int index = 0; index < 5; index++) Assert.IsFalse(schedule.ShouldReward(LevelRoomType.Mob));

            var policy = new RogueChoicePolicy { SelectedUpgradeWeightMultiplier = 17, UnlockedComboWeightMultiplier = 1 };
            var session = new RogueChoiceSession("invalid", 19, policy);
            Assert.IsFalse(session.TryBegin("room"));
            Assert.IsFalse(session.HasPending);
            var field = typeof(RogueChoiceSession).GetField("usedSources", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            var sources = (System.Collections.Generic.HashSet<string>)field.GetValue(session);
            Assert.IsFalse(sources.Contains("room"));
            policy.SelectedUpgradeWeightMultiplier = 1;
            Assert.IsFalse(session.TryBegin("room"));
        }

        /// <summary>
        /// 固定种子比较旧规则与新规则的完整探索、提前撤离及两种选择策略。
        /// </summary>
        [Test]
        public void WriteStage7PacingReport()
        {
            const int runs = 2000;
            CfgRoguePolicyRow baseline = Policy(false, 0, 1, 1);
            CfgRoguePolicyRow revised = Policy(ProjectRulesConfig.RoguePolicy.FirstMobClearReward,
                ProjectRulesConfig.RoguePolicy.MaxConsecutiveMobMisses,
                ProjectRulesConfig.RoguePolicy.SelectedUpgradeWeightMultiplier,
                ProjectRulesConfig.RoguePolicy.UnlockedComboWeightMultiplier);
            var report = new Report
            {
                interpretation = "固定地图路线与开箱假设下的三选一选择流程模拟；不是战斗、存活、地图生成或真实撤离概率。分母均为每组2000局；bloodlustUnlocked指割裂达到2级，bloodlustOffered指至少一次出现在三项中。",
                seedFormula = "seed=100000+runIndex; room RNG=seed^0x53A4; choice RNG=seed^0x5247; random picker=seed^0x1357",
                fullRoute = string.Join(",", FullRoute.Select(type => type.ToString())),
                earlyRoute = string.Join(",", EarlyRoute.Select(type => type.ToString())),
                strategyDefinitions = "bleedPriority: bleed > bloodlust > 已选未满级CombatAugment > 首项CombatAugment > 首项；random: 从三项均匀选择。开箱和Boss各触发一次，NPC不触发；未模拟敌人掉落与死亡。",
                baselinePolicy = "FirstMobClearReward=false; MaxConsecutiveMobMisses=0; SelectedUpgradeWeightMultiplier=1; UnlockedComboWeightMultiplier=1; MobClearChance=" + baseline.MobClearChance + "; BossClearReward=" + baseline.BossClearReward,
                revisedPolicy = "FirstMobClearReward=" + revised.FirstMobClearReward + "; MaxConsecutiveMobMisses=" + revised.MaxConsecutiveMobMisses +
                    "; SelectedUpgradeWeightMultiplier=" + revised.SelectedUpgradeWeightMultiplier +
                    "; UnlockedComboWeightMultiplier=" + revised.UnlockedComboWeightMultiplier + "; MobClearChance=" + revised.MobClearChance +
                    "; BossClearReward=" + revised.BossClearReward,
                results = new Metrics[8]
            };
            int index = 0;
            foreach (bool after in new[] { false, true })
                foreach (bool full in new[] { false, true })
                    foreach (bool bleedPriority in new[] { false, true })
                        report.results[index++] = Sample(after ? revised : baseline, after ? "revised" : "baseline",
                            full ? FullRoute : EarlyRoute, full ? "full" : "early", bleedPriority, runs);
            string directory = Path.GetFullPath(Path.Combine(Application.dataPath, "../../verification"));
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "stage7-pacing.json");
            File.WriteAllText(path, JsonUtility.ToJson(report, true));
            Debug.Log("Rogue 节奏统计：" + path);
            Assert.That(report.results.All(item => item.selections > 0), Is.True);
        }

        /// <summary>
        /// 建立与正式配置同概率但可独立调整节奏参数的测试策略。
        /// </summary>
        /// <param name="first">首次小怪清房是否必奖。</param>
        /// <param name="misses">允许的连续落空次数，零关闭保底。</param>
        /// <param name="selected">已选强化权重倍数。</param>
        /// <param name="combo">解锁组合权重倍数。</param>
        /// <returns>与全局配置隔离的测试策略。</returns>
        private static CfgRoguePolicyRow Policy(bool first, int misses, int selected, int combo)
        {
            CfgRoguePolicyRow source = ProjectRulesConfig.RoguePolicy;
            return new CfgRoguePolicyRow
            {
                ID = source.ID, RefreshEnabled = false, ExtraChoiceEnabled = false, MaxRefreshes = source.MaxRefreshes,
                MobClearChance = source.MobClearChance, BossClearReward = source.BossClearReward,
                FirstMobClearReward = first, MaxConsecutiveMobMisses = misses,
                SelectedUpgradeWeightMultiplier = selected, UnlockedComboWeightMultiplier = combo
            };
        }

        /// <summary>
        /// 按指定路线和选项策略累计会话指标。
        /// </summary>
        /// <param name="row">本组清房策略。</param>
        /// <param name="phase">基线或修订策略标识。</param>
        /// <param name="route">房间顺序。</param>
        /// <param name="routeName">路线标识。</param>
        /// <param name="bleedPriority">是否使用流血优先选择。</param>
        /// <param name="runs">样本局数。</param>
        /// <returns>按局累计的统计值。</returns>
        private static Metrics Sample(CfgRoguePolicyRow row, string phase, LevelRoomType[] route, string routeName,
            bool bleedPriority, int runs)
        {
            var result = new Metrics { phase = phase, route = routeName, strategy = bleedPriority ? "bleedPriority" : "random", runs = runs };
            for (int run = 0; run < runs; run++)
            {
                int seed = 100000 + run;
                var schedule = new RogueRoomRewardSchedule(row, new System.Random(seed ^ 0x53A4));
                var choicePolicy = new RogueChoicePolicy
                {
                    RefreshEnabled = false, ExtraChoiceEnabled = false, MaxRefreshes = 0,
                    SelectedUpgradeWeightMultiplier = row.SelectedUpgradeWeightMultiplier,
                    UnlockedComboWeightMultiplier = row.UnlockedComboWeightMultiplier
                };
                var session = new RogueChoiceSession("simulation:" + run, seed ^ 0x5247, choicePolicy);
                var picker = new System.Random(seed ^ 0x1357);
                bool unlocked = false;
                for (int room = 0; room < route.Length; room++)
                {
                    bool reward = route[room] == LevelRoomType.Loot || schedule.ShouldReward(route[room]);
                    if (!reward) continue;
                    Assert.That(session.TryBegin("room:" + room), Is.True);
                    RogueChoiceSnapshot offer = session.Active;
                    unlocked |= offer.Options.Any(option => option.Reward.Id == "bloodlust");
                    int selected = bleedPriority ? ChooseBleedPriority(session, offer) : picker.Next(offer.Options.Count);
                    Assert.That(session.TrySelect(offer.OfferId, offer.Revision, selected, _ => true), Is.True);
                    result.selections++;
                }
                var combat = session.Upgrades.Where(upgrade => upgrade.Kind == RogueRewardKind.CombatAugment).ToArray();
                if (combat.Any(upgrade => upgrade.Rank >= 2)) result.rank2Runs++;
                if (combat.Any(upgrade => upgrade.Rank >= 3)) result.rank3Runs++;
                int bleed = combat.FirstOrDefault(upgrade => upgrade.Id == "bleed")?.Rank ?? 0;
                if (bleed >= 2) result.bleedRank2Runs++;
                if (bleed >= 3) result.bleedRank3Runs++;
                if (bleed >= 2) result.bloodlustUnlockedRuns++;
                if (unlocked) result.bloodlustOfferedRuns++;
                if (combat.Any(upgrade => upgrade.Id == "bloodlust")) result.bloodlustSelectedRuns++;
            }
            result.meanSelections = (float)result.selections / runs;
            result.rank2Rate = (float)result.rank2Runs / runs;
            result.rank3Rate = (float)result.rank3Runs / runs;
            result.bloodlustUnlockRate = (float)result.bloodlustUnlockedRuns / runs;
            result.bloodlustSelectionRate = (float)result.bloodlustSelectedRuns / runs;
            return result;
        }

        /// <summary>
        /// 选择流血、已解锁嗜血或本局现有强化的可用升级。
        /// </summary>
        /// <param name="session">真实 Rogue 选择会话。</param>
        /// <param name="offer">当前三选一快照。</param>
        /// <returns>要兑现的选项序号。</returns>
        private static int ChooseBleedPriority(RogueChoiceSession session, RogueChoiceSnapshot offer)
        {
            RogueChoiceOption choice = offer.Options.FirstOrDefault(option => option.Reward.Id == "bleed") ??
                offer.Options.FirstOrDefault(option => option.Reward.Id == "bloodlust") ??
                offer.Options.FirstOrDefault(option => option.Reward.Kind == RogueRewardKind.CombatAugment &&
                    session.Upgrades.Any(upgrade => upgrade.Id == option.Reward.Id)) ??
                offer.Options.FirstOrDefault(option => option.Reward.Kind == RogueRewardKind.CombatAugment) ?? offer.Options[0];
            return choice.Index;
        }
    }
}
