using System;
using System.Collections;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace GameFramework.Tests.UI
{
    /// <summary>
    /// 虚拟列表与真实 UGUI 组件的集成测试。
    /// </summary>
    [TestFixture]
    public sealed class GameVirtualListTests
    {
        private readonly List<GameObject> _roots = new();

        /// <summary>
        /// 销毁测试创建的列表和条目模板。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            foreach (GameObject root in _roots) if (root != null) UnityEngine.Object.DestroyImmediate(root);
            _roots.Clear();
        }

        /// <summary>
        /// 一万条数据仅实例化视口与缓冲区所需条目，滚动时重绑并释放旧作用域。
        /// </summary>
        [Test]
        public void LargeData_ScrollReusesItemsAndDisposesBindings()
        {
            GameVirtualList list = CreateList(false);
            list.SetCount(10000);
            Assert.LessOrEqual(list.InstanceCount, 6);
            GameVirtualListItem first = list.GetVisibleItem(0);
            Assert.IsNotNull(first);
            LifetimeScope scope = first.BindingScope;

            list.ScrollTo(5000);

            Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
            Assert.IsNull(list.GetVisibleItem(0));
            Assert.IsNotNull(list.GetVisibleItem(5000));
            Assert.LessOrEqual(list.InstanceCount, 6);
        }

        /// <summary>
        /// 数据缩减会夹紧滚动偏移并解除已越界条目的绑定。
        /// </summary>
        [Test]
        public void Shrink_ClampsPositionAndReleasesOldData()
        {
            GameVirtualList list = CreateList(false);
            list.SetCount(10000);
            list.ScrollTo(9999);
            list.SetCount(2);

            Assert.AreEqual(2, list.VisibleCount);
            Assert.AreEqual(2, list.InstanceCount);
            Assert.AreEqual(0f, list.GetComponent<ScrollRect>().content.anchoredPosition.y, 0.01f);
            Assert.IsNotNull(list.GetVisibleItem(0));
            Assert.IsNull(list.GetVisibleItem(9999));
        }

        /// <summary>
        /// 局部刷新生成新绑定作用域，禁用和销毁停止所有绑定。
        /// </summary>
        [Test]
        public void RefreshDisableDestroy_StopBindings()
        {
            GameVirtualList list = CreateList(false);
            list.SetCount(30);
            GameVirtualListItem item = list.GetVisibleItem(0);
            LifetimeScope first = item.BindingScope;
            list.RefreshItem(0);
            Assert.AreEqual(LifetimeScopeState.Disposed, first.State);
            LifetimeScope second = item.BindingScope;
            list.enabled = false;
            Assert.AreEqual(LifetimeScopeState.Disposed, second.State);
            Assert.AreEqual(0, list.VisibleCount);
            list.enabled = true;
            Assert.IsNotNull(list.GetVisibleItem(0));
            LifetimeScope third = list.GetVisibleItem(0).BindingScope;
            UnityEngine.Object.DestroyImmediate(list.gameObject);
            Assert.AreEqual(LifetimeScopeState.Disposed, third.State);
        }

        /// <summary>
        /// 迟到异步结果可通过旧绑定令牌识别并拒绝写入复用条目。
        /// </summary>
        [Test]
        public void LateResult_OldBindingCannotWriteReusedItem()
        {
            VirtualListAsyncProbe.Gate = new UniTaskCompletionSource();
            VirtualListAsyncProbe.OldWrites = 0;
            GameVirtualList list = CreateList(false, true);
            list.SetCount(100);
            GameVirtualListItem item = list.GetVisibleItem(0);
            LifetimeScope oldScope = item.BindingScope;
            list.ScrollTo(50);
            VirtualListAsyncProbe.Gate.TrySetResult();

            Assert.IsTrue(oldScope.Token.IsCancellationRequested);
            Assert.IsFalse(oldScope.IsActive);
            Assert.AreEqual(0, VirtualListAsyncProbe.OldWrites);
        }

        /// <summary>
        /// 视口尺寸变化会更新条目覆盖范围及交叉轴尺寸。
        /// </summary>
        [Test]
        public void ViewportResize_UpdatesVisibleItems()
        {
            GameVirtualList list = CreateList(false);
            list.SetCount(100);
            ScrollRect scroll = list.GetComponent<ScrollRect>();
            scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 600f);
            list.SendMessage("LateUpdate");
            Assert.AreEqual(7, list.VisibleCount);
            scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 420f);
            list.SendMessage("LateUpdate");
            Assert.AreEqual(420f, ((RectTransform)list.GetVisibleItem(0).transform).rect.width, 0.01f);
            scroll.viewport.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);
            list.SendMessage("LateUpdate");
            Assert.AreEqual(2, list.InstanceCount);
        }

        /// <summary>
        /// 重配不能接受另一个 Content 中原有的非托管子对象。
        /// </summary>
        [Test]
        public void Configure_OtherContentWithForeignChild_IsRejected()
        {
            GameVirtualList list = CreateList(false);
            list.SetCount(20);
            int originalCount = list.VisibleCount;
            var otherRoot = new GameObject("OtherScroll", typeof(RectTransform), typeof(ScrollRect));
            _roots.Add(otherRoot);
            var viewport = new GameObject("Viewport", typeof(RectTransform));
            viewport.transform.SetParent(otherRoot.transform, false);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var foreign = new GameObject("Foreign", typeof(RectTransform));
            foreign.transform.SetParent(content.transform, false);
            ScrollRect other = otherRoot.GetComponent<ScrollRect>();
            other.viewport = (RectTransform)viewport.transform;
            other.content = (RectTransform)content.transform;

            GameVirtualListItem prefab = _roots[1].GetComponent<GameVirtualListItem>();
            Assert.Throws<InvalidOperationException>(() => list.Configure(other, prefab, 100f));
            Assert.AreEqual(originalCount, list.VisibleCount);
            Assert.IsTrue(list.GetVisibleItem(0).BindingScope.IsActive);
        }

        /// <summary>
        /// Bind 抛错后不留下活跃条目，后续可以重新构建。
        /// </summary>
        [Test]
        public void BindFailure_RemovesOrphanAndAllowsRetry()
        {
            VirtualListThrowProbe.ThrowOnBind = true;
            VirtualListThrowProbe.LastScope = null;
            GameVirtualList list = CreateList(false, false, true);
            ScrollRect scroll = list.GetComponent<ScrollRect>();

            Assert.Throws<InvalidOperationException>(() => list.SetCount(3));
            Assert.AreEqual(0, list.InstanceCount);
            Assert.AreEqual(0, list.VisibleCount);
            Assert.AreEqual(0, scroll.content.childCount);
            Assert.AreEqual(LifetimeScopeState.Disposed, VirtualListThrowProbe.LastScope.State);

            VirtualListThrowProbe.ThrowOnBind = false;
            list.SetCount(3);
            Assert.AreEqual(3, list.VisibleCount);
        }

        /// <summary>
        /// 单条 Unbind 抛错不会阻断滚动回收或重配时其他作用域释放。
        /// </summary>
        [Test]
        public void UnbindFailure_StillDisposesAllBindings()
        {
            GameVirtualList list = CreateList(false, false, false, true);
            list.SetCount(20);
            LifetimeScope[] oldScopes = new LifetimeScope[4];
            for (int index = 0; index < oldScopes.Length; index++) oldScopes[index] = list.GetVisibleItem(index).BindingScope;
            ScrollRect scroll = list.GetComponent<ScrollRect>();

            list.ScrollTo(10);
            foreach (LifetimeScope scope in oldScopes) Assert.AreEqual(LifetimeScopeState.Disposed, scope.State);
            Assert.IsNotNull(list.GetVisibleItem(10));

            LifetimeScope[] currentScopes = new LifetimeScope[list.VisibleCount];
            int captured = 0;
            for (int index = 9; index <= 13; index++)
            {
                GameVirtualListItem item = list.GetVisibleItem(index);
                if (item != null) currentScopes[captured++] = item.BindingScope;
            }
            GameVirtualListItem prefab = _roots[1].GetComponent<GameVirtualListItem>();
            list.Configure(scroll, prefab, 100f);
            for (int index = 0; index < captured; index++) Assert.AreEqual(LifetimeScopeState.Disposed, currentScopes[index].State);
            Assert.IsNotNull(list.GetVisibleItem(0));
        }
        /// <summary>
        /// 重配时停止旧惯性并重置位置。
        /// </summary>
        [Test]
        public void Configure_StopsPreviousVelocity()
        {
            GameVirtualList list = CreateList(false);
            list.SetCount(50);
            ScrollRect scroll = list.GetComponent<ScrollRect>();
            list.ScrollTo(10);
            scroll.velocity = new Vector2(0f, 800f);
            GameVirtualListItem prefab = _roots[_roots.Count - 1].GetComponent<GameVirtualListItem>();

            list.Configure(scroll, prefab, 100f);

            Assert.AreEqual(Vector2.zero, scroll.velocity);
            Assert.AreEqual(Vector2.zero, scroll.content.anchoredPosition);
        }

        /// <summary>
        /// PlayMode 重配等待销毁期间旧条目立即禁用。
        /// </summary>
        /// <returns>等待一帧完成延迟销毁的测试协程。</returns>
        [UnityTest]
        public IEnumerator Reconfigure_DisablesOldItemsBeforeDeferredDestroy()
        {
            if (!Application.isPlaying) Assert.Ignore("延迟销毁仅在 PlayMode 验证。");
            GameVirtualList list = CreateList(false);
            list.SetCount(20);
            GameVirtualListItem oldItem = list.GetVisibleItem(0);
            ScrollRect scroll = list.GetComponent<ScrollRect>();

            GameVirtualListItem prefab = _roots[_roots.Count - 1].GetComponent<GameVirtualListItem>();
            list.Configure(scroll, prefab, 100f);

            Assert.IsFalse(oldItem.gameObject.activeSelf);
            Assert.IsNull(oldItem.BindingScope);
            Assert.IsNull(oldItem.transform.parent);
            yield return null;
        }

        /// <summary>
        /// 水平模式使用横向偏移和内容宽度。
        /// </summary>
        [Test]
        public void Horizontal_ScrollToUsesHorizontalOffset()
        {
            GameVirtualList list = CreateList(true);
            list.SetCount(100);
            list.ScrollTo(10);
            ScrollRect scroll = list.GetComponent<ScrollRect>();
            Assert.AreEqual(-1000f, scroll.content.anchoredPosition.x, 0.01f);
            Assert.IsNotNull(list.GetVisibleItem(10));
            Assert.IsFalse(scroll.vertical);
        }

        /// <summary>
        /// 创建带有 ScrollRect、Viewport、Content 和条目预制体的测试层级。
        /// </summary>
        /// <param name="horizontal">是否水平模式。</param>
        /// <param name="probe">是否使用异步探针条目。</param>
        /// <param name="throwing">是否使用绑定失败探针。</param>
        /// <param name="unbindThrow">是否使用解绑失败探针。</param>
        /// <returns>已配置的虚拟列表。</returns>
        private GameVirtualList CreateList(bool horizontal, bool probe = false, bool throwing = false, bool unbindThrow = false)
        {
            var root = new GameObject("Scroll", typeof(RectTransform), typeof(ScrollRect), typeof(GameVirtualList));
            _roots.Add(root);
            var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(Mask));
            viewport.transform.SetParent(root.transform, false);
            RectTransform viewportRect = (RectTransform)viewport.transform;
            viewportRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 300f);
            viewportRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 300f);
            var content = new GameObject("Content", typeof(RectTransform));
            content.transform.SetParent(viewport.transform, false);
            var prefabObject = new GameObject("ItemPrefab", typeof(RectTransform));
            if (probe) prefabObject.AddComponent<VirtualListAsyncProbe>();
            else if (throwing) prefabObject.AddComponent<VirtualListThrowProbe>();
            else if (unbindThrow) prefabObject.AddComponent<VirtualListUnbindThrowProbe>();
            else prefabObject.AddComponent<GameVirtualListItem>();
            _roots.Add(prefabObject);
            GameVirtualListItem prefab = prefabObject.GetComponent<GameVirtualListItem>();
            ScrollRect scroll = root.GetComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = (RectTransform)content.transform;
            GameVirtualList list = root.GetComponent<GameVirtualList>();
            list.Configure(scroll, prefab, 100f, 0f, horizontal, 1);
            return list;
        }
    }

    /// <summary>
    /// 检查旧异步绑定在条目复用后不会回写。
    /// </summary>
    public sealed class VirtualListAsyncProbe : GameVirtualListItem
    {
        /// <summary>
        /// 测试用异步闸门。
        /// </summary>
        public static UniTaskCompletionSource Gate;

        /// <summary>
        /// 旧绑定迟到写入次数。
        /// </summary>
        public static int OldWrites;

        /// <summary>
        /// 使用本轮索引和作用域执行测试绑定。
        /// </summary>
        /// <param name="index">当前数据索引。</param>
        /// <param name="scope">本轮绑定作用域。</param>
        public override void Bind(int index, LifetimeScope scope) => WaitAsync(index, scope).Forget();

        /// <summary>
        /// 等待模拟资源结果后检查绑定仍有效。
        /// </summary>
        /// <param name="index">发起时的数据索引。</param>
        /// <param name="scope">发起时的绑定作用域。</param>
        /// <returns>异步等待任务。</returns>
        private async UniTask WaitAsync(int index, LifetimeScope scope)
        {
            await Gate.Task;
            if (index == 0 && scope.IsActive && BindingScope == scope) OldWrites++;
        }
    }

    /// <summary>
    /// 绑定失败时记录作用域的测试条目。
    /// </summary>
    public sealed class VirtualListThrowProbe : GameVirtualListItem
    {
        /// <summary>
        /// 是否在绑定时抛出测试异常。
        /// </summary>
        public static bool ThrowOnBind;

        /// <summary>
        /// 最近一次绑定作用域。
        /// </summary>
        public static LifetimeScope LastScope;

        /// <summary>
        /// 使用本轮索引和作用域执行测试绑定。
        /// </summary>
        /// <param name="index">当前数据索引。</param>
        /// <param name="scope">本轮绑定作用域。</param>
        public override void Bind(int index, LifetimeScope scope)
        {
            LastScope = scope;
            if (ThrowOnBind) throw new InvalidOperationException("Test binding failure");
        }
    }

    /// <summary>
    /// 在解除绑定时模拟业务回调错误。
    /// </summary>
    public sealed class VirtualListUnbindThrowProbe : GameVirtualListItem
    {
        /// <summary>
        /// 抛出预期错误以验证解绑隔离和继续清理。
        /// </summary>
        public override void Unbind() => throw new InvalidOperationException("Test unbind failure");
    }
}