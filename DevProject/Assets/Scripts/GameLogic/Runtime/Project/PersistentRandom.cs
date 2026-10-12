using System;

namespace GameLogic.Project
{
    /// <summary>
    /// 可保存完整状态的随机源，供关卡、战斗与奖励独立持有。
    /// </summary>
    public sealed class PersistentRandom : Random
    {
        public const int AlgorithmVersion = 1;
        private uint state;

        /// <summary>
        /// 当前随机状态，恢复时不得再次消耗随机数。
        /// </summary>
        public long State => state;

        /// <summary>
        /// 使用稳定种子创建非零的随机状态。
        /// </summary>
        /// <param name="seed">独立随机流种子。</param>
        public PersistentRandom(int seed)
        {
            state = unchecked((uint)seed) ^ 0x9E3779B9u;
            if (state == 0) state = 0xA341316Cu;
        }

        /// <summary>
        /// 从已经校验的持久状态建立随机源。
        /// </summary>
        /// <param name="savedState">非零的三十二位随机状态。</param>
        /// <param name="random">恢复后的独立随机源。</param>
        /// <returns>保存状态合法时为真。</returns>
        public static bool TryRestore(long savedState, out PersistentRandom random)
        {
            random = null;
            if (savedState < 1 || savedState > uint.MaxValue) return false;
            random = new PersistentRandom(0) { state = (uint)savedState };
            return true;
        }

        /// <summary>
        /// 生成下一份无符号随机值。
        /// </summary>
        /// <returns>推进后的随机状态。</returns>
        private uint NextBits()
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        /// <summary>
        /// 生成零到一之间且不包含一的随机值。
        /// </summary>
        /// <returns>均匀随机小数。</returns>
        protected override double Sample() => NextBits() / 4294967296d;

        /// <summary>
        /// 生成零到一之间且不包含一的随机值。
        /// </summary>
        /// <returns>均匀随机小数。</returns>
        public override double NextDouble() => Sample();

        /// <summary>
        /// 生成非负的随机整数。
        /// </summary>
        /// <returns>小于最大有符号整数的随机值。</returns>
        public override int Next() => Next(int.MaxValue);

        /// <summary>
        /// 生成小于上限的非负随机整数。
        /// </summary>
        /// <param name="maxValue">不包含的非负上限。</param>
        /// <returns>范围内的随机整数。</returns>
        public override int Next(int maxValue)
        {
            if (maxValue < 0) throw new ArgumentOutOfRangeException(nameof(maxValue));
            return (int)(Sample() * maxValue);
        }

        /// <summary>
        /// 生成指定半开区间的随机整数。
        /// </summary>
        /// <param name="minValue">包含的下限。</param>
        /// <param name="maxValue">不包含的上限。</param>
        /// <returns>范围内的随机整数。</returns>
        public override int Next(int minValue, int maxValue)
        {
            if (minValue > maxValue) throw new ArgumentOutOfRangeException(nameof(minValue));
            return (int)(minValue + (long)(Sample() * ((long)maxValue - minValue)));
        }

        /// <summary>
        /// 依次填入可重现的随机字节。
        /// </summary>
        /// <param name="buffer">由调用方分配的目标缓冲区。</param>
        public override void NextBytes(byte[] buffer)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            for (int index = 0; index < buffer.Length; index++) buffer[index] = (byte)NextBits();
        }
    }
}
