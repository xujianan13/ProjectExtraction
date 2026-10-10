using System.Collections.Generic;

namespace GameLogic.Project
{
    /// <summary>
    /// 已提交战绩的只读会话接口。
    /// </summary>
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 最近保留的已保存单局战绩；返回独立副本。
        /// </summary>
        public IReadOnlyList<ProjectRunHistoryEntry> RunHistory =>
            profile.RunHistory?.ConvertAll(entry => entry.Copy()) ?? new List<ProjectRunHistoryEntry>();
    }
}
