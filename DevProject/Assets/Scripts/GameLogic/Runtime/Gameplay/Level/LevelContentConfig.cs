using System;
using System.Collections.Generic;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEngine;
using Random = System.Random;

namespace GameLogic.Gameplay.Level
{
    /// <summary>
    /// 一个关卡及其已解析的房间布局。
    /// </summary>
    public sealed class LevelDefinition
    {
        public CfgLevelsRow Row { get; }
        public CfgRoomLayoutsRow Layout { get; }
        public int ID => Row.ID;
        /// <summary>
        /// 当前关卡生成物品时使用的掉落等级。
        /// </summary>
        public int LootLevel => Row.LootLevel;
        public string LootPoolID => Row.LootPoolID;
        public IReadOnlyList<LevelObstacle> Obstacles { get; }

        /// <summary>
        /// 包装关卡和对应布局。
        /// </summary>
        /// <param name="row">关卡配置行。</param>
        /// <param name="layout">房间布局配置行。</param>
        /// <param name="obstacles">布局内的障碍。</param>
        internal LevelDefinition(CfgLevelsRow row, CfgRoomLayoutsRow layout, IReadOnlyList<LevelObstacle> obstacles)
        {
            Row = row;
            Layout = layout;
            Obstacles = obstacles;
        }
    }

    /// <summary>
    /// 一个敌人的数值、资源和掉落配置。
    /// </summary>
    public sealed class EnemyDefinition
    {
        public CfgEnemiesRow Row { get; }
        public int ID => Row.ID;
        public string PrefabAddress => Row.PrefabAddress;
        public Vector3 ModelScale => new Vector3((float)Row.ModelScaleX, (float)Row.ModelScaleY, (float)Row.ModelScaleZ);
        public int SkillID => Row.SkillID;
        public string DropPoolID => Row.DropPoolID;
        public float DropChance => (float)Row.DropChance;

        /// <summary>
        /// 包装敌人配置行。
        /// </summary>
        /// <param name="row">敌人配置行。</param>
        internal EnemyDefinition(CfgEnemiesRow row) => Row = row;

        /// <summary>
        /// 计算指定关卡的敌人生命。
        /// </summary>
        /// <param name="levelId">关卡编号。</param>
        /// <returns>该关卡的生命值。</returns>
        public float HealthAt(int levelId) => (float)(Row.BaseHealth + Row.HealthPerLevel * levelId);

        /// <summary>
        /// 计算指定关卡的击杀经验。
        /// </summary>
        /// <param name="levelId">关卡编号。</param>
        /// <returns>击杀经验。</returns>
        public int ExperienceAt(int levelId) => Row.ExperienceBase + Row.ExperiencePerLevel * levelId;
    }

    /// <summary>
    /// 一个遭遇中的敌人生成位置规则。
    /// </summary>
    public sealed class SpawnDefinition
    {
        public CfgSpawnsRow Row { get; }
        public int EnemyID => Row.EnemyID;
        public int Count => Row.Count;

        /// <summary>
        /// 包装刷怪配置行。
        /// </summary>
        /// <param name="row">刷怪配置行。</param>
        internal SpawnDefinition(CfgSpawnsRow row) => Row = row;

        /// <summary>
        /// 读取组内指定敌人的局部位置。
        /// </summary>
        /// <param name="index">组内从零开始的序号。</param>
        /// <returns>局部 XZ 位置。</returns>
        public Vector3 PositionAt(int index) => new Vector3((float)(Row.StartX + index * Row.StepX), 0f,
            (float)(Row.StartZ + index * Row.StepZ + (index % 2) * Row.AlternateZ));
    }

