using System;
using System.Collections.Generic;

namespace GameFramework
{
    /// <summary>
    /// 组合流程基类，维护子流程列表并提供统一的生命周期传播。
    /// </summary>
    public abstract class CompositeFlowBase : FlowBase
    {
        private readonly List<IFlow> _children;

        /// <summary>创建组合流程。</summary>
        /// <param name="name">流程名称。</param>
        /// <param name="capacity">子流程初始容量。</param>
        protected CompositeFlowBase(string name = null, int capacity = 4) : base(name)
        {
            _children = new List<IFlow>(capacity > 0 ? capacity : 4);
        }

        /// <summary>子流程数量。</summary>
        public int Count => _children.Count;

        /// <summary>添加子流程。</summary>
        /// <param name="flow">子流程。</param>
        public void Add(IFlow flow)
        {
            if (flow == null) throw new ArgumentNullException(nameof(flow));
            _children.Add(flow);
        }

        /// <summary>批量添加子流程。</summary>
        /// <param name="flows">子流程数组。</param>
        public void AddRange(params IFlow[] flows)
        {
            if (flows == null) throw new ArgumentNullException(nameof(flows));

            for (int i = 0; i < flows.Length; i++)
            {
                Add(flows[i]);
            }
        }

        /// <summary>按索引获取子流程。</summary>
        /// <param name="index">子流程索引。</param>
        public IFlow GetChild(int index) => _children[index];

        /// <inheritdoc />
        protected override void OnPause()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                _children[i].Pause();
            }
        }

        /// <inheritdoc />
        protected override void OnResume()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                _children[i].Resume();
            }
        }

        /// <inheritdoc />
        protected override void OnComplete(FlowState state, string error)
        {
            if (state != FlowState.Canceled && state != FlowState.Failed) return;

            for (int i = 0; i < _children.Count; i++)
            {
                if (!_children[i].IsDone)
                {
                    _children[i].Cancel();
                }
            }
        }

        /// <inheritdoc />
        protected override void OnReset()
        {
            for (int i = 0; i < _children.Count; i++)
            {
                _children[i].Reset();
            }
        }
    }
}
