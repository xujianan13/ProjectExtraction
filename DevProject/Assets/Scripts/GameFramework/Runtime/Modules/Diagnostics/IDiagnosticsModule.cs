using System;

namespace GameFramework
{
    /// <summary>
    /// 运行诊断模块的只读快照与内存日志入口。
    /// </summary>
    public interface IDiagnosticsModule : IModule
    {
        /// <summary>
        /// 在主线程按需采集一次诊断快照。
        /// </summary>
        /// <returns>不含 Unity 对象引用的独立快照。</returns>
        DiagnosticSnapshot CaptureSnapshot();

        /// <summary>
        /// 将当前快照导出为 JSON 字符串。
        /// </summary>
        /// <param name="prettyPrint">是否格式化输出。</param>
        /// <returns>诊断快照的 JSON 字符串。</returns>
        string ExportJson(bool prettyPrint = true);

        /// <summary>
        /// 注册一个按需调用的诊断来源。
        /// </summary>
        /// <param name="provider">诊断来源。</param>
        /// <returns>释放时取消注册的句柄。</returns>
        IDisposable RegisterProvider(IDiagnosticProvider provider);

        /// <summary>
        /// 清空内存中的诊断日志。
        /// </summary>
        void ClearLogs();

        /// <summary>
        /// 是否收集框架日志。
        /// </summary>
        bool IsLogCaptureEnabled { get; set; }

        /// <summary>
        /// 内存日志容量，范围为 1 到 2000。
        /// </summary>
        int LogCapacity { get; set; }
    }
}
