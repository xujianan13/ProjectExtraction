using Cysharp.Threading.Tasks;
using GameLogic.Gameplay.Expedition;

namespace GameLogic.Project
{
    /// <summary>
    /// 玩家偏好的独立保存事务。
    /// </summary>
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 已提交偏好的独立副本。
        /// </summary>
        public ProjectPreferences Preferences => profile.Preferences?.Copy() ?? new ProjectPreferences();

        /// <summary>
        /// 主界面空闲或单局暂停菜单打开时允许修改偏好。
        /// </summary>
        public bool CanEditPreferences => !disposed && !saving && pending == null &&
            (CanEdit() || CurrentRun?.IsRunning == true && CurrentRun.ActivePanel == ExpeditionPanelKind.Pause);

        /// <summary>
        /// 保存完整偏好，成功后才更改实际音量与质量。
        /// </summary>
        /// <param name="value">设置界面的偏好副本。</param>
        /// <returns>持久化确认结果。</returns>
        public UniTask<ProjectOperationResult> UpdatePreferencesAsync(ProjectPreferences value)
        {
            if (!CanEditPreferences) return Rejected("当前不能保存设置。");
            if (value == null) return Rejected("缺少偏好设置。");
            string invalid = ProjectPreferences.Validate(value);
            if (invalid != null) return Rejected(invalid);
            ProjectSaveData candidate = Candidate();
            candidate.Preferences = value.Copy();
            return CommitAsync(candidate, "设置已保存。", () => profile.Preferences?.Apply());
        }

        /// <summary>
        /// 记录已经看过的引导版本，不降低已有版本。
        /// </summary>
        /// <param name="version">已显示的引导版本。</param>
        /// <returns>持久化确认结果。</returns>
        public UniTask<ProjectOperationResult> MarkGuideSeenAsync(int version)
        {
            if (!CanEditPreferences) return Rejected("当前不能保存引导进度。");
            if (version < 0) return Rejected("引导版本无效。");
            if (version <= Preferences.GuideVersion) return UniTask.FromResult(new ProjectOperationResult(true, "引导已经记录。"));
            ProjectSaveData candidate = Candidate();
            candidate.Preferences ??= new ProjectPreferences();
            candidate.Preferences.GuideVersion = version;
            return CommitAsync(candidate, "引导进度已保存。", () => profile.Preferences?.Apply());
        }
    }
}
