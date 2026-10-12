using System.Collections.Generic;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 配置软弱点的引用与数值校验。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 校验可选的软弱点数组，合法缺席表示无额外倍率。
        /// </summary>
        /// <param name="vulnerabilities">可为空的配置软弱点。</param>
        /// <returns>伤害效果存在、倍率有限且身份不重复时为真。</returns>
        private bool ValidDamageVulnerabilities(CombatDamageVulnerability[] vulnerabilities)
        {
            if (vulnerabilities == null) return true;
            var ids = new HashSet<int>();
            foreach (var vulnerability in vulnerabilities)
                if (vulnerability.EffectId <= 0 || !ids.Add(vulnerability.EffectId) || !IsFinite(vulnerability.Multiplier) ||
                    vulnerability.Multiplier <= 0f || !HasDamageEffect(vulnerability.EffectId)) return false;
            return true;
        }

        /// <summary>
        /// 查询装配层登记或战斗目录已持有的伤害效果。
        /// </summary>
        /// <param name="effectId">配置伤害效果编号。</param>
        /// <returns>目录中存在该伤害效果时为真。</returns>
        private bool HasDamageEffect(int effectId)
        {
            if (catalog.DamageEffectIds.Contains(effectId)) return true;
            foreach (var skill in catalog.Skills.Values)
                foreach (var node in skill.Nodes)
                    if (ContainsDamageEffect(node.Effects, effectId)) return true;
            foreach (var projectile in catalog.Projectiles.Values)
                if (ContainsDamageEffect(projectile.OnHit, effectId) || ContainsDamageEffect(projectile.OnArrive, effectId) ||
                    ContainsDamageEffect(projectile.OnExpire, effectId)) return true;
            foreach (var buff in catalog.Buffs.Values)
                if (buff.BuffId == effectId && buff.PeriodicDamagePerStack > 0f || ContainsDamageEffect(buff.OnApply, effectId) ||
                    ContainsDamageEffect(buff.OnTick, effectId) || ContainsDamageEffect(buff.OnExpire, effectId)) return true;
            return false;
        }

        /// <summary>
        /// 在一组目录效果中查找指定伤害编号。
        /// </summary>
        /// <param name="effects">已经装配的效果集合。</param>
        /// <param name="effectId">配置伤害效果编号。</param>
        /// <returns>存在匹配的伤害效果时为真。</returns>
        private static bool ContainsDamageEffect(List<CombatEffectDefinition> effects, int effectId)
        {
            foreach (var effect in effects)
                if (effect.Kind == CombatEffectKind.Damage && effect.DefinitionId == effectId) return true;
            return false;
        }
    }
}
