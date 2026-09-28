using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Gameplay.Main
{
    /// <summary>
    /// Main玩法内容；每轮进入持有独立 PrefabPool。
    /// </summary>
    public sealed class MainFeature : GameplayFeature<MainGameplay>
    {
        private PrefabPool pool;
        private PrefabInstance instance;
        private LifetimeScope ownedScope;

        /// <summary>
        /// 加载并生成本轮玩法内容。
        /// </summary>
        /// <param name="enterData">玩法进入参数。</param>
        /// <param name="ct">本轮取消令牌。</param>
        /// <returns>内容进入任务。</returns>
        public override async UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default)
        {
            LifetimeScope scope = Root.GameplayScope;
            ownedScope = scope;
            bool entered = false;
            try
            {
                PrefabPool created = await GameEntry.Get<IPrefabPoolModule>().CreatePoolAsync(
                    GameLogic.Scaffold.ContentAddresses.MainGameplay, ct: ct);
                if (created == null) throw new InvalidOperationException("无法创建Main内容池。");
                if (!ReferenceEquals(ownedScope, scope) || !scope.IsActive)
                {
                    await created.DisposeAsync();
                    return;
                }
                pool = created;
                instance = pool.Spawn();
                if (instance == null) throw new InvalidOperationException("无法生成Main内容实例。");
                entered = true;
            }
            finally
            {
                if (!entered) await CleanupAsync(scope);
            }
        }

        /// <summary>
        /// 等待本轮实例和资源租约释放。
        /// </summary>
        /// <param name="ct">独立退出令牌。</param>
        /// <returns>资源释放任务。</returns>
        public override UniTask ExitAsync(CancellationToken ct = default) => CleanupAsync(ownedScope);

        /// <summary>
        /// 强制停止并安排释放；异步销毁由 PrefabPool 自行完成。
        /// </summary>
        public override void ForceExit()
        {
            instance?.Dispose();
            instance = null;
            pool?.Dispose();
            pool = null;
            ownedScope = null;
        }

        /// <summary>
        /// 清理指定轮次持有的池和实例。
        /// </summary>
        /// <param name="scope">进入时捕获的作用域。</param>
        /// <returns>资源释放任务。</returns>
        private async UniTask CleanupAsync(LifetimeScope scope)
        {
            if (!ReferenceEquals(ownedScope, scope)) return;
            PrefabInstance currentInstance = instance;
            PrefabPool currentPool = pool;
            instance = null;
            pool = null;
            ownedScope = null;
            currentInstance?.Dispose();
            if (currentPool != null) await currentPool.DisposeAsync();
        }
    }
}
