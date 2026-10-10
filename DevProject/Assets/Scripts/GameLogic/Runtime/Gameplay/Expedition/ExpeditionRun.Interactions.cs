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
        private enum InteractionTargetKind { Loot, Exit, Chest, Merchant }

        /// <summary>
        /// 同一房间内可以被明确选择的一次交互目标。
        /// </summary>
        private struct InteractionTarget
        {
            public string Id;
            public InteractionTargetKind Kind;
            public ExpeditionDrop Drop;
            public RoomDirection Direction;
            public float DistanceSquared;
        }

        private readonly List<InteractionTarget> interactionTargets = new();
        private string requestedInteractionId;
        private string describedInteractionId;
        private InteractionTarget selectedInteraction;

        /// <summary>
        /// 当前选定目标的稳定身份，无目标时为空。
        /// </summary>
        public string SelectedInteractionId => selectedInteraction.Id ?? string.Empty;

        /// <summary>
        /// 当前选定地面物品的实例身份，其他目标返回空字符串。
        /// </summary>
        public string SelectedLootId => selectedInteraction.Drop?.Item.Id ?? string.Empty;

        /// <summary>
        /// 当前目标的玩家可见说明，不包含尚未探索的房间信息。
        /// </summary>
        public string InteractionDescription { get; private set; } = string.Empty;

        /// <summary>
        /// 当前交互范围内可切换的目标数量。
        /// </summary>
        public int InteractionCount => interactionTargets.Count;

        /// <summary>
        /// 当前是否存在明确选定的交互目标。
        /// </summary>
        public bool HasInteraction => !string.IsNullOrEmpty(selectedInteraction.Id);

        /// <summary>
        /// 取得当前地面物品的独立副本，供界面仅在目标变化时生成摘要。
        /// </summary>
        /// <param name="item">选定物品的副本，非物品目标时为空。</param>
        /// <returns>当前选定未拾取物品时为真。</returns>
        public bool TryGetInteractionItem(out ProjectItem item)
        {
            ExpeditionDrop drop = selectedInteraction.Drop;
            item = drop != null && !drop.Collected ? drop.Item.Copy() : null;
            return item != null;
        }

        /// <summary>
        /// 按距离与稳定身份切换附近目标，不自动执行交互。
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
        /// 重建附近候选并保持仍然有效的选择，目标离开范围后才重新选择最近目标。
        /// </summary>
        /// <param name="position">玩家权威位置。</param>
        private void UpdateInteractionLabel(Vector3 position)
        {
            string previous = SelectedInteractionId;
            interactionTargets.Clear();
            var layout = CurrentRoom.Plan.Definition.Layout;
            float objectRadius = (float)layout.ObjectInteractionRadius;
            foreach (ExpeditionDrop drop in drops)
            {
                if (drop.Collected || drop.RoomId != Maps.CurrentRoomId) continue;
                AddInteractionTarget(new InteractionTarget { Id = "loot:" + drop.Item.Id, Kind = InteractionTargetKind.Loot, Drop = drop },
                    position, drop.Position, objectRadius);
            }
            float center = (float)(layout.HalfExtent - layout.ExitCenterInset);
            foreach (RoomDirection direction in CurrentRoom.Exits.Keys)
            {
                Vector3 target = direction == RoomDirection.North ? new Vector3(0, 0, center) :
                    direction == RoomDirection.South ? new Vector3(0, 0, -center) :
                    direction == RoomDirection.East ? new Vector3(center, 0, 0) : new Vector3(-center, 0, 0);
                AddInteractionTarget(new InteractionTarget { Id = "exit:" + Maps.CurrentRoomId + ":" + direction,
                    Kind = InteractionTargetKind.Exit, Direction = direction }, position, target, (float)layout.ExitInteractionRadius);
            }
            if (CurrentRoom.Type == LevelRoomType.Loot && !CurrentRoom.ChestOpened)
                AddInteractionTarget(new InteractionTarget { Id = "chest:" + Maps.CurrentRoomId, Kind = InteractionTargetKind.Chest },
                    position, new Vector3((float)layout.ChestX, 0, (float)layout.ChestZ), objectRadius);
            if (CurrentRoom.Type == LevelRoomType.Npc)
                AddInteractionTarget(new InteractionTarget { Id = "merchant:" + Maps.CurrentRoomId, Kind = InteractionTargetKind.Merchant },
                    position, new Vector3((float)layout.NpcX, 0, (float)layout.NpcZ), objectRadius);
            interactionTargets.Sort(CompareInteractionTargets);
            int retained = interactionTargets.FindIndex(value => value.Id == previous);
            selectedInteraction = retained >= 0 ? interactionTargets[retained] : interactionTargets.Count > 0 ? interactionTargets[0] : default;
            RefreshInteractionText();
        }

        /// <summary>
        /// 将仍在水平交互范围内的目标加入候选列表。
        /// </summary>
        /// <param name="target">候选目标。</param>
        /// <param name="playerPosition">玩家权威位置。</param>
        /// <param name="targetPosition">交互中心位置。</param>
        /// <param name="radius">配置的交互半径。</param>
        private void AddInteractionTarget(InteractionTarget target, Vector3 playerPosition, Vector3 targetPosition, float radius)
        {
            float dx = playerPosition.x - targetPosition.x, dz = playerPosition.z - targetPosition.z;
            target.DistanceSquared = dx * dx + dz * dz;
            if (target.DistanceSquared <= radius * radius) interactionTargets.Add(target);
        }

        /// <summary>
        /// 先按距离排序，同距离时按稳定身份排序。
        /// </summary>
        /// <param name="left">左侧目标。</param>
        /// <param name="right">右侧目标。</param>
        /// <returns>两个候选目标的排序关系。</returns>
        private static int CompareInteractionTargets(InteractionTarget left, InteractionTarget right)
        {
            int distance = left.DistanceSquared.CompareTo(right.DistanceSquared);
            return distance != 0 ? distance : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
        }

        /// <summary>
        /// 在选中目标变化时更新摘要，出口信息只读取已过滤的小地图投影。
        /// </summary>
        private void RefreshInteractionText()
        {
            if (describedInteractionId == SelectedInteractionId) return;
            describedInteractionId = SelectedInteractionId;
            InteractionLabel = "交互 / E";
            InteractionDescription = string.Empty;
            if (!HasInteraction) return;
            switch (selectedInteraction.Kind)
            {
                case InteractionTargetKind.Loot:
                    InteractionLabel = "拾取 / E";
                    break;
                case InteractionTargetKind.Exit:
                    InteractionLabel = "进入地图 / E";
                    string direction = selectedInteraction.Direction == RoomDirection.North ? "北" :
                        selectedInteraction.Direction == RoomDirection.South ? "南" : selectedInteraction.Direction == RoomDirection.East ? "东" : "西";
                    string destinationType = "未探索";
                    if (Maps.TryGetDestination(selectedInteraction.Direction, out int destination))
                    {
                        foreach (LevelMinimapRoom room in Maps.GetMinimapProjection().Rooms)
                            if (room.RoomId == destination && room.IsVisited)
                                destinationType = room.Type == LevelRoomType.Boss ? "首领房" : room.Type == LevelRoomType.Loot ? "战利品房" :
                                    room.Type == LevelRoomType.Npc ? "商店" : "战斗房";
                        InteractionDescription = direction + "侧出口 · 地图 " + destination + " · " + destinationType;
                    }
                    break;
                case InteractionTargetKind.Chest:
                    InteractionLabel = "打开箱子 / E";
                    InteractionDescription = "战利品箱 · 随机物品与强化选择";
                    break;
                case InteractionTargetKind.Merchant:
                    InteractionLabel = "查看商店 / E";
                    InteractionDescription = "本局商店 · 查看商品后购买";
                    break;
            }
        }

        /// <summary>
        /// 仅执行按下交互时锁定且本次仍在范围内的目标，拒绝自动替换目标。
        /// </summary>
        /// <param name="position">本次战斗 tick 后的玩家位置。</param>
        private void Interact(Vector3 position)
        {
            if (!CanInteract) return;
            int index = interactionTargets.FindIndex(value => value.Id == requestedInteractionId);
            if (index < 0) { Message = "目标已离开交互范围，请重新选择。"; return; }
            InteractionTarget target = interactionTargets[index];
            if (target.Kind == InteractionTargetKind.Exit) { ChangeRoom(target.Direction); return; }
            if (target.Kind == InteractionTargetKind.Loot)
            {
                target.Drop.Collected = true;
                inventory.Add(target.Drop.Item);
                Message = "拾取：" + target.Drop.Item.Name;
            }
            else if (target.Kind == InteractionTargetKind.Chest)
            {
                CurrentRoom.MarkChestOpened();
                inventory.Add(ProjectEquipmentRules.CreateLootFromPool(random, LootLevel, CurrentRoom.Plan.LootPoolID));
                Message = "已打开箱子，获得一件战利品。";
                TryBeginRogueChoice("chest:" + CurrentRoom.RoomId);
            }
            else if (target.Kind == InteractionTargetKind.Merchant) TryOpenMerchant();
            RoomView.Refresh();
            UpdateInteractionLabel(position);
        }

        /// <summary>
        /// 换房或终局时清除旧目标与尚未执行的交互请求。
        /// </summary>
        private void ClearInteractionTargets()
        {
            interactionTargets.Clear();
            selectedInteraction = default;
            requestedInteractionId = null;
            interactionRequested = false;
            describedInteractionId = null;
            RefreshInteractionText();
        }
    }
}
