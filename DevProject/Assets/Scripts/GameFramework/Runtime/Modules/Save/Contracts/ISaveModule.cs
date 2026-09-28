using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 正式存档模块；业务通过会话保存数据，平台后端在打开前配置。
    /// </summary>
    public interface ISaveModule : IModule
    {
        ISaveBackend Backend { get; }

        /// <summary>
        /// 配置持久化后端，存在会话或在途操作时禁止更换。
        /// </summary>
        /// <param name="backend">平台后端。</param>
        /// <returns>配置结果。</returns>
        SaveResult ConfigureBackend(ISaveBackend backend);

        /// <summary>
        /// 读取存档并创建尚未激活的会话。
        /// </summary>
        /// <typeparam name="T">游戏存档数据类型。</typeparam>
        /// <param name="identity">存档身份。</param>
        /// <param name="definition">游戏数据定义。</param>
        /// <param name="policy">保存策略。</param>
        /// <param name="ct">只取消调用方等待的令牌。</param>
        /// <returns>打开结果。</returns>
        UniTask<SaveOpenResult<T>> OpenAsync<T>(SaveIdentity identity, SaveDefinition<T> definition,
            SavePolicy policy = null, CancellationToken ct = default);

        /// <summary>
        /// 检查可供恢复的记录，不创建可写会话。
        /// </summary>
        /// <typeparam name="T">游戏存档数据类型。</typeparam>
        /// <param name="identity">来源身份。</param>
        /// <param name="definition">数据定义。</param>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>恢复候选列表。</returns>
        UniTask<SaveRecoveryInspection> InspectRecoveryAsync<T>(SaveIdentity identity, SaveDefinition<T> definition,
            CancellationToken ct = default);

        /// <summary>
        /// 将明确选择的候选恢复到新的空存档身份，等待激活后才写入。
        /// </summary>
        /// <typeparam name="T">游戏存档数据类型。</typeparam>
        /// <param name="candidate">恢复候选。</param>
        /// <param name="targetIdentity">新的目标身份。</param>
        /// <param name="definition">数据定义。</param>
        /// <param name="policy">保存策略。</param>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>未激活的目标会话。</returns>
        UniTask<SaveOpenResult<T>> RestoreToNewSlotAsync<T>(SaveRecoveryCandidate candidate, SaveIdentity targetIdentity,
            SaveDefinition<T> definition, SavePolicy policy = null, CancellationToken ct = default);
    }
}
