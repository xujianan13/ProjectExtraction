using System;
using System.Collections.Generic;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 独立玩家检查点，不包含地图、位置、输入、施法、飞弹、位移或其他角色。
    /// 仅保留玩家自身施加的 Buff，临时敌方等外部来源的状态不进入检查点。
    /// 死亡检查点仅保留已处理死亡状态，待处理死亡选择由玩法宿主另行保存。
    /// </summary>
    [Serializable]
    public sealed class CombatPlayerCheckpoint
    {
        public int Version = 1;
        public CombatActorSpec Spec;
        public CombatActorSpec BaseSpec;
        public float Health;
        public float[] PermanentBonuses;
        public int SkillCursor;
        public int DeathIdWatermark;
        public List<int> EnabledPersistentSkills = new();
        public List<CombatSkillCooldownState> SkillCooldowns = new();
        public List<CombatPlayerAugmentGroupCheckpoint> AugmentGroups = new();
        public List<CombatPlayerBuffCheckpoint> Buffs = new();
    }

    /// <summary>
    /// 一组独立来源词条及其按剩余逻辑帧表达的冷却，不携带原世界时钟。
    /// </summary>
    [Serializable]
    public sealed class CombatPlayerAugmentGroupCheckpoint
    {
        public string GroupId;
        public List<CombatAugmentDefinition> Definitions = new();
        public List<CombatPlayerAugmentCooldownCheckpoint> Cooldowns = new();
    }

    /// <summary>
    /// 单项词条尚需等待的逻辑帧数。
    /// </summary>
    [Serializable]
    public sealed class CombatPlayerAugmentCooldownCheckpoint
    {
        public string AugmentId;
        public long RemainingTicks;
    }

    /// <summary>
    /// 玩家自身来源 Buff 的独立状态，恢复不执行首次应用效果。
    /// 常驻 Buff 的剩余时长使用最大整数，周期等待量始终按相对帧数保存。
    /// </summary>
    [Serializable]
    public sealed class CombatPlayerBuffCheckpoint
    {
        public int BuffId;
        public int Stacks;
        public long RemainingTicks;
        public long NextTickIn;
        public float RangeDelta;
        public float RangeMultiplier;
        public bool PassiveDerived;
        public CombatOffenseState Offense;
    }
}
