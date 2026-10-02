using System.Collections.Generic;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 与应用暂停和强化选择分别管理的局内交互面板。
    /// </summary>
    public enum ExpeditionPanelKind { None, Inventory, Merchant }

    public sealed partial class ExpeditionRun
    {
        public ExpeditionPanelKind ActivePanel { get; private set; }
        public int PlayerLevel => session.Level;
        public int SafeCapacity => PrototypeRules.SafeSlots;
        public bool CanManageInventory => ActivePanel == ExpeditionPanelKind.Inventory && CanManagePanel;
        public bool CanManageMerchant => ActivePanel == ExpeditionPanelKind.Merchant && CanManagePanel;
        private bool CanManagePanel => IsRunning && IsReady && !paused && !IsChoosingRogue && !disposed && !stopped &&
            !session.HasPendingSave && !GameEntry.IsInBackground && World.TryGetActor(Player, out var actor) && !actor.Dead;

        /// <summary>
        /// 打开背包并冻结战斗，已有其他交互时拒绝切换。
        /// </summary>
        /// <returns>背包已经打开或本次成功打开时为真。</returns>
        public bool TryOpenInventory()
        {
            if (CanManageInventory) return true;
            if (!CanInteract || !CanManagePanel) return false;
            SetActivePanel(ExpeditionPanelKind.Inventory);
            return true;
        }

        /// <summary>
        /// 关闭指定面板，仅释放该面板自己的暂停原因。
        /// </summary>
        /// <param name="kind">窗口拥有的面板种类。</param>
        public void ClosePanel(ExpeditionPanelKind kind)
        {
            if (kind == ExpeditionPanelKind.None || ActivePanel != kind) return;
            SetActivePanel(ExpeditionPanelKind.None);
        }

        /// <summary>
        /// 在面板切换边界清除残留输入和待推进时间。
        /// </summary>
        /// <param name="kind">新的面板状态。</param>
        private void SetActivePanel(ExpeditionPanelKind kind)
        {
            ActivePanel = kind;
            touchMove = Vector2.zero;
            interactionRequested = false;
            World.ClearPendingTime();
            World.SetMove(Player, Vector2.zero);
            Changed?.Invoke();
        }

        /// <summary>
        /// 穿戴或卸下本局装备，先完成战斗属性校验再提交装备槽。
        /// </summary>
        /// <param name="itemId">本局物品实例身份。</param>
        /// <returns>装备状态和战斗属性已一起更新时为真。</returns>
        public bool TryEquip(string itemId)
        {
            if (!CanManageInventory) return false;
            ProjectItem item = inventory.Find(value => value.Id == itemId);
            if (item == null || item.Type == ProjectItemType.Item) return ReportInventoryFailure("请选择武器或护甲。");
            if (IsProtected(itemId)) return ReportInventoryFailure("请先将物品移出安全箱，再穿戴。");
            if (!item.Equipped && PlayerLevel < item.RequiredLevel) return ReportInventoryFailure("角色等级不足，暂时不能穿戴。");
            List<ProjectItem> proposed = CreateEquipmentProposal(item);
            CombatActorSpec spec = EquipmentSpec(proposed);
            if (!World.TryReplaceBaseAttributes(Player, spec))
                return ReportInventoryFailure("当前装备属性无法应用，请稍后重试。");
            bool equipped = !item.Equipped;
            for (int index = 0; index < inventory.Count; index++) inventory[index].Equipped = proposed[index].Equipped;
            Message = (equipped ? "已装备：" : "已卸下：") + item.Name;
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 预览一次穿脱操作对角色最终属性的影响，商店物品也可只读比较。
        /// </summary>
        /// <param name="item">待比较物品，已有物品以本局权威实例为准。</param>
        /// <param name="current">当前装备及局内强化的属性。</param>
        /// <param name="preview">模拟换装后的装备及局内强化属性。</param>
        /// <returns>可以计算装备比较时为真。</returns>
        public bool TryPreviewEquipment(ProjectItem item, out ProjectCharacterStats current, out ProjectCharacterStats preview)
        {
            current = null;
            preview = null;
            if (disposed || stopped || item == null || item.Type == ProjectItemType.Item || ProjectEquipmentRules.ValidateItem(item) != null) return false;
            ProjectItem owned = inventory.Find(value => value.Id == item.Id);
            if (!World.TryPreviewBaseAttributes(Player, EquipmentSpec(inventory), out CombatActorSpec before) ||
                !World.TryPreviewBaseAttributes(Player, EquipmentSpec(CreateEquipmentProposal(owned ?? item)), out CombatActorSpec after)) return false;
            current = ProjectStats(before);
            preview = ProjectStats(after);
            return true;
        }

        /// <summary>
        /// 创建只调整同类装备槽的独立候选，不触碰背包或安全箱。
        /// </summary>
        /// <param name="selected">准备穿戴或卸下的物品。</param>
        /// <returns>保留原有物品次序的候选集合。</returns>
        private List<ProjectItem> CreateEquipmentProposal(ProjectItem selected)
        {
            List<ProjectItem> proposed = inventory.ConvertAll(value => value.Copy());
            bool owned = proposed.Exists(value => value.Id == selected.Id);
            bool equip = !owned || !selected.Equipped;
            foreach (ProjectItem value in proposed)
            {
                bool sameSlot = value.Type == selected.Type || ProjectEquipmentRules.IsArmor(value.Type) && ProjectEquipmentRules.IsArmor(selected.Type);
                if (sameSlot && (equip || value.Id == selected.Id)) value.Equipped = value.Id == selected.Id && equip;
            }
            if (!owned)
            {
                ProjectItem copy = selected.Copy();
                copy.Equipped = true;
                proposed.Add(copy);
            }
            return proposed;
        }

        /// <summary>
        /// 将装备与局外技能汇总为战斗层的基础数值输入。
        /// </summary>
        /// <param name="items">准备计算的物品集合。</param>
        /// <returns>仅填入战斗属性的配置值。</returns>
        private CombatActorSpec EquipmentSpec(IReadOnlyList<ProjectItem> items)
        {
            ProjectCharacterStats stats = ProjectEquipmentRules.Calculate(items, session.PowerRank, session.VitalityRank, session.AgilityRank);
            return new CombatActorSpec
            {
                MaxHealth = stats.MaxHealth, Attack = stats.Attack, Defense = stats.Defense, CriticalRating = stats.CriticalRating,
                CriticalDamage = stats.CriticalDamage, DodgeChance = stats.DodgeChance, DamageIncrease = stats.DamageIncrease,
                DamageReduction = stats.DamageReduction, AttackRange = stats.AttackRange, MoveSpeed = stats.MoveSpeed
            };
        }

        /// <summary>
        /// 将战斗属性投影为界面使用的独立快照。
        /// </summary>
        /// <param name="spec">包含局内强化的最终数值。</param>
        /// <returns>供装备比较使用的属性快照。</returns>
        private static ProjectCharacterStats ProjectStats(CombatActorSpec spec) => new()
        {
            MaxHealth = spec.MaxHealth, Attack = spec.Attack, Defense = spec.Defense, CriticalRating = spec.CriticalRating,
            CriticalDamage = spec.CriticalDamage, DodgeChance = spec.DodgeChance, DamageIncrease = spec.DamageIncrease,
            DamageReduction = spec.DamageReduction, AttackRange = spec.AttackRange, MoveSpeed = spec.MoveSpeed
        };

        /// <summary>
        /// 发布可预期的物品操作失败，不修改背包内容。
        /// </summary>
        /// <param name="reason">玩家可读的失败原因。</param>
        /// <returns>始终为假。</returns>
        private bool ReportInventoryFailure(string reason)
        {
            Message = reason;
            Changed?.Invoke();
            return false;
        }
    }
}
