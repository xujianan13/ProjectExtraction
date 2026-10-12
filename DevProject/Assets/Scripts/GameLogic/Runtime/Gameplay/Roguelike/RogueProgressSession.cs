using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace GameLogic.Gameplay.Roguelike
{
    /// <summary>
    /// 保存因构筑耗尽而关闭的一段连续技能资格。
    /// </summary>
    [Serializable]
    public sealed class RogueClosedQualificationRange
    {
        public long FirstId;
        public long LastId;

        /// <summary>
        /// 创建独立的关闭范围记录。
        /// </summary>
        /// <returns>独立范围副本。</returns>
        public RogueClosedQualificationRange Copy() => (RogueClosedQualificationRange)MemberwiseClone();
    }

    /// <summary>
    /// 加权刷怪进度、资格队列和构筑完成事实的保存状态。
    /// </summary>
    [Serializable]
    public sealed class RogueProgressState
    {
        public int Version = 1;
        public int Threshold;
        public int RemainingProgress;
        public long QualificationSerial;
        public long SelectedChoices;
        public long ClosedChoices;
        public bool IsExhausted;
        public List<long> PendingQualifications = new();
        public List<RogueClosedQualificationRange> ClosedRanges = new();

        /// <summary>
        /// 创建不共享队列和关闭记录的保存候选。
        /// </summary>
        /// <returns>完整进度深拷贝。</returns>
        public RogueProgressState Copy()
        {
            var copy = (RogueProgressState)MemberwiseClone();
            copy.PendingQualifications = new List<long>(PendingQualifications);
            copy.ClosedRanges = ClosedRanges.ConvertAll(range => range.Copy());
            return copy;
        }
    }

    /// <summary>
    /// 仅累加调用方已统一确认的死亡贡献，不自行消费战斗事件或发放经验物品。
    /// </summary>
    public sealed class RogueProgressSession
    {
        private readonly int threshold;
        private readonly Queue<long> pending = new();
        private readonly List<RogueClosedQualificationRange> closedRanges = new();

        /// <summary>
        /// 使用配置阈值创建整关共用的强化进度。
        /// </summary>
        /// <param name="threshold">每份技能选择资格要求的正整数贡献。</param>
        public RogueProgressSession(int threshold) => this.threshold = threshold;

        /// <summary>
        /// 下一份技能资格的配置阈值。
        /// </summary>
        public int Threshold => threshold;

        /// <summary>
        /// 尚未兑换为技能资格的进度余量。
        /// </summary>
        public int RemainingProgress { get; private set; }

        /// <summary>
        /// 累计触发的资格序号，同时表示已触发资格总数。
        /// </summary>
        public long TriggeredChoices { get; private set; }

        /// <summary>
        /// 已实际兑现的技能选择次数。
        /// </summary>
        public long SelectedChoices { get; private set; }

        /// <summary>
        /// 因构筑耗尽而关闭、未计为实际选择的资格数。
        /// </summary>
        public long ClosedChoices { get; private set; }

        /// <summary>
        /// 尚未完成的技能资格数，包含调用方当前展示的队首资格。
        /// </summary>
        public int PendingCount => pending.Count;

        /// <summary>
        /// 构筑完成后不再累计进度或产生新资格。
        /// </summary>
        public bool IsExhausted { get; private set; }

        /// <summary>
        /// 返回与内部队列隔离的有序资格快照。
        /// </summary>
        public IReadOnlyList<long> PendingQualifications => new ReadOnlyCollection<long>(new List<long>(pending));

        /// <summary>
        /// 累计已确认贡献，保留溢出并按顺序创建所有达到阈值的资格。
        /// </summary>
        /// <param name="contribution">本批死亡共同提交的非负贡献，不是死亡事件的去重身份。</param>
        /// <returns>进度和资格已完整应用时为真。</returns>
        public bool TryAddProgress(int contribution)
        {
            if (threshold < 1 || contribution < 0 || IsExhausted) return false;
            long total = (long)RemainingProgress + contribution;
            long count = total / threshold;
            if (count > int.MaxValue - pending.Count || TriggeredChoices > long.MaxValue - count) return false;
            RemainingProgress = (int)(total % threshold);
            for (long index = 0; index < count; index++) pending.Enqueue(++TriggeredChoices);
            return true;
        }

        /// <summary>
        /// 取得下一份应展示的资格，不提前移出队列。
        /// </summary>
        /// <param name="qualificationId">队首的稳定资格序号。</param>
        /// <returns>存在待完成资格时为真。</returns>
        public bool TryPeek(out long qualificationId)
        {
            qualificationId = pending.Count == 0 ? 0 : pending.Peek();
            return pending.Count > 0;
        }

        /// <summary>
        /// 在对应技能已成功兑现后推进队首，拒绝重复或乱序确认。
        /// </summary>
        /// <param name="qualificationId">已成功兑现的队首资格序号。</param>
        /// <returns>该资格首次完成时为真。</returns>
        public bool TryComplete(long qualificationId)
        {
            if (pending.Count == 0 || pending.Peek() != qualificationId) return false;
            pending.Dequeue();
            SelectedChoices++;
            return true;
        }

        /// <summary>
        /// 在技能会话确认构筑耗尽后关闭剩余资格并清零余量。
        /// </summary>
        /// <returns>本次关闭且不计为实际选择的资格数。</returns>
        public int CloseExhausted()
        {
            int count = pending.Count;
            if (count > 0)
            {
                closedRanges.Add(new RogueClosedQualificationRange { FirstId = pending.Peek(), LastId = TriggeredChoices });
                ClosedChoices += count;
                pending.Clear();
            }
            RemainingProgress = 0;
            IsExhausted = true;
            return count;
        }

        /// <summary>
        /// 冻结进度、资格队列和关闭范围，交给统一保存入口。
        /// </summary>
        /// <returns>不共享内部集合的完整进度状态。</returns>
        public RogueProgressState CaptureState() => new()
        {
            Threshold = threshold, RemainingProgress = RemainingProgress, QualificationSerial = TriggeredChoices,
            SelectedChoices = SelectedChoices, ClosedChoices = ClosedChoices, IsExhausted = IsExhausted,
            PendingQualifications = new List<long>(pending), ClosedRanges = closedRanges.ConvertAll(range => range.Copy())
        };

        /// <summary>
        /// 验证计数、连续资格和关闭事实后恢复，失败不修改当前进度。
        /// </summary>
        /// <param name="state">已持久确认的进度状态。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>状态满足资格守恒且与配置阈值一致时为真。</returns>
        public bool TryRestore(RogueProgressState state, out string error)
        {
            error = "强化进度保存状态无效。";
            if (state == null || state.Version != 1 || threshold < 1 || state.Threshold != threshold || state.RemainingProgress < 0 ||
                state.RemainingProgress >= threshold || state.QualificationSerial < 0 || state.SelectedChoices < 0 ||
                state.ClosedChoices < 0 || state.PendingQualifications == null || state.ClosedRanges == null ||
                state.SelectedChoices > state.QualificationSerial || state.ClosedChoices > state.QualificationSerial - state.SelectedChoices ||
                state.QualificationSerial - state.SelectedChoices - state.ClosedChoices != state.PendingQualifications.Count) return false;
            if (state.IsExhausted)
            {
                if (state.RemainingProgress != 0 || state.PendingQualifications.Count != 0 || state.ClosedRanges.Count > 1) return false;
            }
            else if (state.ClosedChoices != 0 || state.ClosedRanges.Count != 0) return false;
            long expected = state.SelectedChoices;
            foreach (long qualification in state.PendingQualifications)
            {
                if (expected == long.MaxValue || qualification != ++expected) return false;
            }
            if (state.ClosedChoices == 0 && state.ClosedRanges.Count != 0) return false;
            if (state.ClosedChoices > 0)
            {
                if (state.ClosedRanges.Count != 1) return false;
                RogueClosedQualificationRange range = state.ClosedRanges[0];
                if (range == null || state.SelectedChoices == long.MaxValue || range.FirstId != state.SelectedChoices + 1 ||
                    range.LastId != state.QualificationSerial || range.LastId - range.FirstId + 1 != state.ClosedChoices) return false;
            }
            RemainingProgress = state.RemainingProgress;
            TriggeredChoices = state.QualificationSerial;
            SelectedChoices = state.SelectedChoices;
            ClosedChoices = state.ClosedChoices;
            IsExhausted = state.IsExhausted;
            pending.Clear();
            foreach (long qualification in state.PendingQualifications) pending.Enqueue(qualification);
            closedRanges.Clear();
            closedRanges.AddRange(state.ClosedRanges.ConvertAll(range => range.Copy()));
            error = null;
            return true;
        }
    }
}
