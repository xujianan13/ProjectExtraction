using UnityEngine;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 项目统一图片组件，提供基于 UGUI Image 的稳定业务引用类型。
    /// </summary>
    [AddComponentMenu("Game/UI/Game Image")]
    public sealed class GameImage : Image
    {
        /// <summary>
        /// 设置显示精灵，并可选同步原生尺寸。
        /// </summary>
        /// <param name="value">需要显示的精灵，允许为 null。</param>
        /// <param name="setNativeSize">是否在精灵非空时同步原生尺寸。</param>
        public void SetSprite(Sprite value, bool setNativeSize = false)
        {
            sprite = value;
            if (setNativeSize && value != null)
                SetNativeSize();
        }

        /// <summary>
        /// 清除当前显示精灵。
        /// </summary>
        public void ClearSprite() => sprite = null;
    }
}
