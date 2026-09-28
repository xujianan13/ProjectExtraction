namespace GameFramework
{
    /// <summary>
    /// 顺序流程。按添加顺序依次执行子流程，所有子流程成功后整体成功。
    /// </summary>
    public sealed class SequenceFlow : CompositeFlowBase
    {
        private int _currentIndex;

        /// <summary>创建顺序流程。</summary>
        /// <param name="name">流程名称。</param>
        public SequenceFlow(string name = null) : base(name)
        {
        }

        /// <summary>当前正在执行的子流程；无活动子流程时返回 null。</summary>
        public IFlow Current => _currentIndex >= 0 && _currentIndex < Count ? GetChild(_currentIndex) : null;

        /// <inheritdoc />
        protected override void OnStart()
        {
            _currentIndex = 0;
            ApplyStartResult(StartNextRunnable());
        }

        /// <inheritdoc />
        protected override void OnReset()
        {
            base.OnReset();
            _currentIndex = 0;
        }

        /// <inheritdoc />
        protected override FlowTickResult OnTick(float deltaTime)
        {
            if (_currentIndex >= Count)
            {
                return FlowTickResult.Succeeded;
            }

            IFlow current = GetChild(_currentIndex);
            if (current.State == FlowState.Running)
            {
                current.Tick(deltaTime);
            }

            if (!current.IsDone)
            {
                return FlowTickResult.Running;
            }

            if (current.State == FlowState.Failed)
            {
                return FlowTickResult.Failed($"子流程 {current.Name} 失败");
            }

            if (current.State == FlowState.Canceled)
            {
                return FlowTickResult.Canceled;
            }

            _currentIndex++;
            return StartNextRunnable();
        }

        private FlowTickResult StartNextRunnable()
        {
            while (_currentIndex < Count)
            {
                IFlow current = GetChild(_currentIndex);
                if (current.State == FlowState.Idle)
                {
                    current.Start();
                }

                if (current.State == FlowState.Succeeded)
                {
                    _currentIndex++;
                    continue;
                }

                if (current.State == FlowState.Failed)
                {
                    return FlowTickResult.Failed($"子流程 {current.Name} 失败");
                }

                if (current.State == FlowState.Canceled)
                {
                    return FlowTickResult.Canceled;
                }

                return FlowTickResult.Running;
            }

            return FlowTickResult.Succeeded;
        }

        private void ApplyStartResult(FlowTickResult result)
        {
            if (result.State == FlowState.Succeeded)
            {
                Succeed();
            }
            else if (result.State == FlowState.Failed)
            {
                Fail(result.Error);
            }
            else if (result.State == FlowState.Canceled)
            {
                Cancel();
            }
        }
    }
}
