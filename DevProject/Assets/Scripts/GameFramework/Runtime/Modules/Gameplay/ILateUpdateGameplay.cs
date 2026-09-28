namespace GameFramework
{
    /// <summary>
    /// 可选的玩法帧末更新接口；旧玩法实现无需修改。
    /// </summary>
    public interface ILateUpdateGameplay
    {
        /// <summary>
        /// 帧末更新。
        /// </summary>
        /// <param name="deltaTime">距上一帧的时间间隔（秒）。</param>
        void LateUpdate(float deltaTime);
    }
}
