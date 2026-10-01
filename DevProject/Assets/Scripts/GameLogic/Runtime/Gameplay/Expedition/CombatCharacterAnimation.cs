using System;
using GameFramework;
using GameLogic.Gameplay.Combat;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 将战斗快照映射到通用动画播放器，仅控制表现状态和朝向。
    /// </summary>
    public sealed class CombatCharacterAnimation : IDisposable
    {
        public const int HitDurationTicks = 11;
        public const int DeathDurationTicks = 36;
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
        public void Sample(in CombatAnimationSnapshot snapshot)
        {
            string state;
            string path;
            float phase;
            bool attack = IsAttackCue(snapshot.CueId) && snapshot.CueElapsedTicks < snapshot.CueDurationTicks;
            if (snapshot.Dead)
            {
                state = "Death";
                path = "Base Layer.Death";
                phase = Mathf.Clamp01(snapshot.DeathElapsedTicks / (float)DeathDurationTicks);
            }
            else if (snapshot.HitElapsedTicks >= 0 && snapshot.HitElapsedTicks < HitDurationTicks)
            {
                state = "Hit";
                path = "Base Layer.Hit";
                phase = snapshot.HitElapsedTicks / (float)HitDurationTicks;
            }
            else if (attack)
            {
                state = "Attack";
                path = "Base Layer.Attack";
                phase = snapshot.CueElapsedTicks / (float)snapshot.CueDurationTicks;
            }
            else if (snapshot.Velocity.sqrMagnitude > 0.0001f)
            {
                state = "Move";
                path = "Base Layer.Move";
                phase = Mathf.Repeat((float)(snapshot.ClockTick * CombatWorld.TickSeconds / 0.6), 1f);
            }
            else
            {
                state = "Idle";
                path = "Base Layer.Idle";
                phase = Mathf.Repeat((float)(snapshot.ClockTick * CombatWorld.TickSeconds), 1f);
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

        /// <summary>
        /// 匹配原型技能配置中的动作提示，其余 Cue 可留给其他表现适配器。
        /// </summary>
        /// <param name="cueId">战斗表现提示编号。</param>
        /// <returns>对应当前攻击动作时为真。</returns>
        private static bool IsAttackCue(int cueId) => cueId == 1 || cueId == 20 || cueId == 30;
    }
}
