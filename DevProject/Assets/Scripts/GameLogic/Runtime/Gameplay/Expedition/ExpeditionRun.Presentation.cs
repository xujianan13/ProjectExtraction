using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 战斗结束后的有限表现时钟，不改变终局、伤害或奖励数据。
    /// </summary>
    public sealed partial class ExpeditionRun
    {
        public static float DeathPresentationDuration => CombatCharacterAnimation.DeathDurationTicks * CombatWorld.TickSeconds +
            (float)GameLogic.Tables.GameTables.CfgCombatAnimationSettings.GetById(1).DeathHoldSeconds;
        private bool deathPresentationStarted;
        private float deathPresentationElapsed;
        public bool IsPresentingDeath => deathPresentationStarted && !disposed && deathPresentationElapsed < DeathPresentationDuration;
        public float DeathPresentationElapsed => deathPresentationElapsed;
        public double PresentationSeconds => World.TickIndex * (double)CombatWorld.TickSeconds + deathPresentationElapsed;
        public bool FeedbackPaused => paused || GameEntry.IsInBackground || ProjectServices.Coordinator?.IsPaused == true ||
            IsChoosingRogue || (!IsRunning && !IsPresentingDeath);

        /// <summary>
        /// 仅对真实死亡且已冻结的角色开启倒下与短暂停留；不重新开放任何输入。
        /// </summary>
        public void BeginDeathPresentation()
        {
            if (disposed || !stopped || deathPresentationStarted || !World.TryGetActor(Player, out var actor) || !actor.Dead) return;
            deathPresentationStarted = true;
            deathPresentationElapsed = 0f;
        }

        /// <summary>
        /// 推进终局表现，后台暂停时不累计时间，恢复时不追赶大帧。
        /// </summary>
        /// <param name="deltaTime">本帧经过秒数。</param>
        private void AdvanceDeathPresentation(float deltaTime)
        {
            if (!IsPresentingDeath || FeedbackPaused || deltaTime <= 0f || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            deathPresentationElapsed = Mathf.Min(DeathPresentationDuration, deathPresentationElapsed + Mathf.Min(deltaTime, 0.1f));
        }
    }
}
