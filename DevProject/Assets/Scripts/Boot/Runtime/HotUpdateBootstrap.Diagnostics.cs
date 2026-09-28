using System.Globalization;
using GameFramework;
using YooAsset;

namespace MiniGame.Boot
{
    /// <summary>
    /// 将宿主热更状态接入通用诊断，避免框架引用项目启动层。
    /// </summary>
    public sealed partial class HotUpdateBootstrap : IDiagnosticProvider
    {
        private ScopeRegistration diagnosticLifetime;
        private UpdateProgress diagnosticProgress;
        private bool hasDiagnosticProgress;

        /// <summary>
        /// 诊断分区名称。
        /// </summary>
        string IDiagnosticProvider.Name => "Hot Update";

        /// <summary>
        /// 在框架运行后登记提供者，并随宿主销毁或框架停止释放。
        /// </summary>
        private void RegisterDiagnostics()
        {
            IDiagnosticsModule diagnostics = GameEntry.Get<IDiagnosticsModule>();
            if (diagnostics == null || diagnosticLifetime != null) return;
            GameEntry.ApplicationScope.TryOwn(diagnostics.RegisterProvider(this), out diagnosticLifetime);
        }

        /// <summary>
        /// 读取当前热更事务和启动状态，不触发检查、下载或版本切换。
        /// </summary>
        /// <param name="section">本次快照中的热更分区。</param>
        void IDiagnosticProvider.Collect(DiagnosticSection section)
        {
            section.Add("Package", HotUpdatePackageNames.Game);
            section.Add("Mode", settings != null ? settings.ResolvePlayMode().ToString() : "Not initialized");
            section.Add("Game started", HasLaunchedGame.ToString());
            section.Add("Process requires restart before relaunch", HotUpdateLauncher.RequiresRestart.ToString());
            section.Add("Last update phase", hasDiagnosticProgress ? diagnosticProgress.Phase.ToString() : "Not started");
            if (hasDiagnosticProgress)
            {
                section.Add("Content progress", diagnosticProgress.NormalizedProgress.ToString("P0", CultureInfo.InvariantCulture));
                section.Add("Downloaded files", $"{diagnosticProgress.DownloadedCount}/{diagnosticProgress.TotalCount}");
            }
            if (YooAssets.TryGetPackage(HotUpdatePackageNames.Game, out ResourcePackage package) && package.PackageValid)
                section.Add("Active manifest", package.GetPackageVersion());
            if (versionStateStore == null) return;
            PackageVersionState state = versionStateStore.Load(HotUpdatePackageNames.Game);
            section.Add("Committed version", state.CurrentVersion);
            section.Add("Last good version", state.LastGoodVersion);
            section.Add("Pending version", state.PendingVersion, state.HasPending ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info);
            section.Add("Rejected version", state.RejectedVersion);
            section.Add("Last failure", state.LastFailureCode.ToString(),
                state.LastFailureCode == UpdateErrorCode.None ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning);
        }
    }
}
