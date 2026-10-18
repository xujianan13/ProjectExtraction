using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        private enum InteractionTargetKind { Loot, Chest, Merchant, Extraction }

        /// <summary>
        /// 当前区域内可明确选择的一个目标。
        /// </summary>
        private struct InteractionTarget
        {
            public string Id;
            public InteractionTargetKind Kind;
            public ExpeditionDrop Drop;
            public float DistanceSquared;
        }

        private readonly List<InteractionTarget> interactionTargets = new();
        private readonly HashSet<string> touchingTargets = new(StringComparer.Ordinal);
        private string requestedInteractionId;
        private InteractionTarget selectedInteraction;
        public string SelectedInteractionId => selectedInteraction.Id ?? string.Empty;
        public string SelectedLootId => selectedInteraction.Drop?.Item.Id ?? string.Empty;
        public string InteractionDescription { get; private set; } = string.Empty;
        public int InteractionCount => interactionTargets.Count;
        public bool HasInteraction => !string.IsNullOrEmpty(selectedInteraction.Id);

        /// <summary>
        /// 取得选定地面物品的独立详情。
        /// </summary>
        /// <param name="item">选定物品副本。</param>
        /// <returns>选定未领取物品时为真。</returns>
        public bool TryGetInteractionItem(out ProjectItem item)
        {
            ExpeditionDrop drop = selectedInteraction.Drop;
            item = drop != null && !drop.Collected ? drop.Item.Copy() : null;
            return item != null;
        }

        /// <summary>
        /// 按距离和稳定身份切换附近目标，不自动执行交互。
        /// </summary>
        public void CycleInteraction()
        {
            if (!CanInteract || !World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead) return;
            UpdateInteractionLabel(player.Position);
            if (interactionTargets.Count < 2) return;
            int index = interactionTargets.FindIndex(value => value.Id == SelectedInteractionId);
            selectedInteraction = interactionTargets[(index + 1) % interactionTargets.Count];
            RefreshInteractionText();
            Changed?.Invoke();
        }

        /// <summary>
        /// 重建范围内候选并保持仍然有效的选择。
        /// </summary>
        /// <param name="position">玩家权威位置。</param>
        private void UpdateInteractionLabel(Vector3 position)
        {
            string previous = SelectedInteractionId;
            interactionTargets.Clear();
            RegionRuntimeState region = CurrentRoom;
            float radius = region.Plan.InteractionRadius;
            foreach (ExpeditionDrop drop in drops)
                if (!drop.Collected && drop.RoomId == region.Plan.Request.RegionId)
                    AddInteractionTarget(new InteractionTarget { Id = "loot:" + drop.Item.Id, Kind = InteractionTargetKind.Loot, Drop = drop },
                        position, drop.Position, radius);
            foreach (RegionChestPlan chest in region.Plan.Chests)
            {
                RegionChestState state = region.Chests.Find(value => value.Id == chest.Id);
                if (!state.Opened)
                    AddInteractionTarget(new InteractionTarget { Id = chest.Id, Kind = InteractionTargetKind.Chest }, position, chest.Position, radius);
            }
            foreach (RegionNpcPlan npc in region.Plan.Npcs)
                AddInteractionTarget(new InteractionTarget { Id = npc.Id, Kind = InteractionTargetKind.Merchant }, position, npc.Position, radius);
            if (region.ExitEnabled && !IsFinalRoom)
                AddInteractionTarget(new InteractionTarget { Id = RunId + ":exit:" + region.Plan.Request.RegionId, Kind = InteractionTargetKind.Extraction },
                    position, region.Plan.ExitPosition, radius);
            interactionTargets.Sort((left, right) => left.DistanceSquared != right.DistanceSquared ?
                left.DistanceSquared.CompareTo(right.DistanceSquared) : string.CompareOrdinal(left.Id, right.Id));
            int retained = interactionTargets.FindIndex(value => value.Id == previous);
            selectedInteraction = retained >= 0 ? interactionTargets[retained] : interactionTargets.Count > 0 ? interactionTargets[0] : default;
            RefreshInteractionText();
        }

        /// <summary>
        /// 将处于水平交互距离内的目标加入列表。
        /// </summary>
        /// <param name="target">候选目标。</param>
        /// <param name="playerPosition">玩家位置。</param>
        /// <param name="targetPosition">目标位置。</param>
        /// <param name="radius">允许交互距离。</param>
        private void AddInteractionTarget(InteractionTarget target, Vector3 playerPosition, Vector3 targetPosition, float radius)
        {
            float dx = playerPosition.x - targetPosition.x, dz = playerPosition.z - targetPosition.z;
            target.DistanceSquared = dx * dx + dz * dz;
            if (target.DistanceSquared <= radius * radius) interactionTargets.Add(target);
        }

        /// <summary>
        /// 以权威状态生成具体交互目标说明。
        /// </summary>
        private void RefreshInteractionText()
        {
            InteractionLabel = "交互 / E";
            InteractionDescription = string.Empty;
            if (!HasInteraction) return;
            switch (selectedInteraction.Kind)
            {
                case InteractionTargetKind.Loot:
                    InteractionLabel = "拾取 / E";
                    InteractionDescription = selectedInteraction.Drop.Item.Name;
                    break;
                case InteractionTargetKind.Chest:
                    InteractionLabel = "触碰宝箱";
                    InteractionDescription = "触碰后自动领取宝箱物品并展示收获";
                    break;
                case InteractionTargetKind.Merchant:
                    InteractionLabel = "触碰商人";
                    InteractionDescription = "触碰后查看商店，离开再靠近可重新打开";
                    break;
                case InteractionTargetKind.Extraction:
                    InteractionLabel = "触碰房间出口";
                    InteractionDescription = "触碰后选择前进，关闭后离开再靠近可重开";
                    break;
            }
        }

        /// <summary>
        /// 进入 NPC、宝箱或出口范围时自动触发，关闭面板后必须离开范围才能再次触发。
        /// </summary>
        /// <param name="position">完整逻辑帧后的玩家权威位置。</param>
        private void ProcessTouchInteractions(Vector3 position)
        {
            touchingTargets.RemoveWhere(id => !interactionTargets.Exists(target => target.Kind != InteractionTargetKind.Loot && target.Id == id));
            InteractionTarget entered = default;
            foreach (InteractionTarget target in interactionTargets)
            {
                if (target.Kind == InteractionTargetKind.Loot || !touchingTargets.Add(target.Id)) continue;
                if (entered.Id == null) entered = target;
            }
            if (entered.Id == null) return;
            selectedInteraction = entered;
            RefreshInteractionText();
            ExecuteInteraction(entered, position);
        }

        /// <summary>
        /// 手动拾取仍在范围内的地面物品，房间设施统一通过触碰触发。
        /// </summary>
        /// <param name="position">完整逻辑帧后的玩家位置。</param>
        private void Interact(Vector3 position)
        {
            if (!CanInteract) return;
            int index = interactionTargets.FindIndex(value => value.Id == requestedInteractionId);
            if (index < 0) { Message = "目标已离开交互范围，请重新选择。"; return; }
            InteractionTarget target = interactionTargets[index];
            if (target.Kind == InteractionTargetKind.Loot) ExecuteInteraction(target, position);
        }

        /// <summary>
        /// 执行当前合法目标，将领取或访问事实保存后再开放对应暂停面板。
        /// </summary>
        /// <param name="target">已经确认进入范围或手动拾取的目标。</param>
        /// <param name="position">当前玩家权威位置。</param>
        private void ExecuteInteraction(InteractionTarget target, Vector3 position)
        {
            if (!CanInteract) return;
            if (target.Kind == InteractionTargetKind.Extraction) { TryOpenExtractionConfirmation(); return; }
            if (target.Kind == InteractionTargetKind.Merchant) { TryOpenMerchant(target.Id); return; }
            if (target.Kind == InteractionTargetKind.Loot)
            {
                if (target.Drop.Collected) return;
                target.Drop.Collected = true;
                inventory.Add(target.Drop.Item.Copy());
                SaveMutation("已拾取：" + target.Drop.Item.Name, () => UpdateInteractionLabel(position));
                return;
            }
            RegionChestPlan chest = CurrentRoom.Plan.Chests.Find(value => value.Id == target.Id);
            RegionChestState state = CurrentRoom.Chests.Find(value => value.Id == target.Id);
            if (chest == null || state == null) return;
            if (!state.Opened && !CurrentRoom.TryOpenChest(chest.Id)) return;
            var received = new List<ProjectItem>();
            foreach (ProjectItem item in chest.Items)
            {
                if (state.ClaimedItemIds.Contains(item.Id)) continue;
                if (!CurrentRoom.TryClaimChestItem(chest.Id, item.Id)) { HandleFault("宝箱物品归属异常。"); return; }
                inventory.Add(item.Copy());
                received.Add(item.Copy());
            }
            SaveMutation("已领取宝箱中的 " + received.Count + " 件物品。", () =>
            {
                if (stopped || !IsRunning || !IsReady) return;
                UpdateInteractionLabel(position);
                if (received.Count == 0) return;
                chestLootItems.AddRange(received);
                SetActivePanel(ExpeditionPanelKind.ChestLoot);
            });
        }

        /// <summary>
        /// 区域切换和终局时清除旧目标及待执行意图。
        /// </summary>
        private void ClearInteractionTargets()
        {
            interactionTargets.Clear();
            touchingTargets.Clear();
            selectedInteraction = default;
            requestedInteractionId = null;
            InteractionLabel = "交互 / E";
            InteractionDescription = string.Empty;
        }
    }
}
