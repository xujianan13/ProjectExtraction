using System;

namespace GameFramework
{
    /// <summary>
    /// 流程构建辅助类，提供常用组合流程的快捷创建方法。
    /// </summary>
    public static class FlowBuilder
    {
        /// <summary>创建顺序流程。</summary>
        /// <param name="flows">子流程数组。</param>
        public static SequenceFlow Sequence(params IFlow[] flows)
        {
            var flow = new SequenceFlow();
            AddRange(flow, flows);
            return flow;
        }

        /// <summary>创建命名顺序流程。</summary>
        /// <param name="name">流程名称。</param>
        /// <param name="flows">子流程数组。</param>
        public static SequenceFlow Sequence(string name, params IFlow[] flows)
        {
            var flow = new SequenceFlow(name);
            AddRange(flow, flows);
            return flow;
        }

        /// <summary>创建全部成功策略的并行流程。</summary>
        /// <param name="flows">子流程数组。</param>
        public static ParallelFlow Parallel(params IFlow[] flows)
        {
            return Parallel(FlowParallelPolicy.All, flows);
        }

        /// <summary>创建指定策略的并行流程。</summary>
        /// <param name="policy">完成策略。</param>
        /// <param name="flows">子流程数组。</param>
        public static ParallelFlow Parallel(FlowParallelPolicy policy, params IFlow[] flows)
        {
            var flow = new ParallelFlow(policy);
            AddRange(flow, flows);
            return flow;
        }

        /// <summary>创建命名并行流程。</summary>
        /// <param name="name">流程名称。</param>
        /// <param name="policy">完成策略。</param>
        /// <param name="flows">子流程数组。</param>
        public static ParallelFlow Parallel(string name, FlowParallelPolicy policy, params IFlow[] flows)
        {
            var flow = new ParallelFlow(policy, name);
            AddRange(flow, flows);
            return flow;
        }

        private static void AddRange(CompositeFlowBase target, IFlow[] flows)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            if (flows == null) throw new ArgumentNullException(nameof(flows));

            for (int i = 0; i < flows.Length; i++)
            {
                target.Add(flows[i]);
            }
        }
    }
}
