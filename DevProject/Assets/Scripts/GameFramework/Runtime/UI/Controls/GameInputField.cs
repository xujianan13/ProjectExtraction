using TMPro;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 项目统一输入框组件，以 TMP_InputField 作为输入基线。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Input Field")]
    public sealed class GameInputField : TMP_InputField
    {
        /// <summary>
        /// 设置输入值，并将 null 规范化为空字符串。
        /// </summary>
        /// <param name="value">新的输入值。</param>
        /// <param name="notify">是否触发 onValueChanged 回调。</param>
        public void SetValue(string value, bool notify = true)
        {
            value ??= string.Empty;
            if (notify)
                text = value;
            else
                SetTextWithoutNotify(value);
        }

        /// <summary>
        /// 清空输入值。
        /// </summary>
        /// <param name="notify">是否触发 onValueChanged 回调。</param>
        public void ClearValue(bool notify = false) => SetValue(string.Empty, notify);
    }
}
