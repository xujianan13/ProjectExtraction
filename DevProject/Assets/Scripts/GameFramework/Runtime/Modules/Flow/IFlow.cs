namespace GameFramework
{
    /// <summary>
    /// 流程接口。流程由 FlowModule 或组合流程按帧轮询推进。
    /// </summary>
    public interface IFlow
    {
        /// <summary>流程名称，主要用于日志与调试。</summary>
        string Name { get; }

        /// <summary>当前流程状态。</summary>
        FlowState State { get; }

        /// <summary>流程是否已经进入终态。</summary>
        bool IsDone { get; }

        /// <summary>启动流程。只有 Idle 状态会响应。</summary>
        void Start();

        /// <summary>暂停流程。只有 Running 状态会响应。</summary>
        void Pause();

        /// <summary>恢复流程。只有 Paused 状态会响应。</summary>
        void Resume();

        /// <summary>取消流程。已进入终态时无效。</summary>
        void Cancel();

        /// <summary>标记流程失败。已进入终态时无效。</summary>
        /// <param name="error">失败原因。</param>
        void Fail(string error);

        /// <summary>重置流程到 Idle 状态，用于复用流程实例。</summary>
        void Reset();

        /// <summary>按帧推进流程。</summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        FlowTickResult Tick(float deltaTime);
    }
}
