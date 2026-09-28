using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 可检查的恢复记录来源。
    /// </summary>
    public enum SaveRecoverySource { A, B, PreMigrationA, PreMigrationB }

    /// <summary>
    /// 一条恢复候选的不可变描述。
    /// </summary>
    public sealed class SaveRecoveryCandidate
    {
        public string CandidateId { get; }
        public SaveIdentity Identity { get; }
        public SaveRecoverySource Source { get; }
        public int SchemaVersion { get; }
        public long Sequence { get; }
        public string Checksum { get; }
        public bool IsUsable { get; }
        public SaveResult Result { get; }

        /// <summary>
        /// 创建恢复候选描述。
        /// </summary>
        /// <param name="candidateId">稳定候选标识。</param>
        /// <param name="identity">来源存档身份。</param>
        /// <param name="source">来源槽。</param>
        /// <param name="schemaVersion">来源 schema 版本。</param>
        /// <param name="sequence">来源序号。</param>
        /// <param name="checksum">来源校验值。</param>
        /// <param name="isUsable">当前定义是否能够恢复。</param>
        /// <param name="result">候选检查结果。</param>
        internal SaveRecoveryCandidate(string candidateId, SaveIdentity identity, SaveRecoverySource source, int schemaVersion,
            long sequence, string checksum, bool isUsable, SaveResult result)
        {
            CandidateId = candidateId;
            Identity = identity;
            Source = source;
            SchemaVersion = schemaVersion;
            Sequence = sequence;
            Checksum = checksum;
            IsUsable = isUsable;
            Result = result;
        }
    }

    /// <summary>
    /// 恢复记录检查结果。
    /// </summary>
    public sealed class SaveRecoveryInspection
    {
        public SaveResult Result { get; }
        public IReadOnlyList<SaveRecoveryCandidate> Candidates { get; }

        /// <summary>
        /// 创建恢复检查结果。
        /// </summary>
        /// <param name="result">读取结果。</param>
        /// <param name="candidates">已识别候选。</param>
        internal SaveRecoveryInspection(SaveResult result, IReadOnlyList<SaveRecoveryCandidate> candidates)
        {
            Result = result;
            Candidates = candidates;
        }
    }
}
