namespace GameFramework
{
    /// <summary>
    /// 存档模块的只读会话状态诊断。
    /// </summary>
    public sealed partial class SaveModule
    {
        /// <summary>
        /// 统计会话阶段与脏数据数量，不读取存档正文或身份。
        /// </summary>
        /// <param name="section">待填充的诊断区域。</param>
        internal void CollectDiagnostics(DiagnosticSection section)
        {
            int dirty = 0, inactive = 0, idle = 0, saving = 0, error = 0, uncertain = 0, closed = 0;
            foreach (ISaveSession session in _sessions)
            {
                SaveSessionStatus status = session.GetStatus();
                if (status.Dirty) dirty++;
                switch (status.Phase)
                {
                    case SavePhase.Inactive: inactive++; break;
                    case SavePhase.Idle: idle++; break;
                    case SavePhase.Saving: saving++; break;
                    case SavePhase.Error: error++; break;
                    case SavePhase.Uncertain: uncertain++; break;
                    case SavePhase.Closed: closed++; break;
                }
            }
            section.Add("Backend", Backend?.GetType().Name ?? "Unavailable");
            section.Add("Sessions", _sessions.Count.ToString());
            section.Add("DirtySessions", dirty.ToString(), dirty > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info);
            section.Add("Inactive", inactive.ToString());
            section.Add("Idle", idle.ToString());
            section.Add("Saving", saving.ToString());
            section.Add("Error", error.ToString(), error > 0 ? DiagnosticSeverity.Error : DiagnosticSeverity.Info);
            section.Add("Uncertain", uncertain.ToString(), uncertain > 0 ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info);
            section.Add("Closed", closed.ToString());
            section.Add("InFlightOwnerships", _ownedCount.ToString());
        }
    }
}
