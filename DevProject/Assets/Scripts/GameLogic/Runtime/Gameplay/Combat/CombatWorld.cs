using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 单局战斗的 ECS 状态与固定帧调度器。
    /// </summary>
    public sealed partial class CombatWorld
    {
        private sealed class EntitySlot
        {
            public int Generation = 1;
            public bool Alive;
        }

        private sealed class ActorComponent
        {
            public CombatActorSpec Spec;
            public CombatActorSpec BaseSpec;
            public readonly float[] PermanentBonuses = new float[8];
            public Vector3 Position;
            public Vector3 Forward = Vector3.forward;
            public Vector2 Move;
            public float Health;
            public float MoveBonus;
            public float RangeBonus;
            public readonly Dictionary<int, int> SkillCooldowns = new Dictionary<int, int>();
            public bool DeadPending;
            public bool DeathPublished;
            public int LastInstigatorActorId;
            public Vector3 Velocity;
            public Vector3 CueDirection;
            public int CueId;
            public int CueDurationTicks;
            public long CueClock = -1;
            public long HitClock = -1;
            public long DeathClock = -1;
        }

        private readonly struct CombatOffense
        {
            public readonly float Attack;
            public readonly float CriticalRating;
            public readonly float CriticalDamage;
            public readonly float DamageIncrease;

            /// <summary>
            /// 冻结一次效果来源的进攻属性。
            /// </summary>
            /// <param name="spec">已经校验的来源角色配置。</param>
            public CombatOffense(CombatActorSpec spec)
            {
                Attack = spec.Attack;
                CriticalRating = spec.CriticalRating;
                CriticalDamage = spec.CriticalDamage;
                DamageIncrease = spec.DamageIncrease;
            }
        }

        private sealed class CastComponent
        {
            public int Owner;
            public int SkillId;
            public long StartTick;
            public CombatEntityRef Target;
            public Vector3 TargetPoint;
            public Vector3 Forward;
            public int NextNode;
            public float RangeDelta;
            public bool UsesWorldClock;
            public CombatOffense Offense;
        }

        private sealed class DisplacementComponent
        {
            public int Owner;
            public Vector3 Direction;
            public float DistancePerTick;
            public int RemainingTicks;
            public long CreatedTick;
        }

        private sealed class ProjectileComponent
        {
            public int ProjectileId;
            public float RangeDelta;
            public int RoomId;
            public int InstigatorActorId;
            public CombatOffense Offense;
            public CombatEntityRef Source;
            public CombatEntityRef Target;
            public Vector3 LastTargetPoint;
            public Vector3 Position;
            public Vector3 Previous;
            public Vector3 Origin;
            public Vector3 EndPoint;
            public Vector3 Velocity;
            public Vector3 Direction;
            public int FlightTicks;
            public int ElapsedTicks;
            public long CreatedTick;
            public int Bounces;
            public float Distance;
            public int DistinctHits;
            public bool Terminal;
            public readonly Dictionary<CombatEntityRef, HitRecord> Hits = new Dictionary<CombatEntityRef, HitRecord>();
        }

        private struct HitRecord
        {
            public long Tick;
            public int Count;
        }

        private sealed class BuffComponent
        {
            public float RangeDelta;
            public int Owner;
            public int BuffId;
            public int SourceActorId;
            public CombatOffense Offense;
            public int Stacks;
            public long ExpireTick;
            public long NextTick;
            public bool Removed;
        }

        private struct EffectCommand
        {
            public CombatEffectDefinition Definition;
            public float RangeDelta;
            public int Source;
            public CombatEntityRef Target;
            public Vector3 Position;
            public Vector3 Direction;
            public int InstigatorActorId;
            public CombatOffense Offense;
            public long RootId;
            public long TriggerId;
            public int Depth;
        }

        private static int nextWorldId;
        private readonly int worldId;
        private readonly CombatCatalog catalog;
        private readonly System.Random random;
        private readonly List<EntitySlot> slots = new List<EntitySlot> { new EntitySlot() };
        private readonly Stack<int> freeIds = new Stack<int>();
        private readonly Dictionary<int, ActorComponent> actors = new Dictionary<int, ActorComponent>();
        private readonly Dictionary<int, CastComponent> casts = new Dictionary<int, CastComponent>();
        private readonly Dictionary<int, DisplacementComponent> displacements = new Dictionary<int, DisplacementComponent>();
        private readonly Dictionary<int, ProjectileComponent> projectiles = new Dictionary<int, ProjectileComponent>();
        private readonly Dictionary<int, BuffComponent> buffs = new Dictionary<int, BuffComponent>();
        private readonly Dictionary<int, Rect> roomBounds = new Dictionary<int, Rect>();
        private readonly Dictionary<int, List<Rect>> obstacles = new Dictionary<int, List<Rect>>();
        private readonly Dictionary<int, long> roomTicks = new Dictionary<int, long>();
        private readonly Queue<EffectCommand> effectQueue = new Queue<EffectCommand>();
        private readonly List<CombatEvent> events = new List<CombatEvent>();
        private readonly HashSet<int> clearedRooms = new HashSet<int>();
        private readonly List<int> ids = new List<int>();
        private double accumulator;
        private long nextRootId;
        private long nextTriggerId;
        private long activeRootId;
        private int activeDepth;
        private int remainingEffectBudget;
        private int nextDeathId;
        private int activeRoomId;
        private int playerId;
        private bool faulted;

        public const float TickSeconds = 1f / 60f;
        public long TickIndex { get; private set; }
        public int ActiveRoomId => activeRoomId;
        public bool Faulted => faulted;
        public bool IsFaulted => faulted;

        /// <summary>
        /// 创建单局战斗世界。
        /// </summary>
        /// <param name="catalog">战斗配置集合。</param>
        /// <param name="randomSeed">本局确定性战斗随机种子。</param>
        public CombatWorld(CombatCatalog catalog, int randomSeed = 0)
        {
            this.catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
            random = new System.Random(randomSeed);
            worldId = Interlocked.Increment(ref nextWorldId);
        }

        /// <summary>
        /// 设定地图内角色活动的 XZ 矩形边界。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="xzBounds">XZ 平面的矩形边界。</param>
        /// <returns>参数有效且设定成功则为真。</returns>
        public bool SetRoomBounds(int roomId, Rect xzBounds)
        {
            if (roomId <= 0 || !IsFinite(xzBounds.x) || !IsFinite(xzBounds.y) ||
                !IsFinite(xzBounds.width) || !IsFinite(xzBounds.height) ||
                xzBounds.width <= 0f || xzBounds.height <= 0f) return false;
            roomBounds[roomId] = xzBounds;
            return true;
        }

        /// <summary>
        /// 为地图增加矩形实体障碍。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="xzBounds">XZ 平面的障碍范围。</param>
        /// <returns>参数有效且增加成功则为真。</returns>
        public bool AddObstacle(int roomId, Rect xzBounds)
        {
            if (roomId <= 0 || !IsFinite(xzBounds.x) || !IsFinite(xzBounds.y) ||
                !IsFinite(xzBounds.width) || !IsFinite(xzBounds.height) ||
                xzBounds.width <= 0f || xzBounds.height <= 0f) return false;
            if (!obstacles.TryGetValue(roomId, out var roomObstacles))
            {
                roomObstacles = new List<Rect>();
                obstacles.Add(roomId, roomObstacles);
            }
            roomObstacles.Add(xzBounds);
            return true;
        }

        /// <summary>
        /// 在指定地图创建角色，不生成关卡内容。
        /// </summary>
        /// <param name="spec">角色初始数据。</param>
        /// <returns>有效实体引用；参数无效时返回默认值。</returns>
        public CombatEntityRef CreateActor(CombatActorSpec spec)
        {
            if (spec.ActorId <= 0 || spec.RoomId <= 0 || spec.Team == 0 || !IsFinite(spec.MaxHealth) ||
                !IsFinite(spec.Attack) || !IsFinite(spec.Defense) || !IsFinite(spec.CriticalRating) ||
                !IsFinite(spec.CriticalDamage) || !IsFinite(spec.DodgeChance) || !IsFinite(spec.DamageIncrease) ||
                !IsFinite(spec.DamageReduction) || !IsFinite(spec.MoveSpeed) || !IsFinite(spec.Radius) ||
                !IsFinite(spec.AttackRange) || !IsFinite(spec.AggroRange) || spec.MaxHealth <= 0f ||
                spec.Attack < 0f || spec.Defense < 0f || spec.CriticalRating < 0f || spec.CriticalDamage < 0f ||
                spec.DodgeChance < 0f || spec.DamageIncrease < 0f || spec.DamageReduction < 0f ||
                spec.MoveSpeed < 0f || spec.Radius <= 0f || spec.AttackRange < 0f || !IsFinite(spec.Position) ||
                !CanOccupy(spec.RoomId, spec.Position, spec.Radius, 0)) return default;
            if (spec.EnemySkills != null)
            {
                var skillIds = new HashSet<int>();
                foreach (var skill in spec.EnemySkills)
                    if (!skillIds.Add(skill.SkillId) || skill.SkillId <= 0 || skill.Weight <= 0 || !IsFinite(skill.MinHealthRatio) ||
                        !IsFinite(skill.MaxHealthRatio) || !IsFinite(skill.MinDistance) || !IsFinite(skill.MaxDistance) ||
                        skill.MinHealthRatio < 0f || skill.MaxHealthRatio > 1f || skill.MinHealthRatio > skill.MaxHealthRatio ||
                        skill.MinDistance < 0f || skill.MinDistance > skill.MaxDistance ||
                        !catalog.Skills.ContainsKey(skill.SkillId)) return default;
            }
            spec.CriticalDamage = spec.CriticalDamage == 0f ? 1.5f : Mathf.Max(1f, spec.CriticalDamage);
            spec.DodgeChance = Mathf.Min(0.75f, spec.DodgeChance);
            spec.DamageReduction = Mathf.Min(0.75f, spec.DamageReduction);
            foreach (var actor in actors.Values)
                if (actor.Spec.ActorId == spec.ActorId) return default;
            var entity = Allocate();
            if (spec.EnemySkills != null) spec.EnemySkills = (CombatEnemySkillDefinition[])spec.EnemySkills.Clone();
            actors.Add(entity.Id, new ActorComponent { Spec = spec, BaseSpec = spec, Position = spec.Position, Health = spec.MaxHealth });
            if (spec.IsPlayer) playerId = entity.Id;
            if (activeRoomId == 0) activeRoomId = spec.RoomId;
            return entity;
        }

        /// <summary>
        /// 设置角色移动摇杆输入。
        /// </summary>
        /// <param name="actor">角色实体引用。</param>
        /// <param name="axis">XZ 平面移动轴，长度上限为一。</param>
        /// <returns>角色存在且可行动则为真。</returns>
        public bool SetMove(CombatEntityRef actor, Vector2 axis)
        {
            if (!IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) || state.DeadPending ||
                !IsFinite(axis)) return false;
            state.Move = Vector2.ClampMagnitude(axis, 1f);
            return true;
        }

        /// <summary>
        /// 将角色放入当前地图的合法入口位置。
        /// </summary>
        /// <param name="actor">角色实体引用。</param>
        /// <param name="position">目标世界坐标。</param>
        /// <returns>位置合法且设置成功则为真。</returns>
        public bool TrySetPosition(CombatEntityRef actor, Vector3 position)
        {
            if (!IsValid(actor) || !actors.TryGetValue(actor.Id, out var state) || !IsFinite(position) ||
                !CanOccupy(state.Spec.RoomId, position, state.Spec.Radius, actor.Id)) return false;
            state.Position = position;
            state.Velocity = Vector3.zero;
            return true;
        }

        /// <summary>
        /// 成功换图后设置活跃地图并取消玩家尚未完成的局部动作。
        /// </summary>
        /// <param name="roomId">目标地图编号。</param>
        /// <param name="player">玩家实体引用。</param>
        /// <returns>玩家有效且目标地图编号合法则为真。</returns>
        public bool SetActiveRoom(int roomId, CombatEntityRef player)
        {
            if (roomId <= 0 || !IsValid(player) || !actors.TryGetValue(player.Id, out var state) ||
                !state.Spec.IsPlayer || state.DeadPending) return false;
            CancelOwnedCast(player.Id, true);
            RemoveOwnedDisplacement(player.Id);
            state.Spec.RoomId = roomId;
            state.Move = Vector2.zero;
            foreach (var actor in actors.Values) actor.Velocity = Vector3.zero;
            state.CueId = 0;
            state.CueClock = -1;
            activeRoomId = roomId;
            playerId = player.Id;
            if (!roomTicks.ContainsKey(roomId)) roomTicks.Add(roomId, 0);
            return true;
        }

        /// <summary>
        /// 原子校验并将玩家转移至目标地图的合法入口。
        /// </summary>
        /// <param name="player">玩家实体引用。</param>
        /// <param name="roomId">目标地图编号。</param>
        /// <param name="position">目标入口坐标。</param>
        /// <returns>目标位置可占用且成功切图则为真。</returns>
        public bool TryTransferActor(CombatEntityRef player, int roomId, Vector3 position)
        {
            if (roomId <= 0 || !IsValid(player) || !actors.TryGetValue(player.Id, out var state) ||
                !state.Spec.IsPlayer || state.DeadPending || !IsFinite(position) ||
                !CanOccupy(roomId, position, state.Spec.Radius, player.Id)) return false;
            if (!SetActiveRoom(roomId, player)) return false;
            state.Position = position;
            state.Velocity = Vector3.zero;
            return true;
        }

        /// <summary>
        /// 手动接纳一次战斗技能。
        /// </summary>
        /// <param name="actor">施法角色。</param>
        /// <param name="skillId">战斗技能编号。</param>
        /// <param name="target">目标实体，可为空引用。</param>
        /// <returns>条件满足并开始施法则为真。</returns>
        public bool TryStartSkill(CombatEntityRef actor, int skillId, CombatEntityRef target)
        {
            if (!IsValid(actor) || !actors.TryGetValue(actor.Id, out var source) || source.DeadPending ||
                source.Spec.RoomId != activeRoomId || HasCast(actor.Id) ||
                !catalog.Skills.TryGetValue(skillId, out var definition)) return false;
            if (source.SkillCooldowns.TryGetValue(skillId, out var cooldown) && cooldown > 0) return false;
            var rangeDelta = source.Spec.IsPlayer && skillId == source.Spec.BasicSkillId
                ? source.Spec.AttackRange - catalog.InitialAttackRange : 0f;
            var startRange = Mathf.Max(0f, definition.StartRange + (definition.AttackRangeSource ? rangeDelta : 0f));
            if (target.Id != 0 && (!IsValid(target) || !actors.TryGetValue(target.Id, out var targetActor) ||
                targetActor.DeadPending || targetActor.Spec.RoomId != source.Spec.RoomId ||
                Vector3.Distance(source.Position, targetActor.Position) > startRange + source.RangeBonus)) return false;
            var cast = Allocate();
            var point = target.Id != 0 ? actors[target.Id].Position : source.Position + source.Forward * startRange;
            casts.Add(cast.Id, new CastComponent
            {
                Owner = actor.Id, SkillId = skillId, StartTick = source.Spec.IsPlayer ? TickIndex : GetRoomTick(source.Spec.RoomId),
                Target = target, TargetPoint = point, Forward = source.Forward, NextNode = 0,
                UsesWorldClock = source.Spec.IsPlayer, Offense = new CombatOffense(source.Spec), RangeDelta = rangeDelta
            });
            source.SkillCooldowns[skillId] = Mathf.Max(0, definition.CooldownTicks);
            return true;
        }

        /// <summary>
        /// 推进一帧确定性的战斗模拟。
        /// </summary>
        public void Step()
        {
            if (faulted || activeRoomId == 0) return;
            TickIndex++;
            remainingEffectBudget = 4096;
            roomTicks[activeRoomId] = GetRoomTick(activeRoomId) + 1;
            ExpireBuffsAndCooldowns();
            if (!DrainEffects()) return;
            MoveActorsAndDisplacements();
            MoveProjectiles();
            if (!DrainEffects()) return;
            PulseBuffs();
            if (!DrainEffects()) return;
            AutoCombat();
            AdvanceCasts();
            if (!DrainEffects()) return;
            PublishDeathsAndCleanup();
        }

        /// <summary>
        /// 根据真实经过时间补走固定的六十分之一秒战斗帧。
        /// </summary>
        /// <param name="deltaTime">真实经过的秒数。</param>
        /// <param name="afterTick">每个完整 tick 后的回调；返回假时停止本次剩余推进。</param>
        /// <returns>本次完成的逻辑帧数量。</returns>
        public int Advance(float deltaTime, Func<bool> afterTick = null)
        {
            if (faulted || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime) || deltaTime < 0f) return 0;
            accumulator += deltaTime;
            var stepped = 0;
            while (accumulator + 1e-8 >= TickSeconds && !faulted)
            {
                accumulator -= TickSeconds;
                Step();
                if (faulted) break;
                stepped++;
                if (afterTick != null && !afterTick()) break;
            }
            return stepped;
        }

        /// <summary>
        /// 清除尚未执行的累计时间，避免暂停或终局后补跑战斗。
        /// </summary>
        public void ClearPendingTime() => accumulator = 0;

        /// <summary>
        /// 查询角色状态快照。
        /// </summary>
        /// <param name="actor">角色实体引用。</param>
        /// <param name="snapshot">查询成功时的状态快照。</param>
        /// <returns>角色实体仍有效则为真。</returns>
        public bool TryGetActor(CombatEntityRef actor, out CombatActorSnapshot snapshot)
        {
            snapshot = default;
            if (!IsValid(actor) || !actors.TryGetValue(actor.Id, out var state)) return false;
            snapshot = MakeSnapshot(actor.Id, state);
            return true;
        }

        /// <summary>
        /// 查询角色动画所需的只读战斗状态。
        /// </summary>
        /// <param name="actor">角色实体引用。</param>
        /// <param name="snapshot">查询成功时的动画状态快照。</param>
        /// <returns>引用属于当前世界且角色仍存在则为真。</returns>
        public bool TryGetAnimationSnapshot(CombatEntityRef actor, out CombatAnimationSnapshot snapshot)
        {
            snapshot = default;
            if (!IsValid(actor) || !actors.TryGetValue(actor.Id, out var state)) return false;
            var clock = state.Spec.IsPlayer ? TickIndex : GetRoomTick(state.Spec.RoomId);
            var cueElapsed = state.CueClock >= 0 ? clock - state.CueClock : -1;
            var cueActive = state.CueId != 0 && cueElapsed >= 0 && cueElapsed < state.CueDurationTicks;
            snapshot = new CombatAnimationSnapshot(actor, state.Spec.ActorId, state.Spec.RoomId, clock, state.Forward,
                state.Velocity, cueActive ? state.CueDirection : Vector3.zero, cueActive ? state.CueId : 0,
                cueActive ? (int)cueElapsed : 0, cueActive ? state.CueDurationTicks : 0,
                state.HitClock >= 0 ? (int)Math.Min(int.MaxValue, clock - state.HitClock) : -1,
                state.DeathClock >= 0 ? (int)Math.Min(int.MaxValue, clock - state.DeathClock) : -1, state.DeadPending);
            return true;
        }

        /// <summary>
        /// 把当前全部角色快照复制到调用方列表。
        /// </summary>
        /// <param name="destination">接收快照的列表。</param>
        /// <returns>写入的角色数量。</returns>
        public int GetActorsSnapshot(List<CombatActorSnapshot> destination)
        {
            if (destination == null) return 0;
            destination.Clear();
            ids.Clear();
            ids.AddRange(actors.Keys);
            ids.Sort();
            foreach (var id in ids) destination.Add(MakeSnapshot(id, actors[id]));
            return destination.Count;
        }

        /// <summary>
        /// 将未读取的战斗事件复制到调用方列表并清空内部事件队列。
        /// </summary>
        /// <param name="destination">接收事件的列表。</param>
        /// <returns>写入的事件数量。</returns>
        public int DrainEvents(List<CombatEvent> destination)
        {
            if (destination == null) return 0;
            destination.AddRange(events);
            var count = events.Count;
            events.Clear();
            return count;
        }

        /// <summary>
        /// 创建唯一实体引用。
        /// </summary>
        /// <returns>可用于组件存储的实体引用。</returns>
        private CombatEntityRef Allocate()
        {
            var id = freeIds.Count > 0 ? freeIds.Pop() : slots.Count;
            if (id == slots.Count) slots.Add(new EntitySlot());
            slots[id].Alive = true;
            return new CombatEntityRef(worldId, id, slots[id].Generation);
        }

        /// <summary>
        /// 释放实体槽位并递增代次。
        /// </summary>
        /// <param name="id">实体槽位编号。</param>
        private void Release(int id)
        {
            if (id <= 0 || id >= slots.Count || !slots[id].Alive) return;
            slots[id].Alive = false;
            slots[id].Generation++;
            freeIds.Push(id);
        }

        /// <summary>
        /// 判断引用是否属于当前世界且代次有效。
        /// </summary>
        /// <param name="entity">待检查的引用。</param>
        /// <returns>引用仍有效则为真。</returns>
        private bool IsValid(CombatEntityRef entity) => entity.World == worldId && entity.Id > 0 &&
            entity.Id < slots.Count && slots[entity.Id].Alive && slots[entity.Id].Generation == entity.Generation;

        /// <summary>
        /// 获取槽位当前引用。
        /// </summary>
        /// <param name="id">实体槽位编号。</param>
        /// <returns>该槽位当前代次的引用。</returns>
        private CombatEntityRef Ref(int id) => new CombatEntityRef(worldId, id, slots[id].Generation);

        /// <summary>
        /// 生成只读角色快照。
        /// </summary>
        /// <param name="id">角色槽位编号。</param>
        /// <param name="actor">角色组件。</param>
        /// <returns>角色当前状态快照。</returns>
        private CombatActorSnapshot MakeSnapshot(int id, ActorComponent actor) =>
            new CombatActorSnapshot(Ref(id), actor.Spec.ActorId, actor.Spec.RoomId, actor.Position,
                actor.Health, actor.Spec.MaxHealth, actor.DeadPending, actor.Spec.Attack, actor.Spec.Defense,
                actor.Spec.CriticalRating, actor.Spec.CriticalDamage, actor.Spec.DodgeChance,
                actor.Spec.DamageIncrease, actor.Spec.DamageReduction);

        /// <summary>
        /// 获取地图已推进的战斗帧。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <returns>地图局部逻辑帧。</returns>
        private long GetRoomTick(int roomId) => roomTicks.TryGetValue(roomId, out var tick) ? tick : 0;

        /// <summary>
        /// 检查浮点数是否为有限值。
        /// </summary>
        /// <param name="value">待检查数值。</param>
        /// <returns>数值有限则为真。</returns>
        private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        /// <summary>
        /// 检查三维向量是否为有限值。
        /// </summary>
        /// <param name="value">待检查向量。</param>
        /// <returns>三个分量均为有限值则为真。</returns>
        private static bool IsFinite(Vector3 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y) && !float.IsNaN(value.z) && !float.IsInfinity(value.z);

        /// <summary>
        /// 检查二维向量是否为有限值。
        /// </summary>
        /// <param name="value">待检查向量。</param>
        /// <returns>两个分量均为有限值则为真。</returns>
        private static bool IsFinite(Vector2 value) => !float.IsNaN(value.x) && !float.IsInfinity(value.x) &&
            !float.IsNaN(value.y) && !float.IsInfinity(value.y);
    }
}
