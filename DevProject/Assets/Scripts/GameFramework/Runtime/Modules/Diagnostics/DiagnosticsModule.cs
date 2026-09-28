using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using UnityEngine.InputSystem;
#endif

namespace GameFramework
{
    /// <summary>
    /// 按需汇总框架状态并在内存中保存有限条框架日志。
    /// </summary>
    public sealed class DiagnosticsModule : ModuleBase, IDiagnosticsModule, ILogSink
    {
        private const int MaxCapacity = 2000;
        private const int MaxMessageLength = 4096;
        private readonly List<ProviderRegistration> _providers = new();
        private readonly HashSet<IDiagnosticProvider> _reportedFailures = new();
        private readonly object _logLock = new();
        private DiagnosticLogEntry[] _logs = new DiagnosticLogEntry[200];
        private int _logStart, _logCount;
        private bool _capturing;
        private bool _shutdown;
        private ILogModule _log;

        /// <summary>
        /// 在日志模块之后、其他系统模块之前初始化。
        /// </summary>
        public override int Priority => -900;

        /// <inheritdoc />
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public bool IsLogCaptureEnabled { get; set; } = true;
#else
        public bool IsLogCaptureEnabled { get; set; }
#endif

        /// <inheritdoc />
        public int LogCapacity
        {
            get { lock (_logLock) return _logs.Length; }
            set
            {
                int capacity = Math.Max(1, Math.Min(MaxCapacity, value));
                lock (_logLock)
                {
                    if (capacity == _logs.Length) return;
                    var resized = new DiagnosticLogEntry[capacity];
                    int count = Math.Min(_logCount, capacity);
                    for (int i = 0; i < count; i++) resized[i] = _logs[(_logStart + _logCount - count + i) % _logs.Length];
                    _logs = resized;
                    _logStart = 0;
                    _logCount = count;
                }
            }
        }

        /// <inheritdoc />
        public override void OnInit()
        {
            _shutdown = false;
            _log = GameEntry.Get<ILogModule>();
            _log?.AddSink(this);
        }

