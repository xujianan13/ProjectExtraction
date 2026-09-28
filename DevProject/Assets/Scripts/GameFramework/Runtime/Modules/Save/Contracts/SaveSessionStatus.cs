namespace GameFramework
{
    /// <summary>
    /// 会话当前阶段。
    /// </summary>
    public enum SavePhase { Inactive, Idle, Saving, Error, Uncertain, Closed }

    /// <summary>
    /// 会话状态的不可变快照。
    /// </summary>
    public readonly struct SaveSessionStatus
    {
        public SavePhase Phase { get; }
        public bool Active { get; }
        public bool Dirty { get; }
        public long Sequence { get; }
        public long Revision { get; }
        public long SavedRevision { get; }
        public SaveResult LastResult { get; }

        /// <summary>
        /// 创建状态快照。
        /// </summary>
        /// <param name="phase">当前阶段。</param>
        /// <param name="active">是否激活。</param>
        /// <param name="dirty">是否有未保存变化。</param>
        /// <param name="sequence">持久序号。</param>
        /// <param name="revision">内存修订号。</param>
        /// <param name="savedRevision">已保存修订号。</param>
        /// <param name="lastResult">最近结果。</param>
        internal SaveSessionStatus(SavePhase phase, bool active, bool dirty, long sequence, long revision, long savedRevision, SaveResult lastResult)
        {
            Phase = phase;
            Active = active;
            Dirty = dirty;
            Sequence = sequence;
            Revision = revision;
            SavedRevision = savedRevision;
            LastResult = lastResult;
        }
    }

    /// <summary>
    /// 保存屏障回执。
    /// </summary>
    public readonly struct SaveFlushResult
    {
        public SaveResult Result { get; }
        public long Sequence { get; }
        public long TargetRevision { get; }
        public long SavedRevision { get; }

        /// <summary>
        /// 创建保存回执。
        /// </summary>
        /// <param name="result">保存结果。</param>
        /// <param name="sequence">已提交序号。</param>
        /// <param name="targetRevision">本次等待目标。</param>
        /// <param name="savedRevision">当前已提交修订号。</param>
        internal SaveFlushResult(SaveResult result, long sequence, long targetRevision, long savedRevision)
        {
            Result = result;
            Sequence = sequence;
            TargetRevision = targetRevision;
            SavedRevision = savedRevision;
        }
    }

    /// <summary>
    /// 打开会话的结果。
    /// </summary>
    /// <typeparam name="T">游戏存档数据类型。</typeparam>
    public sealed class SaveOpenResult<T>
    {
        public SaveResult Result { get; }
        public SaveSession<T> Session { get; }
        public SaveLoadResult<T> LoadResult => Session?.LoadResult;

        /// <summary>
        /// 创建会话打开结果。
        /// </summary>
        /// <param name="result">操作结果。</param>
        /// <param name="session">打开成功的会话。</param>
        internal SaveOpenResult(SaveResult result, SaveSession<T> session = null)
        {
            Result = result;
            Session = session;
        }
    }
}
