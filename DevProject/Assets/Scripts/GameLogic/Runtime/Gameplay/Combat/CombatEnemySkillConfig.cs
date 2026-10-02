using System;
using System.Collections.Generic;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 将敌人技能选择表转换为不依赖配置表的战斗数据。
    /// </summary>
    public static class CombatEnemySkillConfig
    {
        /// <summary>
        /// 校验技能引用、选择范围及每个敌人的生命阶段覆盖。
        /// </summary>
        /// <param name="error">失败时的首个错误。</param>
        /// <returns>所有敌人都具有完整有效的技能配置时为真。</returns>
        public static bool Validate(out string error)
        {
            error = null;
            if (GameTables.CfgEnemySkills == null || GameTables.CfgEnemies == null || GameTables.CfgCombatSkills == null)
            {
                error = "敌人技能表尚未完整加载。";
                return false;
            }
            var profiles = new Dictionary<int, List<CfgEnemySkillsRow>>();
            var identities = new HashSet<(int, int)>();
            foreach (CfgEnemySkillsRow row in GameTables.CfgEnemySkills.GetAll())
            {
                CfgEnemiesRow enemy = GameTables.CfgEnemies.GetById(row.EnemyID);
                CfgCombatSkillsRow skill = GameTables.CfgCombatSkills.GetById(row.SkillID);
                if (row.ID <= 0 || enemy == null || skill == null || skill.StartRangeSource != 0 ||
                    row.Priority < 0 || row.Weight <= 0 || !Finite(row.MinHealthRatio) || !Finite(row.MaxHealthRatio) ||
                    row.MinHealthRatio < 0 || row.MaxHealthRatio > 1 || row.MinHealthRatio >= row.MaxHealthRatio ||
                    !Finite(row.MinDistance) || !Finite(row.MaxDistance) || row.MinDistance < 0 ||
                    row.MinDistance >= row.MaxDistance || row.MaxDistance > skill.StartRange || row.MaxDistance > enemy.AggroRange ||
                    !identities.Add((row.EnemyID, row.SkillID)))
                {
                    error = "敌人技能选择配置无效：" + row.ID;
                    return false;
                }
                if (!profiles.TryGetValue(row.EnemyID, out List<CfgEnemySkillsRow> rows))
                    profiles.Add(row.EnemyID, rows = new List<CfgEnemySkillsRow>());
                rows.Add(row);
            }
            foreach (CfgEnemiesRow enemy in GameTables.CfgEnemies.GetAll())
            {
                if (!profiles.TryGetValue(enemy.ID, out List<CfgEnemySkillsRow> rows))
                {
                    error = "敌人没有技能选择配置：" + enemy.ID;
                    return false;
                }
                rows.Sort((left, right) => left.MinHealthRatio.CompareTo(right.MinHealthRatio));
                double covered = 0;
                long weight = 0;
                foreach (CfgEnemySkillsRow row in rows)
                {
                    weight += row.Weight;
                    if (row.MinHealthRatio > covered || weight > int.MaxValue)
                    {
                        error = "敌人技能生命阶段存在空档或权重超出范围：" + enemy.ID;
                        return false;
                    }
                    covered = Math.Max(covered, row.MaxHealthRatio);
                }
                if (covered < 1)
                {
                    error = "敌人技能没有覆盖完整生命阶段：" + enemy.ID;
                    return false;
                }
            }
            return true;
        }

        /// <summary>
        /// 复制指定敌人的技能选择条件，避免战斗世界持有可变表行。
        /// </summary>
        /// <param name="enemyId">敌人配置编号。</param>
        /// <returns>按配置编号排序的独立技能条件数组。</returns>
        public static CombatEnemySkillDefinition[] Create(int enemyId)
        {
            var rows = new List<CfgEnemySkillsRow>();
            foreach (CfgEnemySkillsRow row in GameTables.CfgEnemySkills.GetAll())
                if (row.EnemyID == enemyId) rows.Add(row);
            rows.Sort((left, right) => left.ID.CompareTo(right.ID));
            var result = new CombatEnemySkillDefinition[rows.Count];
            for (int i = 0; i < rows.Count; i++)
            {
                CfgEnemySkillsRow row = rows[i];
                result[i] = new CombatEnemySkillDefinition
                {
                    SkillId = row.SkillID, Priority = row.Priority, Weight = row.Weight,
                    MinHealthRatio = (float)row.MinHealthRatio, MaxHealthRatio = (float)row.MaxHealthRatio,
                    MinDistance = (float)row.MinDistance, MaxDistance = (float)row.MaxDistance
                };
            }
            return result;
        }

        /// <summary>
        /// 判断表值能否安全转换为战斗浮点数。
        /// </summary>
        /// <param name="value">待检查的值。</param>
        /// <returns>有限且不超过浮点表示范围时为真。</returns>
        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) <= float.MaxValue;
    }
}
