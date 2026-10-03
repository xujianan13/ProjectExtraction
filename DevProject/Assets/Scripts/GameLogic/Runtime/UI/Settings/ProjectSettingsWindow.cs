using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Project;
using GameLogic.UI.Common;
using UnityEngine;

namespace GameLogic.UI.Settings
{
    /// <summary>
    /// 音量与画质的本地设置窗口，保存确认后才应用更改。
    /// </summary>
    public sealed class ProjectSettingsWindow : GameWindow
    {
        private ProjectSession session;
        private ProjectPreferences draft;
        private UnityEngine.UI.Text musicValue;
        private UnityEngine.UI.Text soundValue;
        private UnityEngine.UI.Text qualityValue;
        private UnityEngine.UI.Text feedback;
        private UnityEngine.UI.Button saveButton;
        private UnityEngine.UI.Button retryButton;
        private bool operating;
        private bool ownPending;

        /// <summary>
        /// 创建适配竖屏与横屏的滚动设置内容。
        /// </summary>
        protected override void Init()
        {
            RectTransform surface = ProjectUiFactory.Fill(RootTransform, "SettingsContent", new Color(0.035f, 0.07f, 0.11f, 0.98f));
            RectTransform layout = ProjectUiFactory.Vertical(surface, "Layout", 12f);
            ProjectUiFactory.Stretch(layout, 22f);
            RectTransform header = ProjectUiFactory.Horizontal(layout, "Header", 12f);
            ProjectUiFactory.Height(header, 72f);
            UnityEngine.UI.Text title = ProjectUiFactory.Label(header, "设置", 31, TextAnchor.MiddleLeft, Color.white);
            title.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button close = ProjectUiFactory.ActionButton(header, "关闭", () => CloseSelfAsync().Forget());
            close.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 110f;

            UnityEngine.UI.ScrollRect scroll = ProjectUiFactory.Scroll(layout, "SettingsList", out RectTransform content);
            UnityEngine.UI.LayoutElement scrollSize = scroll.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            scrollSize.flexibleHeight = 1f;
            scrollSize.minHeight = 100f;
            musicValue = AddChoice(content, "音乐音量", () => ChangeVolume(true, -0.1f), () => ChangeVolume(true, 0.1f));
            soundValue = AddChoice(content, "音效音量", () => ChangeVolume(false, -0.1f), () => ChangeVolume(false, 0.1f));
            qualityValue = AddChoice(content, "画质档位", () => ChangeQuality(-1), () => ChangeQuality(1));

            saveButton = ProjectUiFactory.ActionButton(layout, "保存", () => SaveAsync().Forget());
            ProjectUiFactory.Height(saveButton.GetComponent<RectTransform>(), 80f);
            retryButton = ProjectUiFactory.ActionButton(layout, "重试保存", () => RetryAsync().Forget());
            ProjectUiFactory.Height(retryButton.GetComponent<RectTransform>(), 80f);
            feedback = ProjectUiFactory.Label(layout, string.Empty, 20, TextAnchor.MiddleLeft, new Color(1f, 0.8f, 0.5f));
            ProjectUiFactory.Height(feedback.rectTransform, 64f);
        }

        /// <summary>
        /// 从会话读取已提交偏好，并订阅保存状态变化。
        /// </summary>
        /// <param name="args">窗口打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            session = ProjectServices.Session;
            draft = session?.Preferences ?? new ProjectPreferences();
            if (session != null)
            {
                session.Changed += Refresh;
                OpenScope.Defer(() => session.Changed -= Refresh);
            }
            else feedback.text = "项目数据尚未就绪。";
        }

        /// <summary>
        /// 显示当前草稿与保存状态。
        /// </summary>
        /// <param name="args">窗口显示参数。</param>
        protected override void Show(GameWindowOpenArgs args) => Refresh();

