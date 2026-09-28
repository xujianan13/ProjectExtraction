namespace GameFramework
{
    /// <summary>宿主程序更新检查结果。</summary>
    public readonly struct HostApplicationUpdateResult
    {
        /// <summary>创建宿主更新结果。</summary>
        public HostApplicationUpdateResult(bool updateRequired, bool updateAvailable, string message)
        {
            UpdateRequired = updateRequired;
            UpdateAvailable = updateAvailable || updateRequired;
            Message = message ?? string.Empty;
        }

        /// <summary>是否必须先更新宿主。</summary>
        public bool UpdateRequired { get; }
        /// <summary>是否存在宿主更新。</summary>
        public bool UpdateAvailable { get; }
        /// <summary>平台诊断信息。</summary>
        public string Message { get; }
    }
}
