using System;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Tables;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 将战斗快照映射到通用动画播放器，仅控制表现状态和朝向。
    /// </summary>
    public sealed class CombatCharacterAnimation : IDisposable
    {
        /// <summary>
        /// 受击表现持续的逻辑帧数。
        /// </summary>
        public static int HitDurationTicks => GameTables.CfgCombatAnimationSettings.GetById(1).HitTicks;

        /// <summary>
        /// 死亡动作持续的逻辑帧数。
        /// </summary>
        public static int DeathDurationTicks => GameTables.CfgCombatAnimationSettings.GetById(1).DeathTicks;

        /// <summary>
        /// 死亡动作后的停留秒数。
        /// </summary>
        public static float DeathHoldSeconds => (float)GameTables.CfgCombatAnimationSettings.GetById(1).DeathHoldSeconds;

        private readonly Transform root;
        private readonly AnimatorPlayback playback;
        private string failedState;

        /// <summary>
        /// 当前采样的动画状态名。
        /// </summary>
        public string CurrentState { get; private set; }

        /// <summary>
        /// 创建单个角色的战斗动画适配器。
        /// </summary>
        /// <param name="model">由场景拥有的角色模型实例。</param>
        public CombatCharacterAnimation(GameObject model)
        {
            root = model.transform;
            Animator animator = model.GetComponent<Animator>();
            if (animator != null) animator.applyRootMotion = false;
            playback = new AnimatorPlayback(animator);
        }

        /// <summary>
        /// 根据角色时钟和战斗事实采样动作，相同快照重复采样不会推进时间。
        /// </summary>
        /// <param name="snapshot">由 ECS 导出的角色表现快照。</param>
        /// <param name="deathTailSeconds">模拟结束后仅推进倒下动作的额外秒数。</param>
        public void Sample(in CombatAnimationSnapshot snapshot, float deathTailSeconds = 0f)
        {
            CfgCombatAnimationSettingsRow settings = GameTables.CfgCombatAnimationSettings.GetById(1);
            CfgCombatAnimationCuesRow cue = GameTables.CfgCombatAnimationCues.GetById(snapshot.CueId);
            string state;
            string path;
            float phase;
            bool attack = cue != null && cue.IsAttack && snapshot.CueElapsedTicks < snapshot.CueDurationTicks;
            if (snapshot.Dead)
            {
                state = "Death";
                path = settings.DeathState;
                phase = Mathf.Clamp01((snapshot.DeathElapsedTicks * CombatWorld.TickSeconds + deathTailSeconds) /
                    (settings.DeathTicks * CombatWorld.TickSeconds));
            }
            else if (snapshot.HitElapsedTicks >= 0 && snapshot.HitElapsedTicks < settings.HitTicks)
            {
                state = "Hit";
                path = settings.HitState;
                phase = snapshot.HitElapsedTicks / (float)settings.HitTicks;
            }
            else if (attack)
            {
                state = cue.StateName;
                path = cue.StatePath;
                phase = snapshot.CueElapsedTicks / (float)snapshot.CueDurationTicks;
            }
            else if (snapshot.Velocity.sqrMagnitude > 0.0001f)
            {
                state = "Move";
                path = settings.MoveState;
                phase = Mathf.Repeat((float)(snapshot.ClockTick * CombatWorld.TickSeconds / settings.MoveSeconds), 1f);
            }
            else
            {
                state = "Idle";
                path = settings.IdleState;
                phase = Mathf.Repeat((float)(snapshot.ClockTick * CombatWorld.TickSeconds / settings.IdleSeconds), 1f);
            }
            Vector3 facing = attack && !snapshot.Dead ? snapshot.CueDirection : snapshot.Forward;
            facing.y = 0f;
            if (facing.sqrMagnitude > 0.0001f) root.rotation = Quaternion.LookRotation(facing);
            CurrentState = state;
            if (playback.TrySample(path, phase)) failedState = null;
            else if (failedState != state)
            {
                failedState = state;
                GameEntry.Get<ILogModule>()?.Error("Animation", "角色动画无法采样：" + state);
            }
        }

        /// <summary>
        /// 释放动画控制权，模型和资源仍由场景负责销毁。
        /// </summary>
        public void Dispose() => playback.Dispose();
    }
}
