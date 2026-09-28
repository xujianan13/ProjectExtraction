using UnityEngine;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 项目统一标签组件，复用 UGUI Toggle 与 ToggleGroup 的单选语义。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Tab")]
    public sealed class GameTab : Toggle
    {
        /// <summary>
        /// 获取或设置当前标签是否选中。
        /// </summary>
        public bool IsSelected
        {
            get => isOn;
            set => isOn = value;
        }

        /// <summary>
        /// 设置选中状态且不触发 onValueChanged 回调。
        /// </summary>
        /// <param name="selected">是否选中。</param>
        public void SetSelectedWithoutNotify(bool selected) => SetIsOnWithoutNotify(selected);
    }
}
