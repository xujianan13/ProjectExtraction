namespace GameFramework
{
    /// <summary>
    /// 资源模块的只读缓存与租约诊断聚合。
    /// </summary>
    public sealed partial class ResourceModule
    {
        /// <summary>
        /// 统计资源缓存、在途加载及活动租约，不触发维护或加载。
        /// </summary>
        /// <param name="section">待填充的诊断区域。</param>
        internal void CollectDiagnostics(DiagnosticSection section)
        {
            int leases = 0, pins = 0, waiters = 0;
            foreach (ResourceCacheEntry entry in _resourceEntries.Values)
            {
                leases += entry.ActiveLeaseCount;
                pins += entry.PinCount;
                waiters += entry.WaiterCount;
            }
            section.Add("CacheEntries", _resourceEntries.Count.ToString());
            section.Add("LoadingEntries", _loadingEntries.Count.ToString());
            section.Add("IdleEntries", _idleEntryCount.ToString());
            section.Add("ActiveLeases", leases.ToString());
            section.Add("ResidentPins", pins.ToString());
            section.Add("WaitingAcquires", waiters.ToString());
            section.Add("PendingLeaseReleases", _pendingLeaseReleases.Count.ToString());
        }
    }
}
