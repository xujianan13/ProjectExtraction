namespace GameFramework
{
    /// <summary>
    /// 可选的固定帧模块接口，不参与模块注册键解析。
    /// </summary>
    public interface IFixedUpdateModule
    {
        /// <summary>
        /// 在 Unity 固定帧阶段更新。
        /// </summary>
        /// <param name="deltaTime">固定帧时间间隔（秒）。</param>
        void OnFixedUpdate(float deltaTime);
    }
}