        /// <summary>
        /// 创建带左右调节按钮的一行设置。
        /// </summary>
        /// <param name="parent">滚动内容根节点。</param>
        /// <param name="caption">设置名称。</param>
        /// <param name="decrease">向前调节动作。</param>
        /// <param name="increase">向后调节动作。</param>
        /// <returns>显示当前值的文本。</returns>
        private static UnityEngine.UI.Text AddChoice(Transform parent, string caption, System.Action decrease, System.Action increase)
        {
            RectTransform row = ProjectUiFactory.Horizontal(parent, caption + "Row", 8f);
            ProjectUiFactory.Height(row, 76f);
            UnityEngine.UI.Text label = ProjectUiFactory.Label(row, caption, 23, TextAnchor.MiddleLeft, Color.white);
            label.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 170f;
            UnityEngine.UI.Button minus = ProjectUiFactory.ActionButton(row, "−", decrease);
            minus.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 68f;
            UnityEngine.UI.Text value = ProjectUiFactory.Label(row, string.Empty, 23, TextAnchor.MiddleCenter, Color.white);
            value.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().flexibleWidth = 1f;
            UnityEngine.UI.Button plus = ProjectUiFactory.ActionButton(row, "+", increase);
            plus.gameObject.AddComponent<UnityEngine.UI.LayoutElement>().preferredWidth = 68f;
            return value;
        }

        /// <summary>
        /// 按十分之一调节草稿音量。
        /// </summary>
        /// <param name="music">是否调节音乐通道。</param>
        /// <param name="delta">音量变化量。</param>
        private void ChangeVolume(bool music, float delta)
        {
            if (draft == null || operating || ownPending) return;
            float current = music ? draft.MusicVolume : draft.SoundVolume;
            float changed = Mathf.Round(Mathf.Clamp01(current + delta) * 10f) / 10f;
            if (music) draft.MusicVolume = changed;
            else draft.SoundVolume = changed;
            Refresh();
        }

        /// <summary>
        /// 在项目默认与运行平台实际提供的画质档位间切换。
        /// </summary>
        /// <param name="direction">切换方向。</param>
        private void ChangeQuality(int direction)
        {
            if (draft == null || operating || ownPending) return;
            draft.QualityLevel = Mathf.Clamp(draft.QualityLevel + direction, -1, QualitySettings.names.Length - 1);
            Refresh();
        }

        /// <summary>
        /// 更新草稿显示与事务按钮。
        /// </summary>
        private void Refresh()
        {
            if (draft == null) return;
            musicValue.text = Mathf.RoundToInt(draft.MusicVolume * 100f) + "%";
            soundValue.text = Mathf.RoundToInt(draft.SoundVolume * 100f) + "%";
            qualityValue.text = draft.QualityLevel < 0 ? "项目默认" :
                draft.QualityLevel < QualitySettings.names.Length ? QualitySettings.names[draft.QualityLevel] : "当前平台不可用";
            saveButton.interactable = session?.CanEditPreferences == true && !operating && !ownPending;
            retryButton.gameObject.SetActive(ownPending && session?.HasPendingSave == true && !operating);
        }

        /// <summary>
        /// 持久化草稿；失败时保留已应用的旧偏好。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid SaveAsync()
        {
            if (session == null || !saveButton.interactable || operating) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.UpdatePreferencesAsync(draft);
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            ownPending = !result.Success && session.HasPendingSave;
            if (result.Success) draft = session.Preferences;
            feedback.text = result.Message;
            Refresh();
        }

        /// <summary>
        /// 重试本窗口未完成的同一份保存候选。
        /// </summary>
        /// <returns>异步操作。</returns>
        private async UniTaskVoid RetryAsync()
        {
            if (session == null || !ownPending || !session.HasPendingSave || operating) return;
            operating = true;
            Refresh();
            ProjectOperationResult result = await session.RetryPendingSaveAsync();
            if (Root == null || State != GameWindowState.Opened) return;
            operating = false;
            ownPending = !result.Success && session.HasPendingSave;
            if (result.Success) draft = session.Preferences;
            feedback.text = result.Message;
            Refresh();
        }
    }
}
