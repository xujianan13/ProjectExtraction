using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace GameFramework.Tests.Utilities.QuadTree
{
    /// <summary>QuadTree 插入、移除、查询与移动测试。</summary>
    [TestFixture]
    public sealed class QuadTreeTests
    {
        /// <summary>插入后应能查询到相交对象。</summary>
        [Test]
        public void Query_InsertedItems_ReturnsIntersectingItems()
        {
            var tree = CreateTree();
            var a = new TestItem(10f, 10f, 2f, 2f);
            var b = new TestItem(70f, 70f, 2f, 2f);

            Assert.IsTrue(tree.Insert(a));
            Assert.IsTrue(tree.Insert(b));

            var results = new List<TestItem>(4);
            int added = tree.Query(new QuadTreeRect(8f, 8f, 10f, 10f), results);

            Assert.AreEqual(1, added);
            Assert.AreEqual(1, results.Count);
            Assert.AreSame(a, results[0]);
        }

        /// <summary>Query 应追加结果而不是清空调用方列表。</summary>
        [Test]
        public void Query_WithExistingResults_AppendsItems()
        {
            var tree = CreateTree();
            var existing = new TestItem(200f, 200f, 1f, 1f);
            var item = new TestItem(20f, 20f, 1f, 1f);
            tree.Insert(item);

            var results = new List<TestItem>(4) { existing };
            int added = tree.Query(new QuadTreeRect(19f, 19f, 4f, 4f), results);

            Assert.AreEqual(1, added);
            Assert.AreEqual(2, results.Count);
            Assert.AreSame(existing, results[0]);
            Assert.AreSame(item, results[1]);
        }

        /// <summary>Retrieve 传入 null 时应返回新列表。</summary>
        [Test]
        public void Retrieve_NullResults_ReturnsNewList()
        {
            var tree = CreateTree();
            var item = new TestItem(1f, 1f, 1f, 1f);
            tree.Insert(item);

            List<TestItem> results = tree.Retrieve(new QuadTreeRect(0f, 0f, 4f, 4f));

            Assert.AreEqual(1, results.Count);
            Assert.AreSame(item, results[0]);
        }

        /// <summary>构造时可预热节点池。</summary>
        [Test]
        public void Construct_WithInitialNodeCapacity_WarmsNodePool()
        {
            var tree = new QuadTree<TestItem>(
                new QuadTreeRect(0f, 0f, 100f, 100f),
                initialNodeCapacity: 8);

            Assert.AreEqual(8, tree.CachedNodeCount);
        }

        /// <summary>WarmupNodes 与 ClearNodeCache 应管理独立节点池。</summary>
        [Test]
        public void WarmupNodes_AndClearNodeCache_ManageNodePool()
        {
            var tree = CreateTree();

            tree.WarmupNodes(6);
            Assert.AreEqual(6, tree.CachedNodeCount);

            tree.WarmupNodes(3);
            Assert.AreEqual(6, tree.CachedNodeCount);

            tree.ClearNodeCache();
            Assert.AreEqual(0, tree.CachedNodeCount);
        }

        /// <summary>移除后查询不应再返回对象。</summary>
        [Test]
        public void Remove_ExistingItem_RemovesFromQueries()
        {
            var tree = CreateTree();
            var item = new TestItem(10f, 10f, 2f, 2f);
            tree.Insert(item);

            Assert.IsTrue(tree.Remove(item));

            var results = new List<TestItem>(4);
            Assert.AreEqual(0, tree.Query(new QuadTreeRect(0f, 0f, 20f, 20f), results));
            Assert.AreEqual(0, tree.Count);
        }

        /// <summary>移除后对象数量低于阈值时应合并空闲子节点。</summary>
        [Test]
        public void Remove_ItemsBelowThreshold_CollapsesChildren()
        {
            var tree = new QuadTree<TestItem>(
                new QuadTreeRect(0f, 0f, 100f, 100f),
                maxDepth: 4,
                maxItemsPerNode: 1,
                looseFactor: 1f);
            var a = new TestItem(10f, 10f, 1f, 1f);
            var b = new TestItem(70f, 70f, 1f, 1f);
            tree.Insert(a);
            tree.Insert(b);

            Assert.IsTrue(tree.Remove(a));

            var results = new List<TestItem>(4);
            tree.Query(new QuadTreeRect(69f, 69f, 4f, 4f), results);

            Assert.AreEqual(1, tree.Count);
            Assert.AreEqual(4, tree.CachedNodeCount);
            Assert.AreEqual(1, results.Count);
            Assert.AreSame(b, results[0]);
        }

        /// <summary>对象移动后 Relocate 应更新索引位置。</summary>
        [Test]
        public void Relocate_MovedItem_UpdatesQueryResult()
        {
            var tree = CreateTree();
            var item = new TestItem(10f, 10f, 2f, 2f);
            tree.Insert(item);

            item.SetBounds(80f, 80f, 2f, 2f);
            Assert.IsTrue(tree.Relocate(item));

            var oldResults = new List<TestItem>(4);
            var newResults = new List<TestItem>(4);
            tree.Query(new QuadTreeRect(8f, 8f, 10f, 10f), oldResults);
            tree.Query(new QuadTreeRect(78f, 78f, 10f, 10f), newResults);

            Assert.AreEqual(0, oldResults.Count);
            Assert.AreEqual(1, newResults.Count);
            Assert.AreSame(item, newResults[0]);
        }

        /// <summary>对象移出根节点松散范围后 Relocate 应从树中移除。</summary>
        [Test]
        public void Relocate_OutsideRootLooseBounds_RemovesItem()
        {
            var tree = CreateTree(looseFactor: 1f);
            var item = new TestItem(10f, 10f, 2f, 2f);
            tree.Insert(item);

            item.SetBounds(120f, 120f, 2f, 2f);
            Assert.IsFalse(tree.Relocate(item));

            Assert.IsFalse(tree.Contains(item));
            Assert.AreEqual(0, tree.Count);
        }

        /// <summary>跨象限大对象应留在父节点且不会漏查。</summary>
        [Test]
        public void Query_LargeItemCrossesCenter_ReturnsItem()
        {
            var tree = new QuadTree<TestItem>(
                new QuadTreeRect(0f, 0f, 100f, 100f),
                maxDepth: 4,
                maxItemsPerNode: 1,
                looseFactor: 1f);
            var filler = new TestItem(5f, 5f, 1f, 1f);
            var large = new TestItem(45f, 45f, 20f, 20f);

            tree.Insert(filler);
            tree.Insert(large);

            var results = new List<TestItem>(4);
            tree.Query(new QuadTreeRect(60f, 60f, 10f, 10f), results);

            Assert.Contains(large, results);
        }

        /// <summary>重复插入同一对象应按当前位置重新定位，不重复计数。</summary>
        [Test]
        public void Insert_ExistingItem_RelocatesWithoutDuplicating()
        {
            var tree = CreateTree();
            var item = new TestItem(10f, 10f, 2f, 2f);
            tree.Insert(item);

            item.SetBounds(40f, 40f, 2f, 2f);
            Assert.IsTrue(tree.Insert(item));

            var results = new List<TestItem>(4);
            tree.Query(new QuadTreeRect(38f, 38f, 10f, 10f), results);

            Assert.AreEqual(1, tree.Count);
            Assert.AreEqual(1, results.Count);
            Assert.AreSame(item, results[0]);
        }

        /// <summary>构造参数非法时应抛出对应异常。</summary>
        [Test]
        public void Construct_InvalidArguments_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuadTree<TestItem>(new QuadTreeRect(0f, 0f, 0f, 1f)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuadTree<TestItem>(new QuadTreeRect(0f, 0f, 1f, 1f), maxDepth: -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuadTree<TestItem>(new QuadTreeRect(0f, 0f, 1f, 1f), maxItemsPerNode: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuadTree<TestItem>(new QuadTreeRect(0f, 0f, 1f, 1f), looseFactor: 0.9f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new QuadTree<TestItem>(new QuadTreeRect(0f, 0f, 1f, 1f), initialNodeCapacity: -1));
        }

        /// <summary>Insert 与 Query 的 null 参数应抛异常。</summary>
        [Test]
        public void NullArguments_Throw()
        {
            var tree = CreateTree();

            Assert.Throws<ArgumentNullException>(() => tree.Insert(null));
            Assert.Throws<ArgumentNullException>(() => tree.Query(new QuadTreeRect(0f, 0f, 1f, 1f), null));
        }

        private static QuadTree<TestItem> CreateTree(float looseFactor = 1.5f)
        {
            return new QuadTree<TestItem>(
                new QuadTreeRect(0f, 0f, 100f, 100f),
                maxDepth: 4,
                maxItemsPerNode: 2,
                looseFactor: looseFactor);
        }

        private sealed class TestItem : IQuadTreeItem
        {
            private QuadTreeRect _bounds;

            public TestItem(float x, float y, float width, float height)
            {
                _bounds = new QuadTreeRect(x, y, width, height);
            }

            public QuadTreeRect QuadTreeBounds => _bounds;

            public void SetBounds(float x, float y, float width, float height)
            {
                _bounds.Set(x, y, width, height);
            }
        }
    }
}
