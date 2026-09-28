namespace GameFramework
{
    /// <summary>
    /// 可选的玩法固定帧更新接口；旧玩法实现无需修改。
    /// </summary>
    public interface IFixedUpdateGameplay
    {
        /// <summary>
        /// 固定帧更新。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        void FixedUpdate(float deltaTime);
    }
}
