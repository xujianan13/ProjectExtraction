using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Project
{
    /// <summary>
    /// 战斗事件飘字和持续状态的配置访问与启动校验。
    /// </summary>
    public static class ProjectCombatFeedbackConfig
    {
        /// <summary>
        /// 优先采用具体效果样式，再回退到该事件的通用样式。
        /// </summary>
        /// <param name="fact">已确认的战斗事件。</param>
        /// <returns>匹配的显示配置；该事件无需显示时为空。</returns>
        public static CfgCombatFeedbackRow Event(CombatEvent fact)
        {
            if (fact.Kind != CombatEventKind.Damage && fact.Kind != CombatEventKind.Dodged && fact.Kind != CombatEventKind.Heal) return null;
            CfgCombatFeedbackRow exact = Find(fact.Kind, fact.DefinitionId, fact.IsCritical);
            if (exact != null) return exact;
            if (fact.IsCritical && fact.DefinitionId != 0)
            {
                exact = Find(fact.Kind, fact.DefinitionId, false);
                if (exact != null) return exact;
            }
            return Find(fact.Kind, 0, fact.IsCritical) ?? Find(fact.Kind, 0, false);
        }

        /// <summary>
        /// 按真实 Buff 定义查找持续状态名称与颜色。
        /// </summary>
        /// <param name="definitionId">Buff 配置标识。</param>
        /// <returns>状态配置；没有显示定义时为空。</returns>
        public static CfgCombatFeedbackRow Status(int definitionId) => Find(CombatEventKind.BuffApplied, definitionId, false);

        /// <summary>
        /// 将配置中的颜色通道转为文字颜色。
        /// </summary>
        /// <param name="row">已经通过启动校验的显示配置。</param>
        /// <returns>不透明的文字颜色。</returns>
        public static Color Color(CfgCombatFeedbackRow row) => new((float)row.ColorR, (float)row.ColorG, (float)row.ColorB, 1f);

        /// <summary>
        /// 校验事件样式、Buff 覆盖和引用，缺失配置时阻止进入玩法。
        /// </summary>
        /// <param name="error">首个配置错误；成功时为空。</param>
        /// <returns>配置完整且可用时为真。</returns>
        public static bool Validate(out string error)
        {
            error = "战斗反馈配置未全部加载。";
            if (GameTables.CfgCombatFeedback == null || GameTables.CfgCombatEffects == null || GameTables.CfgCombatBuffs == null) return false;
            var keys = new HashSet<string>();
            foreach (CfgCombatFeedbackRow row in GameTables.CfgCombatFeedback.GetAll())
            {
                var kind = (CombatEventKind)row.EventKind;
                bool status = kind == CombatEventKind.BuffApplied;
                if (string.IsNullOrWhiteSpace(row.ID) || string.IsNullOrWhiteSpace(row.Text) || row.DefinitionID < 0 ||
                    kind != CombatEventKind.Damage && kind != CombatEventKind.Dodged && kind != CombatEventKind.Heal && !status ||
                    row.Critical && kind != CombatEventKind.Damage || !Channel(row.ColorR) || !Channel(row.ColorG) || !Channel(row.ColorB) ||
                    !keys.Add(row.EventKind + ":" + row.DefinitionID + ":" + row.Critical) ||
                    kind != CombatEventKind.Damage && (row.ShowHit || row.ShowFlash) ||
                    (kind == CombatEventKind.Damage || kind == CombatEventKind.Heal) && !row.Text.Contains("{value}"))
                { error = "战斗反馈字段或匹配键无效：" + row.ID; return false; }
                if (status && (row.DefinitionID == 0 || GameTables.CfgCombatBuffs.GetById(row.DefinitionID) == null) ||
                    !status && row.DefinitionID > 0 && !HasEffect(kind, row.DefinitionID))
                { error = "战斗反馈定义引用无效：" + row.ID; return false; }
            }
            if (Find(CombatEventKind.Damage, 0, false) == null || Find(CombatEventKind.Damage, 0, true) == null ||
                Find(CombatEventKind.Dodged, 0, false) == null || Find(CombatEventKind.Heal, 0, false) == null)
            { error = "战斗反馈缺少伤害、暴击、闪避或治疗通用样式。"; return false; }
            foreach (CfgCombatBuffsRow buff in GameTables.CfgCombatBuffs.GetAll())
                if (Status(buff.ID) == null)
                { error = "Buff 缺少状态显示配置：" + buff.ID; return false; }
            error = null;
            return true;
        }

        /// <summary>
        /// 按事件、定义和暴击标识取得唯一配置。
        /// </summary>
        /// <param name="kind">事件类型。</param>
        /// <param name="definitionId">效果或 Buff 定义标识。</param>
        /// <param name="critical">是否匹配暴击专用样式。</param>
        /// <returns>匹配行；没有匹配时为空。</returns>
        private static CfgCombatFeedbackRow Find(CombatEventKind kind, int definitionId, bool critical)
        {
            if (GameTables.CfgCombatFeedback == null) return null;
            foreach (CfgCombatFeedbackRow row in GameTables.CfgCombatFeedback.GetAll())
                if (row.EventKind == (int)kind && row.DefinitionID == definitionId && row.Critical == critical) return row;
            return null;
        }

        /// <summary>
        /// 校验具体显示定义可以由同类型的效果产生。
        /// </summary>
        /// <param name="kind">事件类型。</param>
        /// <param name="definitionId">产生反馈的效果标识。</param>
        /// <returns>能够产生对应事件时为真。</returns>
        private static bool HasEffect(CombatEventKind kind, int definitionId)
        {
            foreach (CfgCombatEffectsRow effect in GameTables.CfgCombatEffects.GetAll())
                if (effect.DefinitionID == definitionId && (kind == CombatEventKind.Heal ? effect.Kind == (int)CombatEffectKind.Heal :
                    effect.Kind == (int)CombatEffectKind.Damage)) return true;
            return false;
        }

        /// <summary>
        /// 判断颜色通道是否是零到一之间的有限数值。
        /// </summary>
        /// <param name="value">待检查的通道值。</param>
        /// <returns>通道有效时为真。</returns>
        private static bool Channel(double value) => !double.IsNaN(value) && value >= 0d && value <= 1d;
    }
}