    /// <summary>
    /// 关卡表的读取、解析与启动验证。
    /// </summary>
    public static partial class LevelContentConfig
    {
        /// <summary>
        /// 校验关卡、模板、遭遇、几何和全部引用。
        /// </summary>
        /// <param name="error">失败时的说明。</param>
        /// <returns>全部关卡内容可用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgLevels == null || GameTables.CfgRoomLayouts == null || GameTables.CfgObstacles == null ||
                GameTables.CfgRoomTemplates == null || GameTables.CfgEncounters == null || GameTables.CfgSpawns == null ||
                GameTables.CfgEnemies == null || GameTables.CfgCombatSkills == null || GameTables.CfgLootPool == null ||
                GameTables.CfgPlayerCombat?.GetById(1) == null)
            { error = "关卡配置表未完整加载。"; return false; }
            int defaults = 0;
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                if (row.IsDefault) defaults++;
                if (row.ID < 1 || row.ID > PrototypeRules.LegacySkillRankLimit || row.LootLevel < 1 || row.LootLevel > 1000 ||
                    string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.Description) || row.RequiredLevel < 1 ||
                    row.GridWidth < 2 || row.GridHeight < 2 || (long)row.GridWidth * row.GridHeight < 6 ||
                    (long)row.GridWidth * row.GridHeight > 1000 ||
                    row.ExtraConnectionMin < 0 || row.ExtraConnectionMax < row.ExtraConnectionMin ||
                    row.ExtraConnectionMax > (row.GridWidth - 1) * (row.GridHeight - 1) ||
                    row.LootRoomCount < 0 || row.LootRoomCount > row.GridWidth * row.GridHeight - 3 ||
                    !Finite(row.ExtraExtractionChance) || row.ExtraExtractionChance < 0 || row.ExtraExtractionChance > 1 ||
                    GameTables.CfgRoomLayouts.GetById(row.LayoutID) == null || !HasPool(row.LootPoolID, row.LootLevel))
                { error = "关卡配置无效：" + row.ID; return false; }
            }
            if (defaults != 1 || GameTables.CfgLevels.GetAll().Count == 0)
            { error = "关卡必须且只能有一项默认条目。"; return false; }
            foreach (CfgRoomLayoutsRow row in GameTables.CfgRoomLayouts.GetAll())
            {
                if (!FinitePositive(row.HalfExtent) || row.HalfExtent > 32 || !FinitePositive(row.WallHeight) ||
                    !FinitePositive(row.WallThickness) || !FinitePositive(row.ExitHalfWidth) ||
                    row.ExitHalfWidth >= row.HalfExtent || !Finite(row.SpawnInset) || row.SpawnInset <= row.WallThickness * 0.5 ||
                    row.SpawnInset >= row.HalfExtent || !Finite(row.ExitCenterInset) || row.ExitCenterInset < 0 || row.ExitCenterInset >= row.HalfExtent ||
                    !FinitePositive(row.ExitInteractionRadius) || !FinitePositive(row.ObjectInteractionRadius) ||
                    !FinitePositive(row.ObstacleHeight) || !Inside(row.ChestX, row.ChestZ, row.HalfExtent) ||
                    !Inside(row.NpcX, row.NpcZ, row.HalfExtent) || !Inside(row.ExtractionX, row.ExtractionZ, row.HalfExtent))
                { error = "房间布局无效：" + row.ID; return false; }
            }
            foreach (CfgObstaclesRow row in GameTables.CfgObstacles.GetAll())
            {
                CfgRoomLayoutsRow layout = GameTables.CfgRoomLayouts.GetById(row.LayoutID);
                if (layout == null || !Inside(row.MinX, row.MinZ, layout.HalfExtent) ||
                    !Inside(row.MaxX, row.MaxZ, layout.HalfExtent) || row.MinX >= row.MaxX || row.MinZ >= row.MaxZ)
                { error = "房间障碍无效：" + row.ID; return false; }
            }
            foreach (CfgEnemiesRow row in GameTables.CfgEnemies.GetAll())
            {
                if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.PrefabAddress) ||
                    !FinitePositive(row.ModelScaleX) || !FinitePositive(row.ModelScaleY) || !FinitePositive(row.ModelScaleZ) ||
                    !FinitePositive(row.BaseHealth) || !Finite(row.HealthPerLevel) || row.HealthPerLevel < 0 || !FinitePositive(row.Attack) ||
                    !FinitePositive(row.MoveSpeed) || !FinitePositive(row.Radius) || !FinitePositive(row.AttackRange) ||
                    !Finite(row.AggroRange) || row.AggroRange < row.AttackRange || GameTables.CfgCombatSkills.GetById(row.SkillID) == null ||
                    row.ExperienceBase < 0 || row.ExperiencePerLevel < 0 || !Finite(row.DropChance) || row.DropChance < 0 || row.DropChance > 1 ||
                    !HasPool(row.DropPoolID))
                { error = "敌人配置无效：" + row.ID; return false; }
            }
            foreach (CfgSpawnsRow row in GameTables.CfgSpawns.GetAll())
                if (GameTables.CfgEncounters.GetById(row.EncounterID) == null ||
                    GameTables.CfgEnemies.GetById(row.EnemyID) == null || row.Count < 1 || row.Count > 999 ||
                    !Finite(row.StartX) || !Finite(row.StartZ) || !Finite(row.StepX) ||
                    !Finite(row.StepZ) || !Finite(row.AlternateZ))
                { error = "刷怪配置无效：" + row.ID; return false; }
            foreach (CfgEncountersRow row in GameTables.CfgEncounters.GetAll())
                if (string.IsNullOrWhiteSpace(row.PoolID) || GameTables.CfgRoomLayouts.GetById(row.LayoutID) == null ||
                    row.MinLevel < 1 || row.MaxLevel < row.MinLevel || row.Weight <= 0 || GetSpawns(row.ID).Count == 0)
                { error = "遭遇配置无效：" + row.ID; return false; }
            foreach (CfgRoomTemplatesRow row in GameTables.CfgRoomTemplates.GetAll())
            {
                if (!Enum.TryParse(row.RoomType, out LevelRoomType type) || row.LevelID <= 0 ||
                    GameTables.CfgLevels.GetById(row.LevelID) == null ||
                    GameTables.CfgRoomLayouts.GetById(row.LayoutID) == null || row.Weight <= 0 ||
                    row.AllowedExits < 1 || row.AllowedExits > 15 || row.IsStart && type != LevelRoomType.Mob ||
                    (type == LevelRoomType.Loot || type == LevelRoomType.Npc) &&
                        !HasPool(row.LootPoolID, GameTables.CfgLevels.GetById(row.LevelID)?.LootLevel ?? 0) ||
                    type == LevelRoomType.Npc && (row.NpcPrice < 1 || row.NpcStock < 1) ||
                    (type == LevelRoomType.Mob || type == LevelRoomType.Boss) && string.IsNullOrWhiteSpace(row.EncounterPoolID) ||
                    (type == LevelRoomType.Npc || type == LevelRoomType.Loot) && !string.IsNullOrWhiteSpace(row.EncounterPoolID))
                { error = "房间模板无效：" + row.ID; return false; }
                if (type == LevelRoomType.Mob || type == LevelRoomType.Boss)
                {
                    bool hasEncounter = false;
                    foreach (CfgEncountersRow encounter in GameTables.CfgEncounters.GetAll())
                    {
                        if (encounter.PoolID != row.EncounterPoolID || encounter.LayoutID != row.LayoutID ||
                            encounter.MinLevel > row.LevelID || encounter.MaxLevel < row.LevelID) continue;
                        hasEncounter = true;
                        if (!ValidateEncounter(row, encounter, out error)) return false;
                    }
                    if (!hasEncounter) { error = "模板缺少可用遭遇：" + row.ID; return false; }
                }
                if (!ValidateGeometry(row, out error)) return false;
            }
            foreach (CfgLevelsRow level in GameTables.CfgLevels.GetAll())
                foreach (LevelRoomType type in Enum.GetValues(typeof(LevelRoomType)))
                    for (int mask = 1; mask <= 15; mask++)
                        if (!HasTemplate(level.ID, type, false, mask) ||
                            type == LevelRoomType.Mob && !HasTemplate(level.ID, type, true, mask))
                        { error = "关卡房间模板缺少类型或出口覆盖：" + level.ID + "/" + type + "/" + mask; return false; }
            return true;
        }

        /// <summary>
        /// 取得局外关卡选择条目。
        /// </summary>
        /// <returns>按编号排列的关卡条目。</returns>
        public static ProjectLevelOption[] CreateLevels()
        {
            if (GameTables.CfgLevels == null) return Array.Empty<ProjectLevelOption>();
            var result = new List<ProjectLevelOption>();
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
                result.Add(new ProjectLevelOption { Id = row.ID, Name = row.Name, Description = row.Description, RequiredLevel = row.RequiredLevel });
            result.Sort((a, b) => a.Id.CompareTo(b.Id));
            return result.ToArray();
        }

        /// <summary>
        /// 读取并解析指定关卡的旧默认布局。
        /// </summary>
        /// <param name="id">关卡编号。</param>
        /// <param name="level">存在时的关卡内容。</param>
        /// <returns>关卡及默认布局均存在时为真。</returns>
        public static bool TryGetLevel(int id, out LevelDefinition level)
        {
            CfgLevelsRow row = GameTables.CfgLevels?.GetById(id);
            level = row == null ? null : CreateDefinition(row, row.LayoutID);
            return level != null;
        }

        /// <summary>
        /// 读取敌人配置。
        /// </summary>
        /// <param name="id">敌人编号。</param>
        /// <param name="enemy">存在时的敌人定义。</param>
        /// <returns>敌人存在时为真。</returns>
        public static bool TryGetEnemy(int id, out EnemyDefinition enemy)
        {
            CfgEnemiesRow row = GameTables.CfgEnemies?.GetById(id);
            enemy = row == null ? null : new EnemyDefinition(row);
            return enemy != null;
        }

        /// <summary>
        /// 按房间图和固定随机源抽选模板与遭遇。
        /// </summary>
        /// <param name="level">所属关卡。</param>
        /// <param name="room">已生成出口的房间。</param>
        /// <param name="random">本局固定随机源。</param>
        /// <param name="plan">选定的内容计划。</param>
        /// <returns>模板和遭遇都可用时为真。</returns>
        public static bool TryCreateRoomPlan(LevelDefinition level, LevelRoomState room, Random random, out LevelRoomPlan plan)
        {
            plan = null;
            if (level == null || room == null || random == null || GameTables.CfgRoomTemplates == null) return false;
            int mask = ExitMask(room.Exits.Keys);
            var templates = new List<CfgRoomTemplatesRow>();
            foreach (CfgRoomTemplatesRow template in GameTables.CfgRoomTemplates.GetAll())
                if (template.LevelID == level.ID && template.RoomType == room.Type.ToString() &&
                    template.IsStart == room.IsStart && (template.AllowedExits & mask) == mask && template.Weight > 0)
                    templates.Add(template);
            templates.Sort((a, b) => a.ID.CompareTo(b.ID));
            CfgRoomTemplatesRow selected = WeightedPick(templates, row => row.Weight, random);
            if (selected == null) return false;
            int encounterId = 0;
            IReadOnlyList<SpawnDefinition> spawns = Array.Empty<SpawnDefinition>();
            if (room.Type == LevelRoomType.Mob || room.Type == LevelRoomType.Boss)
            {
                var encounters = new List<CfgEncountersRow>();
                foreach (CfgEncountersRow encounter in GameTables.CfgEncounters.GetAll())
                    if (encounter.PoolID == selected.EncounterPoolID && encounter.LayoutID == selected.LayoutID &&
                        encounter.MinLevel <= level.ID && encounter.MaxLevel >= level.ID && encounter.Weight > 0)
                        encounters.Add(encounter);
                encounters.Sort((a, b) => a.ID.CompareTo(b.ID));
                CfgEncountersRow chosen = WeightedPick(encounters, row => row.Weight, random);
                if (chosen == null) return false;
                encounterId = chosen.ID;
                spawns = GetSpawns(encounterId);
            }
            LevelDefinition definition = CreateDefinition(level.Row, selected.LayoutID);
            if (definition == null) return false;
            plan = new LevelRoomPlan(selected.ID, encounterId, definition, spawns, selected.LootPoolID,
                selected.NpcPrice, selected.NpcStock);
            return true;
        }

        /// <summary>
        /// 兼容旧调用：返回与默认布局相同的第一组匹配遭遇。
        /// </summary>
        /// <param name="level">关卡默认布局。</param>
        /// <param name="type">房间类型。</param>
        /// <param name="isStart">是否起始房间。</param>
        /// <returns>兼容测试使用的出生组。</returns>
        public static IReadOnlyList<SpawnDefinition> GetSpawns(LevelDefinition level, LevelRoomType type, bool isStart)
        {
            if (level == null || GameTables.CfgRoomTemplates == null) return Array.Empty<SpawnDefinition>();
            foreach (CfgRoomTemplatesRow template in GameTables.CfgRoomTemplates.GetAll())
                if (template.LevelID == level.ID && template.LayoutID == level.Layout.ID &&
                    template.RoomType == type.ToString() && template.IsStart == isStart)
                    foreach (CfgEncountersRow encounter in GameTables.CfgEncounters.GetAll())
                        if (encounter.PoolID == template.EncounterPoolID && encounter.LayoutID == template.LayoutID &&
                            encounter.MinLevel <= level.ID && encounter.MaxLevel >= level.ID)
                            return GetSpawns(encounter.ID);
            return Array.Empty<SpawnDefinition>();
        }

        /// <summary>
        /// 按遭遇编号读取全部出生组。
        /// </summary>
        /// <param name="encounterId">遭遇编号。</param>
        /// <returns>按 ID 排序的出生组。</returns>
        private static IReadOnlyList<SpawnDefinition> GetSpawns(int encounterId)
        {
            var result = new List<SpawnDefinition>();
            foreach (CfgSpawnsRow row in GameTables.CfgSpawns.GetAll())
                if (row.EncounterID == encounterId) result.Add(new SpawnDefinition(row));
            result.Sort((a, b) => a.Row.ID.CompareTo(b.Row.ID));
            return result.AsReadOnly();
        }

        /// <summary>
        /// 创建指定房间布局定义。
        /// </summary>
        /// <param name="row">所属关卡。</param>
        /// <param name="layoutId">选定布局编号。</param>
        /// <returns>包含该布局障碍的定义。</returns>
        private static LevelDefinition CreateDefinition(CfgLevelsRow row, int layoutId)
        {
            CfgRoomLayoutsRow layout = GameTables.CfgRoomLayouts?.GetById(layoutId);
            if (layout == null) return null;
            var obstacles = new List<LevelObstacle>();
            if (GameTables.CfgObstacles != null)
                foreach (CfgObstaclesRow obstacle in GameTables.CfgObstacles.GetAll())
                    if (obstacle.LayoutID == layoutId)
                        obstacles.Add(new LevelObstacle(new Vector2((float)obstacle.MinX, (float)obstacle.MinZ),
                            new Vector2((float)obstacle.MaxX, (float)obstacle.MaxZ)));
            return new LevelDefinition(row, layout, obstacles);
        }

        /// <summary>
        /// 按正权重选择一行。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="rows">稳定排序后的候选。</param>
        /// <param name="weight">权重读取函数。</param>
        /// <param name="random">本局随机源。</param>
        /// <returns>选中行；无候选时为空。</returns>
        private static T WeightedPick<T>(IReadOnlyList<T> rows, Func<T, int> weight, Random random) where T : class
        {
            long total = 0;
            foreach (T row in rows) total += weight(row);
            if (total <= 0) return null;
            long draw = (long)(random.NextDouble() * total);
            foreach (T row in rows)
            {
                draw -= weight(row);
                if (draw < 0) return row;
            }
            return rows[rows.Count - 1];
        }

        /// <summary>
        /// 将出口方向转成配置掩码。
        /// </summary>
        /// <param name="exits">房间全部出口。</param>
        /// <returns>North=1、East=2、South=4、West=8。</returns>
        private static int ExitMask(IEnumerable<RoomDirection> exits)
        {
            int mask = 0;
            foreach (RoomDirection direction in exits) mask |= 1 << (int)direction;
            return mask;
        }

        /// <summary>
        /// 检查关卡类型与出口掩码有可用模板。
        /// </summary>
        /// <param name="levelId">关卡编号。</param>
        /// <param name="type">房间类型。</param>
        /// <param name="start">是否起始房间。</param>
        /// <param name="mask">需要包含的出口。</param>
        /// <returns>存在可用模板时为真。</returns>
        private static bool HasTemplate(int levelId, LevelRoomType type, bool start, int mask)
        {
            foreach (CfgRoomTemplatesRow row in GameTables.CfgRoomTemplates.GetAll())
                if (row.LevelID == levelId && row.RoomType == type.ToString() && row.IsStart == start &&
                    (row.AllowedExits & mask) == mask && row.Weight > 0) return true;
            return false;
        }

        /// <summary>
        /// 检查池在指定关卡有正权重掉落。
        /// </summary>
        /// <param name="poolId">池标识。</param>
        /// <param name="levelId">关卡编号；零表示任意关卡。</param>
        /// <returns>至少一项可用时为真。</returns>
        private static bool HasPool(string poolId, int levelId = 0)
        {
            if (string.IsNullOrWhiteSpace(poolId) || GameTables.CfgLootPool == null) return false;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (row.PoolID == poolId && row.Weight > 0 &&
                    (levelId == 0 || row.MinLevel <= levelId && levelId <= row.MaxLevel)) return true;
            return false;
        }

        /// <summary>
        /// 检查坐标严格位于布局边界内。
        /// </summary>
        /// <param name="x">水平坐标。</param>
        /// <param name="z">纵向坐标。</param>
        /// <param name="extent">房间半边长。</param>
        /// <returns>位置位于边界内时为真。</returns>
        private static bool Inside(double x, double z, double extent) => Finite(x) && Finite(z) && Math.Abs(x) < extent && Math.Abs(z) < extent;

        /// <summary>
        /// 检查数值有限。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>有限时为真。</returns>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        /// <summary>
        /// 检查数值有限且为正。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>有限正数时为真。</returns>
        private static bool FinitePositive(double value) => Finite(value) && value > 0 && value <= float.MaxValue;
    }
}
