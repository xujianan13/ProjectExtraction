using System;
using System.Collections.Generic;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 开局已经确定的区域身份、Boss 绑定与独立内容种子。
    /// </summary>
    [Serializable]
    public sealed class RegionGenerationRequest
    {
        public string RunId;
        public int RegionId;
        public int RegionIndex;
        public int LevelId;
        public int RoomDefinitionId;
        public ExpeditionRoomKind RoomKind;
        public int LootLevel;
        public int EnemyLevel;
        public int BossDefinitionId;
        public int BossEnemyId;
        public int BossEnemyLevel;
        public string BossAnchorId;
        public int MapId;
        public string MapVersion;
        public string ConfigVersion;
        public int MonsterSeed;
        public int EliteSeed;
        public int ChestSeed;
        public int NpcSeed;

        /// <summary>
        /// 创建不可变生成候选使用的独立请求副本。
        /// </summary>
        /// <returns>独立请求副本。</returns>
        public RegionGenerationRequest Copy() => (RegionGenerationRequest)MemberwiseClone();
    }

    /// <summary>
    /// 普通怪群的成员数量与锚点局部排列。
    /// </summary>
    public sealed class RegionSpawnMemberDefinition
    {
        public int EnemyId;
        public int Count;
        public Vector3 StartOffset;
        public Vector3 StepOffset;
        public float AlternateZ;
        public float Radius;

        /// <summary>
        /// 计算组内成员相对锚点的位置。
        /// </summary>
        /// <param name="index">成员序号。</param>
        /// <returns>该成员的局部偏移。</returns>
        public Vector3 OffsetAt(int index) => StartOffset + StepOffset * index + new Vector3(0f, 0f, index % 2 * AlternateZ);
    }

    /// <summary>
    /// 与地图资源及 Boss 无关的普通怪群候选。
    /// </summary>
    public sealed class RegionEncounterCandidate
    {
        public int Id;
        public int Weight;
        public int MinLevel;
        public int MaxLevel;
        public List<RegionSpawnMemberDefinition> Members = new();
    }

    /// <summary>
    /// 独立精英候选。
    /// </summary>
    public sealed class RegionEliteCandidate
    {
        public int Id;
        public int EnemyId;
        public int Weight;
        public int MinLevel;
        public int MaxLevel;
        public float Radius;
    }

    /// <summary>
    /// 宝箱内容池候选。
    /// </summary>
    public sealed class RegionChestCandidate
    {
        public int Id;
        public int Weight;
        public string LootPoolId;
        public int ItemCount;
    }

    /// <summary>
    /// NPC 商品池、固定价格和库存候选。
    /// </summary>
    public sealed class RegionNpcCandidate
    {
        public int Id;
        public int Weight;
        public string LootPoolId;
        public int Price;
        public int Stock;
    }

    /// <summary>
    /// 配置层提供的区域内容规则，工厂只在首次生成时使用。
    /// </summary>
    public sealed class RegionContentDefinition
    {
        public int MonsterGroupCount;
        public int EliteCount;
        public int ChestCount;
        public int NpcCount;
        public string MonsterAnchorTag;
        public string EliteAnchorTag;
        public string ChestAnchorTag;
        public string NpcAnchorTag;
        public float BossRadius;
        public float PlayerRadius;
        public float InteractionRadius;
        public float ReachabilityGridSpacing;
        public int ReachabilityMaxCells;
        public int MaxSpawnSlots;
        public List<RegionEncounterCandidate> MonsterEncounters = new();
        public List<RegionEliteCandidate> EliteCandidates = new();
        public List<RegionChestCandidate> ChestCandidates = new();
        public List<RegionNpcCandidate> NpcCandidates = new();
        public Func<System.Random, int, string, ProjectItem> ItemFactory;
    }

    /// <summary>
    /// 区域首次生成后永久固定的一个敌人槽位。
    /// </summary>
    [Serializable]
    public sealed class RegionSpawnSlotPlan
    {
        public string SlotId;
        public string GroupId;
        public int EncounterId;
        public int EnemyId;
        public int EnemyLevel;
        public RegionEnemyKind Kind;
        public string AnchorId;
        public Vector3 HomePosition;
        public float Radius;

        /// <summary>
        /// 创建槽位计划副本，不携带 ECS 临时身份。
        /// </summary>
        /// <returns>独立计划副本。</returns>
        public RegionSpawnSlotPlan Copy() => (RegionSpawnSlotPlan)MemberwiseClone();
    }

    /// <summary>
    /// 宝箱冻结的内容及位置。
    /// </summary>
    [Serializable]
    public sealed class RegionChestPlan
    {
        public string Id;
        public int DefinitionId;
        public string AnchorId;
        public Vector3 Position;
        public List<ProjectItem> Items = new();

        /// <summary>
        /// 创建包含独立物品实例的箱子副本。
        /// </summary>
        /// <returns>独立箱子计划。</returns>
        public RegionChestPlan Copy() => new RegionChestPlan
        {
            Id = Id, DefinitionId = DefinitionId, AnchorId = AnchorId, Position = Position, Items = Items.ConvertAll(item => item.Copy())
        };
    }

    /// <summary>
    /// NPC 已冻结的一件商品。
    /// </summary>
    [Serializable]
    public sealed class RegionNpcOfferPlan
    {
        public string Id;
        public int Price;
        public ProjectItem Item;

        /// <summary>
        /// 创建商品及物品实例副本。
        /// </summary>
        /// <returns>独立商品计划。</returns>
        public RegionNpcOfferPlan Copy() => new RegionNpcOfferPlan { Id = Id, Price = Price, Item = Item.Copy() };
    }

    /// <summary>
    /// NPC 已冻结的位置与全部商品。
    /// </summary>
    [Serializable]
    public sealed class RegionNpcPlan
    {
        public string Id;
        public int DefinitionId;
        public string AnchorId;
        public Vector3 Position;
        public List<RegionNpcOfferPlan> Offers = new();

        /// <summary>
        /// 创建不共享商品的 NPC 副本。
        /// </summary>
        /// <returns>独立 NPC 计划。</returns>
        public RegionNpcPlan Copy() => new RegionNpcPlan
        {
            Id = Id, DefinitionId = DefinitionId, AnchorId = AnchorId, Position = Position, Offers = Offers.ConvertAll(value => value.Copy())
        };
    }

    /// <summary>
    /// 持久确认后直接读取的区域组合，恢复不得重新随机。
    /// </summary>
    [Serializable]
    public sealed class RegionPlan
    {
        public RegionGenerationRequest Request;
        public string EntryAnchorId;
        public string ExitAnchorId;
        public Vector3 EntryPosition;
        public Vector3 ExitPosition;
        public float PlayerRadius;
        public float InteractionRadius;
        public long MonsterRandomState;
        public long EliteRandomState;
        public long ChestRandomState;
        public long NpcRandomState;
        public List<RegionSpawnSlotPlan> SpawnSlots = new();
        public List<RegionChestPlan> Chests = new();
        public List<RegionNpcPlan> Npcs = new();

        /// <summary>
        /// 创建可提交保存的完整独立候选。
        /// </summary>
        /// <returns>不共享集合及物品的房间副本。</returns>
        public RegionPlan Copy() => new RegionPlan
        {
            Request = Request.Copy(), EntryAnchorId = EntryAnchorId, ExitAnchorId = ExitAnchorId,
            EntryPosition = EntryPosition, ExitPosition = ExitPosition, PlayerRadius = PlayerRadius, InteractionRadius = InteractionRadius,
            MonsterRandomState = MonsterRandomState,
            EliteRandomState = EliteRandomState, ChestRandomState = ChestRandomState, NpcRandomState = NpcRandomState,
            SpawnSlots = SpawnSlots.ConvertAll(value => value.Copy()), Chests = Chests.ConvertAll(value => value.Copy()),
            Npcs = Npcs.ConvertAll(value => value.Copy())
        };

        /// <summary>
        /// 检查存档组合的身份、版本、槽位及物品唯一性。
        /// </summary>
        /// <param name="map">恢复所使用的地图描述。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>计划完整且匹配地图时为真。</returns>
        public bool TryValidate(RegionMapDefinition map, out string error)
        {
            error = null;
            if (!TryValidateStructure(out error)) return false;
            if (map == null) { error = "区域地图描述缺失。"; return false; }
            if (!map.TryValidate(out error)) return false;
            if (Request == null || string.IsNullOrWhiteSpace(Request.RunId) || Request.RegionId <= 0 || Request.RegionIndex < 0 ||
                Request.LevelId <= 0 || Request.LootLevel <= 0 || Request.EnemyLevel <= 0 || !ValidRoomRequest(Request) ||
                string.IsNullOrWhiteSpace(Request.ConfigVersion) ||
                Request.MapId != map.MapId || Request.MapVersion != map.Version || SpawnSlots == null || Chests == null || Npcs == null ||
                !RegionValueRules.Finite(InteractionRadius) || InteractionRadius <= 0f ||
                !PersistentRandom.TryRestore(MonsterRandomState, out _) || !PersistentRandom.TryRestore(EliteRandomState, out _) ||
                !PersistentRandom.TryRestore(ChestRandomState, out _) || !PersistentRandom.TryRestore(NpcRandomState, out _))
            { error = "区域计划身份、版本或规则无效。"; return false; }
            if (!MatchesAnchor(map, EntryAnchorId, RegionAnchorKind.Entry, EntryPosition) ||
                !MatchesAnchor(map, ExitAnchorId, RegionAnchorKind.Exit, ExitPosition))
            { error = "区域出入锚点与冻结计划不匹配。"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            var geometry = new RegionMapGeometry(map);
            if (geometry.IsBlocked(EntryPosition, PlayerRadius) || geometry.IsBlocked(ExitPosition, PlayerRadius))
            { error = "区域出入口不能容纳玩家。"; return false; }
            var anchorGroups = new Dictionary<string, string>(StringComparer.Ordinal);
            var anchorCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            int bosses = 0;
            foreach (RegionSpawnSlotPlan slot in SpawnSlots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.SlotId) || !ids.Add(slot.SlotId) ||
                    string.IsNullOrWhiteSpace(slot.GroupId) || slot.EncounterId <= 0 || slot.EnemyId <= 0 || slot.EnemyLevel <= 0 ||
                    !Enum.IsDefined(typeof(RegionEnemyKind), slot.Kind) || !RegionValueRules.Finite(slot.Radius) || slot.Radius <= 0f ||
                    !map.TryGetAnchor(slot.AnchorId, out RegionMapAnchor anchor) || anchor.Kind != AnchorKind(slot.Kind) ||
                    geometry.IsBlocked(slot.HomePosition, slot.Radius) ||
                    slot.Kind != RegionEnemyKind.Boss && geometry.IsSpawnForbidden(slot.HomePosition, slot.Radius))
                { error = "区域敌人槽位无效。"; return false; }
                if (anchorGroups.TryGetValue(slot.AnchorId, out string groupId) && groupId != slot.GroupId)
                { error = "区域多个怪群使用了同一出生锚点。"; return false; }
                anchorGroups[slot.AnchorId] = slot.GroupId;
                anchorCounts.TryGetValue(slot.AnchorId, out int memberCount);
                anchorCounts[slot.AnchorId] = memberCount + 1;
                if (memberCount >= anchor.Capacity) { error = "区域怪群超过冻结锚点容量。"; return false; }
                if (slot.Kind != RegionEnemyKind.Boss) continue;
                bosses++;
                if (slot.EnemyId != Request.BossEnemyId || slot.EnemyLevel != Request.BossEnemyLevel ||
                    slot.AnchorId != Request.BossAnchorId || slot.HomePosition != anchor.Position)
                { error = "Boss 槽位与开局绑定不匹配。"; return false; }
            }
            if (bosses != (Request.RoomKind == ExpeditionRoomKind.Boss ? 1 : 0))
            { error = "房间 Boss 数量与类型不匹配。"; return false; }
            foreach (RegionChestPlan chest in Chests)
            {
                if (chest == null || string.IsNullOrWhiteSpace(chest.Id) || !ids.Add(chest.Id) || chest.DefinitionId <= 0 ||
                    !MatchesAnchor(map, chest.AnchorId, RegionAnchorKind.Chest, chest.Position) || chest.Items == null || chest.Items.Count == 0)
                { error = "区域宝箱计划无效。"; return false; }
                foreach (ProjectItem item in chest.Items)
                    if (!ValidItem(item, itemIds)) { error = "区域宝箱物品无效或重复。"; return false; }
            }
            foreach (RegionNpcPlan npc in Npcs)
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.Id) || !ids.Add(npc.Id) || npc.DefinitionId <= 0 ||
                    !MatchesAnchor(map, npc.AnchorId, RegionAnchorKind.Npc, npc.Position) || npc.Offers == null || npc.Offers.Count == 0)
                { error = "区域 NPC 计划无效。"; return false; }
                foreach (RegionNpcOfferPlan offer in npc.Offers)
                    if (offer == null || string.IsNullOrWhiteSpace(offer.Id) || !ids.Add(offer.Id) || offer.Price <= 0 ||
                        !ValidItem(offer.Item, itemIds) || offer.Id != offer.Item.Id)
                    { error = "区域商品身份、价格或物品无效。"; return false; }
            }
            return true;
        }

        /// <summary>
        /// 在复制或恢复前检查不依赖地图资源的全部字段及物品结构。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>可安全深复制且结构完整时为真。</returns>
        public bool TryValidateStructure(out string error)
        {
            error = null;
            if (Request == null || string.IsNullOrWhiteSpace(Request.RunId) || Request.RegionId <= 0 || Request.RegionIndex < 0 ||
                Request.LevelId <= 0 || Request.LootLevel <= 0 || Request.EnemyLevel <= 0 || !ValidRoomRequest(Request) ||
                Request.MapId <= 0 || string.IsNullOrWhiteSpace(Request.MapVersion) ||
                string.IsNullOrWhiteSpace(Request.ConfigVersion) ||
                string.IsNullOrWhiteSpace(EntryAnchorId) || string.IsNullOrWhiteSpace(ExitAnchorId) ||
                !RegionValueRules.Finite(EntryPosition) || !RegionValueRules.Finite(ExitPosition) ||
                !RegionValueRules.Finite(PlayerRadius) || PlayerRadius <= 0f || !RegionValueRules.Finite(InteractionRadius) || InteractionRadius <= 0f ||
                SpawnSlots == null || Chests == null || Npcs == null || !PersistentRandom.TryRestore(MonsterRandomState, out _) ||
                !PersistentRandom.TryRestore(EliteRandomState, out _) || !PersistentRandom.TryRestore(ChestRandomState, out _) ||
                !PersistentRandom.TryRestore(NpcRandomState, out _))
            { error = "区域计划字段、随机状态或规则不完整。"; return false; }
            if (Request.RoomKind == ExpeditionRoomKind.Combat && (SpawnSlots.Count == 0 || Chests.Count != 0 || Npcs.Count != 0) ||
                Request.RoomKind == ExpeditionRoomKind.Npc && (SpawnSlots.Count != 0 || Chests.Count != 0 || Npcs.Count != 1) ||
                Request.RoomKind == ExpeditionRoomKind.Chest && (SpawnSlots.Count != 0 || Chests.Count != 1 || Npcs.Count != 0) ||
                Request.RoomKind == ExpeditionRoomKind.Boss && (SpawnSlots.Count != 1 || Chests.Count != 0 || Npcs.Count != 0))
            { error = "冻结内容与房间类型不匹配。"; return false; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var itemIds = new HashSet<string>(StringComparer.Ordinal);
            int bosses = 0;
            foreach (RegionSpawnSlotPlan slot in SpawnSlots)
            {
                if (slot == null || string.IsNullOrWhiteSpace(slot.SlotId) || !ids.Add(slot.SlotId) ||
                    string.IsNullOrWhiteSpace(slot.GroupId) || string.IsNullOrWhiteSpace(slot.AnchorId) || slot.EncounterId <= 0 ||
                    slot.EnemyId <= 0 || slot.EnemyLevel <= 0 || !Enum.IsDefined(typeof(RegionEnemyKind), slot.Kind) ||
                    !RegionValueRules.Finite(slot.HomePosition) || !RegionValueRules.Finite(slot.Radius) || slot.Radius <= 0f)
                { error = "区域槽位结构无效。"; return false; }
                if (slot.Kind != RegionEnemyKind.Boss) continue;
                bosses++;
                if (slot.EnemyId != Request.BossEnemyId || slot.EnemyLevel != Request.BossEnemyLevel || slot.AnchorId != Request.BossAnchorId)
                { error = "Boss 身份与冻结绑定不一致。"; return false; }
            }
            if (bosses != (Request.RoomKind == ExpeditionRoomKind.Boss ? 1 : 0))
            { error = "房间 Boss 数量与类型不匹配。"; return false; }
            foreach (RegionChestPlan chest in Chests)
            {
                if (chest == null || string.IsNullOrWhiteSpace(chest.Id) || !ids.Add(chest.Id) || chest.DefinitionId <= 0 ||
                    string.IsNullOrWhiteSpace(chest.AnchorId) || !RegionValueRules.Finite(chest.Position) || chest.Items == null || chest.Items.Count == 0)
                { error = "区域宝箱结构无效。"; return false; }
                foreach (ProjectItem item in chest.Items)
                    if (!ValidItem(item, itemIds)) { error = "区域宝箱物品无效或重复。"; return false; }
            }
            foreach (RegionNpcPlan npc in Npcs)
            {
                if (npc == null || string.IsNullOrWhiteSpace(npc.Id) || !ids.Add(npc.Id) || npc.DefinitionId <= 0 ||
                    string.IsNullOrWhiteSpace(npc.AnchorId) || !RegionValueRules.Finite(npc.Position) || npc.Offers == null || npc.Offers.Count == 0)
                { error = "区域 NPC 结构无效。"; return false; }
                foreach (RegionNpcOfferPlan offer in npc.Offers)
                    if (offer == null || string.IsNullOrWhiteSpace(offer.Id) || !ids.Add(offer.Id) || offer.Price <= 0 ||
                        !ValidItem(offer.Item, itemIds) || offer.Id != offer.Item.Id)
                    { error = "区域商品无效或重复。"; return false; }
            }
            return true;
        }

        /// <summary>
        /// 检查房间类型与可选 Boss 身份一致。
        /// </summary>
        /// <param name="request">冻结的房间请求。</param>
        /// <returns>房间定义及 Boss 字段一致时为真。</returns>
        internal static bool ValidRoomRequest(RegionGenerationRequest request) => request.RoomDefinitionId > 0 &&
            Enum.IsDefined(typeof(ExpeditionRoomKind), request.RoomKind) && (request.RoomKind == ExpeditionRoomKind.Boss
                ? request.BossDefinitionId > 0 && request.BossEnemyId > 0 && request.BossEnemyLevel > 0 && !string.IsNullOrWhiteSpace(request.BossAnchorId)
                : request.BossDefinitionId == 0 && request.BossEnemyId == 0 && request.BossEnemyLevel == 0 && string.IsNullOrEmpty(request.BossAnchorId));

        /// <summary>
        /// 根据敌人类别取得锚点用途。
        /// </summary>
        /// <param name="kind">敌人类别。</param>
        /// <returns>对应的地图锚点类别。</returns>
        internal static RegionAnchorKind AnchorKind(RegionEnemyKind kind) => kind == RegionEnemyKind.Normal ? RegionAnchorKind.Monster :
            kind == RegionEnemyKind.Elite ? RegionAnchorKind.Elite : RegionAnchorKind.Boss;

        /// <summary>
        /// 校验冻结的静态目标确实引用对应锚点。
        /// </summary>
        /// <param name="map">地图描述。</param>
        /// <param name="id">锚点身份。</param>
        /// <param name="kind">预期用途。</param>
        /// <param name="position">冻结坐标。</param>
        /// <returns>身份、用途及坐标一致时为真。</returns>
        private static bool MatchesAnchor(RegionMapDefinition map, string id, RegionAnchorKind kind, Vector3 position) =>
            RegionValueRules.Finite(position) && map.TryGetAnchor(id, out RegionMapAnchor anchor) && anchor.Kind == kind && anchor.Position == position;

        /// <summary>
        /// 校验已冻结物品及跨容器身份。
        /// </summary>
        /// <param name="item">冻结物品。</param>
        /// <param name="ids">本区域已见物品身份。</param>
        /// <returns>实例有效且身份唯一时为真。</returns>
        private static bool ValidItem(ProjectItem item, HashSet<string> ids) => ProjectEquipmentRules.ValidateItem(item) == null &&
            !item.Equipped && ids.Add(item.Id);
    }
}
