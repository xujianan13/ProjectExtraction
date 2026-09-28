namespace GameFramework
{
    /// <summary>
    /// 并行流程。启动后同帧推进所有未完成子流程，并按策略决定整体结果。
    /// </summary>
    public sealed class ParallelFlow : CompositeFlowBase
    {
        private readonly FlowParallelPolicy _policy;

        /// <summary>创建并行流程。</summary>
        /// <param name="policy">完成策略。</param>
        /// <param name="name">流程名称。</param>
        public ParallelFlow(FlowParallelPolicy policy = FlowParallelPolicy.All, string name = null) : base(name)
        {
            _policy = policy;
        }

        /// <summary>完成策略。</summary>
        public FlowParallelPolicy Policy => _policy;

        /// <inheritdoc />
        protected override void OnStart()
        {
            for (int i = 0; i < Count; i++)
            {
                IFlow child = GetChild(i);
                if (child.State == FlowState.Idle)
                {
                    child.Start();
                }
            }

            if (Count == 0)
            {
                Succeed();
            }
        }

        /// <inheritdoc />
        protected override FlowTickResult OnTick(float deltaTime)
        {
            int succeededCount = 0;
            int doneCount = 0;
            bool hasFailed = false;
            bool hasCanceled = false;

            for (int i = 0; i < Count; i++)
            {
                IFlow child = GetChild(i);
                if (child.State == FlowState.Running)
                {
                    child.Tick(deltaTime);
                }

                if (!child.IsDone) continue;

                doneCount++;
                if (child.State == FlowState.Succeeded)
                {
                    succeededCount++;
                }
                else if (child.State == FlowState.Failed)
                {
                    hasFailed = true;
                }
                else if (child.State == FlowState.Canceled)
                {
                    hasCanceled = true;
                }
            }

            return EvaluateResult(succeededCount, doneCount, hasFailed, hasCanceled);
        }

        private FlowTickResult EvaluateResult(int succeededCount, int doneCount, bool hasFailed, bool hasCanceled)
        {
            if (_policy == FlowParallelPolicy.Any)
            {
                if (succeededCount > 0)
                {
                    CancelUnfinishedChildren();
                    return FlowTickResult.Succeeded;
                }

                if (doneCount == Count)
                {
                    return hasFailed ? FlowTickResult.Failed("所有并行子流程均未成功") : FlowTickResult.Canceled;
                }

                return FlowTickResult.Running;
            }

            if (_policy == FlowParallelPolicy.FailFast && (hasFailed || hasCanceled))
            {
                CancelUnfinishedChildren();
                return hasFailed ? FlowTickResult.Failed("并行子流程失败") : FlowTickResult.Canceled;
            }

            if (_policy == FlowParallelPolicy.All)
            {
                if (hasFailed)
                {
                    return FlowTickResult.Failed("并行子流程失败");
                }

                if (hasCanceled)
                {
                    return FlowTickResult.Canceled;
                }
            }

            if (doneCount == Count)
            {
                return FlowTickResult.Succeeded;
            }

            return FlowTickResult.Running;
        }

        private void CancelUnfinishedChildren()
        {
            for (int i = 0; i < Count; i++)
            {
                IFlow child = GetChild(i);
                if (!child.IsDone)
                {
                    child.Cancel();
                }
            }
        }
    }
}
