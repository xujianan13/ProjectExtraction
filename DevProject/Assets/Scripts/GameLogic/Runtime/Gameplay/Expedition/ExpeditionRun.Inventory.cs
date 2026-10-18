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
    public enum ExpeditionPanelKind { None, Inventory, Merchant, Progression, Pause, ExtractionConfirmation, DeathChoice, ChestLoot, AdExtraction }

    public sealed partial class ExpeditionRun
    {
        private readonly List<ProjectItem> chestLootItems = new();

        /// <summary>
        /// 最近一次已保存开箱的实际收获副本，仅在获得物品面板期间保留。
        /// </summary>
        public IReadOnlyList<ProjectItem> ChestLootItems => chestLootItems.ConvertAll(item => item.Copy());

        public ExpeditionPanelKind ActivePanel { get; private set; }
        public int PlayerLevel => session.Level;
        public bool CanManageInventory => ActivePanel == ExpeditionPanelKind.Inventory && CanManagePanel;
        public bool CanManageMerchant => ActivePanel == ExpeditionPanelKind.Merchant && CanManagePanel;
        private bool CanManagePanel => IsRunning && IsReady && !paused && !IsClaimingBossLoot && !disposed && !stopped && !IsSavingRun &&
            !session.HasUncommittedSave && !IsReviveAdPending && !IsExtractionAdPending && !GameEntry.IsInBackground && World.TryGetActor(Player, out var actor) && !actor.Dead;

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
        /// 当前暂停菜单是否允许主动放弃，后台或保存门禁期间拒绝操作。
        /// </summary>
        public bool CanAbandon => ActivePanel == ExpeditionPanelKind.Pause && CanManagePanel;

        /// <summary>
        /// 从正常游玩进入手动暂停，清除输入及未推进时间。
        /// </summary>
        /// <returns>菜单已经打开或成功接纳暂停时为真。</returns>
        public bool TryOpenPause()
        {
            if (CanAbandon) return true;
            if (!CanInteract || !CanManagePanel) return false;
            SetActivePanel(ExpeditionPanelKind.Pause);
            return true;
        }

        /// <summary>
        /// 关闭指定面板，仅释放该面板自己的暂停原因。
        /// </summary>
        /// <param name="kind">窗口拥有的面板种类。</param>
        public void ClosePanel(ExpeditionPanelKind kind)
        {
            if (kind == ExpeditionPanelKind.None || ActivePanel != kind) return;
            if (kind == ExpeditionPanelKind.ExtractionConfirmation)
                Message = "当前房间已完成，可通过上方出口前进。";
            SetActivePanel(kind == ExpeditionPanelKind.AdExtraction && IsRunning && !stopped
                ? IsAwaitingDeathChoice ? ExpeditionPanelKind.DeathChoice : ExpeditionPanelKind.Pause : ExpeditionPanelKind.None);
        }

        /// <summary>
        /// 在面板切换边界清除残留输入和待推进时间。
        /// </summary>
        /// <param name="kind">新的面板状态。</param>
        private void SetActivePanel(ExpeditionPanelKind kind)
        {
            if (ActivePanel == ExpeditionPanelKind.ChestLoot && kind != ExpeditionPanelKind.ChestLoot) chestLootItems.Clear();
            ActivePanel = kind;
            touchMove = Vector2.zero;
            interactionRequested = false;
            World.ClearPendingTime();
            World.SetMove(Player, Vector2.zero);
            Changed?.Invoke();
        }

        /// <summary>
        /// 将装备与局外技能汇总为战斗层的基础数值输入。
        /// </summary>
        /// <param name="items">准备计算的物品集合。</param>
        /// <returns>仅填入战斗属性的配置值。</returns>
        private CombatActorSpec EquipmentSpec(IReadOnlyList<ProjectItem> items)
        {
            ProjectCharacterStats stats = session.CalculateCharacterStats(items);
            return new CombatActorSpec
            {
                MaxHealth = stats.MaxHealth, Attack = stats.Attack, Defense = stats.Defense, CriticalRating = stats.CriticalRating,
                CriticalDamage = stats.CriticalDamage, DodgeChance = stats.DodgeChance, DamageIncrease = stats.DamageIncrease,
                DamageReduction = stats.DamageReduction, AttackRange = stats.AttackRange, MoveSpeed = stats.MoveSpeed
            };
        }

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
