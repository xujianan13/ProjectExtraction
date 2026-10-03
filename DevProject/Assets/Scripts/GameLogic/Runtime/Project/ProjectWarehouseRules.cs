using System;
using System.Collections.Generic;

namespace GameLogic.Project
{
    /// <summary>
    /// 仓库展示排序方式。
    /// </summary>
    public enum ProjectWarehouseSort { Default, Quality, Level, SellGold }

    /// <summary>
    /// 仓库锁定、出售和排序的纯数据规则。
    /// </summary>
    public static class ProjectWarehouseRules
    {
        /// <summary>
        /// 按所选方式返回独立排序列表，数值相同时保留原库存顺序。
        /// </summary>
        /// <param name="items">当前库存快照。</param>
        /// <param name="sort">排序方式。</param>
        /// <returns>排序后的列表。</returns>
        public static List<ProjectItem> Sort(IReadOnlyList<ProjectItem> items, ProjectWarehouseSort sort)
        {
            var result = new List<ProjectItem>();
            if (items == null) return result;
            foreach (ProjectItem item in items)
            {
                int index = result.Count;
                while (index > 0 && Compare(item, result[index - 1], sort) < 0) index--;
                result.Insert(index, item);
            }
            return result;
        }

        /// <summary>
        /// 在未提交候选中设置指定物品的锁定状态。
        /// </summary>
        /// <param name="candidate">待修改的存档候选。</param>
        /// <param name="itemId">物品实例身份。</param>
        /// <param name="locked">目标锁定状态。</param>
        /// <param name="error">拒绝原因。</param>
        /// <returns>是否修改了候选。</returns>
        public static bool TrySetLocked(ProjectSaveData candidate, string itemId, bool locked, out string error)
        {
            error = "物品不存在或身份无效。";
            if (candidate?.Warehouse == null || string.IsNullOrWhiteSpace(itemId)) return false;
            ProjectItem item = candidate.Warehouse.Find(value => value.Id == itemId);
            if (item == null) return false;
            error = "物品已处于目标状态。";
            if (item.Locked == locked) return false;
            item.Locked = locked;
            error = null;
            return true;
        }

        /// <summary>
        /// 预先完整验证选中的实例，再一次性删除并增加金币。
        /// </summary>
        /// <param name="candidate">待修改的存档候选。</param>
        /// <param name="itemIds">明确选中的实例身份。</param>
        /// <param name="totalGold">本批次出售总价。</param>
        /// <param name="error">拒绝原因。</param>
        /// <returns>候选是否完成出售。</returns>
        public static bool TrySell(ProjectSaveData candidate, IReadOnlyList<string> itemIds, out int totalGold, out string error)
        {
            totalGold = 0;
            error = "请选择要出售的物品。";
            if (candidate?.Warehouse == null || itemIds == null || itemIds.Count == 0) return false;
            var selected = new List<ProjectItem>(itemIds.Count);
            var unique = new HashSet<string>(StringComparer.Ordinal);
            long total = 0;
            foreach (string id in itemIds)
            {
                error = "出售列表包含重复或无效身份。";
                if (string.IsNullOrWhiteSpace(id) || !unique.Add(id)) return false;
                ProjectItem item = candidate.Warehouse.Find(value => value.Id == id);
                error = "出售物品不存在。";
                if (item == null) return false;
                error = "请先卸下要出售的物品。";
                if (item.Equipped) return false;
                error = "锁定物品不能出售。";
                if (item.Locked) return false;
                error = "物品出售价格无效。";
                if (item.SellGold <= 0) return false;
                total += item.SellGold;
                error = "金币余额已达到上限。";
                if (total > int.MaxValue - (long)candidate.Gold) return false;
                selected.Add(item);
            }
            foreach (ProjectItem item in selected) candidate.Warehouse.Remove(item);
            totalGold = (int)total;
            candidate.Gold += totalGold;
            error = null;
            return true;
        }

        /// <summary>
        /// 比较排序字段，值高的物品排在前面。
        /// </summary>
        /// <param name="left">左侧物品。</param>
        /// <param name="right">右侧物品。</param>
        /// <param name="sort">排序方式。</param>
        /// <returns>排序比较结果。</returns>
        private static int Compare(ProjectItem left, ProjectItem right, ProjectWarehouseSort sort)
        {
            if (sort == ProjectWarehouseSort.Quality) return right.Quality.CompareTo(left.Quality);
            if (sort == ProjectWarehouseSort.Level) return right.RequiredLevel.CompareTo(left.RequiredLevel);
            if (sort == ProjectWarehouseSort.SellGold) return right.SellGold.CompareTo(left.SellGold);
            return 0;
        }
    }
}