        /// <inheritdoc />
        public override void OnUpdate(float deltaTime)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Keyboard.current?.f8Key.wasPressedThisFrame == true)
            {
                if (RuntimeDiagnosticsPanel.IsVisible) RuntimeDiagnosticsPanel.Hide();
                else RuntimeDiagnosticsPanel.Show();
            }
#endif
        }

        /// <inheritdoc />
        public override void OnShutdown()
        {
            _shutdown = true;
            _log?.RemoveSink(this);
            _log = null;
            while (_providers.Count > 0) _providers[_providers.Count - 1].Dispose();
            _reportedFailures.Clear();
            ClearLogs();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            RuntimeDiagnosticsPanel.Hide();
#endif
        }

        /// <inheritdoc />
        public DiagnosticSnapshot CaptureSnapshot()
        {
            var snapshot = new DiagnosticSnapshot { CapturedAtUtc = DateTime.UtcNow.ToString("O") };
            if (_capturing)
            {
                snapshot.Sections.Add(new DiagnosticSection { Name = "Diagnostics", Error = "诊断采集正在执行。" });
                return snapshot;
            }

            _capturing = true;
            try
            {
                try { CollectFramework(snapshot.Sections); }
                catch (Exception error) { snapshot.Sections.Add(new DiagnosticSection { Name = "Framework", Error = "采集失败：" + error.GetType().Name }); }
                CollectModules(snapshot.Sections);
                ProviderRegistration[] registrations = _providers.ToArray();
                for (int i = 0; i < registrations.Length; i++)
                {
                    ProviderRegistration registration = registrations[i];
                    if (!registration.IsActive) continue;
                    var section = new DiagnosticSection { Name = "Provider" };
                    try { section.Name = SafeText(registration.Provider.Name); registration.Provider.Collect(section); }
                    catch (Exception error)
                    {
                        section.Error = "采集失败：" + error.GetType().Name;
                        if (registration.IsActive && _reportedFailures.Add(registration.Provider))
                            _log?.Error("Diagnostics", $"诊断来源 {section.Name} 采集失败：{error.GetType().Name}");
                    }
                    snapshot.Sections.Add(section);
                }
                lock (_logLock)
                {
                    for (int i = 0; i < _logCount; i++)
                    {
                        DiagnosticLogEntry entry = _logs[(_logStart + i) % _logs.Length];
                        snapshot.Logs.Add(new DiagnosticLogEntry { TimestampUtc = entry.TimestampUtc, Tag = entry.Tag, Message = entry.Message, Level = entry.Level });
                    }
                }
                for (int i = 0; i < snapshot.Sections.Count; i++) snapshot.Sections[i] = CopySection(snapshot.Sections[i]);
                return snapshot;
            }
            finally { _capturing = false; }
        }

        /// <inheritdoc />
        public string ExportJson(bool prettyPrint = true) => JsonUtility.ToJson(CaptureSnapshot(), prettyPrint);

        /// <inheritdoc />
        public IDisposable RegisterProvider(IDiagnosticProvider provider)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (_shutdown) return new ProviderRegistration(null, null);
            var registration = new ProviderRegistration(this, provider);
            _providers.Add(registration);
            return registration;
        }

        /// <inheritdoc />
        public void ClearLogs()
        {
            lock (_logLock)
            {
                Array.Clear(_logs, 0, _logs.Length);
                _logStart = 0;
                _logCount = 0;
            }
        }

        /// <inheritdoc />
        public void Write(LogLevel level, string tag, string message)
        {
            if (!IsLogCaptureEnabled || _shutdown) return;
            var entry = new DiagnosticLogEntry { TimestampUtc = DateTime.UtcNow.ToString("O"), Tag = SafeText(tag), Message = SafeText(message), Level = level };
            lock (_logLock)
            {
                if (!IsLogCaptureEnabled || _shutdown) return;
                int index = (_logStart + _logCount) % _logs.Length;
                _logs[index] = entry;
                if (_logCount < _logs.Length) _logCount++;
                else _logStart = (_logStart + 1) % _logs.Length;
            }
        }

        /// <inheritdoc />
        public void Flush() { }

        /// <summary>
        /// 复制来源区域，防止来源保留传入对象并在采集后改动已返回快照。
        /// </summary>
        /// <param name="source">来源区域。</param>
        /// <returns>独立区域。</returns>
        private static DiagnosticSection CopySection(DiagnosticSection source)
        {
            var copy = new DiagnosticSection { Name = SafeText(source.Name), Error = SafeText(source.Error) };
            if (source.Values == null) return copy;
            for (int i = 0; i < source.Values.Count; i++)
            {
                DiagnosticValue value = source.Values[i];
                if (value == null) continue;
                copy.Add(SafeText(value.Name), SafeText(value.Value), value.Severity);
            }
            return copy;
        }

        /// <summary>
        /// 限制文本长度并遮盖常见地址和凭据片段。
        /// </summary>
        /// <param name="value">原始文本。</param>
        /// <returns>可展示的文本。</returns>
        private static string SafeText(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            string clipped = value.Length > MaxMessageLength ? value.Substring(0, MaxMessageLength) : value;
            clipped = Regex.Replace(clipped, @"\b(?:https?|wss?)://\S+", "[URL]", RegexOptions.IgnoreCase);
            clipped = Regex.Replace(clipped, @"\b(?:token|authorization|password|secret|api[_-]?key)\s*[:=]\s*\S+", "[credential]", RegexOptions.IgnoreCase);
            return clipped;
        }

        /// <summary>
        /// 填充框架宿主和作用域的只读状态。
        /// </summary>
        /// <param name="sections">目标区域列表。</param>
        private static void CollectFramework(List<DiagnosticSection> sections)
        {
            var section = new DiagnosticSection { Name = "Framework" };
            section.Add("Running", GameEntry.IsRunning.ToString());
            section.Add("Host", GameEntry.HasHost.ToString());
            section.Add("Background", GameEntry.IsInBackground.ToString());
            section.Add("GameplayPaused", GameEntry.IsGameplayPaused.ToString());
            LifetimeScope scope = GameEntry.ApplicationScope;
            section.Add("ApplicationScope", scope == null ? "Unavailable" : scope.State.ToString());
            if (scope != null)
            {
                section.Add("ScopeChildren", scope.ChildCount.ToString());
                section.Add("ScopeRegistrations", scope.RegistrationCount.ToString());
            }
            sections.Add(section);
            var environment = new DiagnosticSection { Name = "Environment" };
            environment.Add("ApplicationVersion", SafeText(Application.version));
            environment.Add("UnityVersion", SafeText(Application.unityVersion));
            environment.Add("Platform", Application.platform.ToString());
            environment.Add("TimeScale", Time.timeScale.ToString("0.###"));
            environment.Add("UnscaledFrameMs", (Time.unscaledDeltaTime * 1000f).ToString("0.###"));
            environment.Add("ManagedMemoryBytes", GC.GetTotalMemory(false).ToString());
            sections.Add(environment);
        }

        /// <summary>
        /// 从已注册模块读取可靠的聚合状态。
        /// </summary>
        /// <param name="sections">目标区域列表。</param>
        private static void CollectModules(List<DiagnosticSection> sections)
        {
            var scene = new DiagnosticSection { Name = "Scene" };
            try
            {
                ISceneModule scenes = GameEntry.Get<ISceneModule>();
                if (scenes == null) scene.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    scene.Add("Current", SafeText(scenes.CurrentScene?.Name ?? "None"));
                    scene.Add("Changing", scenes.IsChanging.ToString());
                    scene.Add("StackDepth", scenes.StackDepth.ToString());
                }
            }
            catch (Exception error) { scene.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(scene);

            var gameplay = new DiagnosticSection { Name = "Gameplay" };
            try
            {
                IGameplayModule games = GameEntry.Get<IGameplayModule>();
                if (games == null) gameplay.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    gameplay.Add("Count", games.Count.ToString());
                    gameplay.Add("Paused", games.IsPaused.ToString());
                    gameplay.Add("ApplicationPaused", games.IsApplicationPaused.ToString());
                }
            }
            catch (Exception error) { gameplay.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(gameplay);

            var ui = new DiagnosticSection { Name = "UI" };
            try
            {
                IUIModule windows = GameEntry.Get<IUIModule>();
                if (windows == null) ui.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    ui.Add("Ready", windows.IsReady.ToString());
                    ui.Add("BlocksGameplayInput", windows.BlocksGameplayInput.ToString());
                    ui.Add("OpenedWindows", windows.OpenedWindows?.Count.ToString() ?? "Unavailable");
                    if (windows.OpenedWindows != null)
                    {
                        for (int i = 0; i < windows.OpenedWindows.Count && i < 20; i++)
                        {
                            GameWindow window = windows.OpenedWindows[i];
                            if (window != null) ui.Add("Window" + i, window.GetType().Name + " #" + window.Id + " " + window.State);
                        }
                        if (windows.OpenedWindows.Count > 20) ui.Add("MoreWindows", (windows.OpenedWindows.Count - 20).ToString());
                    }
                }
            }
            catch (Exception error) { ui.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(ui);

            var input = new DiagnosticSection { Name = "Input" };
            try
            {
                IInputModule inputs = GameEntry.Get<IInputModule>();
                if (inputs == null) input.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    input.Add("Device", inputs.ActiveDevice.ToString());
                    input.Add("Pointers", inputs.Pointers?.Count.ToString() ?? "Unavailable");
                    if (inputs is InputModule concreteInput) concreteInput.CollectDiagnostics(input);
                    else input.Add("ContextAndBlockDetails", "Unavailable");
                }
            }
            catch (Exception error) { input.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(input);

            var resource = new DiagnosticSection { Name = "Resource" };
            try
            {
                IResourceModule resources = GameEntry.Get<IResourceModule>();
                if (resources == null) resource.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    resource.Add("Initialized", resources.IsInitialized.ToString());
                    if (resources is ResourceModule concreteResource) concreteResource.CollectDiagnostics(resource);
                    else resource.Add("CacheAndLeases", "Unavailable");
                }
            }
            catch (Exception error) { resource.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(resource);

            var pool = new DiagnosticSection { Name = "PrefabPool" };
            try
            {
                IPrefabPoolModule pools = GameEntry.Get<IPrefabPoolModule>();
                if (pools == null) pool.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    pool.Add("Pools", pools.Count.ToString());
                    if (pools is PrefabPoolModule concretePool) concretePool.CollectDiagnostics(pool);
                    else pool.Add("Instances", "Unavailable");
                }
            }
            catch (Exception error) { pool.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(pool);

            var audio = new DiagnosticSection { Name = "Audio" };
            try
            {
                IAudioModule sounds = GameEntry.Get<IAudioModule>();
                if (sounds == null) audio.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else
                {
                    foreach (AudioChannel channel in new[] { AudioChannel.Music, AudioChannel.Sfx, AudioChannel.Voice })
                        audio.Add(channel + "Active", sounds.GetActiveCount(channel).ToString());
                    if (sounds is AudioModule concreteAudio) audio.Add("Paused", concreteAudio.IsPaused.ToString());
                }
            }
            catch (Exception error) { audio.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(audio);

            var save = new DiagnosticSection { Name = "Save" };
            try
            {
                ISaveModule saves = GameEntry.Get<ISaveModule>();
                if (saves == null) save.Add("Status", "Unavailable", DiagnosticSeverity.Warning);
                else if (saves is SaveModule concreteSave) concreteSave.CollectDiagnostics(save);
                else save.Add("Sessions", "Unavailable");
            }
            catch (Exception error) { save.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(save);

            var timer = new DiagnosticSection { Name = "TimerAndTween" };
            try
            {
                ITimerModule timers = GameEntry.Get<ITimerModule>();
                if (timers is TimerModule concreteTimer) timer.Add("Timers", concreteTimer.ActiveCount.ToString());
                else timer.Add("Timers", "Unavailable");
                ITweenModule tweens = GameEntry.Get<ITweenModule>();
                timer.Add("Tweens", tweens == null ? "Unavailable" : tweens.ActiveCount.ToString());
            }
            catch (Exception error) { timer.Error = "采集失败：" + error.GetType().Name; }
            sections.Add(timer);
        }

        /// <summary>
        /// 可释放的诊断来源登记。
        /// </summary>
        private sealed class ProviderRegistration : IDisposable
        {
            private DiagnosticsModule _owner;
            internal IDiagnosticProvider Provider;
            internal bool IsActive => _owner != null;

            /// <summary>
            /// 创建来源登记。
            /// </summary>
            /// <param name="owner">所属诊断模块。</param>
            /// <param name="provider">诊断来源。</param>
            internal ProviderRegistration(DiagnosticsModule owner, IDiagnosticProvider provider) { _owner = owner; Provider = provider; }

            /// <summary>
            /// 幂等取消登记。
            /// </summary>
            public void Dispose()
            {
                DiagnosticsModule owner = _owner;
                if (owner == null) return;
                _owner = null;
                owner._providers.Remove(this);
                owner._reportedFailures.Remove(Provider);
                Provider = null;
            }
        }
    }
}
