using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace GameFramework
{
    /// <summary>
    /// 只读进度条组件，组合 GameImage 填充图和可选 GameText 文本。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Progress Bar")]
    public sealed class GameProgressBar : UIBehaviour
    {
        [SerializeField] private GameImage _fillImage;
        [SerializeField] private GameText _valueText;
        [SerializeField] private float _minValue;
        [SerializeField] private float _maxValue = 1f;
        [SerializeField] private float _value = 1f;
        [SerializeField] private bool _wholeNumbers;
        [SerializeField] private GameProgressTextMode _textMode = GameProgressTextMode.Percentage;
        [SerializeField, Range(0, 3)] private int _decimalPlaces;

        /// <summary>
        /// 获取当前最小值。
        /// </summary>
        public float MinValue => _minValue;

        /// <summary>
        /// 获取当前最大值。
        /// </summary>
        public float MaxValue => _maxValue;

        /// <summary>
        /// 获取或设置当前值，赋值会被限制在有效范围内。
        /// </summary>
        public float Value
        {
            get => _value;
            set
            {
                float normalizedValue = NormalizeValue(value);
                if (Mathf.Approximately(_value, normalizedValue))
                    return;

                _value = normalizedValue;
                RefreshVisuals();
            }
        }

        /// <summary>
        /// 获取或设置 [0, 1] 范围内的归一化进度。
        /// </summary>
        public float NormalizedValue
        {
            get => Mathf.InverseLerp(_minValue, _maxValue, _value);
            set => Value = Mathf.Lerp(_minValue, _maxValue, Mathf.Clamp01(value));
        }

        /// <summary>
        /// 获取或设置是否将进度值约束为整数。
        /// </summary>
        public bool WholeNumbers
        {
            get => _wholeNumbers;
            set
            {
                if (_wholeNumbers == value)
                    return;

                _wholeNumbers = value;
                _value = NormalizeValue(_value);
                RefreshVisuals();
            }
        }

        /// <summary>
        /// 获取或设置文本显示模式。
        /// </summary>
        public GameProgressTextMode TextMode
        {
            get => _textMode;
            set
            {
                if (_textMode == value)
                    return;

                _textMode = value;
                RefreshText();
            }
        }

        /// <summary>
        /// 获取或设置文本小数位数，允许范围为 0 到 3。
        /// </summary>
        public int DecimalPlaces
        {
            get => _decimalPlaces;
            set
            {
                int clampedValue = Mathf.Clamp(value, 0, 3);
                if (_decimalPlaces == clampedValue)
                    return;

                _decimalPlaces = clampedValue;
                RefreshText();
            }
        }

        /// <summary>
        /// 配置进度范围。
        /// </summary>
        /// <param name="minimum">最小值。</param>
        /// <param name="maximum">最大值，必须大于最小值。</param>
        public void SetRange(float minimum, float maximum)
        {
            if (maximum <= minimum)
                throw new ArgumentOutOfRangeException(nameof(maximum), "进度条最大值必须大于最小值。");

            _minValue = minimum;
            _maxValue = maximum;
            _value = NormalizeValue(_value);
            RefreshVisuals();
        }

        /// <summary>
        /// 绑定填充图与可选文本组件。
        /// </summary>
        /// <param name="fillImage">用于显示 fillAmount 的填充图。</param>
        /// <param name="valueText">用于显示进度文本的可选组件。</param>
        public void SetVisuals(GameImage fillImage, GameText valueText = null)
        {
            _fillImage = fillImage != null
                ? fillImage
                : throw new ArgumentNullException(nameof(fillImage));
            _valueText = valueText;
            RefreshVisuals();
        }

        /// <summary>
        /// 立即刷新填充图和文本。
        /// </summary>
        public void RefreshVisuals()
        {
            if (_fillImage != null)
                _fillImage.fillAmount = NormalizedValue;

            RefreshText();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            RefreshVisuals();
        }

#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            if (_maxValue <= _minValue)
                _maxValue = _minValue + 1f;

            _decimalPlaces = Mathf.Clamp(_decimalPlaces, 0, 3);
            _value = NormalizeValue(_value);
            RefreshVisuals();
        }
#endif

        private float NormalizeValue(float value)
        {
            if (_wholeNumbers)
                value = Mathf.Round(value);

            return Mathf.Clamp(value, _minValue, _maxValue);
        }

        private void RefreshText()
        {
            if (_valueText == null)
                return;

            int decimalPlaces = _wholeNumbers ? 0 : _decimalPlaces;
            switch (_textMode)
            {
                case GameProgressTextMode.None:
                    _valueText.SetTextOrEmpty(null);
                    break;
                case GameProgressTextMode.Percentage:
                    SetPercentageText(NormalizedValue * 100f, decimalPlaces);
                    break;
                case GameProgressTextMode.Value:
                    SetValueText(_value, decimalPlaces);
                    break;
                case GameProgressTextMode.ValueAndMaximum:
                    SetValueAndMaximumText(_value, _maxValue, decimalPlaces);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(_textMode), _textMode, "未知的进度文本模式。");
            }
        }

        private void SetPercentageText(float percentage, int decimalPlaces)
        {
            switch (decimalPlaces)
            {
                case 0:
                    _valueText.SetText("{0:0}%", percentage);
                    break;
                case 1:
                    _valueText.SetText("{0:0.0}%", percentage);
                    break;
                case 2:
                    _valueText.SetText("{0:0.00}%", percentage);
                    break;
                default:
                    _valueText.SetText("{0:0.000}%", percentage);
                    break;
            }
        }

        private void SetValueText(float value, int decimalPlaces)
        {
            switch (decimalPlaces)
            {
                case 0:
                    _valueText.SetText("{0:0}", value);
                    break;
                case 1:
                    _valueText.SetText("{0:0.0}", value);
                    break;
                case 2:
                    _valueText.SetText("{0:0.00}", value);
                    break;
                default:
                    _valueText.SetText("{0:0.000}", value);
                    break;
            }
        }

        private void SetValueAndMaximumText(float value, float maximum, int decimalPlaces)
        {
            switch (decimalPlaces)
            {
                case 0:
                    _valueText.SetText("{0:0}/{1:0}", value, maximum);
                    break;
                case 1:
                    _valueText.SetText("{0:0.0}/{1:0.0}", value, maximum);
                    break;
                case 2:
                    _valueText.SetText("{0:0.00}/{1:0.00}", value, maximum);
                    break;
                default:
                    _valueText.SetText("{0:0.000}/{1:0.000}", value, maximum);
                    break;
            }
        }
    }
}
