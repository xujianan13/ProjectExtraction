using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Gameplay.Main
{
    /// <summary>
    /// Main玩法根；在这里扩展玩法规则。
    /// </summary>
    public sealed class MainGameplay : GameplayRoot<MainGameplay>
    {
        /// <summary>
        /// 创建玩法并连接本地功能模块。
        /// </summary>
        public MainGameplay()
        {
            AddFeature<MainFeature>();
            
        }

        /// <summary>
        /// 初始化不属于单轮进入的玩法数据。
        /// </summary>
        /// <param name="ct">初始化取消令牌。</param>
        /// <returns>初始化任务。</returns>
        protected override UniTask OnInitGameplayAsync(CancellationToken ct) => UniTask.CompletedTask;

        /// <summary>
        /// 每帧推进玩法规则。
        /// </summary>
        /// <param name="deltaTime">本帧经过的秒数。</param>
        protected override void OnUpdateGameplay(float deltaTime) { }

        /// <summary>
        /// 在固定步长阶段更新物理玩法。
        /// </summary>
        /// <param name="fixedDeltaTime">本次固定步长秒数。</param>
        protected override void OnFixedUpdateGameplay(float fixedDeltaTime) { }

        /// <summary>
        /// 在帧末阶段更新跟随和表现。
        /// </summary>
        /// <param name="deltaTime">本帧经过的秒数。</param>
        protected override void OnLateUpdateGameplay(float deltaTime) { }

    }
}
