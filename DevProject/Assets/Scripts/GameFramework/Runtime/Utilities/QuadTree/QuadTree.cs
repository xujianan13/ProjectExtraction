using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GameFramework
{
    /// <summary>
    /// 非线程安全的二维 loose 四叉树，适用于 Unity 主线程上的空间检索。
    /// </summary>
    /// <typeparam name="T">空间对象类型。</typeparam>
    public sealed class QuadTree<T> where T : class, IQuadTreeItem
    {
        private const int DefaultMaxDepth = 8;
        private const int DefaultMaxItemsPerNode = 8;
        private const float DefaultLooseFactor = 1.5f;

        private readonly int _maxDepth;
        private readonly int _maxItemsPerNode;
        private readonly float _looseFactor;
        private readonly Dictionary<T, Node> _owners;
        private readonly NodePool _nodePool;
        private readonly Node _root;

        /// <summary>
        /// 创建四叉树。
        /// </summary>
        /// <param name="bounds">根节点范围。</param>
        /// <param name="maxDepth">最大深度。</param>
        /// <param name="maxItemsPerNode">单节点对象数阈值，超过后尝试拆分。</param>
        /// <param name="looseFactor">节点松散范围倍数，必须大于等于 1。</param>
        /// <param name="initialCapacity">对象映射初始容量。</param>
        /// <param name="initialNodeCapacity">节点池初始容量。</param>
        public QuadTree(
            QuadTreeRect bounds,
            int maxDepth = DefaultMaxDepth,
            int maxItemsPerNode = DefaultMaxItemsPerNode,
            float looseFactor = DefaultLooseFactor,
            int initialCapacity = 0,
            int initialNodeCapacity = 0)
        {
            if (bounds.Width <= 0f) throw new ArgumentOutOfRangeException(nameof(bounds));
            if (bounds.Height <= 0f) throw new ArgumentOutOfRangeException(nameof(bounds));
            if (maxDepth < 0) throw new ArgumentOutOfRangeException(nameof(maxDepth));
            if (maxItemsPerNode <= 0) throw new ArgumentOutOfRangeException(nameof(maxItemsPerNode));
            if (looseFactor < 1f) throw new ArgumentOutOfRangeException(nameof(looseFactor));
            if (initialCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            if (initialNodeCapacity < 0) throw new ArgumentOutOfRangeException(nameof(initialNodeCapacity));

            _maxDepth = maxDepth;
            _maxItemsPerNode = maxItemsPerNode;
            _looseFactor = looseFactor;
            _owners = new Dictionary<T, Node>(initialCapacity, ReferenceEqualityComparer.Instance);
            _nodePool = new NodePool(_maxItemsPerNode, initialNodeCapacity);
            _root = new Node(_maxItemsPerNode);
            _root.Reset(bounds, 0, _looseFactor, null);
        }

        /// <summary>根节点范围。</summary>
        public QuadTreeRect Bounds => _root.Bounds;

        /// <summary>树中对象数量。</summary>
        public int Count => _owners.Count;

        /// <summary>最大深度。</summary>
        public int MaxDepth => _maxDepth;

        /// <summary>单节点对象数阈值。</summary>
        public int MaxItemsPerNode => _maxItemsPerNode;

        /// <summary>节点松散范围倍数。</summary>
        public float LooseFactor => _looseFactor;

        /// <summary>当前缓存的空闲节点数量。</summary>
        public int CachedNodeCount => _nodePool.Count;

        /// <summary>
        /// 插入对象；对象必须完整落在根节点松散范围内。
        /// </summary>
        /// <param name="item">待插入对象。</param>
        /// <returns>插入成功返回 true；超出根节点松散范围返回 false。</returns>
        public bool Insert(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (_owners.ContainsKey(item)) return Relocate(item);

            QuadTreeRect bounds = item.QuadTreeBounds;
            if (!_root.LooseBounds.Contains(bounds)) return false;

            InsertInto(_root, item, bounds);
            return true;
        }

        /// <summary>
        /// 移除对象。
        /// </summary>
        /// <param name="item">待移除对象。</param>
        /// <returns>成功移除返回 true；对象不存在返回 false。</returns>
        public bool Remove(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!_owners.TryGetValue(item, out Node node)) return false;

            int index = IndexOfReference(node.Items, item);
            if (index >= 0)
            {
                node.Items.RemoveAt(index);
            }

            _owners.Remove(item);
            TryCollapseUp(node);
            return true;
        }

        /// <summary>
        /// 对象位置变化后重新定位。
        /// </summary>
        /// <param name="item">待重新定位对象。</param>
        /// <returns>对象存在且仍在根节点松散范围内返回 true；对象不存在或越界返回 false。</returns>
        public bool Relocate(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            if (!_owners.TryGetValue(item, out Node node)) return false;

            QuadTreeRect bounds = item.QuadTreeBounds;
            if (!_root.LooseBounds.Contains(bounds))
            {
                Remove(item);
                return false;
            }

            Node target = FindBestNode(_root, bounds);
            if (ReferenceEquals(node, target)) return true;

            Remove(item);
            InsertInto(_root, item, bounds);
            return true;
        }

        /// <summary>
        /// 判断对象是否已在树中。
        /// </summary>
        /// <param name="item">待检查对象。</param>
        /// <returns>已在树中返回 true。</returns>
        public bool Contains(T item)
        {
            if (item == null) throw new ArgumentNullException(nameof(item));
            return _owners.ContainsKey(item);
        }

        /// <summary>
        /// 查询与指定区域相交的对象，并追加到结果列表。
        /// </summary>
        /// <param name="area">查询区域。</param>
        /// <param name="results">结果列表，不可为 null；调用方负责按需清空。</param>
        /// <returns>本次追加的对象数量。</returns>
        public int Query(QuadTreeRect area, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            int startCount = results.Count;
            QueryNode(_root, area, results);
            return results.Count - startCount;
        }

        /// <summary>
        /// 查询与指定区域相交的对象。
        /// </summary>
        /// <param name="area">查询区域。</param>
        /// <param name="results">结果列表；传 null 时会分配新列表。</param>
        /// <returns>包含查询结果的列表。</returns>
        public List<T> Retrieve(QuadTreeRect area, List<T> results = null)
        {
            if (results == null)
            {
                results = new List<T>();
            }

            Query(area, results);
            return results;
        }

        /// <summary>
        /// 清空树中所有对象，并回收已拆分节点。
        /// </summary>
        public void Clear()
        {
            ClearChildren(_root);
            _root.Items.Clear();
            _owners.Clear();
        }

        /// <summary>
        /// 预创建节点缓存，减少运行中拆分节点时的分配。
        /// </summary>
        /// <param name="count">希望缓存的节点数量。</param>
        public void WarmupNodes(int count)
        {
            _nodePool.Warmup(count);
        }

        /// <summary>
        /// 清空节点缓存；不影响树中已使用的节点。
        /// </summary>
        public void ClearNodeCache()
        {
            _nodePool.Clear();
        }

        private void InsertInto(Node startNode, T item, QuadTreeRect bounds)
        {
            Node node = startNode;
            while (true)
            {
                if (node.HasChildren && TryGetChild(node, bounds, out Node child))
                {
                    node = child;
                    continue;
                }

                node.Items.Add(item);
                _owners[item] = node;

                if (!node.HasChildren && node.Depth < _maxDepth && node.Items.Count > _maxItemsPerNode)
                {
                    Subdivide(node);
                    Redistribute(node);
                }

                return;
            }
        }

        private Node FindBestNode(Node startNode, QuadTreeRect bounds)
        {
            Node node = startNode;
            while (node.HasChildren && TryGetChild(node, bounds, out Node child))
            {
                node = child;
            }

            return node;
        }

        private void Redistribute(Node node)
        {
            for (int i = node.Items.Count - 1; i >= 0; i--)
            {
                T item = node.Items[i];
                QuadTreeRect bounds = item.QuadTreeBounds;
                if (!TryGetChild(node, bounds, out Node child)) continue;

                node.Items.RemoveAt(i);
                InsertInto(child, item, bounds);
            }
        }

        private void QueryNode(Node node, QuadTreeRect area, List<T> results)
        {
            if (!node.LooseBounds.Intersects(area)) return;

            if (node.HasChildren)
            {
                QueryNode(node.BottomLeft, area, results);
                QueryNode(node.BottomRight, area, results);
                QueryNode(node.TopLeft, area, results);
                QueryNode(node.TopRight, area, results);
            }

            List<T> items = node.Items;
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                T item = items[i];
                if (item.QuadTreeBounds.Intersects(area))
                {
                    results.Add(item);
                }
            }
        }

        private void Subdivide(Node node)
        {
            float halfWidth = node.Bounds.Width * 0.5f;
            float halfHeight = node.Bounds.Height * 0.5f;
            float x = node.Bounds.X;
            float y = node.Bounds.Y;
            float centerX = x + halfWidth;
            float centerY = y + halfHeight;
            int depth = node.Depth + 1;

            node.BottomLeft = RentNode(new QuadTreeRect(x, y, halfWidth, halfHeight), depth, node);
            node.BottomRight = RentNode(new QuadTreeRect(centerX, y, halfWidth, halfHeight), depth, node);
            node.TopLeft = RentNode(new QuadTreeRect(x, centerY, halfWidth, halfHeight), depth, node);
            node.TopRight = RentNode(new QuadTreeRect(centerX, centerY, halfWidth, halfHeight), depth, node);
        }

        private bool TryGetChild(Node node, QuadTreeRect bounds, out Node child)
        {
            bool left = bounds.CenterX <= node.Bounds.CenterX;
            bool bottom = bounds.CenterY <= node.Bounds.CenterY;

            child = left ? (bottom ? node.BottomLeft : node.TopLeft) : (bottom ? node.BottomRight : node.TopRight);
            return child != null && child.LooseBounds.Contains(bounds);
        }

        private static int IndexOfReference(List<T> items, T item)
        {
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(items[i], item)) return i;
            }

            return -1;
        }

        private Node RentNode(QuadTreeRect bounds, int depth, Node parent)
        {
            Node node = _nodePool.Rent();
            node.Reset(bounds, depth, _looseFactor, parent);
            return node;
        }

        private void ReleaseNode(Node node)
        {
            _nodePool.Release(node);
        }

        private void ClearChildren(Node node)
        {
            if (!node.HasChildren) return;

            ClearChildren(node.BottomLeft);
            ClearChildren(node.BottomRight);
            ClearChildren(node.TopLeft);
            ClearChildren(node.TopRight);

            ReleaseSubtree(node.BottomLeft);
            ReleaseSubtree(node.BottomRight);
            ReleaseSubtree(node.TopLeft);
            ReleaseSubtree(node.TopRight);

            node.BottomLeft = null;
            node.BottomRight = null;
            node.TopLeft = null;
            node.TopRight = null;
        }

        private void TryCollapseUp(Node node)
        {
            while (node != null)
            {
                if (node.HasChildren && CanCollapse(node))
                {
                    Collapse(node);
                }

                node = node.Parent;
            }
        }

        private bool CanCollapse(Node node)
        {
            int total = node.Items.Count;
            return CountDescendantItems(node.BottomLeft, ref total)
                && CountDescendantItems(node.BottomRight, ref total)
                && CountDescendantItems(node.TopLeft, ref total)
                && CountDescendantItems(node.TopRight, ref total);
        }

        private bool CountDescendantItems(Node node, ref int total)
        {
            if (node == null) return true;

            total += node.Items.Count;
            if (total > _maxItemsPerNode) return false;

            return CountDescendantItems(node.BottomLeft, ref total)
                && CountDescendantItems(node.BottomRight, ref total)
                && CountDescendantItems(node.TopLeft, ref total)
                && CountDescendantItems(node.TopRight, ref total);
        }

        private void Collapse(Node node)
        {
            MoveSubtreeItems(node.BottomLeft, node);
            MoveSubtreeItems(node.BottomRight, node);
            MoveSubtreeItems(node.TopLeft, node);
            MoveSubtreeItems(node.TopRight, node);

            ReleaseSubtree(node.BottomLeft);
            ReleaseSubtree(node.BottomRight);
            ReleaseSubtree(node.TopLeft);
            ReleaseSubtree(node.TopRight);

            node.BottomLeft = null;
            node.BottomRight = null;
            node.TopLeft = null;
            node.TopRight = null;
        }

        private void MoveSubtreeItems(Node source, Node target)
        {
            if (source == null) return;

            List<T> items = source.Items;
            int count = items.Count;
            for (int i = 0; i < count; i++)
            {
                T item = items[i];
                target.Items.Add(item);
                _owners[item] = target;
            }

            MoveSubtreeItems(source.BottomLeft, target);
            MoveSubtreeItems(source.BottomRight, target);
            MoveSubtreeItems(source.TopLeft, target);
            MoveSubtreeItems(source.TopRight, target);
        }

        private void ReleaseSubtree(Node node)
        {
            if (node == null) return;

            ReleaseSubtree(node.BottomLeft);
            ReleaseSubtree(node.BottomRight);
            ReleaseSubtree(node.TopLeft);
            ReleaseSubtree(node.TopRight);
            ReleaseNode(node);
        }

        private sealed class NodePool
        {
            private readonly int _itemCapacity;
            private readonly Stack<Node> _nodes;

            public NodePool(int itemCapacity, int initialCapacity)
            {
                _itemCapacity = itemCapacity;
                _nodes = new Stack<Node>(initialCapacity);
                Warmup(initialCapacity);
            }

            public int Count => _nodes.Count;

            public Node Rent()
            {
                return _nodes.Count > 0 ? _nodes.Pop() : new Node(_itemCapacity);
            }

            public void Release(Node node)
            {
                if (node == null) throw new ArgumentNullException(nameof(node));
                node.Clear();
                _nodes.Push(node);
            }

            public void Warmup(int count)
            {
                if (count <= _nodes.Count) return;

                for (int i = _nodes.Count; i < count; i++)
                {
                    _nodes.Push(new Node(_itemCapacity));
                }
            }

            public void Clear()
            {
                _nodes.Clear();
            }
        }

        private sealed class Node
        {
            public readonly List<T> Items;

            public QuadTreeRect Bounds;
            public QuadTreeRect LooseBounds;
            public int Depth;
            public Node Parent;
            public Node BottomLeft;
            public Node BottomRight;
            public Node TopLeft;
            public Node TopRight;

            public Node(int itemCapacity)
            {
                Items = new List<T>(itemCapacity);
            }

            public bool HasChildren => BottomLeft != null;

            public void Reset(QuadTreeRect bounds, int depth, float looseFactor, Node parent)
            {
                Bounds = bounds;
                LooseBounds = bounds.Expand(looseFactor);
                Depth = depth;
                Parent = parent;
            }

            public void Clear()
            {
                Items.Clear();
                Parent = null;
                BottomLeft = null;
                BottomRight = null;
                TopLeft = null;
                TopRight = null;
            }
        }

        private sealed class ReferenceEqualityComparer : IEqualityComparer<T>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public bool Equals(T x, T y) => ReferenceEquals(x, y);

            public int GetHashCode(T obj) => RuntimeHelpers.GetHashCode(obj);
        }
    }
}
