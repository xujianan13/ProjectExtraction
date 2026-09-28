namespace GameFramework
{
    /// <summary>
    /// 流程单次轮询结果。
    /// </summary>
    public readonly struct FlowTickResult
    {
        /// <summary>当前流程状态。</summary>
        public FlowState State { get; }

        /// <summary>失败原因；仅 <see cref="FlowState.Failed"/> 时建议填写。</summary>
        public string Error { get; }

        /// <summary>创建流程轮询结果。</summary>
        /// <param name="state">当前流程状态。</param>
        /// <param name="error">失败原因。</param>
        public FlowTickResult(FlowState state, string error = null)
        {
            State = state;
            Error = error;
        }

        /// <summary>运行中结果。</summary>
        public static FlowTickResult Running => new(FlowState.Running);

        /// <summary>成功结果。</summary>
        public static FlowTickResult Succeeded => new(FlowState.Succeeded);

        /// <summary>取消结果。</summary>
        public static FlowTickResult Canceled => new(FlowState.Canceled);

        /// <summary>创建失败结果。</summary>
        /// <param name="error">失败原因。</param>
        public static FlowTickResult Failed(string error) => new(FlowState.Failed, error);

        /// <summary>按状态创建结果。</summary>
        /// <param name="state">流程状态。</param>
        public static FlowTickResult FromState(FlowState state) => new(state);
    }
}
