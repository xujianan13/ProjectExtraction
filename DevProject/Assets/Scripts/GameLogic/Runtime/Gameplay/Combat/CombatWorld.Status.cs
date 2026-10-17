using System.Collections.Generic;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 战斗 Buff 实例的只读状态副本。
    /// </summary>
    public readonly struct CombatBuffSnapshot
    {
        public readonly int InstanceId;
        public readonly int ActorId;
        public readonly int SourceActorId;
        public readonly int RoomId;
        public readonly int DefinitionId;
        public readonly int Stacks;
        public readonly long RemainingTicks;
        public readonly int DurationTicks;
        public readonly CombatBuffLifetime Lifetime;

        /// <summary>
        /// 构造与战斗世界状态脱离的 Buff 快照。
        /// </summary>
        /// <param name="instanceId">Buff 实体编号。</param>
        /// <param name="actorId">持有者的稳定角色编号。</param>
        /// <param name="sourceActorId">来源的稳定角色编号。</param>
        /// <param name="roomId">持有者当前房间编号。</param>
        /// <param name="definitionId">Buff 定义编号。</param>
        /// <param name="stacks">当前层数。</param>
        /// <param name="remainingTicks">按持有者逻辑钟计算的剩余帧数。</param>
        /// <param name="durationTicks">Buff 定义时长帧数。</param>
        /// <param name="lifetime">状态显式生命周期。</param>
        public CombatBuffSnapshot(int instanceId, int actorId, int sourceActorId, int roomId, int definitionId, int stacks,
            long remainingTicks, int durationTicks, CombatBuffLifetime lifetime = CombatBuffLifetime.Timed)
        {
            InstanceId = instanceId;
            ActorId = actorId;
            SourceActorId = sourceActorId;
            RoomId = roomId;
            DefinitionId = definitionId;
            Stacks = stacks;
            RemainingTicks = remainingTicks;
            DurationTicks = durationTicks;
            Lifetime = lifetime;
        }
    }

    /// <summary>
    /// 将当前房间中的真实 Buff 状态投影为只读快照。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 导出当前房间存活角色尚未到期的 Buff，并清空复用列表。
        /// </summary>
        /// <param name="output">接收状态副本的复用列表。</param>
        public void GetBuffsSnapshot(List<CombatBuffSnapshot> output)
        {
            if (output == null) return;
            output.Clear();
            foreach (var pair in buffs)
            {
                var buff = pair.Value;
                if (buff.Removed || !actors.TryGetValue(buff.Owner, out var owner) || owner.DeadPending ||
                    owner.Spec.RoomId != activeRoomId || !catalog.Buffs.TryGetValue(buff.BuffId, out var definition)) continue;
                var remaining = buff.ExpireTick - GetBuffClock(owner);
                if (remaining <= 0) continue;
                output.Add(new CombatBuffSnapshot(pair.Key, owner.Spec.ActorId, buff.SourceActorId, owner.Spec.RoomId,
                    buff.BuffId, buff.Stacks, remaining, definition.DurationTicks, definition.Lifetime));
            }
            output.Sort((left, right) => left.InstanceId.CompareTo(right.InstanceId));
        }
    }
}
