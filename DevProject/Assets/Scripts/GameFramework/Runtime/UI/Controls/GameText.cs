using TMPro;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 项目统一文本组件，以 TextMeshProUGUI 作为文本渲染基线。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Text")]
    public sealed class GameText : TextMeshProUGUI
    {
        /// <summary>
        /// 设置文本，并将 null 规范化为空字符串。
        /// </summary>
        /// <param name="value">需要显示的文本。</param>
        public void SetTextOrEmpty(string value) => text = value ?? string.Empty;
    }
}
