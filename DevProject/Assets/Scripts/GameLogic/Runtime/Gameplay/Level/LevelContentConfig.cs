using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 关卡配置身份、固定房间序列和物品等级。
    /// </summary>
    public sealed class LevelDefinition
    {
        public CfgLevelsRow Row { get; }
        public int ID => Row.ID;
        public int LootLevel => Row.LootLevel;

        /// <summary>
        /// 包装已加载的关卡行。
        /// </summary>
        /// <param name="row">关卡行。</param>
        internal LevelDefinition(CfgLevelsRow row) => Row = row;
    }

    /// <summary>
    /// 敌人的数值、角色、资源与伤害弱点定义。
    /// </summary>
    public sealed class EnemyDefinition
    {
        public CfgEnemiesRow Row { get; }
        public int ID => Row.ID;
        public string PrefabAddress => Row.PrefabAddress;
        public Vector3 ModelScale => new Vector3((float)Row.ModelScaleX, (float)Row.ModelScaleY, (float)Row.ModelScaleZ);
        public int SkillID => Row.SkillID;
        public RegionEnemyKind Role => (RegionEnemyKind)Row.Role;

        /// <summary>
        /// 包装已加载的敌人行。
        /// </summary>
        /// <param name="row">敌人行。</param>
        internal EnemyDefinition(CfgEnemiesRow row) => Row = row;

        /// <summary>
        /// 按房间强度计算生命值。
        /// </summary>
        /// <param name="level">房间强度，等于关卡物品等级加房间配置偏移。</param>
        /// <returns>敌人生命值。</returns>
        public float HealthAt(int level) => (float)(Row.BaseHealth + Row.HealthPerLevel * level);

        /// <summary>
        /// 按房间强度计算击杀经验。
        /// </summary>
        /// <param name="level">房间强度，等于关卡物品等级加房间配置偏移。</param>
        /// <returns>击杀经验值。</returns>
        public int ExperienceAt(int level) => Row.ExperienceBase + Row.ExperiencePerLevel * level;

        /// <summary>
        /// 为战斗创建独立的伤害弱点数组。
        /// </summary>
        /// <returns>按效果编号配置的伤害倍率。</returns>
        public CombatDamageVulnerability[] CreateVulnerabilities()
        {
            var result = new CombatDamageVulnerability[Row.VulnerableEffectIDs.Length];
            for (int index = 0; index < result.Length; index++)
                result[index] = new CombatDamageVulnerability { EffectId = Row.VulnerableEffectIDs[index], Multiplier = (float)Row.VulnerabilityMultiplier };
            return result;
        }
    }

    /// <summary>
    /// 将唯一的业务表集映射为区域请求和首次冻结内容。
    /// </summary>
    public static class LevelContentConfig
    {
        /// <summary>
        /// 检查区域内容、Boss 绑定、角色与所有跨表引用。
        /// </summary>
        /// <param name="error">首个失败原因。</param>
        /// <returns>配置完整可用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgLevels == null || GameTables.CfgRooms == null || GameTables.CfgRoomMaps == null ||
                GameTables.CfgRoomAnchors == null || GameTables.CfgRoomObstacles == null || GameTables.CfgEnemies == null ||
                GameTables.CfgBosses == null || GameTables.CfgBossRewards == null || GameTables.CfgEncounters == null ||
                GameTables.CfgSpawns == null || GameTables.CfgChestPool == null || GameTables.CfgNpcPool == null ||
                GameTables.CfgRunPersistence?.GetById(1) == null || GameTables.CfgCombatSkills == null ||
                GameTables.CfgCombatEffects == null || GameTables.CfgLootPool == null || GameTables.CfgPlayerCombat?.GetById(1) == null)
                return Fail("房间配置表未完整加载。", out error);
            foreach (CfgEnemiesRow enemy in GameTables.CfgEnemies.GetAll())
            {
                if (enemy.ID <= 0 || string.IsNullOrWhiteSpace(enemy.Name) || string.IsNullOrWhiteSpace(enemy.PrefabAddress) ||
                    !Positive(enemy.ModelScaleX) || !Positive(enemy.ModelScaleY) || !Positive(enemy.ModelScaleZ) ||
                    !Positive(enemy.BaseHealth) || !Nonnegative(enemy.HealthPerLevel) || !Positive(enemy.Attack) ||
                    !Positive(enemy.MoveSpeed) || !Positive(enemy.Radius) || !Positive(enemy.AttackRange) ||
                    !Nonnegative(enemy.AggroRange) || enemy.AggroRange < enemy.AttackRange || enemy.ExperienceBase < 0 ||
                    enemy.ExperiencePerLevel < 0 || enemy.Role < 0 || enemy.Role > (int)RegionEnemyKind.Boss ||
                    !Nonnegative(enemy.Defense) || !Positive(enemy.VulnerabilityMultiplier) || enemy.VulnerableEffectIDs == null ||
                    GameTables.CfgCombatSkills.GetById(enemy.SkillID) == null)
                    return Fail("敌人配置无效：" + enemy.ID, out error);
                var effects = new HashSet<int>();
                foreach (int id in enemy.VulnerableEffectIDs)
                    if (!effects.Add(id) || GameTables.CfgCombatEffects.GetById(id)?.Kind != (int)CombatEffectKind.Damage)
                        return Fail("敌人伤害弱点引用无效：" + enemy.ID, out error);
            }

            foreach (CfgBossesRow boss in GameTables.CfgBosses.GetAll())
                if (boss.ID <= 0 || string.IsNullOrWhiteSpace(boss.Name) ||
                    GameTables.CfgEnemies.GetById(boss.EnemyID)?.Role != (int)RegionEnemyKind.Boss ||
                    GameTables.CfgBossRewards.GetById(boss.RewardID) == null)
                    return Fail("Boss 或奖励绑定无效：" + boss.ID, out error);
            foreach (CfgSpawnsRow spawn in GameTables.CfgSpawns.GetAll())
                if (spawn.ID <= 0 || GameTables.CfgEncounters.GetById(spawn.EncounterID) == null || spawn.Count <= 0 ||
                    GameTables.CfgEnemies.GetById(spawn.EnemyID)?.Role != (int)RegionEnemyKind.Normal ||
                    !Finite(spawn.StartX) || !Finite(spawn.StartZ) || !Finite(spawn.StepX) || !Finite(spawn.StepZ) || !Finite(spawn.AlternateZ))
                    return Fail("房间怪群成员无效：" + spawn.ID, out error);
            foreach (CfgEncountersRow encounter in GameTables.CfgEncounters.GetAll())
                if (encounter.ID <= 0 || encounter.MinLevel <= 0 || encounter.MaxLevel < encounter.MinLevel ||
                    encounter.Weight <= 0 || !ValidateMembers(encounter.ID, out error))
                    return error != null ? false : Fail("怪群配置无效：" + encounter.ID, out error);
            foreach (CfgChestPoolRow chest in GameTables.CfgChestPool.GetAll())
                if (chest.ID <= 0 || chest.ItemCount <= 0 || chest.Weight <= 0 || !HasLootPool(chest.LootPoolID, 0))
                    return Fail("宝箱配置无效：" + chest.ID, out error);
            foreach (CfgNpcPoolRow npc in GameTables.CfgNpcPool.GetAll())
                if (npc.ID <= 0 || npc.Price <= 0 || npc.Stock <= 0 || npc.Weight <= 0 || !HasLootPool(npc.LootPoolID, 0))
                    return Fail("商人配置无效：" + npc.ID, out error);
            foreach (CfgRoomMapsRow row in GameTables.CfgRoomMaps.GetAll())
                if (!TryCreateRoomMap(row.ID, out _, out error)) return false;
            foreach (CfgRoomAnchorsRow anchor in GameTables.CfgRoomAnchors.GetAll())
                if (GameTables.CfgRoomMaps.GetById(anchor.MapID) == null || !Nonnegative(anchor.NoSpawnRadius))
                    return Fail("地图锚点引用无效：" + anchor.ID, out error);
            foreach (CfgRoomObstaclesRow obstacle in GameTables.CfgRoomObstacles.GetAll())
                if (GameTables.CfgRoomMaps.GetById(obstacle.MapID) == null) return Fail("地图障碍引用无效。", out error);
            foreach (CfgRoomsRow room in GameTables.CfgRooms.GetAll())
            {
                if (room.ID <= 0 || string.IsNullOrWhiteSpace(room.Name) || !Enum.IsDefined(typeof(ExpeditionRoomKind), room.Kind) ||
                    GameTables.CfgRoomMaps.GetById(room.MapID) == null || room.EnemyLevelOffset < 0 ||
                    !Positive(room.InteractionRadius) || !Positive(room.ReachabilityGridSpacing) || room.ReachabilityMaxCells <= 0 || room.MaxSpawnSlots <= 0)
                    return Fail("房间配置无效：" + room.ID, out error);
                ExpeditionRoomKind kind = (ExpeditionRoomKind)room.Kind;
                int expectedChests = kind == ExpeditionRoomKind.Chest || kind == ExpeditionRoomKind.Boss ? 1 : 0;
                if (room.ChestCount != expectedChests) return Fail("房间宝箱数量与类型不匹配：" + room.ID, out error);
                if (kind == ExpeditionRoomKind.Combat && (GameTables.CfgEncounters.GetById(room.EncounterID) == null || room.BossID != 0) ||
                    kind == ExpeditionRoomKind.Boss && (GameTables.CfgBosses.GetById(room.BossID) == null || room.EncounterID != 0) ||
                    kind != ExpeditionRoomKind.Combat && kind != ExpeditionRoomKind.Boss && (room.EncounterID != 0 || room.BossID != 0))
                    return Fail("房间类型与战斗内容不匹配：" + room.ID, out error);
                bool chestFound = false, npcFound = false;
                foreach (CfgChestPoolRow candidate in GameTables.CfgChestPool.GetAll()) if (candidate.PoolID == room.ChestPoolID) chestFound = true;
                foreach (CfgNpcPoolRow candidate in GameTables.CfgNpcPool.GetAll()) if (candidate.PoolID == room.NpcPoolID) npcFound = true;
                if (kind == ExpeditionRoomKind.Chest && !chestFound || kind == ExpeditionRoomKind.Npc && !npcFound ||
                    kind != ExpeditionRoomKind.Chest && !string.IsNullOrEmpty(room.ChestPoolID) ||
                    kind != ExpeditionRoomKind.Npc && !string.IsNullOrEmpty(room.NpcPoolID))
                    return Fail("房间交互内容与类型不匹配：" + room.ID, out error);
            }
            int defaults = 0;
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                if (row.IsDefault) defaults++;
                if (row.ID <= 0 || row.RequiredLevel <= 0 || row.LootLevel <= 0 || string.IsNullOrWhiteSpace(row.Name) ||
                    string.IsNullOrWhiteSpace(row.Description) || row.RoomIDs == null || row.RoomIDs.Length == 0 ||
                    !HasLootPool(row.LootPoolID, row.LootLevel)) return Fail("关卡配置无效：" + row.ID, out error);
                for (int index = 0; index < row.RoomIDs.Length; index++)
                {
                    CfgRoomsRow room = GameTables.CfgRooms.GetById(row.RoomIDs[index]);
                    if (room == null || (room.Kind == (int)ExpeditionRoomKind.Boss) != (index == row.RoomIDs.Length - 1) ||
                        (long)row.LootLevel + room.EnemyLevelOffset > int.MaxValue)
                        return Fail("关卡房间顺序无效，只有最后一房可以是 Boss：" + row.ID, out error);
                    int intensity = row.LootLevel + room.EnemyLevelOffset;
                    if (room.Kind == (int)ExpeditionRoomKind.Combat)
                    {
                        CfgEncountersRow encounter = GameTables.CfgEncounters.GetById(room.EncounterID);
                        if (intensity < encounter.MinLevel || intensity > encounter.MaxLevel) return Fail("怪群等级不覆盖房间强度。", out error);
                    }
                    foreach (CfgChestPoolRow candidate in GameTables.CfgChestPool.GetAll())
                        if (candidate.PoolID == room.ChestPoolID && !HasLootPool(candidate.LootPoolID, row.LootLevel))
                            return Fail("宝箱掉落池不覆盖关卡等级。", out error);
                    foreach (CfgNpcPoolRow candidate in GameTables.CfgNpcPool.GetAll())
                        if (candidate.PoolID == room.NpcPoolID && !HasLootPool(candidate.LootPoolID, row.LootLevel))
                            return Fail("商人掉落池不覆盖关卡等级。", out error);
                }
            }
            return defaults == 1 ? true : Fail("关卡必须且只能有一个默认条目。", out error);
        }

        /// <summary>
        /// 取得按编号排序的局外关卡选项。
        /// </summary>
        /// <returns>关卡选择条目。</returns>
        public static ProjectLevelOption[] CreateLevels()
        {
            var result = new List<ProjectLevelOption>();
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
                result.Add(new ProjectLevelOption { Id = row.ID, Name = row.Name, Description = row.Description, RequiredLevel = row.RequiredLevel });
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            return result.ToArray();
        }

        /// <summary>
        /// 读取指定关卡身份。
        /// </summary>
        /// <param name="id">关卡编号。</param>
        /// <param name="level">找到的关卡定义。</param>
        /// <returns>存在该关卡时为真。</returns>
        public static bool TryGetLevel(int id, out LevelDefinition level)
        {
            CfgLevelsRow row = GameTables.CfgLevels?.GetById(id);
            level = row == null ? null : new LevelDefinition(row);
            return level != null;
        }

        /// <summary>
        /// 读取指定敌人定义。
        /// </summary>
        /// <param name="id">敌人编号。</param>
        /// <param name="enemy">找到的敌人定义。</param>
        /// <returns>存在该敌人时为真。</returns>
        public static bool TryGetEnemy(int id, out EnemyDefinition enemy)
        {
            CfgEnemiesRow row = GameTables.CfgEnemies?.GetById(id);
            enemy = row == null ? null : new EnemyDefinition(row);
            return enemy != null;
        }

        /// <summary>
        /// 按关卡固定顺序建立房间身份、地图和独立内容种子。
        /// </summary>
        /// <param name="runId">全局局次身份。</param>
        /// <param name="seed">本局种子。</param>
        /// <param name="level">已经通过配置门禁的关卡。</param>
        /// <param name="requests">按零起点序号排列的房间请求，最后一项为 Boss 房。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>所有区域绑定均可保存时为真。</returns>
        public static bool TryCreateRoomRequests(string runId, int seed, LevelDefinition level,
            out List<RegionGenerationRequest> requests, out string error)
        {
            requests = null;
            error = null;
            if (string.IsNullOrWhiteSpace(runId) || level == null || !Validate(out error))
                return error != null ? false : Fail("房间开局请求身份无效。", out error);
            var generated = new List<RegionGenerationRequest>();
            var seeds = new PersistentRandom(seed);
            for (int index = 0; index < level.Row.RoomIDs.Length; index++)
            {
                CfgRoomsRow room = GameTables.CfgRooms.GetById(level.Row.RoomIDs[index]);
                CfgRoomMapsRow map = GameTables.CfgRoomMaps.GetById(room.MapID);
                CfgBossesRow boss = room.Kind == (int)ExpeditionRoomKind.Boss ? GameTables.CfgBosses.GetById(room.BossID) : null;
                int intensity = level.LootLevel + room.EnemyLevelOffset;
                string bossAnchor = null;
                if (boss != null)
                    foreach (CfgRoomAnchorsRow anchor in GameTables.CfgRoomAnchors.GetAll())
                        if (anchor.MapID == room.MapID && anchor.Kind == (int)RegionAnchorKind.Boss) bossAnchor = anchor.AnchorID;
                generated.Add(new RegionGenerationRequest
                {
                    RunId = runId, RegionId = index + 1, RegionIndex = index, LevelId = level.ID, LootLevel = level.LootLevel,
                    RoomDefinitionId = room.ID, RoomKind = (ExpeditionRoomKind)room.Kind, EnemyLevel = intensity,
                    BossDefinitionId = boss == null ? 0 : boss.ID, BossEnemyId = boss == null ? 0 : boss.EnemyID,
                    BossEnemyLevel = boss == null ? 0 : intensity, BossAnchorId = bossAnchor, MapId = map.ID, MapVersion = map.Version,
                    ConfigVersion = GameTables.CfgRunPersistence.GetById(1).ConfigVersion,
                    MonsterSeed = seeds.Next(), EliteSeed = seeds.Next(), ChestSeed = seeds.Next(), NpcSeed = seeds.Next()
                });
            }
            requests = generated;
            return true;
        }

        /// <summary>
        /// 按房间类型构建首次进入时的冻结内容。
        /// </summary>
        /// <param name="request">开局保存的区域请求。</param>
        /// <returns>引用当前唯一配置表集的区域内容定义。</returns>
        public static RegionContentDefinition CreateRegionContent(RegionGenerationRequest request)
        {
            CfgRoomsRow row = GameTables.CfgRooms.GetById(request.RoomDefinitionId);
            var result = new RegionContentDefinition
            {
                MonsterGroupCount = request.RoomKind == ExpeditionRoomKind.Combat ? 1 : 0,
                ChestCount = row.ChestCount,
                NpcCount = request.RoomKind == ExpeditionRoomKind.Npc ? 1 : 0,
                MonsterAnchorTag = "monster", ChestAnchorTag = "chest", NpcAnchorTag = "npc",
                BossRadius = request.RoomKind == ExpeditionRoomKind.Boss ? (float)GameTables.CfgEnemies.GetById(request.BossEnemyId).Radius : 0f,
                PlayerRadius = (float)GameTables.CfgPlayerCombat.GetById(1).Radius, InteractionRadius = (float)row.InteractionRadius,
                ReachabilityGridSpacing = (float)row.ReachabilityGridSpacing, ReachabilityMaxCells = row.ReachabilityMaxCells,
                MaxSpawnSlots = row.MaxSpawnSlots, ItemFactory = ProjectEquipmentRules.CreateLootFromPool
            };
            if (request.RoomKind == ExpeditionRoomKind.Combat)
            {
                CfgEncountersRow encounter = GameTables.CfgEncounters.GetById(row.EncounterID);
                var candidate = new RegionEncounterCandidate
                { Id = encounter.ID, Weight = encounter.Weight, MinLevel = encounter.MinLevel, MaxLevel = encounter.MaxLevel };
                foreach (CfgSpawnsRow spawn in GameTables.CfgSpawns.GetAll())
                    if (spawn.EncounterID == encounter.ID) candidate.Members.Add(new RegionSpawnMemberDefinition
                    {
                        EnemyId = spawn.EnemyID, Count = spawn.Count, StartOffset = new Vector3((float)spawn.StartX, 0f, (float)spawn.StartZ),
                        StepOffset = new Vector3((float)spawn.StepX, 0f, (float)spawn.StepZ), AlternateZ = (float)spawn.AlternateZ,
                        Radius = (float)GameTables.CfgEnemies.GetById(spawn.EnemyID).Radius
                    });
                result.MonsterEncounters.Add(candidate);
            }
            foreach (CfgChestPoolRow chest in GameTables.CfgChestPool.GetAll())
                if (chest.PoolID == row.ChestPoolID) result.ChestCandidates.Add(new RegionChestCandidate
                { Id = chest.ID, Weight = chest.Weight, LootPoolId = chest.LootPoolID, ItemCount = chest.ItemCount });
            foreach (CfgNpcPoolRow npc in GameTables.CfgNpcPool.GetAll())
                if (npc.PoolID == row.NpcPoolID) result.NpcCandidates.Add(new RegionNpcCandidate
                { Id = npc.ID, Weight = npc.Weight, LootPoolId = npc.LootPoolID, Price = npc.Price, Stock = npc.Stock });
            return result;
        }

        /// <summary>
        /// 检查每组出生相对偏移及圆形占位不重叠。
        /// </summary>
        /// <param name="encounterId">遭遇编号。</param>
        /// <param name="error">失败说明。</param>
        /// <returns>成员完整且初生占位独立时为真。</returns>
        private static bool ValidateMembers(int encounterId, out string error)
        {
            var positions = new List<Vector2>();
            var radii = new List<float>();
            foreach (CfgSpawnsRow spawn in GameTables.CfgSpawns.GetAll())
            {
                if (spawn.EncounterID != encounterId) continue;
                float radius = (float)GameTables.CfgEnemies.GetById(spawn.EnemyID).Radius;
                for (int index = 0; index < spawn.Count; index++)
                {
                    var position = new Vector2((float)(spawn.StartX + spawn.StepX * index),
                        (float)(spawn.StartZ + spawn.StepZ * index + index % 2 * spawn.AlternateZ));
                    for (int other = 0; other < positions.Count; other++)
                        if ((positions[other] - position).sqrMagnitude < (radii[other] + radius) * (radii[other] + radius))
                            return Fail("普通怪群刷怪点相互重叠：" + encounterId, out error);
                    positions.Add(position);
                    radii.Add(radius);
                }
            }
            error = null;
            return positions.Count > 0;
        }

        /// <summary>
        /// 从唯一配置源构建长条房间几何，供运行时与资产烘焙共用。
        /// </summary>
        /// <param name="mapId">地图编号。</param>
        /// <param name="map">成功时返回的独立地图。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>配置完整、入口下出上且锚点可达时为真。</returns>
        public static bool TryCreateRoomMap(int mapId, out RegionMapDefinition map, out string error)
        {
            map = null;
            error = null;
            CfgRoomMapsRow row = GameTables.CfgRoomMaps?.GetById(mapId);
            if (row == null || string.IsNullOrWhiteSpace(row.Version) || string.IsNullOrWhiteSpace(row.PrefabAddress) ||
                string.IsNullOrWhiteSpace(row.GeometryAddress) ||
                !Finite(row.BoundsX) || !Finite(row.BoundsZ) || !Positive(row.Width) || !Positive(row.Length) ||
                row.Length <= row.Width || !Finite(row.NorthYawDegrees)) return Fail("长条房间地图配置无效。", out error);
            return TryCreateRoomMap(row, GameTables.CfgRoomAnchors.GetAll(), GameTables.CfgRoomObstacles.GetAll(), out map, out error);
        }

        /// <summary>
        /// 使用同一份导表数据构建地图，供编辑器无需安装全局表集地烘焙资产。
        /// </summary>
        /// <param name="row">地图配置。</param>
        /// <param name="anchors">全部锚点配置。</param>
        /// <param name="obstacles">全部障碍配置。</param>
        /// <param name="map">成功时的地图描述。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>全部几何及锚点合法时为真。</returns>
        public static bool TryCreateRoomMap(CfgRoomMapsRow row, IReadOnlyList<CfgRoomAnchorsRow> anchors,
            IReadOnlyList<CfgRoomObstaclesRow> obstacles, out RegionMapDefinition map, out string error)
        {
            map = null;
            error = null;
            if (row == null || anchors == null || obstacles == null || string.IsNullOrWhiteSpace(row.Version) ||
                string.IsNullOrWhiteSpace(row.PrefabAddress) || string.IsNullOrWhiteSpace(row.GeometryAddress) ||
                !Finite(row.BoundsX) || !Finite(row.BoundsZ) ||
                !Positive(row.Width) || !Positive(row.Length) || row.Length <= row.Width || !Finite(row.NorthYawDegrees))
                return Fail("长条房间地图配置无效。", out error);
            var created = new RegionMapDefinition
            {
                MapId = row.ID, Version = row.Version, Bounds = new Rect((float)row.BoundsX, (float)row.BoundsZ, (float)row.Width, (float)row.Length),
                NorthYawDegrees = (float)row.NorthYawDegrees
            };
            RegionMapAnchor entry = null, exit = null;
            foreach (CfgRoomAnchorsRow anchor in anchors)
            {
                if (anchor.MapID != row.ID) continue;
                if (!Finite(anchor.X) || !Finite(anchor.Z) || !Nonnegative(anchor.NoSpawnRadius)) return Fail("房间锚点坐标无效。", out error);
                var value = new RegionMapAnchor
                {
                    Id = anchor.AnchorID, Kind = (RegionAnchorKind)anchor.Kind, Tag = anchor.Tag,
                    Position = new Vector3((float)anchor.X, 0f, (float)anchor.Z), Capacity = anchor.Capacity
                };
                created.Anchors.Add(value);
                if (value.Kind == RegionAnchorKind.Entry)
                {
                    if (entry != null) return Fail("房间必须只有一个入口。", out error);
                    entry = value;
                }
                if (value.Kind == RegionAnchorKind.Exit)
                {
                    if (exit != null) return Fail("房间必须只有一个出口。", out error);
                    exit = value;
                }
                if (anchor.NoSpawnRadius > 0) created.NoSpawnZones.Add(new RegionNoSpawnZone { Center = value.Position, Radius = (float)anchor.NoSpawnRadius });
            }
            foreach (CfgRoomObstaclesRow obstacle in obstacles)
                if (obstacle.MapID == row.ID) created.Obstacles.Add(new RegionMapObstacle
                { Min = new Vector2((float)obstacle.MinX, (float)obstacle.MinZ), Max = new Vector2((float)obstacle.MaxX, (float)obstacle.MaxZ) });
            if (entry == null || exit == null || entry.Position.z >= exit.Position.z) return Fail("房间必须下进上出。", out error);
            if (!created.TryValidate(out error)) return false;
            map = created;
            return true;
        }

        /// <summary>
        /// 检查指定物品池的正权重等级覆盖。
        /// </summary>
        /// <param name="poolId">物品池。</param>
        /// <param name="lootLevel">物品等级；零表示任意等级。</param>
        /// <returns>存在可抽选条目时为真。</returns>
        private static bool HasLootPool(string poolId, int lootLevel)
        {
            if (string.IsNullOrWhiteSpace(poolId)) return false;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (row.PoolID == poolId && row.Weight > 0 && (lootLevel == 0 || row.MinLevel <= lootLevel && lootLevel <= row.MaxLevel)) return true;
            return false;
        }

        /// <summary>
        /// 检查数值有限且可转换为单精度。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>有限时为真。</returns>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;

        /// <summary>
        /// 检查有限非负值。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>有限非负时为真。</returns>
        private static bool Nonnegative(double value) => Finite(value) && value >= 0;

        /// <summary>
        /// 检查有限正值。
        /// </summary>
        /// <param name="value">数值。</param>
        /// <returns>有限正值时为真。</returns>
        private static bool Positive(double value) => Finite(value) && value > 0;

        /// <summary>
        /// 返回带说明的配置失败。
        /// </summary>
        /// <param name="message">错误说明。</param>
        /// <param name="error">返回的错误说明。</param>
        /// <returns>恒为假。</returns>
        private static bool Fail(string message, out string error) { error = message; return false; }
    }
}
