using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Tables;

namespace GameLogic.Project
{
    /// <summary>
    /// 项目内容的统一装载和启动门禁；只在创建局外会话之前切换整套配置。
    /// </summary>
    public static class ProjectContentConfig
    {
        private static readonly string[] assetRoles = { "Scene", "Player", "Npc",
            "Chest", "Loot", "Projectile", "MeleeCue", "HealthBar", "DamageText", "Hit", "Telegraph",
            "SwingSound", "HitSound", "CriticalSound" };
        private static int generation;
        public static bool IsReady { get; private set; }
        public static CfgPlayerCombatRow Player => GameTables.CfgPlayerCombat.GetById(1);

        /// <summary>
        /// 取得启动门禁已经验证的唯一显示配置。
        /// </summary>
        public static CfgDisplayRow Display => GameTables.CfgDisplay.GetById(1);

        /// <summary>
        /// 装载内容包内的全部表，再执行跨表业务校验，失败时禁止创建玩家会话。
        /// </summary>
        /// <param name="ct">应用初始化取消令牌。</param>
        /// <returns>整套项目内容是否通过校验。</returns>
        public static async UniTask<bool> LoadAsync(CancellationToken ct)
        {
            IsReady = false;
            int current = ++generation;
            await GameTables.LoadAsync(GameEntry.Get<IResourceModule>(), ct);
            ct.ThrowIfCancellationRequested();
            if (!Validate(out string error))
            {
                GameEntry.Get<ILogModule>()?.Error("ProjectConfig", error);
                return false;
            }
            IsReady = true;
            GameEntry.ApplicationScope.Defer(() => { if (current == generation) IsReady = false; });
            return true;
        }

        /// <summary>
        /// 校验完整表集和运行所需的引用、固定角色与出生位置。
        /// </summary>
        /// <param name="error">首个配置错误说明。</param>
        /// <returns>配置可用于创建玩法时为真。</returns>
        public static bool Validate(out string error)
        {
            if (!CombatContentConfig.Validate(out error) || !ProjectRulesConfig.Validate(out error) ||
                !CombatEnemySkillConfig.Validate(out error)) return false;
            CfgDisplayRow display = GameTables.CfgDisplay?.GetById(1);
            if (display == null || GameTables.CfgDisplay.GetAll().Count != 1 || display.TargetFrameRate < 1 || display.TargetFrameRate > 240)
            { error = "显示配置缺失、不是唯一规则或目标帧率超出 1 到 240。"; return false; }
            error = "项目资源绑定或玩家战斗表缺失。";
            if (GameTables.CfgAssets == null || GameTables.CfgPlayerCombat == null || Player == null ||
                GameTables.CfgPlayerCombat.GetAll().Count != 1) return false;
            foreach (string role in assetRoles)
                if (string.IsNullOrWhiteSpace(GameTables.CfgAssets.GetById(role)?.Address))
                { error = "缺少项目资源绑定：" + role; return false; }
            CfgRunPersistenceRow persistence = GameTables.CfgRunPersistence?.GetById(1);
            if (persistence == null || GameTables.CfgRunPersistence.GetAll().Count != 1 ||
                string.IsNullOrWhiteSpace(persistence.ConfigVersion) || persistence.CheckpointTicks <= 0 ||
                persistence.MaxPendingTicks <= 0 || persistence.MaxStateBytes <= 0)
            { error = "局次存档预算或配置版本无效。"; return false; }
            var player = Player;
            if (!Positive(player.Radius) || !Positive(player.ScaleX) || !Positive(player.ScaleY) || !Positive(player.ScaleZ) ||
                double.IsNaN(player.SpawnX) || double.IsInfinity(player.SpawnX) || double.IsNaN(player.SpawnZ) ||
                double.IsInfinity(player.SpawnZ) || GameTables.CfgCombatSkills.GetById(player.SkillID) == null)
            { error = "玩家战斗属性或基础技能引用无效。"; return false; }
            if (GameTables.CfgCombatSkills.GetById(player.SkillID).SkillType != (int)CombatSkillType.BasicAttack ||
                player.SkillIDs == null || player.SkillIDs.Length == 0)
            { error = "玩家基础攻击类型或普通技能列表无效。"; return false; }
            var playerSkills = new System.Collections.Generic.HashSet<int>();
            foreach (int skillId in player.SkillIDs)
            {
                CfgCombatSkillsRow skill = GameTables.CfgCombatSkills.GetById(skillId);
                if (skill == null || skill.SkillType != (int)CombatSkillType.Normal || !playerSkills.Add(skillId))
                { error = "玩家普通技能列表存在重复、缺失或类型不符：" + skillId; return false; }
            }
            foreach (CfgItemDefinitionsRow item in GameTables.CfgItemDefinitions.GetAll())
            {
                if (item.Type != (int)ProjectItemType.Weapon)
                {
                    if (item.WeaponClass != -1 || item.BasicSkillID != 0)
                    { error = "非武器物品不得配置武器类别或普攻：" + item.ID; return false; }
                    continue;
                }
                CfgCombatSkillsRow basic = GameTables.CfgCombatSkills.GetById(item.BasicSkillID);
                if (item.WeaponClass < 0 || item.WeaponClass > 2 || basic == null ||
                    basic.SkillType != (int)CombatSkillType.BasicAttack || !basic.OccupiesAction ||
                    basic.TargetKind != (int)CombatSkillTargetKind.Enemy || basic.CastMode != (int)CombatSkillCastMode.Automatic)
                { error = "武器类别或普攻映射无效：" + item.ID; return false; }
            }
            if (!LevelContentConfig.Validate(out error) || !ProjectHomeShopConfig.Validate(out error) ||
                !ProjectCombatFeedbackConfig.Validate(out error) || !ProjectGuideConfig.Validate(out error)) return false;
            error = null;
            return true;
        }

        /// <summary>
        /// 取得已经在启动门禁中验证的资源角色地址。
        /// </summary>
        /// <param name="role">固定表现资源角色。</param>
        /// <returns>资源逻辑地址；配置未装载或角色缺席时为空。</returns>
        public static string Asset(string role) => GameTables.CfgAssets?.GetById(role)?.Address;

        /// <summary>
        /// 判断配置数值是否为有限正数。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>数值可用时为真。</returns>
        private static bool Positive(double value) => value > 0 && value <= float.MaxValue && !double.IsInfinity(value);
    }
}
