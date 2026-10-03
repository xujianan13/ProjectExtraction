using System;
using GameLogic.Gameplay.Level;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// 按小怪清房顺序抽取奖励，Boss 与其他房间不改变小怪保底状态。
    /// </summary>
    public sealed class RogueRoomRewardSchedule
    {
        private readonly bool firstMobClearReward;
        private readonly int maxConsecutiveMobMisses;
        private readonly double mobClearChance;
        private readonly bool bossClearReward;
        private readonly Random random;
        private int mobClears;
        private int consecutiveMobMisses;

        /// <summary>
        /// 创建单局清房奖励调度器。
        /// </summary>
        /// <param name="policy">冻结的 Rogue 策略。</param>
        /// <param name="random">与其他玩法隔离的清房随机源。</param>
        public RogueRoomRewardSchedule(CfgRoguePolicyRow policy, Random random)
        {
            firstMobClearReward = policy.FirstMobClearReward;
            maxConsecutiveMobMisses = policy.MaxConsecutiveMobMisses;
            mobClearChance = policy.MobClearChance;
            bossClearReward = policy.BossClearReward;
            this.random = random;
        }

        /// <summary>
        /// 判断本次首次清房是否产生三选一。
        /// </summary>
        /// <param name="type">已清空房间的类型。</param>
        /// <returns>是否产生清房奖励。</returns>
        public bool ShouldReward(LevelRoomType type)
        {
            if (type == LevelRoomType.Boss) return bossClearReward;
            if (type != LevelRoomType.Mob) return false;

            bool reward = mobClears++ == 0 && firstMobClearReward ||
                maxConsecutiveMobMisses > 0 && consecutiveMobMisses >= maxConsecutiveMobMisses ||
                random.NextDouble() < mobClearChance;
            consecutiveMobMisses = reward ? 0 : consecutiveMobMisses + 1;
            return reward;
        }
    }
}
