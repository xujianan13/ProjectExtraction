using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace GameLogic.Project
{
    /// <summary>
    /// 局外仓库锁定与批量出售持久化入口。
    /// </summary>
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 将指定物品的锁定状态写入存档，不影响穿戴和入场携带。
        /// </summary>
        /// <param name="itemId">物品实例身份。</param>
        /// <param name="locked">目标锁定状态。</param>
        /// <returns>保存确认后的结果。</returns>
        public UniTask<ProjectOperationResult> SetItemLockedAsync(string itemId, bool locked)
        {
            if (!CanEdit()) return Rejected("当前不能整理仓库。");
            ProjectSaveData candidate = Candidate();
            if (!ProjectWarehouseRules.TrySetLocked(candidate, itemId, locked, out string error)) return Rejected(error);
            return CommitAsync(candidate, locked ? "物品已锁定。" : "物品已解锁。");
        }

        /// <summary>
        /// 对明确选中的物品执行一次保存的整批出售。
        /// </summary>
        /// <param name="itemIds">要出售的全部实例身份。</param>
        /// <returns>保存确认后的结果。</returns>
        public UniTask<ProjectOperationResult> SellItemsAsync(IReadOnlyList<string> itemIds)
        {
            if (!CanEdit()) return Rejected("当前不能出售。");
            ProjectSaveData candidate = Candidate();
            if (!ProjectWarehouseRules.TrySell(candidate, itemIds, out int totalGold, out string error)) return Rejected(error);
            return CommitAsync(candidate, "出售成功，金币 +" + totalGold);
        }
    }
}
