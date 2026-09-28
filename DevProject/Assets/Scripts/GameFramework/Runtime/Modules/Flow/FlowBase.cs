namespace GameFramework
{
    /// <summary>
    /// 流程基类，封装状态流转与生命周期模板方法。
    /// </summary>
    public abstract class FlowBase : IFlow
    {
        private FlowState _state;
        private string _error;
        private float _elapsed;

        /// <summary>创建流程实例。</summary>
        /// <param name="name">流程名称；为空时使用类型名。</param>
        protected FlowBase(string name = null)
        {
            Name = string.IsNullOrEmpty(name) ? GetType().Name : name;
        }

        /// <inheritdoc />
        public string Name { get; }

        /// <inheritdoc />
        public FlowState State => _state;

        /// <inheritdoc />
        public bool IsDone => IsFinalState(_state);

        /// <summary>流程运行累计时间。</summary>
        protected float Elapsed => _elapsed;

        /// <summary>失败原因。</summary>
        protected string Error => _error;

        /// <inheritdoc />
        public void Start()
        {
            if (_state != FlowState.Idle) return;

            _state = FlowState.Running;
            _elapsed = 0f;
            _error = null;
            OnStart();
        }

        /// <inheritdoc />
        public void Pause()
        {
            if (_state != FlowState.Running) return;

            _state = FlowState.Paused;
            OnPause();
        }

        /// <inheritdoc />
        public void Resume()
        {
            if (_state != FlowState.Paused) return;

            _state = FlowState.Running;
            OnResume();
        }

        /// <inheritdoc />
        public void Cancel()
        {
            Complete(FlowState.Canceled, null);
        }

        /// <inheritdoc />
        public void Fail(string error)
        {
            Complete(FlowState.Failed, error);
        }

        /// <inheritdoc />
        public void Reset()
        {
            _state = FlowState.Idle;
            _elapsed = 0f;
            _error = null;
            OnReset();
        }

        /// <inheritdoc />
        public FlowTickResult Tick(float deltaTime)
        {
            if (_state != FlowState.Running)
            {
                return new FlowTickResult(_state, _error);
            }

            _elapsed += deltaTime;
            var result = OnTick(deltaTime);
            if (IsFinalState(result.State))
            {
                Complete(result.State, result.Error);
            }

            return new FlowTickResult(_state, _error);
        }

        /// <summary>标记流程成功完成。</summary>
        protected void Succeed()
        {
            Complete(FlowState.Succeeded, null);
        }

        /// <summary>流程启动时调用。</summary>
        protected virtual void OnStart() { }

        /// <summary>流程暂停时调用。</summary>
        protected virtual void OnPause() { }

        /// <summary>流程恢复时调用。</summary>
        protected virtual void OnResume() { }

        /// <summary>流程重置时调用。</summary>
        protected virtual void OnReset() { }

        /// <summary>流程进入终态时调用。</summary>
        /// <param name="state">终态。</param>
        /// <param name="error">失败原因。</param>
        protected virtual void OnComplete(FlowState state, string error) { }

        /// <summary>流程运行中每帧调用。</summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        protected virtual FlowTickResult OnTick(float deltaTime) => FlowTickResult.Running;

        private void Complete(FlowState state, string error)
        {
            if (IsFinalState(_state)) return;
            if (!IsFinalState(state)) return;

            _state = state;
            _error = error;
            OnComplete(state, error);
        }

        private static bool IsFinalState(FlowState state)
        {
            return state == FlowState.Succeeded ||
                   state == FlowState.Failed ||
                   state == FlowState.Canceled;
        }
    }
}
