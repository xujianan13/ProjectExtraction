using GameFramework;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 将战斗固定步进与表现接入框架暂停和玩法作用域。
    /// </summary>
    public sealed class ExpeditionGameplay : GameplayRoot<ExpeditionGameplay>
    {
        private readonly ExpeditionRun run;
        private readonly ExpeditionPresentation presentation;

        /// <summary>
        /// 创建本次关卡玩法驱动器。
        /// </summary>
        /// <param name="run">本局逻辑状态。</param>
        /// <param name="presentation">本局表现适配器。</param>
        public ExpeditionGameplay(ExpeditionRun run, ExpeditionPresentation presentation)
        {
            this.run = run;
            this.presentation = presentation;
        }

        /// <summary>
        /// 由唯一更新入口累计并执行固定战斗 tick。
        /// </summary>
        /// <param name="deltaTime">本帧模拟秒数。</param>
        protected override void OnUpdateGameplay(float deltaTime) => run.Advance(deltaTime);

        /// <summary>
        /// 使用 ECS 快照刷新表现，不反向驱动战斗。
        /// </summary>
        /// <param name="deltaTime">本帧经过秒数。</param>
        protected override void OnLateUpdateGameplay(float deltaTime) => presentation.Refresh();
    }
}
