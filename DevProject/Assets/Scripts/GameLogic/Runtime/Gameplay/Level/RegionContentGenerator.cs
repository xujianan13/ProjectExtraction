using System;
using System.Collections.Generic;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 用相互独立的随机序列冻结区域内容，空间映射不消耗内容随机源。
    /// </summary>
    public static class RegionContentGenerator
    {
        /// <summary>
        /// 生成尚未激活的完整区域候选，失败不修改请求或配置。
        /// </summary>
        /// <param name="request">开局已保存的区域身份与种子。</param>
        /// <param name="map">Boss 已绑定的地图描述。</param>
        /// <param name="content">区域内容规则及物品工厂。</param>
        /// <param name="plan">成功时可以持久确认的完整组合。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>所有内容、空间和引用均可用时为真。</returns>
        public static bool TryGenerate(RegionGenerationRequest request, RegionMapDefinition map, RegionContentDefinition content,
            out RegionPlan plan, out string error)
        {
            plan = null;
            error = null;
            if (request == null || map == null || !map.TryValidate(out error) || !ValidateContent(content, out error)) return false;
            if (string.IsNullOrWhiteSpace(request.RunId) || request.RegionId <= 0 || request.RegionIndex < 0 || request.LevelId <= 0 ||
                request.LootLevel <= 0 || request.EnemyLevel <= 0 || !RegionPlan.ValidRoomRequest(request) ||
                string.IsNullOrWhiteSpace(request.ConfigVersion) || request.MapId != map.MapId || request.MapVersion != map.Version)
            { error = "区域生成请求或地图版本无效。"; return false; }
            if (!TrySingleAnchor(map, RegionAnchorKind.Entry, out RegionMapAnchor entry) ||
                !TrySingleAnchor(map, RegionAnchorKind.Exit, out RegionMapAnchor exit))
            { error = "区域入口、出口或绑定的 Boss 锚点不可用。"; return false; }
            var generated = new RegionPlan
            {
                Request = request.Copy(), EntryAnchorId = entry.Id, EntryPosition = entry.Position, ExitAnchorId = exit.Id,
                ExitPosition = exit.Position, PlayerRadius = content.PlayerRadius, InteractionRadius = content.InteractionRadius
            };
            var monsterRandom = new PersistentRandom(request.MonsterSeed);
            var eliteRandom = new PersistentRandom(request.EliteSeed);
            var chestRandom = new PersistentRandom(request.ChestSeed);
            var npcRandom = new PersistentRandom(request.NpcSeed);
            var encounters = Filter(content.MonsterEncounters, value => value.MinLevel <= request.EnemyLevel && request.EnemyLevel <= value.MaxLevel,
                value => value.Id);
            var elites = Filter(content.EliteCandidates, value => value.MinLevel <= request.EnemyLevel && request.EnemyLevel <= value.MaxLevel,
                value => value.Id);
            if (content.MonsterGroupCount > 0 && encounters.Count == 0 || content.EliteCount > 0 && elites.Count == 0)
            { error = "区域等级没有可用的怪群或精英候选。"; return false; }
            var chosenGroups = new List<RegionEncounterCandidate>();
            var chosenElites = new List<RegionEliteCandidate>();
            for (int index = 0; index < content.MonsterGroupCount; index++) chosenGroups.Add(Pick(encounters, value => value.Weight, monsterRandom));
            for (int index = 0; index < content.EliteCount; index++) chosenElites.Add(Pick(elites, value => value.Weight, eliteRandom));
            var chestCandidates = Filter(content.ChestCandidates, _ => true, value => value.Id);
            var npcCandidates = Filter(content.NpcCandidates, _ => true, value => value.Id);
            string prefix = request.RunId + ":region:" + request.RegionId;
            for (int index = 0; index < content.ChestCount; index++)
            {
                RegionChestCandidate selected = Pick(chestCandidates, value => value.Weight, chestRandom);
                var chest = new RegionChestPlan { Id = prefix + ":chest:" + index, DefinitionId = selected.Id };
                for (int itemIndex = 0; itemIndex < selected.ItemCount; itemIndex++)
                {
                    ProjectItem item = content.ItemFactory(chestRandom, request.LootLevel, selected.LootPoolId);
                    if (!FreezeItem(item, chest.Id + ":item:" + itemIndex, out ProjectItem frozen))
                    { error = "区域宝箱物品生成失败。"; return false; }
                    chest.Items.Add(frozen);
                }
                generated.Chests.Add(chest);
            }
            for (int index = 0; index < content.NpcCount; index++)
            {
                RegionNpcCandidate selected = Pick(npcCandidates, value => value.Weight, npcRandom);
                var npc = new RegionNpcPlan { Id = prefix + ":npc:" + index, DefinitionId = selected.Id };
                for (int offerIndex = 0; offerIndex < selected.Stock; offerIndex++)
                {
                    string offerId = npc.Id + ":offer:" + offerIndex;
                    ProjectItem item = content.ItemFactory(npcRandom, request.LootLevel, selected.LootPoolId);
                    if (!FreezeItem(item, offerId, out ProjectItem frozen))
                    { error = "区域 NPC 商品生成失败。"; return false; }
                    npc.Offers.Add(new RegionNpcOfferPlan { Id = offerId, Price = selected.Price, Item = frozen });
                }
                generated.Npcs.Add(npc);
            }
            generated.MonsterRandomState = monsterRandom.State;
            generated.EliteRandomState = eliteRandom.State;
            generated.ChestRandomState = chestRandom.State;
            generated.NpcRandomState = npcRandom.State;
            var monsterAnchors = Anchors(map, RegionAnchorKind.Monster, content.MonsterAnchorTag);
            var eliteAnchors = Anchors(map, RegionAnchorKind.Elite, content.EliteAnchorTag);
            var chestAnchors = Anchors(map, RegionAnchorKind.Chest, content.ChestAnchorTag);
            var npcAnchors = Anchors(map, RegionAnchorKind.Npc, content.NpcAnchorTag);
            if (monsterAnchors.Count < chosenGroups.Count || eliteAnchors.Count < chosenElites.Count ||
                chestAnchors.Count < generated.Chests.Count || npcAnchors.Count < generated.Npcs.Count)
            { error = "区域地图锚点不足，不能减少内容或更换内容池。"; return false; }
            var monsterPositions = new PersistentRandom(unchecked(request.MonsterSeed ^ 0x4D41504D));
            var elitePositions = new PersistentRandom(unchecked(request.EliteSeed ^ 0x4D415045));
            var chestPositions = new PersistentRandom(unchecked(request.ChestSeed ^ 0x4D415043));
            var npcPositions = new PersistentRandom(unchecked(request.NpcSeed ^ 0x4D41504E));
            for (int groupIndex = 0; groupIndex < chosenGroups.Count; groupIndex++)
            {
                RegionEncounterCandidate encounter = chosenGroups[groupIndex];
                long requestedCapacity = 0;
                foreach (RegionSpawnMemberDefinition member in encounter.Members) requestedCapacity += member.Count;
                if (requestedCapacity > int.MaxValue || generated.SpawnSlots.Count + requestedCapacity > content.MaxSpawnSlots ||
                    !TakeAnchor(monsterAnchors, (int)requestedCapacity, monsterPositions, out RegionMapAnchor anchor))
                { error = "区域怪群超过槽位预算或锚点容量。"; return false; }
                string groupId = prefix + ":group:" + groupIndex;
                int memberIndex = 0;
                foreach (RegionSpawnMemberDefinition member in encounter.Members)
                    for (int index = 0; index < member.Count; index++) generated.SpawnSlots.Add(new RegionSpawnSlotPlan
                    {
                        SlotId = groupId + ":member:" + memberIndex++, GroupId = groupId, EncounterId = encounter.Id,
                        EnemyId = member.EnemyId, EnemyLevel = request.EnemyLevel, Kind = RegionEnemyKind.Normal,
                        AnchorId = anchor.Id, HomePosition = anchor.Position + member.OffsetAt(index), Radius = member.Radius
                    });
            }
            for (int index = 0; index < chosenElites.Count; index++)
            {
                if (!TakeAnchor(eliteAnchors, 1, elitePositions, out RegionMapAnchor anchor))
                { error = "区域精英锚点容量不足。"; return false; }
                RegionEliteCandidate elite = chosenElites[index];
                string id = prefix + ":elite:" + index;
                generated.SpawnSlots.Add(new RegionSpawnSlotPlan
                {
                    SlotId = id, GroupId = id, EncounterId = elite.Id, EnemyId = elite.EnemyId, EnemyLevel = request.EnemyLevel,
                    Kind = RegionEnemyKind.Elite, AnchorId = anchor.Id, HomePosition = anchor.Position, Radius = elite.Radius
                });
            }
            foreach (RegionChestPlan chest in generated.Chests)
            {
                if (!TakeAnchor(chestAnchors, 1, chestPositions, out RegionMapAnchor anchor))
                { error = "区域宝箱锚点容量不足。"; return false; }
                chest.AnchorId = anchor.Id;
                chest.Position = anchor.Position;
            }
            foreach (RegionNpcPlan npc in generated.Npcs)
            {
                if (!TakeAnchor(npcAnchors, 1, npcPositions, out RegionMapAnchor anchor))
                { error = "区域 NPC 锚点容量不足。"; return false; }
                npc.AnchorId = anchor.Id;
                npc.Position = anchor.Position;
            }
            if (request.RoomKind == ExpeditionRoomKind.Boss)
            {
                if (!map.TryGetAnchor(request.BossAnchorId, out RegionMapAnchor boss) || boss.Kind != RegionAnchorKind.Boss)
                { error = "Boss 房缺少绑定锚点。"; return false; }
                string bossId = prefix + ":boss";
                generated.SpawnSlots.Add(new RegionSpawnSlotPlan
                {
                    SlotId = bossId, GroupId = bossId, EncounterId = request.BossDefinitionId, EnemyId = request.BossEnemyId,
                    EnemyLevel = request.BossEnemyLevel, Kind = RegionEnemyKind.Boss, AnchorId = boss.Id,
                    HomePosition = boss.Position, Radius = content.BossRadius
                });
            }
            if (generated.SpawnSlots.Count > content.MaxSpawnSlots || !generated.TryValidate(map, out error) ||
                !ValidatePositions(generated, map, content, out error)) return false;
            plan = generated;
            return true;
        }

        /// <summary>
        /// 检查全部内容候选，配置错误不能被合法空池掩盖。
        /// </summary>
        /// <param name="content">区域内容。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>规则及全部候选有效时为真。</returns>
        private static bool ValidateContent(RegionContentDefinition content, out string error)
        {
            error = null;
            if (content == null || content.MonsterGroupCount < 0 || content.EliteCount < 0 || content.ChestCount < 0 || content.NpcCount < 0 ||
                content.MaxSpawnSlots <= 0 || (long)content.MonsterGroupCount + content.EliteCount > content.MaxSpawnSlots ||
                !RegionValueRules.NonNegative(content.BossRadius) || !Positive(content.PlayerRadius) || !Positive(content.InteractionRadius) ||
                !Positive(content.ReachabilityGridSpacing) || content.ReachabilityMaxCells <= 0 ||
                content.MonsterEncounters == null || content.EliteCandidates == null || content.ChestCandidates == null || content.NpcCandidates == null ||
                content.MonsterGroupCount > 0 && string.IsNullOrWhiteSpace(content.MonsterAnchorTag) ||
                content.EliteCount > 0 && string.IsNullOrWhiteSpace(content.EliteAnchorTag) ||
                content.ChestCount > 0 && (string.IsNullOrWhiteSpace(content.ChestAnchorTag) || content.ChestCandidates.Count == 0) ||
                content.NpcCount > 0 && (string.IsNullOrWhiteSpace(content.NpcAnchorTag) || content.NpcCandidates.Count == 0) ||
                (content.ChestCount > 0 || content.NpcCount > 0) && content.ItemFactory == null)
            { error = "区域内容规则无效。"; return false; }
            var ids = new HashSet<int>();
            foreach (RegionEncounterCandidate encounter in content.MonsterEncounters)
            {
                if (encounter == null || !Candidate(encounter.Id, encounter.Weight, ids) || encounter.MinLevel <= 0 ||
                    encounter.MaxLevel < encounter.MinLevel || encounter.Members == null || encounter.Members.Count == 0)
                { error = "区域普通怪群候选无效。"; return false; }
                foreach (RegionSpawnMemberDefinition member in encounter.Members)
                    if (member == null || member.EnemyId <= 0 || member.Count <= 0 || member.Count > content.MaxSpawnSlots ||
                        !Positive(member.Radius) || !RegionValueRules.Finite(member.StartOffset) || !RegionValueRules.Finite(member.StepOffset) ||
                        !RegionValueRules.Finite(member.AlternateZ))
                    { error = "区域怪群成员规则无效。"; return false; }
            }
            ids.Clear();
            foreach (RegionEliteCandidate elite in content.EliteCandidates)
                if (elite == null || !Candidate(elite.Id, elite.Weight, ids) || elite.EnemyId <= 0 || elite.MinLevel <= 0 ||
                    elite.MaxLevel < elite.MinLevel || !Positive(elite.Radius))
                { error = "区域精英候选无效。"; return false; }
            ids.Clear();
            foreach (RegionChestCandidate chest in content.ChestCandidates)
                if (chest == null || !Candidate(chest.Id, chest.Weight, ids) || string.IsNullOrWhiteSpace(chest.LootPoolId) || chest.ItemCount <= 0)
                { error = "区域宝箱候选无效。"; return false; }
            ids.Clear();
            foreach (RegionNpcCandidate npc in content.NpcCandidates)
                if (npc == null || !Candidate(npc.Id, npc.Weight, ids) || string.IsNullOrWhiteSpace(npc.LootPoolId) || npc.Price <= 0 || npc.Stock <= 0)
                { error = "区域 NPC 候选无效。"; return false; }
            return true;
        }

        /// <summary>
        /// 检查目标可达和初始敌人互相占位。
        /// </summary>
        /// <param name="plan">冻结计划。</param>
        /// <param name="map">地图几何。</param>
        /// <param name="content">验证参数。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>所有初始位置可用时为真。</returns>
        private static bool ValidatePositions(RegionPlan plan, RegionMapDefinition map, RegionContentDefinition content, out string error)
        {
            error = null;
            for (int index = 0; index < plan.SpawnSlots.Count; index++)
            {
                RegionSpawnSlotPlan slot = plan.SpawnSlots[index];
                if (Overlaps(slot.HomePosition, slot.Radius, plan.EntryPosition, content.PlayerRadius))
                { error = "区域敌人与玩家入口重叠。"; return false; }
                for (int earlier = 0; earlier < index; earlier++)
                    if (Overlaps(slot.HomePosition, slot.Radius, plan.SpawnSlots[earlier].HomePosition, plan.SpawnSlots[earlier].Radius))
                    { error = "区域敌人初始占位重叠。"; return false; }
            }
            var targets = new List<Vector3> { plan.EntryPosition, plan.ExitPosition };
            foreach (RegionSpawnSlotPlan slot in plan.SpawnSlots) targets.Add(slot.HomePosition);
            foreach (RegionChestPlan chest in plan.Chests) targets.Add(chest.Position);
            foreach (RegionNpcPlan npc in plan.Npcs) targets.Add(npc.Position);
            return new RegionMapGeometry(map).TryValidateReachability(targets, content.PlayerRadius, content.ReachabilityGridSpacing,
                content.ReachabilityMaxCells, out error);
        }

        /// <summary>
        /// 把物品工厂结果冻结为业务身份稳定的实例。
        /// </summary>
        /// <param name="item">工厂结果。</param>
        /// <param name="id">确定性实例身份。</param>
        /// <param name="frozen">冻结副本。</param>
        /// <returns>结果有效时为真。</returns>
        private static bool FreezeItem(ProjectItem item, string id, out ProjectItem frozen)
        {
            frozen = null;
            if (item == null) return false;
            ProjectItem candidate = item.Copy();
            candidate.Id = id;
            candidate.Equipped = false;
            if (ProjectEquipmentRules.ValidateItem(candidate) != null) return false;
            frozen = candidate;
            return true;
        }

        /// <summary>
        /// 校验权重候选身份。
        /// </summary>
        /// <param name="id">候选身份。</param>
        /// <param name="weight">抽样权重。</param>
        /// <param name="ids">已见候选身份。</param>
        /// <returns>身份唯一且权重为正时为真。</returns>
        private static bool Candidate(int id, int weight, HashSet<int> ids) => id > 0 && weight > 0 && ids.Add(id);

        /// <summary>
        /// 检查有限正数。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>有限且为正时为真。</returns>
        private static bool Positive(float value) => RegionValueRules.Finite(value) && value > 0f;

        /// <summary>
        /// 查询唯一固定用途的锚点。
        /// </summary>
        /// <param name="map">地图描述。</param>
        /// <param name="kind">目标用途。</param>
        /// <param name="anchor">唯一锚点。</param>
        /// <returns>恰有一个目标用途锚点时为真。</returns>
        private static bool TrySingleAnchor(RegionMapDefinition map, RegionAnchorKind kind, out RegionMapAnchor anchor)
        {
            anchor = null;
            foreach (RegionMapAnchor candidate in map.Anchors)
            {
                if (candidate.Kind != kind) continue;
                if (anchor != null) return false;
                anchor = candidate;
            }
            return anchor != null;
        }

        /// <summary>
        /// 收集稳定排序的可用空间锚点。
        /// </summary>
        /// <param name="map">地图描述。</param>
        /// <param name="kind">用途。</param>
        /// <param name="tag">要求的标签。</param>
        /// <returns>独立候选列表。</returns>
        private static List<RegionMapAnchor> Anchors(RegionMapDefinition map, RegionAnchorKind kind, string tag)
        {
            List<RegionMapAnchor> result = map.Anchors.FindAll(value => value.Kind == kind && value.Tag == tag);
            result.Sort((left, right) => StringComparer.Ordinal.Compare(left.Id, right.Id));
            return result;
        }

        /// <summary>
        /// 从容量足够的候选锚点中抽取且移除一个。
        /// </summary>
        /// <param name="anchors">尚未使用的空间候选。</param>
        /// <param name="capacity">本次所需容量。</param>
        /// <param name="random">只用于位置的随机源。</param>
        /// <param name="anchor">选中锚点。</param>
        /// <returns>有足够容量的锚点时为真。</returns>
        private static bool TakeAnchor(List<RegionMapAnchor> anchors, int capacity, System.Random random, out RegionMapAnchor anchor)
        {
            var available = anchors.FindAll(value => value.Capacity >= capacity);
            if (available.Count > 0)
            {
                int smallestCapacity = int.MaxValue;
                foreach (RegionMapAnchor candidate in available) smallestCapacity = Math.Min(smallestCapacity, candidate.Capacity);
                available.RemoveAll(value => value.Capacity != smallestCapacity);
            }
            anchor = available.Count == 0 ? null : available[random.Next(available.Count)];
            if (anchor == null) return false;
            anchors.Remove(anchor);
            return true;
        }

        /// <summary>
        /// 稳定排序后构建候选副列表。
        /// </summary>
        /// <typeparam name="T">候选类型。</typeparam>
        /// <typeparam name="TKey">可比较身份类型。</typeparam>
        /// <param name="source">源候选。</param>
        /// <param name="predicate">筛选条件。</param>
        /// <param name="key">稳定排序身份。</param>
        /// <returns>筛选及排序后的副列表。</returns>
        private static List<T> Filter<T, TKey>(List<T> source, Predicate<T> predicate, Func<T, TKey> key)
        {
            List<T> result = source.FindAll(predicate);
            result.Sort((left, right) => Comparer<TKey>.Default.Compare(key(left), key(right)));
            return result;
        }

        /// <summary>
        /// 按正整数权重抽样，避免权重累加溢出。
        /// </summary>
        /// <typeparam name="T">候选类型。</typeparam>
        /// <param name="candidates">稳定排序候选。</param>
        /// <param name="weight">权重读取器。</param>
        /// <param name="random">内容独立随机源。</param>
        /// <returns>选中的候选。</returns>
        private static T Pick<T>(List<T> candidates, Func<T, int> weight, System.Random random)
        {
            long total = 0;
            foreach (T candidate in candidates) total += weight(candidate);
            double roll = random.NextDouble() * total;
            foreach (T candidate in candidates)
            {
                roll -= weight(candidate);
                if (roll < 0) return candidate;
            }
            return candidates[candidates.Count - 1];
        }

        /// <summary>
        /// 检查两个平面圆形占位是否重叠。
        /// </summary>
        /// <param name="first">第一圆心。</param>
        /// <param name="firstRadius">第一半径。</param>
        /// <param name="second">第二圆心。</param>
        /// <param name="secondRadius">第二半径。</param>
        /// <returns>严格重叠时为真。</returns>
        private static bool Overlaps(Vector3 first, float firstRadius, Vector3 second, float secondRadius)
        {
            float dx = first.x - second.x, dz = first.z - second.z, radius = firstRadius + secondRadius;
            return dx * dx + dz * dz < radius * radius;
        }
    }
}
