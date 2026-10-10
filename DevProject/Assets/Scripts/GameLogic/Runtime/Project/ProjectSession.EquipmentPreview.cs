using System;

namespace GameLogic.Project
{
    /// <summary>
    /// 局外装备候选与只读角色属性预览。
    /// </summary>
    public sealed partial class ProjectSession
    {
        /// <summary>
        /// 计算指定仓库物品穿戴或卸下后的属性，不改变已提交存档和仓库状态。
        /// </summary>
        /// <param name="itemId">仓库物品实例身份。</param>
        /// <param name="preview">成功时返回独立的操作后属性。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>找到可操作装备并完成属性计算时为真。</returns>
        public bool TryGetEquipmentPreview(string itemId, out ProjectCharacterStats preview, out string error)
        {
            preview = null;
            error = "项目数据尚未就绪。";
            if (disposed || profile == null) return false;
            ProjectSaveData candidate = profile.Copy();
            if (!TryApplyEquipment(candidate, itemId, out _, out error)) return false;
            preview = ProjectEquipmentRules.Calculate(candidate.Warehouse, candidate.PowerRank, candidate.VitalityRank, candidate.AgilityRank);
            return true;
        }

        /// <summary>
        /// 计算物品候选的潜在穿卸属性，优先采用已拥有的同身份物品，允许预览尚未满足等级的装备。
        /// </summary>
        /// <param name="item">仓库物品快照或独立商品物品。</param>
        /// <param name="preview">成功时返回独立的潜在穿卸后属性。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>物品可用于只读装备比较且完成属性计算时为真。</returns>
        public bool TryGetEquipmentPreview(ProjectItem item, out ProjectCharacterStats preview, out string error)
        {
            preview = null;
            error = "项目数据尚未就绪。";
            if (disposed || profile == null) return false;
            error = "请选择可穿戴的武器或装备。";
            if (item == null) return false;
            ProjectItem owned = profile.Warehouse.Find(value => value.Id == item.Id);
            ProjectItem selected = owned ?? item;
            if (owned == null)
            {
                error = ProjectEquipmentRules.ValidateItem(selected);
                if (error != null) return false;
            }
            error = "请选择可穿戴的武器或装备。";
            if (selected.Type == ProjectItemType.Item) return false;
            var candidates = profile.Warehouse.ConvertAll(value => value.Copy());
            ProjectItem candidate = candidates.Find(value => value.Id == selected.Id);
            bool equip = owned == null || !owned.Equipped;
            if (candidate == null)
            {
                candidate = selected.Copy();
                candidates.Add(candidate);
            }
            foreach (ProjectItem other in candidates)
                if (other.Type == selected.Type || ProjectEquipmentRules.IsArmor(other.Type) && ProjectEquipmentRules.IsArmor(selected.Type))
                    other.Equipped = false;
            candidate.Equipped = equip;
            preview = ProjectEquipmentRules.Calculate(candidates, profile.PowerRank, profile.VitalityRank, profile.AgilityRank);
            error = null;
            return true;
        }

        /// <summary>
        /// 在独立候选中应用一次与正式仓库事务相同的装备槽变化。
        /// </summary>
        /// <param name="candidate">待修改的独立存档候选。</param>
        /// <param name="itemId">仓库物品实例身份。</param>
        /// <param name="equip">操作后是否穿戴该物品。</param>
        /// <param name="error">失败原因，成功时为空。</param>
        /// <returns>候选装备槽成功更新时为真。</returns>
        private static bool TryApplyEquipment(ProjectSaveData candidate, string itemId, out bool equip, out string error)
        {
            equip = false;
            error = "请选择可穿戴的武器或装备。";
            if (candidate?.Warehouse == null || string.IsNullOrEmpty(itemId)) return false;
            ProjectItem item = candidate.Warehouse.Find(value => value.Id == itemId);
            if (item == null || item.Type == ProjectItemType.Item) return false;
            error = "角色等级不足，无法穿戴。";
            if (item.RequiredLevel > candidate.Level) return false;
            equip = !item.Equipped;
            foreach (ProjectItem other in candidate.Warehouse)
                if (other.Type == item.Type || ProjectEquipmentRules.IsArmor(other.Type) && ProjectEquipmentRules.IsArmor(item.Type)) other.Equipped = false;
            item.Equipped = equip;
            error = null;
            return true;
        }
    }
}
