namespace GameFramework
{
    /// <summary>
    /// 四叉树使用的二维轴对齐矩形。
    /// </summary>
    public struct QuadTreeRect
    {
        /// <summary>零矩形。</summary>
        public static readonly QuadTreeRect Zero = new QuadTreeRect();

        /// <summary>左下角 X 坐标。</summary>
        public float X { get; set; }

        /// <summary>左下角 Y 坐标。</summary>
        public float Y { get; set; }

        /// <summary>宽度。</summary>
        public float Width { get; set; }

        /// <summary>高度。</summary>
        public float Height { get; set; }

        /// <summary>
        /// 创建二维轴对齐矩形。
        /// </summary>
        /// <param name="x">左下角 X 坐标。</param>
        /// <param name="y">左下角 Y 坐标。</param>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        public QuadTreeRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>中心点 X 坐标。</summary>
        public float CenterX
        {
            get => X + Width * 0.5f;
            set => X = value - Width * 0.5f;
        }

        /// <summary>中心点 Y 坐标。</summary>
        public float CenterY
        {
            get => Y + Height * 0.5f;
            set => Y = value - Height * 0.5f;
        }

        /// <summary>最小 X 坐标。</summary>
        public float MinX
        {
            get => X;
            set => X = value;
        }

        /// <summary>最大 X 坐标。</summary>
        public float MaxX
        {
            get => X + Width;
            set => X = value - Width;
        }

        /// <summary>最小 Y 坐标。</summary>
        public float MinY
        {
            get => Y;
            set => Y = value;
        }

        /// <summary>最大 Y 坐标。</summary>
        public float MaxY
        {
            get => Y + Height;
            set => Y = value - Height;
        }

        /// <summary>
        /// 设置矩形。
        /// </summary>
        /// <param name="x">左下角 X 坐标。</param>
        /// <param name="y">左下角 Y 坐标。</param>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        public void Set(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        /// <summary>
        /// 设置中心点与尺寸。
        /// </summary>
        /// <param name="centerX">中心点 X 坐标。</param>
        /// <param name="centerY">中心点 Y 坐标。</param>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        public void SetCenter(float centerX, float centerY, float width, float height)
        {
            Width = width;
            Height = height;
            CenterX = centerX;
            CenterY = centerY;
        }

        /// <summary>
        /// 判断当前矩形是否完整包含目标矩形。
        /// </summary>
        /// <param name="rect">目标矩形。</param>
        /// <returns>完整包含时返回 true。</returns>
        public bool Contains(QuadTreeRect rect)
        {
            return MinX <= rect.MinX && MaxX >= rect.MaxX && MinY <= rect.MinY && MaxY >= rect.MaxY;
        }

        /// <summary>
        /// 判断当前矩形是否包含目标点。
        /// </summary>
        /// <param name="x">目标点 X 坐标。</param>
        /// <param name="y">目标点 Y 坐标。</param>
        /// <returns>包含时返回 true。</returns>
        public bool Contains(float x, float y)
        {
            return MinX <= x && MaxX >= x && MinY <= y && MaxY >= y;
        }

        /// <summary>
        /// 判断当前矩形是否与目标矩形相交。
        /// </summary>
        /// <param name="rect">目标矩形。</param>
        /// <returns>相交时返回 true。</returns>
        public bool Intersects(QuadTreeRect rect)
        {
            return MinX <= rect.MaxX && MaxX >= rect.MinX && MinY <= rect.MaxY && MaxY >= rect.MinY;
        }

        /// <summary>
        /// 按中心点等比扩展矩形。
        /// </summary>
        /// <param name="factor">扩展倍数，1 表示不扩展。</param>
        /// <returns>扩展后的矩形。</returns>
        public QuadTreeRect Expand(float factor)
        {
            if (factor == 1f) return this;

            float newWidth = Width * factor;
            float newHeight = Height * factor;
            return new QuadTreeRect(
                CenterX - newWidth * 0.5f,
                CenterY - newHeight * 0.5f,
                newWidth,
                newHeight);
        }
    }
}
