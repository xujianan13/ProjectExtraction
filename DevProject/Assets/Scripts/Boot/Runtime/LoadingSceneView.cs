using System;
using UnityEngine;
using UnityEngine.UI;

namespace MiniGame.Boot
{
    /// <summary>
    /// 显示内容更新进度及失败后的重试入口。
    /// </summary>
    public sealed class LoadingSceneView : MonoBehaviour
    {
        [SerializeField] private Text statusText;
        [SerializeField] private Text progressText;
        [SerializeField] private Image progressFill;
        [SerializeField] private GameObject failurePanel;
        [SerializeField] private Text failureText;
        [SerializeField] private Button retryButton;

        /// <summary>
        /// 当前加载场景中的视图实例。
        /// </summary>
        public static LoadingSceneView Instance { get; private set; }

        /// <summary>
        /// 用户点击重试按钮时触发。
        /// </summary>
        public event Action RetryRequested;

        /// <summary>
        /// 绑定由场景构建器创建的界面控件。
        /// </summary>
        /// <param name="status">当前步骤文字。</param>
        /// <param name="progress">百分比文字。</param>
        /// <param name="fill">进度条填充图片。</param>
        /// <param name="failure">失败信息容器。</param>
        /// <param name="failureMessage">失败说明文字。</param>
        /// <param name="retry">重试按钮。</param>
        public void Bind(Text status, Text progress, Image fill, GameObject failure, Text failureMessage, Button retry)
        {
            statusText = status;
            progressText = progress;
            progressFill = fill;
            failurePanel = failure;
            failureText = failureMessage;
            retryButton = retry;
        }

        /// <summary>
        /// 更新当前加载步骤文字。
        /// </summary>
        /// <param name="message">展示给玩家的步骤说明。</param>
        public void SetStatus(string message)
        {
            if (statusText != null) statusText.text = message ?? string.Empty;
        }

        /// <summary>
        /// 更新范围为零到一的加载进度。
        /// </summary>
        /// <param name="value">加载进度。</param>
        public void SetProgress(float value)
        {
            var progress = Mathf.Clamp01(value);
            if (progressFill != null)
            {
                progressFill.fillAmount = progress;
                progressFill.rectTransform.anchorMax = new Vector2(progress, 1f);
            }
            if (progressText != null) progressText.text = $"{Mathf.RoundToInt(progress * 100f)}%";
        }

        /// <summary>
        /// 显示更新失败信息。
        /// </summary>
        /// <param name="message">失败原因。</param>
        /// <param name="canRetry">是否允许玩家重试。</param>
        public void ShowFailure(string message, bool canRetry)
        {
            if (failureText != null) failureText.text = message ?? string.Empty;
            if (retryButton != null) retryButton.gameObject.SetActive(canRetry);
            if (failurePanel != null) failurePanel.SetActive(true);
        }

        /// <summary>
        /// 隐藏上一次加载失败的提示。
        /// </summary>
        public void HideFailure()
        {
            if (failurePanel != null) failurePanel.SetActive(false);
        }

        /// <summary>
        /// 注册视图及按钮事件。
        /// </summary>
        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            if (retryButton != null) retryButton.onClick.AddListener(RequestRetry);
            HideFailure();
            SetProgress(0f);
        }

        /// <summary>
        /// 释放视图及按钮事件。
        /// </summary>
        private void OnDestroy()
        {
            if (retryButton != null) retryButton.onClick.RemoveListener(RequestRetry);
            if (Instance == this) Instance = null;
        }

        /// <summary>
        /// 将按钮操作转发给加载流程。
        /// </summary>
        private void RequestRetry()
        {
            RetryRequested?.Invoke();
        }
    }
}
