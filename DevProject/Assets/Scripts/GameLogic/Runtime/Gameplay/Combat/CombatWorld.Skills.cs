using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 供界面读取的技能运行状态。
    /// </summary>
    public sealed class CombatSkillSnapshot
    {
        public int SkillId;
        public int CooldownTicks;
        public bool IsCasting;
        public bool IsPersistentActive;
        public CombatSkillEffectClass EffectClass;
        public CombatSkillType SkillType;
    }

    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 校验角色技能列表，供装备事务在提交前检查。
        /// </summary>
        /// <param name="actor">角色引用。</param>
        /// <param name="basicSkillId">新普通攻击编号，零表示没有普通攻击。</param>
        /// <param name="skillIds">包含普通攻击在内的完整有序列表。</param>
        /// <returns>角色和列表均有效时为真。</returns>
        public bool CanSetSkillLoadout(CombatEntityRef actor, int basicSkillId, int[] skillIds) =>
            !faulted && IsValid(actor) && actors.ContainsKey(actor.Id) && basicSkillId >= 0 &&
            ValidSkillLoadout(skillIds) && skillIds != null && (basicSkillId == 0 || Array.IndexOf(skillIds, basicSkillId) >= 0);

        /// <summary>
        /// 替换技能入口，保留生命、状态、在途动作与各技能冷却。
        /// </summary>
        /// <param name="actor">角色引用。</param>
        /// <param name="basicSkillId">新普通攻击编号。</param>
        /// <param name="skillIds">新的完整有序技能列表。</param>
        /// <returns>列表通过预检并提交时为真。</returns>
        public bool TrySetSkillLoadout(CombatEntityRef actor, int basicSkillId, int[] skillIds)
        {
            if (!CanSetSkillLoadout(actor, basicSkillId, skillIds)) return false;
            var state = actors[actor.Id];
            var previous = GetSkillIds(state.Spec);
            var nextSkill = previous.Length > 0 ? previous[state.SkillCursor % previous.Length] : 0;
            if (basicSkillId > 0 && state.Spec.BasicSkillId != basicSkillId && state.SkillCooldowns.TryGetValue(state.Spec.BasicSkillId, out var remaining))
                state.SkillCooldowns[basicSkillId] = Mathf.Max(remaining, state.SkillCooldowns.TryGetValue(basicSkillId, out var prior) ? prior : 0);
            state.Spec.BasicSkillId = basicSkillId;
            state.Spec.SkillIds = (int[])skillIds.Clone();
            state.BaseSpec.BasicSkillId = basicSkillId;
            state.BaseSpec.SkillIds = (int[])skillIds.Clone();
            state.SkillCursor = Mathf.Max(0, Array.IndexOf(skillIds, nextSkill));
            var removed = new List<int>();
            foreach (var id in state.EnabledPersistentSkills) if (Array.IndexOf(skillIds, id) < 0) removed.Add(id);
            foreach (var id in removed)
            {
                state.EnabledPersistentSkills.Remove(id);
                var buffId = catalog.Skills[id].MaintainBuffId;
                foreach (var key in OrderedEntityIds(buffs.Keys))
                    if (buffs[key].Owner == actor.Id && buffs[key].BuffId == buffId) RemoveBuff(key, false);
                foreach (var key in OrderedEntityIds(projectiles.Keys))
                    if (projectiles[key].Source.Equals(actor) && projectiles[key].ControllerSkillId == id)
                    { projectiles.Remove(key); Release(key); }
            }
            return true;
        }

        /// <summary>
        /// 读取指定角色当前列表中各技能的运行状态。
        /// </summary>
        /// <param name="actor">角色引用。</param>
        /// <param name="results">接收独立状态的复用列表。</param>
        public void GetSkillsSnapshot(CombatEntityRef actor, List<CombatSkillSnapshot> results)
        {
            results.Clear();
            if (!IsValid(actor) || !actors.TryGetValue(actor.Id, out var state)) return;
            foreach (var id in GetSkillIds(state.Spec))
            {
                var definition = catalog.Skills[id];
                var casting = false;
                foreach (var cast in casts.Values) if (cast.Owner == actor.Id && cast.SkillId == id) casting = true;
                results.Add(new CombatSkillSnapshot { SkillId = id, CooldownTicks = state.SkillCooldowns.TryGetValue(id, out var cd) ? cd : 0,
                    IsCasting = casting, IsPersistentActive = state.EnabledPersistentSkills.Contains(id),
                    EffectClass = definition.EffectClass, SkillType = definition.SkillType });
            }
        }

        /// <summary>
        /// 检查显式列表是否引用有效且唯一的技能。
        /// </summary>
        /// <param name="skillIds">可选的有序列表，空引用允许使用角色原有入口。</param>
        /// <returns>列表可供调度时为真。</returns>
        private bool ValidSkillLoadout(int[] skillIds)
        {
            if (skillIds == null) return true;
            if (skillIds.Length > 64) return false;
            var seen = new HashSet<int>();
            foreach (var id in skillIds) if (!seen.Add(id) || !catalog.Skills.TryGetValue(id, out var skill) || !ValidSavedSkill(skill)) return false;
            return true;
        }

        /// <summary>
        /// 取得显式列表或从角色既有技能入口构造顺序列表。
        /// </summary>
        /// <param name="spec">角色配置。</param>
        /// <returns>独立的技能列表快照。</returns>
        private static int[] GetSkillIds(CombatActorSpec spec)
        {
            if (spec.SkillIds != null) return (int[])spec.SkillIds.Clone();
            if (!spec.IsPlayer && spec.EnemySkills != null && spec.EnemySkills.Length > 0)
                return Array.ConvertAll(spec.EnemySkills, item => item.SkillId);
            return spec.BasicSkillId > 0 ? new[] { spec.BasicSkillId } : Array.Empty<int>();
        }

        /// <summary>
        /// 求本次技能准入与实际范围使用的索敌距离。
        /// </summary>
        /// <param name="source">施法者。</param>
        /// <param name="skillId">技能编号。</param>
        /// <param name="definition">技能定义。</param>
        /// <returns>已经叠加属性和词条的索敌距离。</returns>
        private float GetSkillStartRange(ActorComponent source, int skillId, CombatSkillDefinition definition)
        {
            GetSkillModifiers(source, skillId, out _, out var multiplier, out _);
            var delta = source.Spec.IsPlayer && skillId == source.Spec.BasicSkillId ? source.Spec.AttackRange - catalog.InitialAttackRange : 0f;
            var range = Mathf.Min(AttributeCeiling, Mathf.Max(0f, definition.StartRange +
                (definition.AttackRangeSource ? delta * definition.AttackRangeMultiplier : 0f) + source.RangeBonus) * multiplier);
            if (skillId == source.Spec.BasicSkillId && (source.Spec.IsPlayer || source.Spec.EnemySkills == null || source.Spec.EnemySkills.Length == 0))
                range = Mathf.Min(range, (source.Spec.AttackRange + source.RangeBonus) * multiplier);
            return range;
        }

        /// <summary>
        /// 检查角色当前是否持有指定状态。
        /// </summary>
        /// <param name="owner">持有者槽位。</param>
        /// <param name="buffId">状态定义编号。</param>
        /// <returns>存在有效状态时为真。</returns>
        private bool HasBuff(int owner, int buffId)
        {
            foreach (var buff in buffs.Values) if (!buff.Removed && buff.Owner == owner && buff.BuffId == buffId) return true;
            return false;
        }

        /// <summary>
        /// 按敌人技能数组记录检查单项生命和距离条件。
        /// </summary>
        /// <param name="source">敌人状态。</param>
        /// <param name="skillId">技能编号。</param>
        /// <param name="distance">与目标的距离。</param>
        /// <returns>不存在额外条件或满足条件时为真。</returns>
        private bool EnemyLoadoutCondition(ActorComponent source, int skillId, float distance)
        {
            if (source.Spec.EnemySkills == null) return true;
            foreach (var skill in source.Spec.EnemySkills)
                if (skill.SkillId == skillId) return EnemySkillAvailable(source, skill, source.Health / source.Spec.MaxHealth, distance);
            return true;
        }

        /// <summary>
        /// 恢复已启用常驻技能的控制状态，不执行首次施放效果。
        /// </summary>
        /// <param name="ownerId">来源角色槽位。</param>
        /// <param name="skillId">常驻技能编号。</param>
        /// <param name="definition">常驻技能定义。</param>
        private void MaintainPersistentSkill(int ownerId, int skillId, CombatSkillDefinition definition)
        {
            if (definition.MaintainBuffId <= 0) return;
            var owner = actors[ownerId];
            if (!HasBuff(ownerId, definition.MaintainBuffId))
                ApplyBuff(Ref(ownerId), definition.MaintainBuffId, owner.Spec.ActorId, new CombatOffense(owner.Spec), 0f, 1f, false, false);
            foreach (var effect in catalog.Buffs[definition.MaintainBuffId].OnApply)
            {
                if (effect.Kind != CombatEffectKind.SpawnProjectile || catalog.Projectiles[effect.LinkedId].Motion != ProjectileMotionKind.Orbit) continue;
                var children = OrderedEntityIds(projectiles.Keys);
                children.RemoveAll(id => projectiles[id].Terminal || !projectiles[id].Source.Equals(Ref(ownerId)) ||
                    projectiles[id].ControllerSkillId != skillId || projectiles[id].ProjectileId != effect.LinkedId);
                for (var index = children.Count - 1; index >= effect.Count; index--)
                { projectiles.Remove(children[index]); Release(children[index]); }
                var initialAngle = children.Count > 0 ? projectiles[children[0]].OrbitAngle - catalog.Projectiles[effect.LinkedId].OrbitInitialAngle : 0f;
                for (var index = children.Count; index < effect.Count; index++)
                {
                    var angle = initialAngle + (effect.Count == 1 ? 0f : effect.SpreadDegrees >= 360f ?
                        effect.SpreadDegrees * index / effect.Count : effect.SpreadDegrees * (index / (effect.Count - 1f) - 0.5f));
                    TrySpawnProjectileInternal(Ref(ownerId), effect.LinkedId, default, owner.Position, owner.Forward,
                        null, false, new CombatOffense(owner.Spec), 0f, 1f, false, owner.Spec.ActorId, null, skillId, angle);
                }
            }
        }
    }
}
