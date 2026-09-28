namespace GameFramework
{
    /// <summary>
    /// 流程模块接口。统一管理独立流程与组合流程的启动、暂停、恢复和取消。
    /// </summary>
    public interface IFlowModule : IModule
    {
        /// <summary>启动一个根流程。</summary>
        /// <param name="flow">待启动流程。</param>
        FlowHandle Start(IFlow flow);

        /// <summary>暂停指定流程。返回是否命中活动流程。</summary>
        /// <param name="handle">流程句柄。</param>
        bool Pause(FlowHandle handle);

        /// <summary>恢复指定流程。返回是否命中活动流程。</summary>
        /// <param name="handle">流程句柄。</param>
        bool Resume(FlowHandle handle);

        /// <summary>取消指定流程。返回是否命中活动流程。</summary>
        /// <param name="handle">流程句柄。</param>
        bool Cancel(FlowHandle handle);

        /// <summary>指定流程是否仍处于运行或暂停状态。</summary>
        /// <param name="handle">流程句柄。</param>
        bool IsRunning(FlowHandle handle);

        /// <summary>
        /// 获取指定流程状态。
        /// 仅对仍在调度器中的活动流程有效；终态后会在当帧末尾 compact，之后返回 Idle。
        /// 需要终态时请持有 <see cref="IFlow"/> 引用读取 <see cref="IFlow.State"/>。
        /// </summary>
        /// <param name="handle">流程句柄。</param>
        FlowState GetState(FlowHandle handle);

        /// <summary>尝试获取根流程实例。</summary>
        /// <param name="handle">流程句柄。</param>
        /// <param name="flow">流程实例。</param>
        bool TryGetFlow(FlowHandle handle, out IFlow flow);

        /// <summary>取消所有活动流程。</summary>
        void CancelAll();
    }
}
