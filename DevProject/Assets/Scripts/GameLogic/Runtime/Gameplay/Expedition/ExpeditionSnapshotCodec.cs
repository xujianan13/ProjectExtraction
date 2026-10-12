using System;
using System.Collections.Generic;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 将完整运行快照拆分为稳定组件，使保存日志只写实际变化的状态。
    /// </summary>
    public static class ExpeditionSnapshotCodec
    {
        /// <summary>
        /// 完整修订必须包含的组件身份，防止遗漏非玩家实体后仍被当成完整世界。
        /// </summary>
        [Serializable]
        private sealed class ComponentManifest
        {
            public List<string> Keys = new();
        }

        /// <summary>
        /// 将完整快照编码为有序组件，静态区域计划与动态生命分开保存。
        /// </summary>
        /// <param name="snapshot">不再修改的完整提交候选。</param>
        /// <param name="components">可交给日志的独立字符串集合。</param>
        /// <param name="error">编码失败的说明。</param>
        /// <returns>结构满足恢复契约时为真。</returns>
        public static bool TryEncode(ExpeditionRunSnapshot snapshot, out List<SaveStateComponent> components, out string error)
        {
            components = new List<SaveStateComponent>();
            error = "关卡快照身份或内容不完整。";
            if (snapshot == null || snapshot.Version != 1 || string.IsNullOrWhiteSpace(snapshot.RunId) || snapshot.World == null ||
                snapshot.Requests == null || snapshot.Regions == null || snapshot.CurrentRegionIndex < 0 ||
                snapshot.CurrentRegionIndex >= snapshot.Requests.Count || !ValidWorldLists(snapshot.World)) return false;
            if (!ValidRequests(snapshot)) return false;
            foreach (RegionRuntimeState region in snapshot.Regions)
                if (!ValidRegionParts(region)) return false;
            var header = new ExpeditionRunSnapshot
            {
                Version = snapshot.Version, ConfigVersion = snapshot.ConfigVersion, RunId = snapshot.RunId, LevelId = snapshot.LevelId,
                Seed = snapshot.Seed, LootLevel = snapshot.LootLevel, CurrentRegionIndex = snapshot.CurrentRegionIndex,
                NextActorId = snapshot.NextActorId, KillExperience = snapshot.KillExperience, GoldSpent = snapshot.GoldSpent,
                AugmentBaseAttackRange = snapshot.AugmentBaseAttackRange, LootRandomState = snapshot.LootRandomState,
                BossRandomState = snapshot.BossRandomState, ActiveRogueQualification = snapshot.ActiveRogueQualification,
                RegionInitialized = snapshot.RegionInitialized, Requests = snapshot.Requests, Inventory = snapshot.Inventory,
                ProtectedItemIds = snapshot.ProtectedItemIds, Drops = snapshot.Drops, PendingBossRegions = snapshot.PendingBossRegions,
                Rogue = snapshot.Rogue, Progress = snapshot.Progress, BossLoot = snapshot.BossLoot,
                Statistics = snapshot.Statistics, HasPendingOutcome = snapshot.HasPendingOutcome, PendingOutcome = snapshot.PendingOutcome
            };
            Add(components, "run", header);
            CombatWorldSnapshot world = snapshot.World;
            Add(components, "world", new CombatWorldSnapshot
            {
                Version = world.Version, RandomAlgorithmVersion = world.RandomAlgorithmVersion, RandomState = world.RandomState,
                Tick = world.Tick, NextEntityKey = world.NextEntityKey, NextRootId = world.NextRootId, NextTriggerId = world.NextTriggerId,
                NextDeathId = world.NextDeathId, HighestActorId = world.HighestActorId, ActiveRoomId = world.ActiveRoomId, PlayerKey = world.PlayerKey
            });
            foreach (CombatRoomState room in world.Rooms) Add(components, "world.room." + room.RoomId, room);
            foreach (CombatActorState actor in world.Actors) Add(components, "world.actor." + actor.Key, actor);
            foreach (CombatCastState cast in world.Casts) Add(components, "world.cast." + cast.Key, cast);
            foreach (CombatDisplacementState displacement in world.Displacements) Add(components, "world.move." + displacement.Key, displacement);
            foreach (CombatProjectileState projectile in world.Projectiles) Add(components, "world.projectile." + projectile.Key, projectile);
            foreach (CombatBuffState buff in world.Buffs) Add(components, "world.buff." + buff.Key, buff);
            foreach (RegionRuntimeState region in snapshot.Regions)
            {
                string prefix = "region." + region.Plan.Request.RegionId;
                Add(components, prefix + ".plan", region.Plan);
                Add(components, prefix + ".state", new RegionRuntimeState
                {
                    EffectiveTick = region.EffectiveTick, BossDefeated = region.BossDefeated, BossRewardsCompleted = region.BossRewardsCompleted,
                    ExitEnabled = region.ExitEnabled, TrackingTargetId = region.TrackingTargetId, Chests = region.Chests, Npcs = region.Npcs
                });
                foreach (RegionSpawnSlotState slot in region.Slots) Add(components, prefix + ".slot." + slot.SlotId, slot);
            }
            components.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            for (int index = 1; index < components.Count; index++)
                if (components[index - 1].Key == components[index].Key) return false;
            Add(components, "manifest", new ComponentManifest { Keys = components.ConvertAll(value => value.Key) });
            components.Sort((left, right) => string.CompareOrdinal(left.Key, right.Key));
            error = null;
            return true;
        }

        /// <summary>
        /// 从同一完整日志修订重组快照，拒绝缺失头、未知组件及重复身份。
        /// </summary>
        /// <param name="components">已由日志验证连续性和完整性的组件。</param>
        /// <param name="snapshot">重组后的运行快照。</param>
        /// <param name="error">格式或结构错误说明。</param>
        /// <returns>全部组件可以重组时为真，内容合法性仍由运行层验证。</returns>
        public static bool TryDecode(IReadOnlyList<SaveStateComponent> components, out ExpeditionRunSnapshot snapshot, out string error)
        {
            snapshot = null;
            error = "关卡日志组件缺失或损坏。";
            if (components == null) return false;
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (SaveStateComponent component in components)
                if (component == null || string.IsNullOrWhiteSpace(component.Key) || string.IsNullOrWhiteSpace(component.Value) ||
                    !values.TryAdd(component.Key, component.Value)) return false;
            if (!values.TryGetValue("run", out string runJson) || !values.TryGetValue("world", out string worldJson) ||
                !values.TryGetValue("manifest", out string manifestJson)) return false;
            try
            {
                ComponentManifest manifest = JsonUtility.FromJson<ComponentManifest>(manifestJson);
                if (manifest?.Keys == null || manifest.Keys.Count != values.Count - 1) return false;
                var manifestKeys = new HashSet<string>(StringComparer.Ordinal);
                foreach (string key in manifest.Keys)
                    if (string.IsNullOrWhiteSpace(key) || key == "manifest" || !manifestKeys.Add(key) || !values.ContainsKey(key)) return false;
                ExpeditionRunSnapshot restored = JsonUtility.FromJson<ExpeditionRunSnapshot>(runJson);
                CombatWorldSnapshot world = JsonUtility.FromJson<CombatWorldSnapshot>(worldJson);
                if (restored == null || world == null || restored.Regions == null || restored.Regions.Count != 0 ||
                    world.Rooms == null || world.Actors == null || world.Casts == null || world.Displacements == null ||
                    world.Projectiles == null || world.Buffs == null ||
                    world.Rooms.Count != 0 || world.Actors.Count != 0 || world.Casts.Count != 0 || world.Displacements.Count != 0 ||
                    world.Projectiles.Count != 0 || world.Buffs.Count != 0) return false;
                if (restored.Version != 1 || string.IsNullOrWhiteSpace(restored.RunId) || restored.Requests == null ||
                    restored.CurrentRegionIndex < 0 || restored.CurrentRegionIndex >= restored.Requests.Count || !ValidRequests(restored)) return false;
                if (!restored.HasPendingOutcome) restored.PendingOutcome = null;
                else if (restored.PendingOutcome == null || restored.PendingOutcome.RunId != restored.RunId) return false;
                restored.World = world;
                var regions = new Dictionary<string, RegionRuntimeState>(StringComparer.Ordinal);
                foreach (var pair in values)
                {
                    if (!pair.Key.StartsWith("region.", StringComparison.Ordinal) || !pair.Key.EndsWith(".plan", StringComparison.Ordinal)) continue;
                    string prefix = pair.Key.Substring(0, pair.Key.Length - 5);
                    if (!values.TryGetValue(prefix + ".state", out string stateJson)) return false;
                    RegionRuntimeState region = JsonUtility.FromJson<RegionRuntimeState>(stateJson);
                    RegionPlan plan = JsonUtility.FromJson<RegionPlan>(pair.Value);
                    if (region == null || plan?.Request == null || region.Slots == null || region.Slots.Count != 0 ||
                        plan.Request.RegionId <= 0 || plan.Request.RunId != restored.RunId ||
                        plan.SpawnSlots == null || region.Chests == null || region.Npcs == null ||
                        prefix != "region." + plan.Request.RegionId || !regions.TryAdd(prefix, region)) return false;
                    region.Plan = plan;
                }
                foreach (var pair in values)
                {
                    string key = pair.Key;
                    if (key == "run" || key == "world" || key == "manifest") continue;
                    if (key.StartsWith("world.room.", StringComparison.Ordinal))
                    {
                        if (!ReadComponent(key, pair.Value, "world.room.", value => value.RoomId, world.Rooms)) return false;
                    }
                    else if (key.StartsWith("world.actor.", StringComparison.Ordinal))
                    {
                        if (!ReadComponent(key, pair.Value, "world.actor.", value => value.Key, world.Actors)) return false;
                    }
                    else if (key.StartsWith("world.cast.", StringComparison.Ordinal))
                    {
                        if (!ReadComponent(key, pair.Value, "world.cast.", value => value.Key, world.Casts)) return false;
                    }
                    else if (key.StartsWith("world.move.", StringComparison.Ordinal))
                    {
                        if (!ReadComponent(key, pair.Value, "world.move.", value => value.Key, world.Displacements)) return false;
                    }
                    else if (key.StartsWith("world.projectile.", StringComparison.Ordinal))
                    {
                        if (!ReadComponent(key, pair.Value, "world.projectile.", value => value.Key, world.Projectiles)) return false;
                    }
                    else if (key.StartsWith("world.buff.", StringComparison.Ordinal))
                    {
                        if (!ReadComponent(key, pair.Value, "world.buff.", value => value.Key, world.Buffs)) return false;
                    }
                    else if (key.StartsWith("region.", StringComparison.Ordinal))
                    {
                        int separator = key.IndexOf('.', 7);
                        if (separator < 0 || !regions.TryGetValue(key.Substring(0, separator), out RegionRuntimeState region)) return false;
                        string suffix = key.Substring(separator);
                        if (suffix.StartsWith(".slot.", StringComparison.Ordinal))
                        {
                            RegionSpawnSlotState slot = JsonUtility.FromJson<RegionSpawnSlotState>(pair.Value);
                            if (slot == null || suffix != ".slot." + slot.SlotId) return false;
                            region.Slots.Add(slot);
                        }
                        else if (suffix != ".plan" && suffix != ".state") return false;
                    }
                    else return false;
                }
                foreach (RegionRuntimeState region in regions.Values)
                {
                    if (!ValidRegionParts(region) || region.Plan.Request.RegionIndex >= restored.Requests.Count ||
                        region.Plan.Request.RegionIndex < 0 || JsonUtility.ToJson(region.Plan.Request) !=
                        JsonUtility.ToJson(restored.Requests[region.Plan.Request.RegionIndex])) return false;
                    var bySlot = new Dictionary<string, RegionSpawnSlotState>(StringComparer.Ordinal);
                    foreach (RegionSpawnSlotState slot in region.Slots) bySlot.Add(slot.SlotId, slot);
                    region.Slots.Clear();
                    foreach (RegionSpawnSlotPlan slot in region.Plan.SpawnSlots) region.Slots.Add(bySlot[slot.SlotId]);
                    restored.Regions.Add(region);
                }
                restored.Regions.Sort((left, right) => left.Plan.Request.RegionIndex.CompareTo(right.Plan.Request.RegionIndex));
                world.Rooms.Sort((left, right) => left.RoomId.CompareTo(right.RoomId));
                world.Actors.Sort((left, right) => left.Key.CompareTo(right.Key));
                world.Casts.Sort((left, right) => left.Key.CompareTo(right.Key));
                world.Displacements.Sort((left, right) => left.Key.CompareTo(right.Key));
                world.Projectiles.Sort((left, right) => left.Key.CompareTo(right.Key));
                world.Buffs.Sort((left, right) => left.Key.CompareTo(right.Key));
                if (!ValidWorldLists(world) || world.ActiveRoomId != restored.Requests[restored.CurrentRegionIndex].RegionId) return false;
                snapshot = restored;
                error = null;
                return true;
            }
            catch (ArgumentException exception)
            {
                error = "关卡日志JSON无效：" + exception.Message;
                return false;
            }
        }

        /// <summary>
        /// 解码单个稳定身份组件，拒绝空对象及组件键与内容不一致。
        /// </summary>
        /// <typeparam name="T">组件的纯数据类型。</typeparam>
        /// <param name="key">日志组件键。</param>
        /// <param name="json">组件 JSON 内容。</param>
        /// <param name="prefix">该组件类型的固定键前缀。</param>
        /// <param name="identity">读取组件内部稳定身份的方法。</param>
        /// <param name="destination">接收合法对象的列表。</param>
        /// <returns>对象非空且稳定身份匹配时为真。</returns>
        private static bool ReadComponent<T>(string key, string json, string prefix, Func<T, long> identity, List<T> destination) where T : class
        {
            T value = JsonUtility.FromJson<T>(json);
            if (value == null) return false;
            long id = identity(value);
            if (id < 1 || key != prefix + id) return false;
            destination.Add(value);
            return true;
        }

        /// <summary>
        /// 检查编码边界的战斗集合，避免空元素在构造组件键时抛出异常。
        /// </summary>
        /// <param name="world">需要拆分的完整战斗快照。</param>
        /// <returns>所有集合及元素均存在时为真。</returns>
        private static bool ValidWorldLists(CombatWorldSnapshot world)
        {
            if (world.Rooms == null || world.Actors == null || world.Casts == null || world.Displacements == null ||
                world.Projectiles == null || world.Buffs == null) return false;
            var rooms = new HashSet<int>();
            var keys = new HashSet<long>();
            foreach (CombatRoomState value in world.Rooms) if (value == null || value.RoomId < 1 || !rooms.Add(value.RoomId)) return false;
            foreach (CombatActorState value in world.Actors) if (value == null || value.Key < 1 || !keys.Add(value.Key)) return false;
            foreach (CombatCastState value in world.Casts) if (value == null || value.Key < 1 || !keys.Add(value.Key)) return false;
            foreach (CombatDisplacementState value in world.Displacements) if (value == null || value.Key < 1 || !keys.Add(value.Key)) return false;
            foreach (CombatProjectileState value in world.Projectiles) if (value == null || value.Key < 1 || !keys.Add(value.Key)) return false;
            foreach (CombatBuffState value in world.Buffs) if (value == null || value.Key < 1 || !keys.Add(value.Key)) return false;
            CombatActorState player = world.Actors.Find(value => value.Key == world.PlayerKey);
            return world.ActiveRoomId > 0 && rooms.Contains(world.ActiveRoomId) && player != null && player.Spec.IsPlayer &&
                player.Spec.ActorId == 1 && player.Spec.RoomId == world.ActiveRoomId;
        }

        /// <summary>
        /// 检查区域计划与全部槽位组件的一一对应，防止缺失或额外组件。
        /// </summary>
        /// <param name="region">已经重组或准备编码的区域。</param>
        /// <returns>每个计划槽位恰有一个非空运行组件时为真。</returns>
        private static bool ValidRegionParts(RegionRuntimeState region)
        {
            if (region?.Plan?.Request == null || region.Plan.Request.RegionId < 1 || region.Slots == null ||
                region.Plan.SpawnSlots == null || region.Plan.Chests == null || region.Plan.Npcs == null ||
                region.Chests == null || region.Npcs == null || region.Slots.Count != region.Plan.SpawnSlots.Count) return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (RegionSpawnSlotPlan slot in region.Plan.SpawnSlots)
                if (slot == null || string.IsNullOrWhiteSpace(slot.SlotId) || !ids.Add(slot.SlotId)) return false;
            foreach (RegionSpawnSlotState slot in region.Slots)
                if (slot == null || !ids.Remove(slot.SlotId)) return false;
            foreach (RegionChestPlan chest in region.Plan.Chests)
            {
                if (chest == null || chest.Items == null) return false;
                foreach (var item in chest.Items) if (item == null) return false;
            }
            foreach (RegionNpcPlan npc in region.Plan.Npcs)
            {
                if (npc == null || npc.Offers == null) return false;
                foreach (RegionNpcOfferPlan offer in npc.Offers) if (offer?.Item == null) return false;
            }
            foreach (RegionChestState chest in region.Chests) if (chest == null || chest.ClaimedItemIds == null) return false;
            foreach (RegionNpcState npc in region.Npcs) if (npc == null || npc.SoldOfferIds == null) return false;
            return ids.Count == 0;
        }

        /// <summary>
        /// 检查整关请求的身份及原始区域顺序，避免空请求进入恢复索引。
        /// </summary>
        /// <param name="snapshot">包含冻结区域请求的快照。</param>
        /// <returns>每个请求与单局身份和序号一致时为真。</returns>
        private static bool ValidRequests(ExpeditionRunSnapshot snapshot)
        {
            var ids = new HashSet<int>();
            for (int index = 0; index < snapshot.Requests.Count; index++)
            {
                RegionGenerationRequest request = snapshot.Requests[index];
                if (request == null || request.RegionId <= 0 || request.RegionIndex != index || request.RunId != snapshot.RunId ||
                    !ids.Add(request.RegionId)) return false;
            }
            return true;
        }

        /// <summary>
        /// 冻结单个组件的JSON文本。
        /// </summary>
        /// <param name="components">目标组件列表。</param>
        /// <param name="key">稳定的业务组件身份。</param>
        /// <param name="value">待保存的纯数据。</param>
        private static void Add(List<SaveStateComponent> components, string key, object value) =>
            components.Add(new SaveStateComponent { Key = key, Value = JsonUtility.ToJson(value) });
    }
}
