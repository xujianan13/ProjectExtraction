using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 单项诊断结果的严重程度。
    /// </summary>
    public enum DiagnosticSeverity { Info, Warning, Error }

    /// <summary>
    /// 一次诊断采集的纯数据结果。
    /// </summary>
    [Serializable]
    public sealed class DiagnosticSnapshot
    {
        public string CapturedAtUtc;
        public List<DiagnosticSection> Sections = new();
        public List<DiagnosticLogEntry> Logs = new();
    }

    /// <summary>
    /// 一个诊断来源的纯数据结果。
    /// </summary>
    [Serializable]
    public sealed class DiagnosticSection
    {
        public string Name;
        public List<DiagnosticValue> Values = new();
        public string Error;

        /// <summary>
        /// 添加一项诊断文本。
        /// </summary>
        /// <param name="name">项目名称。</param>
        /// <param name="value">项目内容。</param>
        /// <param name="severity">严重程度。</param>
        public void Add(string name, string value, DiagnosticSeverity severity = DiagnosticSeverity.Info)
        {
            Values ??= new List<DiagnosticValue>();
            Values.Add(new DiagnosticValue { Name = name, Value = value, Severity = severity });
        }
    }

    /// <summary>
    /// 单项诊断文本。
    /// </summary>
    [Serializable]
    public sealed class DiagnosticValue
    {
        public string Name;
        public string Value;
        public DiagnosticSeverity Severity;
    }

    /// <summary>
    /// 从框架日志模块采集的内存日志。
    /// </summary>
    [Serializable]
    public sealed class DiagnosticLogEntry
    {
        public string TimestampUtc;
        public string Tag;
        public string Message;
        public LogLevel Level;
    }

    /// <summary>
    /// 外部诊断来源，采集时仅写入所给区域。
    /// </summary>
    public interface IDiagnosticProvider
    {
        /// <summary>
        /// 诊断区域名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 在主线程采集当前状态。
        /// </summary>
        /// <param name="section">待填充的诊断区域。</param>
        void Collect(DiagnosticSection section);
    }
}
