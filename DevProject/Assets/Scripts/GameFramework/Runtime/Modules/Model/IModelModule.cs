namespace GameFramework
{
    /// <summary>
    /// 跨场景业务数据模型模块。负责模型注册、查询、有序生命周期和作用域化重置。
    /// 具体模型位于 GameLogic，框架不感知业务数据类型及其来源。
    /// </summary>
    public interface IModelModule : IModule
    {
        /// <summary>是否已调用 <see cref="InitAll"/>。</summary>
        bool IsInitialized { get; }

        /// <summary>注册模型；须在 <see cref="InitAll"/> 前调用，同类型重复注册会替换旧实例。</summary>
        /// <typeparam name="T">模型具体类型，同时作为查询 Key。</typeparam>
        /// <param name="model">待注册模型。</param>
        void RegisterModel<T>(T model) where T : ModelBase;

        /// <summary>获取已注册模型；未注册时返回 null。</summary>
        /// <typeparam name="T">模型具体类型。</typeparam>
        T Get<T>() where T : ModelBase;

        /// <summary>按 <see cref="ModelBase.Order"/> 升序初始化全部模型。</summary>
        void InitAll();

        /// <summary>
        /// 按 Order 降序重置指定作用域及其更短生命周期的模型。
        /// 重置 Account 会同时重置 Player，重置 Application 会重置全部模型。
        /// </summary>
        /// <param name="scope">本次重置的最长生命周期作用域。</param>
        /// <param name="reason">本次重置原因。</param>
        void ResetAll(ModelScope scope, ModelResetReason reason);

        /// <summary>按 Order 降序卸载全部模型并清空注册表。</summary>
        void ShutdownAll();
    }
}
