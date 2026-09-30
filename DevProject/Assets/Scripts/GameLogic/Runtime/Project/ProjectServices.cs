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
        public static ProjectSession Session { get; private set; }

        /// <summary>
        /// 读取玩家存档并绑定当前应用作用域。
        /// </summary>
        /// <param name="ct">应用初始化取消令牌。</param>
        /// <returns>项目状态是否准备成功。</returns>
        public static async UniTask<bool> InitializeAsync(CancellationToken ct)
        {
            Session?.Dispose();
            ProjectSession created = await ProjectSession.OpenAsync(ct);
            if (created == null) return false;
            Session = created;
            GameEntry.ApplicationScope.Defer(() =>
            {
                created.Dispose();
                if (ReferenceEquals(Session, created)) Session = null;
            });
            return true;
        }
    }
}
