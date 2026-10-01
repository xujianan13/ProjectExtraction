using System;
using System.Collections.Generic;
using GameLogic.Project;
using GameLogic.Tables;
using UnityEngine;

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
    /// 一个房间类型的敌人生成位置规则。
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
    public static class LevelContentConfig
    {
        /// <summary>
        /// 校验所有关卡、房间、刷怪与敌人引用。
        /// </summary>
        /// <param name="error">失败时的说明。</param>
        /// <returns>全部关卡内容可用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgLevels == null || GameTables.CfgRoomLayouts == null || GameTables.CfgObstacles == null ||
                GameTables.CfgSpawns == null || GameTables.CfgEnemies == null || GameTables.CfgCombatSkills == null || GameTables.CfgLootPool == null)
            {
                error = "关卡配置表未完整加载。";
                return false;
            }
            int defaults = 0;
            if (GameTables.CfgLevels.GetAll().Count == 0) { error = "没有关卡条目。"; return false; }
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                if (row.IsDefault) defaults++;
                if (row.ID < 1 || row.ID > PrototypeRules.LegacySkillRankLimit || string.IsNullOrWhiteSpace(row.Name) ||
                    string.IsNullOrWhiteSpace(row.Description) || row.RequiredLevel < 1 ||
                    row.GridWidth < 2 || row.GridHeight < 2 || (long)row.GridWidth * row.GridHeight < 6 ||
                    (long)row.GridWidth * row.GridHeight > 1000 ||
                    row.ExtraConnectionMin < 0 || row.ExtraConnectionMax < row.ExtraConnectionMin ||
                    row.ExtraConnectionMax > (row.GridWidth - 1) * (row.GridHeight - 1) ||
                    row.LootRoomCount < 0 || row.LootRoomCount > row.GridWidth * row.GridHeight - 3 ||
                    row.ExtraExtractionChance < 0 || row.ExtraExtractionChance > 1 ||
                    GameTables.CfgRoomLayouts.GetById(row.LayoutID) == null || !HasPool(row.LootPoolID, row.ID))
                { error = "关卡配置无效：" + row.ID; return false; }
            }
            if (defaults != 1) { error = "关卡必须且只能有一项默认条目。"; return false; }
            foreach (CfgRoomLayoutsRow row in GameTables.CfgRoomLayouts.GetAll())
            {
                if (row.HalfExtent <= 0 || row.WallHeight <= 0 || row.WallThickness <= 0 || row.ExitHalfWidth <= 0 ||
                    row.ExitHalfWidth >= row.HalfExtent || row.SpawnInset <= row.WallThickness * 0.5 ||
                    row.SpawnInset >= row.HalfExtent || row.ExitCenterInset < 0 || row.ExitCenterInset >= row.HalfExtent ||
                    row.ExitInteractionRadius <= 0 || row.ObjectInteractionRadius <= 0 || row.ObstacleHeight <= 0 ||
                    !Inside(row.ChestX, row.ChestZ, row.HalfExtent) || !Inside(row.NpcX, row.NpcZ, row.HalfExtent) ||
                    !Inside(row.ExtractionX, row.ExtractionZ, row.HalfExtent))
                { error = "房间布局无效：" + row.ID; return false; }
            }
            foreach (CfgObstaclesRow row in GameTables.CfgObstacles.GetAll())
            {
                CfgRoomLayoutsRow layout = GameTables.CfgRoomLayouts.GetById(row.LayoutID);
                if (layout == null || row.MinX >= row.MaxX || row.MinZ >= row.MaxZ ||
                    !Inside(row.MinX, row.MinZ, layout.HalfExtent) || !Inside(row.MaxX, row.MaxZ, layout.HalfExtent))
                { error = "房间障碍无效：" + row.ID; return false; }
            }
            foreach (CfgEnemiesRow row in GameTables.CfgEnemies.GetAll())
            {
                if (string.IsNullOrWhiteSpace(row.Name) || string.IsNullOrWhiteSpace(row.PrefabAddress) || row.ModelScaleX <= 0 || row.ModelScaleY <= 0 || row.ModelScaleZ <= 0 ||
                    row.BaseHealth <= 0 || row.HealthPerLevel < 0 || row.Attack <= 0 || row.MoveSpeed <= 0 || row.Radius <= 0 ||
                    row.AttackRange <= 0 || row.AggroRange < row.AttackRange || GameTables.CfgCombatSkills.GetById(row.SkillID) == null ||
                    row.ExperienceBase < 0 || row.ExperiencePerLevel < 0 || row.DropChance < 0 || row.DropChance > 1 || !HasPool(row.DropPoolID))
                { error = "敌人配置无效：" + row.ID; return false; }
            }
            foreach (CfgSpawnsRow row in GameTables.CfgSpawns.GetAll())
            {
                CfgRoomLayoutsRow layout = GameTables.CfgRoomLayouts.GetById(row.LayoutID);
                bool type = Enum.TryParse(row.RoomType, out LevelRoomType parsed) &&
                    (parsed == LevelRoomType.Mob || parsed == LevelRoomType.Boss);
                if (!type || layout == null || GameTables.CfgEnemies.GetById(row.EnemyID) == null || row.Count < 1 || row.Count >= 1000)
                { error = "刷怪配置无效：" + row.ID; return false; }
                for (int i = 0; i < row.Count; i++)
                {
                    Vector3 position = new SpawnDefinition(row).PositionAt(i);
                    if (Inside(position.x, position.z, layout.HalfExtent)) continue;
                    error = "刷怪位置超出房间：" + row.ID;
                    return false;
                }
            }
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                if (HasSpawn(row.LayoutID, LevelRoomType.Mob, true) && HasSpawn(row.LayoutID, LevelRoomType.Mob, false) &&
                    HasSpawn(row.LayoutID, LevelRoomType.Boss, false)) continue;
                error = "关卡缺少必要刷怪组：" + row.ID;
                return false;
            }
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
            {
                foreach (CfgSpawnsRow spawn in GameTables.CfgSpawns.GetAll())
                {
                    if (spawn.LayoutID != row.LayoutID) continue;
                    CfgEnemiesRow enemy = GameTables.CfgEnemies.GetById(spawn.EnemyID);
                    if (enemy != null && HasPool(enemy.DropPoolID, row.ID) &&
                        (long)enemy.ExperienceBase + (long)enemy.ExperiencePerLevel * row.ID <= int.MaxValue &&
                        enemy.BaseHealth + enemy.HealthPerLevel * row.ID <= float.MaxValue) continue;
                    error = "关卡敌人没有该等级可抽取的掉落项：" + row.ID + "/" + spawn.EnemyID;
                    return false;
                }
            }
            return ValidateSpawnGroups(out error);
        }

        /// <summary>
        /// 取得局外关卡选择条目。
        /// </summary>
        /// <returns>按编号排列的独立关卡条目。</returns>
        public static ProjectLevelOption[] CreateLevels()
        {
            if (GameTables.CfgLevels == null) return Array.Empty<ProjectLevelOption>();
            var options = new List<ProjectLevelOption>();
            foreach (CfgLevelsRow row in GameTables.CfgLevels.GetAll())
                options.Add(new ProjectLevelOption { Id = row.ID, Name = row.Name, Description = row.Description, RequiredLevel = row.RequiredLevel });
            options.Sort((a, b) => a.Id.CompareTo(b.Id));
            return options.ToArray();
        }

        /// <summary>
        /// 读取并解析指定关卡。
        /// </summary>
        /// <param name="id">关卡编号。</param>
        /// <param name="level">存在时的关卡内容。</param>
        /// <returns>关卡及布局均存在时为真。</returns>
        public static bool TryGetLevel(int id, out LevelDefinition level)
        {
            level = null;
            CfgLevelsRow row = GameTables.CfgLevels?.GetById(id);
            CfgRoomLayoutsRow layout = row == null ? null : GameTables.CfgRoomLayouts?.GetById(row.LayoutID);
            if (layout == null) return false;
            var obstacles = new List<LevelObstacle>();
            if (GameTables.CfgObstacles != null)
                foreach (CfgObstaclesRow obstacle in GameTables.CfgObstacles.GetAll())
                    if (obstacle.LayoutID == layout.ID)
                        obstacles.Add(new LevelObstacle(new Vector2((float)obstacle.MinX, (float)obstacle.MinZ),
                            new Vector2((float)obstacle.MaxX, (float)obstacle.MaxZ)));
            level = new LevelDefinition(row, layout, obstacles);
            return true;
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
        /// 获取指定房间类型的全部刷怪组。
        /// </summary>
        /// <param name="level">当前关卡。</param>
        /// <param name="type">房间类型。</param>
        /// <param name="isStart">是否起始房间。</param>
        /// <returns>按配置编号排列的刷怪组。</returns>
        public static IReadOnlyList<SpawnDefinition> GetSpawns(LevelDefinition level, LevelRoomType type, bool isStart)
        {
            var result = new List<SpawnDefinition>();
            if (level == null || GameTables.CfgSpawns == null) return result;
            foreach (CfgSpawnsRow row in GameTables.CfgSpawns.GetAll())
                if (row.LayoutID == level.Layout.ID && row.RoomType == type.ToString() && row.IsStart == isStart)
                    result.Add(new SpawnDefinition(row));
            result.Sort((a, b) => a.Row.ID.CompareTo(b.Row.ID));
            return result;
        }

        /// <summary>
        /// 校验房间中全部敌人数量、边界、障碍和彼此之间的空间关系。
        /// </summary>
        /// <param name="error">失败时的说明。</param>
        /// <returns>全部刷怪组可以安全生成时为真。</returns>
        private static bool ValidateSpawnGroups(out string error)
        {
            error = null;
            foreach (CfgRoomLayoutsRow layout in GameTables.CfgRoomLayouts.GetAll())
            {
                foreach (LevelRoomType type in new[] { LevelRoomType.Mob, LevelRoomType.Boss })
                {
                    foreach (bool isStart in new[] { false, true })
                    {
                        var positions = new List<Vector2>();
                        var radii = new List<float>();
                        foreach (CfgSpawnsRow spawn in GameTables.CfgSpawns.GetAll())
                        {
                            if (spawn.LayoutID != layout.ID || spawn.RoomType != type.ToString() || spawn.IsStart != isStart) continue;
                            CfgEnemiesRow enemy = GameTables.CfgEnemies.GetById(spawn.EnemyID);
                            float radius = (float)enemy.Radius;
                            for (int i = 0; i < spawn.Count; i++)
                            {
                                if (positions.Count >= 999) { error = "单房间敌人超过实体编号容量：" + layout.ID; return false; }
                                Vector3 point = new SpawnDefinition(spawn).PositionAt(i);
                                float limit = (float)(layout.HalfExtent - layout.WallThickness * 0.5) - radius;
                                if (Mathf.Abs(point.x) >= limit || Mathf.Abs(point.z) >= limit)
                                { error = "刷怪点靠近边界：" + spawn.ID; return false; }
                                var position = new Vector2(point.x, point.z);
                                foreach (CfgObstaclesRow obstacle in GameTables.CfgObstacles.GetAll())
                                {
                                    if (obstacle.LayoutID != layout.ID) continue;
                                    var area = new LevelObstacle(new Vector2((float)obstacle.MinX, (float)obstacle.MinZ),
                                        new Vector2((float)obstacle.MaxX, (float)obstacle.MaxZ));
                                    if (!area.Overlaps(position, radius)) continue;
                                    error = "刷怪点与障碍重叠：" + spawn.ID;
                                    return false;
                                }
                                for (int earlier = 0; earlier < positions.Count; earlier++)
                                {
                                    if (Vector2.Distance(position, positions[earlier]) >= radius + radii[earlier]) continue;
                                    error = "刷怪点相互重叠：" + spawn.ID;
                                    return false;
                                }
                                positions.Add(position);
                                radii.Add(radius);
                            }
                        }
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 检查坐标位于布局边界内。
        /// </summary>
        /// <param name="x">水平坐标。</param>
        /// <param name="z">纵向坐标。</param>
        /// <param name="extent">房间半边长。</param>
        /// <returns>坐标严格位于边界内时为真。</returns>
        private static bool Inside(double x, double z, double extent) => Math.Abs(x) < extent && Math.Abs(z) < extent;

        /// <summary>
        /// 判断掉落池身份是否在物品配置中存在。
        /// </summary>
        /// <param name="poolId">掉落池身份。</param>
        /// <param name="levelId">关卡编号；零表示任意等级。</param>
        /// <returns>至少有一项时为真。</returns>
        private static bool HasPool(string poolId, int levelId = 0)
        {
            if (string.IsNullOrWhiteSpace(poolId) || GameTables.CfgLootPool == null) return false;
            foreach (CfgLootPoolRow row in GameTables.CfgLootPool.GetAll())
                if (row.PoolID == poolId && row.Weight > 0 && (levelId == 0 || row.MinLevel <= levelId && levelId <= row.MaxLevel)) return true;
            return false;
        }

        /// <summary>
        /// 判断布局包含对应房间类型的刷怪组。
        /// </summary>
        /// <param name="layoutId">布局编号。</param>
        /// <param name="type">房间类型。</param>
        /// <param name="isStart">是否起始房间。</param>
        /// <returns>存在刷怪组时为真。</returns>
        private static bool HasSpawn(int layoutId, LevelRoomType type, bool isStart)
        {
            foreach (CfgSpawnsRow row in GameTables.CfgSpawns.GetAll())
                if (row.LayoutID == layoutId && row.RoomType == type.ToString() && row.IsStart == isStart) return true;
            return false;
        }
    }
}
