using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 关卡配置身份和物品等级，不携带旧房间布局。
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
        /// 按区域强度计算生命值。
        /// </summary>
        /// <param name="level">区域强度，等于关卡物品等级加区域序号。</param>
        /// <returns>敌人生命值。</returns>
        public float HealthAt(int level) => (float)(Row.BaseHealth + Row.HealthPerLevel * level);

        /// <summary>
        /// 按区域强度计算初生击杀经验。
        /// </summary>
        /// <param name="level">区域强度，等于关卡物品等级加区域序号。</param>
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
            if (GameTables.CfgLevels == null || GameTables.CfgEnemies == null || GameTables.CfgRegionMaps == null ||
                GameTables.CfgBosses == null || GameTables.CfgBossRewards == null || GameTables.CfgRegionContent == null ||
                GameTables.CfgEncounters == null || GameTables.CfgSpawns == null || GameTables.CfgElitePool == null ||
                GameTables.CfgChestPool == null || GameTables.CfgNpcPool == null || GameTables.CfgRespawnRules == null ||
                GameTables.CfgRunPersistence?.GetById(1) == null || GameTables.CfgCombatSkills == null ||
                GameTables.CfgCombatEffects == null || GameTables.CfgRoguePools == null || GameTables.CfgRogueRewards == null ||
                GameTables.CfgRogueProgress == null ||
                GameTables.CfgLootPool == null || GameTables.CfgPlayerCombat?.GetById(1) == null)
                return Fail("区域配置表未完整加载。", out error);
            foreach (CfgRegionMapsRow map in GameTables.CfgRegionMaps.GetAll())
                if (map.ID <= 0 || string.IsNullOrWhiteSpace(map.Version) || string.IsNullOrWhiteSpace(map.PrefabAddress) ||
                    string.IsNullOrWhiteSpace(map.GeometryAddress) || string.IsNullOrWhiteSpace(map.MinimapAddress))
                    return Fail("区域地图引用无效：" + map.ID, out error);
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
                if (boss.ID <= 0 || string.IsNullOrWhiteSpace(boss.Name) || string.IsNullOrWhiteSpace(boss.PoolID) || boss.Weight <= 0 ||
                    boss.MinLevel <= 0 || boss.MaxLevel < boss.MinLevel || GameTables.CfgRegionMaps.GetById(boss.MapID) == null ||
                    GameTables.CfgEnemies.GetById(boss.EnemyID)?.Role != (int)RegionEnemyKind.Boss ||
                    GameTables.CfgBossRewards.GetById(boss.RewardID) == null)
                    return Fail("Boss 地图或奖励绑定无效：" + boss.ID, out error);
            foreach (CfgRespawnRulesRow rule in GameTables.CfgRespawnRules.GetAll())
                if (rule.ID <= 0 || !TryCreateRespawn(rule, out _)) return Fail("复活规则无效：" + rule.ID, out error);
            foreach (CfgSpawnsRow spawn in GameTables.CfgSpawns.GetAll())
                if (spawn.ID <= 0 || GameTables.CfgEncounters.GetById(spawn.EncounterID) == null || spawn.Count <= 0 ||
                    GameTables.CfgEnemies.GetById(spawn.EnemyID)?.Role != (int)RegionEnemyKind.Normal ||
                    !Finite(spawn.StartX) || !Finite(spawn.StartZ) || !Finite(spawn.StepX) || !Finite(spawn.StepZ) || !Finite(spawn.AlternateZ))
                    return Fail("普通怪群成员无效：" + spawn.ID, out error);
            foreach (CfgEncountersRow encounter in GameTables.CfgEncounters.GetAll())
            {
                if (encounter.ID <= 0 || string.IsNullOrWhiteSpace(encounter.PoolID) || encounter.Weight <= 0 ||
                    encounter.MinLevel <= 0 || encounter.MaxLevel < encounter.MinLevel || !ValidateMembers(encounter.ID, out error))
                    return error != null ? false : Fail("普通怪群候选无效：" + encounter.ID, out error);
            }
            foreach (CfgElitePoolRow elite in GameTables.CfgElitePool.GetAll())
                if (elite.ID <= 0 || string.IsNullOrWhiteSpace(elite.PoolID) || elite.Weight <= 0 || elite.MinLevel <= 0 ||
                    elite.MaxLevel < elite.MinLevel || GameTables.CfgEnemies.GetById(elite.EnemyID)?.Role != (int)RegionEnemyKind.Elite)
                    return Fail("精英候选无效：" + elite.ID, out error);
            foreach (CfgChestPoolRow chest in GameTables.CfgChestPool.GetAll())
                if (chest.ID <= 0 || string.IsNullOrWhiteSpace(chest.PoolID) || chest.Weight <= 0 || chest.ItemCount <= 0 ||
                    !HasLootPool(chest.LootPoolID, 0)) return Fail("宝箱候选无效：" + chest.ID, out error);
            foreach (CfgNpcPoolRow npc in GameTables.CfgNpcPool.GetAll())
                if (npc.ID <= 0 || string.IsNullOrWhiteSpace(npc.PoolID) || npc.Weight <= 0 || npc.Price <= 0 || npc.Stock <= 0 ||
                    !HasLootPool(npc.LootPoolID, 0)) return Fail("商人候选无效：" + npc.ID, out error);
            foreach (CfgRegionContentRow content in GameTables.CfgRegionContent.GetAll())
                if (content.ID <= 0 || content.MonsterGroupCount < 0 || content.EliteCount < 0 || content.ChestCount < 0 || content.NpcCount < 0 ||
                    GameTables.CfgRespawnRules.GetById(content.NormalRespawnID) == null ||
                    GameTables.CfgRespawnRules.GetById(content.EliteRespawnID) == null || !Positive(content.InteractionRadius) ||
                    !Positive(content.ReachabilityGridSpacing) || content.ReachabilityMaxCells <= 0 || content.MaxSpawnSlots <= 0)
                    return Fail("区域内容规则无效：" + content.ID, out error);
            int defaults = 0;
            foreach (CfgLevelsRow level in GameTables.CfgLevels.GetAll())
            {
                if (level.IsDefault) defaults++;
                if (level.ID <= 0 || level.LootLevel <= 0 || level.RegionCount <= 0 || level.RegionCount == int.MaxValue ||
                    (long)level.LootLevel + level.RegionCount > int.MaxValue || level.RequiredLevel <= 0 ||
                    string.IsNullOrWhiteSpace(level.Name) || string.IsNullOrWhiteSpace(level.Description) ||
                    GameTables.CfgRegionContent.GetById(level.ContentID) == null || !HasLootPool(level.LootPoolID, level.LootLevel) ||
                    GameTables.CfgRogueProgress.GetById(level.ProgressRuleID) == null || !HasSkillPool(level.SkillPoolID))
                    return Fail("关卡配置无效：" + level.ID, out error);
                for (int index = 0; index <= level.RegionCount; index++)
                {
                    int intensity = level.LootLevel + index;
                    string pool = index == level.RegionCount ? level.FinalBossPoolID : level.BossPoolID;
                    if (BossCandidates(pool, intensity).Count == 0) return Fail("关卡缺少适用 Boss 池：" + level.ID + "/" + index, out error);
                    if (index < level.RegionCount && !ValidatePools(level, intensity, out error)) return false;
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
        /// 开局一次确定所有区域的 Boss、绑定地图和四条独立内容种子。
        /// </summary>
        /// <param name="runId">全局局次身份。</param>
        /// <param name="seed">本局种子。</param>
        /// <param name="level">已经通过配置门禁的关卡。</param>
        /// <param name="requests">按零起点序号排列的区域请求，最后一项为最终区。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>所有区域绑定均可保存时为真。</returns>
        public static bool TryCreateRegionRequests(string runId, int seed, LevelDefinition level,
            out List<RegionGenerationRequest> requests, out string error)
        {
            requests = null;
            error = null;
            if (string.IsNullOrWhiteSpace(runId) || level == null || !Validate(out error))
                return error != null ? false : Fail("区域开局请求身份无效。", out error);
            var generated = new List<RegionGenerationRequest>();
            var bossRandom = new PersistentRandom(unchecked(seed ^ 0x424F5353));
            var seeds = new PersistentRandom(unchecked(seed ^ 0x53454544));
            for (int index = 0; index <= level.Row.RegionCount; index++)
            {
                int intensity = level.LootLevel + index;
                var candidates = BossCandidates(index == level.Row.RegionCount ? level.Row.FinalBossPoolID : level.Row.BossPoolID, intensity);
                long total = 0;
                foreach (CfgBossesRow candidate in candidates) total += candidate.Weight;
                long draw = (long)(bossRandom.NextDouble() * total);
                CfgBossesRow selected = null;
                foreach (CfgBossesRow candidate in candidates)
                {
                    draw -= candidate.Weight;
                    if (draw >= 0) continue;
                    selected = candidate;
                    break;
                }
                if (selected == null) return Fail("Boss 加权抽选失败。", out error);
                CfgRegionMapsRow map = GameTables.CfgRegionMaps.GetById(selected.MapID);
                generated.Add(new RegionGenerationRequest
                {
                    RunId = runId, RegionId = index + 1, RegionIndex = index, LevelId = level.ID, LootLevel = level.LootLevel,
                    EnemyLevel = intensity, BossDefinitionId = selected.ID, BossEnemyId = selected.EnemyID, BossEnemyLevel = intensity,
                    BossAnchorId = "boss", MapId = map.ID, MapVersion = map.Version,
                    ConfigVersion = GameTables.CfgRunPersistence.GetById(1).ConfigVersion,
                    MonsterSeed = seeds.Next(), EliteSeed = seeds.Next(), ChestSeed = seeds.Next(), NpcSeed = seeds.Next()
                });
            }
            requests = generated;
            return true;
        }

        /// <summary>
        /// 将区域业务配置转换为首次进入时的冻结规则；最终区只生成 Boss。
        /// </summary>
        /// <param name="request">开局保存的区域请求。</param>
        /// <returns>引用当前唯一配置表集的区域内容定义。</returns>
        public static RegionContentDefinition CreateRegionContent(RegionGenerationRequest request)
        {
            CfgLevelsRow level = GameTables.CfgLevels.GetById(request.LevelId);
            CfgRegionContentRow row = GameTables.CfgRegionContent.GetById(level.ContentID);
            bool final = request.RegionIndex == level.RegionCount;
            TryCreateRespawn(GameTables.CfgRespawnRules.GetById(row.NormalRespawnID), out RegionRespawnRule normal);
            TryCreateRespawn(GameTables.CfgRespawnRules.GetById(row.EliteRespawnID), out RegionRespawnRule eliteRule);
            var result = new RegionContentDefinition
            {
                MonsterGroupCount = final ? 0 : row.MonsterGroupCount, EliteCount = final ? 0 : row.EliteCount,
                ChestCount = final ? 0 : row.ChestCount, NpcCount = final ? 0 : row.NpcCount,
                MonsterAnchorTag = "monster", EliteAnchorTag = "elite", ChestAnchorTag = "chest", NpcAnchorTag = "npc",
                BossRadius = (float)GameTables.CfgEnemies.GetById(request.BossEnemyId).Radius,
                PlayerRadius = (float)GameTables.CfgPlayerCombat.GetById(1).Radius, InteractionRadius = (float)row.InteractionRadius,
                ReachabilityGridSpacing = (float)row.ReachabilityGridSpacing, ReachabilityMaxCells = row.ReachabilityMaxCells,
                MaxSpawnSlots = row.MaxSpawnSlots, NormalRespawn = normal, EliteRespawn = eliteRule,
                ItemFactory = ProjectEquipmentRules.CreateLootFromPool
            };
            if (final) return result;
            foreach (CfgEncountersRow encounter in GameTables.CfgEncounters.GetAll())
            {
                if (encounter.PoolID != row.MonsterPoolID) continue;
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
            foreach (CfgElitePoolRow elite in GameTables.CfgElitePool.GetAll())
                if (elite.PoolID == row.ElitePoolID) result.EliteCandidates.Add(new RegionEliteCandidate
                {
                    Id = elite.ID, EnemyId = elite.EnemyID, Weight = elite.Weight, MinLevel = elite.MinLevel, MaxLevel = elite.MaxLevel,
                    Radius = (float)GameTables.CfgEnemies.GetById(elite.EnemyID).Radius
                });
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
        /// 检查关卡当前强度的各内容池及物品等级覆盖。
        /// </summary>
        /// <param name="level">关卡行。</param>
        /// <param name="intensity">区域强度。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>要求的四类内容均有候选时为真。</returns>
        private static bool ValidatePools(CfgLevelsRow level, int intensity, out string error)
        {
            CfgRegionContentRow content = GameTables.CfgRegionContent.GetById(level.ContentID);
            bool monsters = content.MonsterGroupCount == 0;
            bool elites = content.EliteCount == 0;
            bool chests = content.ChestCount == 0;
            bool npcs = content.NpcCount == 0;
            foreach (CfgEncountersRow candidate in GameTables.CfgEncounters.GetAll())
                if (candidate.PoolID == content.MonsterPoolID && candidate.MinLevel <= intensity && intensity <= candidate.MaxLevel) monsters = true;
            foreach (CfgElitePoolRow candidate in GameTables.CfgElitePool.GetAll())
                if (candidate.PoolID == content.ElitePoolID && candidate.MinLevel <= intensity && intensity <= candidate.MaxLevel) elites = true;
            foreach (CfgChestPoolRow candidate in GameTables.CfgChestPool.GetAll())
                if (candidate.PoolID == content.ChestPoolID)
                {
                    if (!HasLootPool(candidate.LootPoolID, level.LootLevel)) return Fail("宝箱物品池缺少当前物品等级。", out error);
                    chests = true;
                }
            foreach (CfgNpcPoolRow candidate in GameTables.CfgNpcPool.GetAll())
                if (candidate.PoolID == content.NpcPoolID)
                {
                    if (!HasLootPool(candidate.LootPoolID, level.LootLevel)) return Fail("商人物品池缺少当前物品等级。", out error);
                    npcs = true;
                }
            error = null;
            return monsters && elites && chests && npcs ? true : Fail("关卡区域缺少普通怪、精英、宝箱或商人候选。", out error);
        }

        /// <summary>
        /// 稳定排序当前强度可用的 Boss 候选。
        /// </summary>
        /// <param name="poolId">Boss 池身份。</param>
        /// <param name="intensity">区域强度。</param>
        /// <returns>正权重候选。</returns>
        private static List<CfgBossesRow> BossCandidates(string poolId, int intensity)
        {
            var result = new List<CfgBossesRow>();
            foreach (CfgBossesRow row in GameTables.CfgBosses.GetAll())
                if (row.PoolID == poolId && row.Weight > 0 && row.MinLevel <= intensity && intensity <= row.MaxLevel) result.Add(row);
            result.Sort((a, b) => a.ID.CompareTo(b.ID));
            return result;
        }

        /// <summary>
        /// 将秒数转换为同战斗时钟一致的有效逻辑帧。
        /// </summary>
        /// <param name="row">复活规则行。</param>
        /// <param name="rule">转换后的独立规则。</param>
        /// <returns>所有时间和倍率合法时为真。</returns>
        private static bool TryCreateRespawn(CfgRespawnRulesRow row, out RegionRespawnRule rule)
        {
            rule = null;
            if (!Nonnegative(row.DelaySeconds) || !Nonnegative(row.WarningSeconds) || !Nonnegative(row.SafeDistance) ||
                !Nonnegative(row.ExperienceMultiplier) || Math.Ceiling(row.DelaySeconds / CombatWorld.TickSeconds) >= long.MaxValue ||
                Math.Ceiling(row.WarningSeconds / CombatWorld.TickSeconds) >= long.MaxValue) return false;
            rule = new RegionRespawnRule
            {
                DelayTicks = (long)Math.Ceiling(row.DelaySeconds / CombatWorld.TickSeconds),
                WarningTicks = (long)Math.Ceiling(row.WarningSeconds / CombatWorld.TickSeconds),
                SafeDistance = (float)row.SafeDistance, ExperienceMultiplier = (float)row.ExperienceMultiplier
            };
            return rule.IsValid();
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
        /// 检查刷怪技能池存在候选。
        /// </summary>
        /// <param name="poolId">技能池身份。</param>
        /// <returns>至少有一个正权重条目时为真。</returns>
        private static bool HasSkillPool(string poolId)
        {
            foreach (CfgRoguePoolsRow row in GameTables.CfgRoguePools.GetAll())
                if (row.PoolID == poolId && GameTables.CfgRogueRewards.GetById(row.RewardID)?.Weight > 0) return true;
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
