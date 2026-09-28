namespace GameFramework
{
    public sealed partial class InputModule
    {
        /// <summary>
        /// 收集输入上下文与阻断状态。
        /// </summary>
        /// <param name="section">待填充的诊断区域。</param>
        internal void CollectDiagnostics(DiagnosticSection section)
        {
            section.Add("ContextCount", _contexts.Count.ToString());
            for (int i = 0; i < _contexts.Count && i < 20; i++)
                section.Add("Context" + i, _contexts[i].Map + " (priority " + _contexts[i].Priority + ")");
            if (_contexts.Count > 20) section.Add("MoreContexts", (_contexts.Count - 20).ToString());
            section.Add("ActiveMap", _activeMap ?? "None");
            section.Add("GameplayActionsLoaded", (_gameplayActions != null).ToString());
            section.Add("GameplayActionsLoading", _loadingGameplay.ToString());
            section.Add("ExplicitBlocks", _blockCount.ToString());
            int blockIndex = 0;
            foreach (string reason in _blockReasons.Values)
            {
                if (blockIndex >= 20) break;
                section.Add("Block" + blockIndex++, reason);
            }
            if (_blockReasons.Count > 20) section.Add("MoreBlocks", (_blockReasons.Count - 20).ToString());
            section.Add("BlockedByBackground", _background.ToString());
            section.Add("BlockedByPause", GameEntry.IsGameplayPaused.ToString());
            section.Add("BlockedByUI", (GameEntry.Get<IUIModule>()?.BlocksGameplayInput == true).ToString());
        }
    }
}
