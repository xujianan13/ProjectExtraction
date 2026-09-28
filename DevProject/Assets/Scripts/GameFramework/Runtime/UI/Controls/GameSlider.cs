using System;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 项目统一滑动条组件，封装范围配置与无通知归一化赋值。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Slider")]
    public sealed class GameSlider : Slider
    {
        /// <summary>
        /// 配置有效值范围。
        /// </summary>
        /// <param name="minimum">最小值。</param>
        /// <param name="maximum">最大值，必须大于最小值。</param>
        /// <param name="useWholeNumbers">是否只允许整数。</param>
        public void SetRange(float minimum, float maximum, bool useWholeNumbers = false)
        {
            if (maximum <= minimum)
                throw new ArgumentOutOfRangeException(nameof(maximum), "滑动条最大值必须大于最小值。");

            float currentValue = value;
            wholeNumbers = useWholeNumbers;
            minValue = minimum;
            maxValue = maximum;
            SetValueWithoutNotify(currentValue);
        }

        /// <summary>
        /// 设置归一化值且不触发 onValueChanged 回调。
        /// </summary>
        /// <param name="normalizedValue">归一化值，超出 [0, 1] 的部分会被截断。</param>
        public void SetNormalizedValueWithoutNotify(float normalizedValue)
        {
            float clampedValue = Mathf.Clamp01(normalizedValue);
            SetValueWithoutNotify(Mathf.Lerp(minValue, maxValue, clampedValue));
        }
    }
}
