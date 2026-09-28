namespace GameFramework
{
    /// <summary>
    /// 可插入四叉树的对象契约。
    /// </summary>
    public interface IQuadTreeItem
    {
        /// <summary>对象当前占用的二维轴对齐包围盒。</summary>
        QuadTreeRect QuadTreeBounds { get; }
    }
}
