using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Project
{
    /// <summary>
    /// 应用作用域内的项目服务入口。
    /// </summary>
    public static class ProjectServices
    {
        public const string BattleSceneName = "Expedition";
        public const int BattleHudWindow = 2200;
        public const int ExpeditionInventoryWindow = 2201;
        public const int ExpeditionMerchantWindow = 2202;
        public const int TransitionWindow = 2300;
        public const int ResultWindow = 2301;
        public const int RogueChoiceWindow = 2500;
        public static ProjectRunCoordinator Coordinator { get; private set; }
        public static ProjectSession Session { get; private set; }
        private static UnityEngine.GameObject lifecycleObserver;

        /// <summary>
        /// 读取玩家存档并绑定当前应用作用域。
        /// </summary>
        /// <param name="ct">应用初始化取消令牌。</param>
        /// <returns>项目状态是否准备成功。</returns>
        public static async UniTask<bool> InitializeAsync(CancellationToken ct)
        {
            if (Coordinator != null && !await Coordinator.CloseAsync()) return false;
            Coordinator = null;
            if (lifecycleObserver != null) UnityEngine.Object.Destroy(lifecycleObserver);
            lifecycleObserver = null;
            Session?.Dispose();
            Session = null;
            if (!await ProjectContentConfig.LoadAsync(ct)) return false;
            ProjectSession created = await ProjectSession.OpenAsync(ct);
            if (created == null) return false;
            Session = created;
            var coordinator = new ProjectRunCoordinator(created);
            Coordinator = coordinator;
            UnityEngine.GameObject observer = new UnityEngine.GameObject("ProjectRunLifecycle");
            UnityEngine.Object.DontDestroyOnLoad(observer);
            observer.AddComponent<ProjectRunLifecycle>();
            lifecycleObserver = observer;
            GameEntry.ApplicationScope.Defer(() =>
            {
                coordinator.Dispose();
                created.Dispose();
                if (observer != null) UnityEngine.Object.Destroy(observer);
                if (ReferenceEquals(lifecycleObserver, observer)) lifecycleObserver = null;
                if (ReferenceEquals(Coordinator, coordinator)) Coordinator = null;
                if (ReferenceEquals(Session, created)) Session = null;
            });
            return true;
        }
    }
}
