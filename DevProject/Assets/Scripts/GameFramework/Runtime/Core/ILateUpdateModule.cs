namespace GameFramework
{
    /// <summary>
    /// 可选的帧末模块接口，不参与模块注册键解析。
    /// </summary>
    public interface ILateUpdateModule
    {
        /// <summary>
        /// 在 Unity 帧末阶段更新。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        void OnLateUpdate(float deltaTime);
    }
}
