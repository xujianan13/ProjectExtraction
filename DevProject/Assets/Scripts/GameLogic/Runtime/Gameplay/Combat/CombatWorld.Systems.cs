using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic.Gameplay.Combat
{
    /// <summary>
    /// 战斗角色、技能、效果与 Buff 的确定性系统。
    /// </summary>
    public sealed partial class CombatWorld
    {
        /// <summary>
        /// 推进活跃地图中角色的普通移动和战斗位移。
        /// </summary>
        private void MoveActorsAndDisplacements()
        {
            var actorIds = new List<int>(actors.Keys);
            actorIds.Sort();
            foreach (var id in actorIds)
            {
                var actor = actors[id];
                if (actor.Spec.RoomId == activeRoomId) actor.Velocity = Vector3.zero;
                if (actor.DeadPending || actor.Spec.RoomId != activeRoomId) continue;
                var motion = new Vector3(actor.Move.x, 0f, actor.Move.y) *
                    Mathf.Max(0f, actor.Spec.MoveSpeed + actor.MoveBonus) * TickSeconds;
                if (FindDisplacement(id, out _)) motion = Vector3.zero;
                MoveActor(id, actor, motion);
            }
            var moveIds = new List<int>(displacements.Keys);
            moveIds.Sort();
            foreach (var id in moveIds)
            {
                var move = displacements[id];
                if (move.CreatedTick >= TickIndex || !actors.TryGetValue(move.Owner, out var actor) ||
                    actor.Spec.RoomId != activeRoomId) continue;
                if (!actor.DeadPending) MoveActor(move.Owner, actor, move.Direction * move.DistancePerTick);
                move.RemainingTicks--;
                if (move.RemainingTicks > 0 && !actor.DeadPending) continue;
                displacements.Remove(id);
                Release(id);
            }
        }

        /// <summary>
        /// 找到角色当前的战斗位移组件。
        /// </summary>
        /// <param name="owner">角色槽位编号。</param>
        /// <param name="displacementId">找到的位移实体槽位。</param>
        /// <returns>存在位移组件则为真。</returns>
        private bool FindDisplacement(int owner, out int displacementId)
        {
            foreach (var pair in displacements)
                if (pair.Value.Owner == owner) { displacementId = pair.Key; return true; }
            displacementId = 0;
            return false;
        }

        /// <summary>
        /// 清理角色尚未完成的战斗位移。
        /// </summary>
        /// <param name="owner">角色槽位编号。</param>
        private void RemoveOwnedDisplacement(int owner)
        {
            if (!FindDisplacement(owner, out var id)) return;
            displacements.Remove(id);
            Release(id);
        }

        /// <summary>
        /// 检查角色是否已拥有进行中的施法。
        /// </summary>
        /// <param name="owner">角色槽位编号。</param>
        /// <returns>存在进行中施法则为真。</returns>
        private bool HasCast(int owner)
        {
            foreach (var cast in casts.Values)
                if (cast.Owner == owner) return true;
            return false;
        }

        /// <summary>
        /// 取消角色未完成的施法。
        /// </summary>
        /// <param name="owner">角色槽位编号。</param>
        /// <param name="mapTransfer">是否因成功换图取消。</param>
        private void CancelOwnedCast(int owner, bool mapTransfer)
        {
            if (actors.TryGetValue(owner, out var cueOwner))
            {
                cueOwner.CueId = 0;
                cueOwner.CueClock = -1;
            }
            var castIds = new List<int>();
            foreach (var pair in casts)
                if (pair.Value.Owner == owner) castIds.Add(pair.Key);
            castIds.Sort();
            foreach (var id in castIds)
            {
                casts.Remove(id);
                Release(id);
                if (mapTransfer && actors.TryGetValue(owner, out var actor))
                    events.Add(new CombatEvent(CombatEventKind.CastCancelled, TickIndex, actor.Spec.RoomId,
                        actor.Spec.ActorId, 0, 0, 0f, actor.Position));
            }
        }

        /// <summary>
        /// 处理当前逻辑帧的冷却递减与 Buff 到期。
        /// </summary>
        private void ExpireBuffsAndCooldowns()
        {
            foreach (var actor in actors.Values)
            {
                if (actor.Spec.RoomId != activeRoomId) continue;
                var skillIds = new List<int>(actor.SkillCooldowns.Keys);
                foreach (var skillId in skillIds)
                {
                    var remaining = actor.SkillCooldowns[skillId] - 1;
                    if (remaining > 0) actor.SkillCooldowns[skillId] = remaining;
                    else actor.SkillCooldowns.Remove(skillId);
                }
            }
            var buffIds = new List<int>(buffs.Keys);
            buffIds.Sort();
            foreach (var id in buffIds)
            {
                var buff = buffs[id];
                if (!actors.TryGetValue(buff.Owner, out var owner) || owner.Spec.RoomId != activeRoomId) continue;
                if (GetBuffClock(owner) < buff.ExpireTick) continue;
                RemoveBuff(id, true);
            }
        }

        /// <summary>
        /// 获取 Buff 持有者使用的逻辑钟。
        /// </summary>
        /// <param name="owner">Buff 持有者。</param>
        /// <returns>玩家全局钟或敌人所在房间局部钟。</returns>
        private long GetBuffClock(ActorComponent owner) =>
            owner.Spec.IsPlayer ? TickIndex : GetRoomTick(owner.Spec.RoomId);

        /// <summary>
        /// 推进当前逻辑帧的 Buff 周期。
        /// </summary>
        private void PulseBuffs()
        {
            var buffIds = new List<int>(buffs.Keys);
            buffIds.Sort();
            foreach (var id in buffIds)
            {
                var buff = buffs[id];
                if (buff.Removed || !catalog.Buffs.TryGetValue(buff.BuffId, out var definition) ||
                    !actors.TryGetValue(buff.Owner, out var owner) || owner.DeadPending ||
                    owner.Spec.RoomId != activeRoomId) continue;
                var now = GetBuffClock(owner);
                if (now < buff.NextTick || now >= buff.ExpireTick) continue;
                foreach (var effect in definition.OnTick)
                {
                    if (effect.Kind == CombatEffectKind.Damage)
                    {
                        var scaled = new CombatEffectDefinition
                        {
                            Kind = effect.Kind, DefinitionId = effect.DefinitionId, Value = effect.Value * buff.Stacks,
                            AttackCoefficient = effect.AttackCoefficient * buff.Stacks,
                            CanCritical = effect.CanCritical, CanDodge = effect.CanDodge, AttackRangeSource = effect.AttackRangeSource,
                            LinkedId = effect.LinkedId, Shape = effect.Shape, Radius = effect.Radius,
                            Angle = effect.Angle, Width = effect.Width, Height = effect.Height,
                            RequireLineOfSight = effect.RequireLineOfSight
                        };
                        Enqueue(scaled, buff.Owner, Ref(buff.Owner), owner.Position, owner.Forward, buff.SourceActorId, buff.Offense, buff.RangeDelta, buff.RangeMultiplier, buff.PassiveDerived);
                    }
                    else Enqueue(effect, buff.Owner, Ref(buff.Owner), owner.Position, owner.Forward, buff.SourceActorId, buff.Offense, buff.RangeDelta, buff.RangeMultiplier, buff.PassiveDerived);
                }
                if (definition.PeriodicDamagePerStack > 0f)
                    Enqueue(new CombatEffectDefinition
                    {
                        Kind = CombatEffectKind.Damage, DefinitionId = definition.BuffId,
                        Value = definition.PeriodicDamagePerStack * buff.Stacks, CanCritical = false, CanDodge = false
                    }, buff.Owner, Ref(buff.Owner), owner.Position, owner.Forward, buff.SourceActorId, buff.Offense, buff.RangeDelta, buff.RangeMultiplier, buff.PassiveDerived);
                buff.NextTick += Mathf.Max(1, definition.IntervalTicks);
            }
        }

        /// <summary>
        /// 移除 Buff 并撤销它的可撤属性修正。
        /// </summary>
        /// <param name="buffId">Buff 实体槽位。</param>
        /// <param name="expired">是否为自然到期。</param>
        private void RemoveBuff(int buffId, bool expired)
        {
            if (!buffs.TryGetValue(buffId, out var buff)) return;
            buff.Removed = true;
            buffs.Remove(buffId);
            Release(buffId);
            RecalculateBonuses(buff.Owner);
            if (expired && actors.TryGetValue(buff.Owner, out var owner))
            {
                if (catalog.Buffs.TryGetValue(buff.BuffId, out var definition))
                    foreach (var effect in definition.OnExpire)
                        Enqueue(effect, buff.Owner, Ref(buff.Owner), owner.Position, owner.Forward, buff.SourceActorId, buff.Offense, buff.RangeDelta, buff.RangeMultiplier, buff.PassiveDerived);
                events.Add(new CombatEvent(CombatEventKind.BuffExpired, TickIndex, owner.Spec.RoomId,
                    buff.SourceActorId, owner.Spec.ActorId, buff.BuffId, 0f, owner.Position));
            }
        }

        /// <summary>
        /// 从当前 Buff 实例重算角色的可撤属性增量。
        /// </summary>
        /// <param name="ownerId">角色槽位。</param>
        private void RecalculateBonuses(int ownerId)
        {
            if (!actors.TryGetValue(ownerId, out var actor)) return;
            actor.MoveBonus = 0f;
            actor.RangeBonus = 0f;
            foreach (var buff in buffs.Values)
            {
                if (buff.Owner != ownerId || buff.Removed || !catalog.Buffs.TryGetValue(buff.BuffId, out var definition)) continue;
                actor.MoveBonus += definition.MoveSpeedAddPerStack * buff.Stacks;
                actor.RangeBonus += definition.AttackRangeAddPerStack * buff.Stacks;
            }
        }

        /// <summary>
        /// 由最近的有效敌人驱动追击和自动技能。
        /// </summary>
        private void AutoCombat()
        {
            var actorIds = new List<int>(actors.Keys);
            actorIds.Sort();
            foreach (var id in actorIds)
            {
                var source = actors[id];
                var enemySkills = !source.Spec.IsPlayer && source.Spec.EnemySkills != null && source.Spec.EnemySkills.Length > 0;
                if (source.DeadPending || source.Spec.RoomId != activeRoomId ||
                    (!enemySkills && source.Spec.BasicSkillId <= 0)) continue;
                GetSkillModifiers(source, source.Spec.BasicSkillId, out _, out var basicRangeMultiplier, out _);
                var basicRange = (source.Spec.AttackRange + source.RangeBonus) * basicRangeMultiplier;
                var searchRange = source.Spec.IsPlayer ? basicRange :
                    Mathf.Max(basicRange, source.Spec.AggroRange);
                if (enemySkills)
                    foreach (var skill in source.Spec.EnemySkills)
                        searchRange = Mathf.Max(searchRange, skill.MaxDistance + source.RangeBonus);
                var targetId = FindNearestEnemy(id, searchRange);
                if (targetId == 0)
                {
                    if (!source.Spec.IsPlayer) source.Move = Vector2.zero;
                    continue;
                }
                var target = actors[targetId];
                var delta = target.Position - source.Position;
                delta.y = 0f;
                var distance = delta.magnitude;
                var hasLineOfSight = !HasWallBetween(source.Spec.RoomId, source.Position, target.Position);
                var casting = HasCast(id);
                var skillId = casting || !hasLineOfSight ? 0 :
                    enemySkills ? SelectEnemySkill(source, distance) : source.Spec.BasicSkillId;
                var inRange = enemySkills ? skillId > 0 : distance <= basicRange;
                if (!source.Spec.IsPlayer)
                {
                    var retreatDistance = enemySkills && !casting && !inRange ? EnemyRetreatDistance(source, distance) : 0f;
                    var retreatDirection = delta.sqrMagnitude > 0.0001f ? delta.normalized : source.Forward;
                    var retreatGoal = source.Position - retreatDirection * (retreatDistance + 0.1f);
                    source.Move = casting || (inRange && hasLineOfSight) ? Vector2.zero : retreatDistance > 0f ?
                        FindPursuitDirection(source.Spec.RoomId, source.Position, retreatGoal, source.Spec.Radius) :
                        FindPursuitDirection(source.Spec.RoomId, source.Position, target.Position, source.Spec.Radius);
                }
                if (!casting && delta.sqrMagnitude > 0.0001f) source.Forward = delta.normalized;
                if (inRange && hasLineOfSight && !casting)
                    TryStartSkill(Ref(id), skillId, Ref(targetId));
            }
        }

        /// <summary>
        /// 选择当前生命阶段与距离内优先级最高的可用敌人技能，并按权重决定同级技能。
        /// </summary>
        /// <param name="source">敌人状态。</param>
        /// <param name="distance">敌人与目标的 XZ 距离。</param>
        /// <returns>选中的技能编号；没有可用技能时为零。</returns>
        private int SelectEnemySkill(ActorComponent source, float distance)
        {
            var healthRatio = source.Health / source.Spec.MaxHealth;
            var bestPriority = int.MinValue;
            long totalWeight = 0;
            foreach (var skill in source.Spec.EnemySkills)
            {
                if (!EnemySkillAvailable(source, skill, healthRatio, distance)) continue;
                if (skill.Priority > bestPriority)
                {
                    bestPriority = skill.Priority;
                    totalWeight = 0;
                }
                if (skill.Priority == bestPriority) totalWeight += skill.Weight;
            }
            if (totalWeight == 0) return 0;
            var draw = (long)(random.NextDouble() * totalWeight);
            foreach (var skill in source.Spec.EnemySkills)
            {
                if (skill.Priority != bestPriority || !EnemySkillAvailable(source, skill, healthRatio, distance)) continue;
                draw -= skill.Weight;
                if (draw < 0) return skill.SkillId;
            }
            return 0;
        }

        /// <summary>
        /// 判断单项敌人技能当前是否可发起。
        /// </summary>
        /// <param name="source">敌人状态。</param>
        /// <param name="skill">自动技能条件。</param>
        /// <param name="healthRatio">敌人当前生命比例。</param>
        /// <param name="distance">敌人与目标的 XZ 距离。</param>
        /// <returns>阶段、距离及独立冷却均满足则为真。</returns>
        private bool EnemySkillAvailable(ActorComponent source, CombatEnemySkillDefinition skill, float healthRatio, float distance)
        {
            if (healthRatio < skill.MinHealthRatio || healthRatio > skill.MaxHealthRatio ||
                distance < skill.MinDistance || distance > skill.MaxDistance + source.RangeBonus ||
                !catalog.Skills.TryGetValue(skill.SkillId, out var definition) ||
                distance > definition.StartRange + source.RangeBonus) return false;
            return !source.SkillCooldowns.TryGetValue(skill.SkillId, out var cooldown) || cooldown <= 0;
        }

        /// <summary>
        /// 无技能可用且目标过近时，求满足当前生命阶段技能的最短退让距离。
        /// </summary>
        /// <param name="source">敌人状态。</param>
        /// <param name="distance">敌人与目标的 XZ 距离。</param>
        /// <returns>需要后撤的距离；无可用近距离条件时为零。</returns>
        private float EnemyRetreatDistance(ActorComponent source, float distance)
        {
            var healthRatio = source.Health / source.Spec.MaxHealth;
            var minDistance = float.PositiveInfinity;
            foreach (var skill in source.Spec.EnemySkills)
            {
                if (healthRatio < skill.MinHealthRatio || healthRatio > skill.MaxHealthRatio ||
                    distance >= skill.MinDistance || !catalog.Skills.ContainsKey(skill.SkillId) ||
                    source.SkillCooldowns.TryGetValue(skill.SkillId, out var cooldown) && cooldown > 0) continue;
                minDistance = Mathf.Min(minDistance, skill.MinDistance);
            }
            return float.IsPositiveInfinity(minDistance) ? 0f : minDistance - distance;
        }

        /// <summary>
        /// 查找范围内最近的敌对角色；玩家候选需可直视，敌人保留隔墙追击目标。
        /// </summary>
        /// <param name="sourceId">来源角色槽位。</param>
        /// <param name="range">最大查找距离。</param>
        /// <returns>目标槽位；没有目标时为零。</returns>
        private int FindNearestEnemy(int sourceId, float range)
        {
            var source = actors[sourceId];
            var best = 0;
            var bestDistance = range * range;
            var actorIds = new List<int>(actors.Keys);
            actorIds.Sort();
            foreach (var id in actorIds)
            {
                var candidate = actors[id];
                if (id == sourceId || candidate.DeadPending || candidate.Spec.RoomId != source.Spec.RoomId ||
                    candidate.Spec.Team == source.Spec.Team) continue;
                var distance = (candidate.Position - source.Position).sqrMagnitude;
                if (distance > bestDistance) continue;
                if (source.Spec.IsPlayer && HasWallBetween(source.Spec.RoomId, source.Position, candidate.Position)) continue;
                best = id;
                bestDistance = distance;
            }
            return best;
        }

        /// <summary>
        /// 按游标执行所有到期节点一次，包含帧间手动施法尚未执行的零帧效果。
        /// </summary>
        private void AdvanceCasts()
        {
            var castIds = new List<int>(casts.Keys);
            castIds.Sort();
            foreach (var id in castIds)
            {
                if (!casts.TryGetValue(id, out var cast)) continue;
                if (!actors.TryGetValue(cast.Owner, out var owner) || owner.DeadPending)
                {
                    casts.Remove(id);
                    Release(id);
                    continue;
                }
                if (owner.Spec.RoomId != activeRoomId || !catalog.Skills.TryGetValue(cast.SkillId, out var definition)) continue;
                var elapsed = (cast.UsesWorldClock ? TickIndex : GetRoomTick(owner.Spec.RoomId)) - cast.StartTick;
                while (cast.NextNode < definition.Nodes.Count && definition.Nodes[cast.NextNode].Tick <= elapsed)
                {
                    var node = definition.Nodes[cast.NextNode++];
                    foreach (var effect in node.Effects)
                    {
                        var target = effect.Kind == CombatEffectKind.SpawnProjectile ||
                            effect.Kind == CombatEffectKind.ApplyBuff || effect.Kind == CombatEffectKind.ModifyAttribute ||
                            effect.Kind == CombatEffectKind.Cue || effect.Kind == CombatEffectKind.Heal ? cast.Target : default;
                        Enqueue(effect, cast.Owner, target, cast.TargetPoint, cast.Forward, owner.Spec.ActorId, cast.Offense, cast.RangeDelta, cast.RangeMultiplier);
                    }
                }
                if (elapsed < definition.DurationTicks) continue;
                casts.Remove(id);
                Release(id);
            }
        }

        /// <summary>
        /// 把效果加入本阶段的因果队列。
        /// </summary>
        /// <param name="effect">待执行效果定义。</param>
        /// <param name="source">直接来源实体槽位。</param>
        /// <param name="target">目标实体引用。</param>
        /// <param name="position">效果中心或目标点。</param>
        /// <param name="direction">效果方向。</param>
        /// <param name="instigatorActorId">稳定的收益归因角色编号。</param>
        /// <param name="offense">效果出生时冻结的来源进攻属性。</param>
        /// <param name="rangeDelta">施放时攻击范围相对目录初值的差值。</param>
        /// <param name="rangeMultiplier">施放时冻结的技能范围倍率。</param>
        /// <param name="passiveDerived">效果是否由被动衍生，不再触发被动。</param>
        private void Enqueue(CombatEffectDefinition effect, int source, CombatEntityRef target, Vector3 position,
            Vector3 direction, int instigatorActorId, CombatOffense offense, float rangeDelta = 0f,
            float rangeMultiplier = 1f, bool passiveDerived = false)
        {
            if (effect != null && (effect.AttackRangeSource && rangeDelta != 0f || rangeMultiplier != 1f))
            {
                var radius = effect.Radius + (effect.AttackRangeSource ? rangeDelta : 0f);
                effect = CopyEffectWithRadius(effect, Mathf.Min(AttributeCeiling, Mathf.Max(0f, radius * rangeMultiplier)));
            }
            if (!ValidEffect(effect)) { MarkFault("Invalid effect definition"); return; }
            effectQueue.Enqueue(new EffectCommand
            {
                Definition = effect, RangeDelta = rangeDelta, RangeMultiplier = rangeMultiplier, PassiveDerived = passiveDerived, Source = source, Target = target, Position = position, Direction = direction,
                InstigatorActorId = instigatorActorId, Offense = offense,
                RootId = activeRootId != 0 ? activeRootId : ++nextRootId,
                TriggerId = ++nextTriggerId, Depth = activeRootId != 0 ? activeDepth + 1 : 0
            });
        }

        /// <summary>
        /// 复制效果并覆盖施放时命中半径，避免修改共享目录。
        /// </summary>
        /// <param name="effect">原始效果。</param>
        /// <param name="radius">快照半径。</param>
        /// <returns>具有独立半径的效果副本。</returns>
        private static CombatEffectDefinition CopyEffectWithRadius(CombatEffectDefinition effect, float radius) =>
            new CombatEffectDefinition
            {
                Kind = effect.Kind, Attribute = effect.Attribute, DefinitionId = effect.DefinitionId,
                Value = effect.Value, AttackCoefficient = effect.AttackCoefficient,
                CanCritical = effect.CanCritical, CanDodge = effect.CanDodge, DurationTicks = effect.DurationTicks,
                LinkedId = effect.LinkedId, Shape = effect.Shape, Radius = radius, Angle = effect.Angle,
                Width = effect.Width, Height = effect.Height, RequireLineOfSight = effect.RequireLineOfSight,
                AttackRangeSource = effect.AttackRangeSource
            };

        /// <summary>
        /// 按 FIFO 顺序排空效果，预算超限后停止战斗推进。
        /// </summary>
        /// <returns>队列已成功排空则为真。</returns>
        private bool DrainEffects()
        {
            while (effectQueue.Count > 0)
            {
                if (--remainingEffectBudget < 0) { MarkFault("Effect budget exceeded"); return false; }
                var command = effectQueue.Dequeue();
                if (command.Depth > 16) { MarkFault("Effect chain depth exceeded"); return false; }
                activeRootId = command.RootId;
                activeDepth = command.Depth;
                ExecuteEffect(command);
                if (faulted) return false;
                activeRootId = 0;
                activeDepth = 0;
            }
            return true;
        }

        /// <summary>
        /// 执行一条类型化战斗效果。
        /// </summary>
        /// <param name="command">效果与上下文。</param>
        private void ExecuteEffect(EffectCommand command)
        {
            var effect = command.Definition;
            switch (effect.Kind)
            {
                case CombatEffectKind.Cue:
                    var cueHasActor = actors.TryGetValue(command.Source, out var cueActor);
                    if (cueHasActor)
                    {
                        var cueDirection = new Vector3(command.Direction.x, 0f, command.Direction.z);
                        if (cueDirection.sqrMagnitude < 0.0001f) cueDirection = cueActor.Forward;
                        cueActor.CueDirection = cueDirection.normalized;
                        cueActor.CueId = effect.DefinitionId;
                        cueActor.CueDurationTicks = effect.DurationTicks > 0 ? effect.DurationTicks : 18;
                        cueActor.CueClock = GetBuffClock(cueActor);
                    }
                    ProjectileComponent cueProjectile = null;
                    var room = cueHasActor ? cueActor.Spec.RoomId :
                        projectiles.TryGetValue(command.Source, out cueProjectile) ? cueProjectile.RoomId : activeRoomId;
                    var cuePosition = cueHasActor ? cueActor.Position :
                        cueProjectile != null ? cueProjectile.Position : command.Position;
                    var cueTargetId = IsValid(command.Target) && actors.TryGetValue(command.Target.Id, out var cueTarget)
                        ? cueTarget.Spec.ActorId : 0;
                    events.Add(new CombatEvent(CombatEventKind.Cue, TickIndex, room, command.InstigatorActorId,
                        cueTargetId, effect.DefinitionId, 0f, cuePosition));
                    break;
                case CombatEffectKind.Damage:
                    ExecuteDamage(command);
                    break;
                case CombatEffectKind.Displace:
                    if (actors.TryGetValue(command.Source, out var mover) && !mover.DeadPending && effect.DurationTicks > 0)
                    {
                        RemoveOwnedDisplacement(command.Source);
                        var entity = Allocate();
                        displacements.Add(entity.Id, new DisplacementComponent
                        {
                            Owner = command.Source, Direction = command.Direction.normalized,
                            DistancePerTick = effect.Value / effect.DurationTicks,
                            RemainingTicks = effect.DurationTicks, CreatedTick = TickIndex
                        });
                    }
                    break;
                case CombatEffectKind.SpawnProjectile:
                    if (!catalog.Projectiles.TryGetValue(effect.LinkedId, out var projectileDefinition) ||
                        !ValidProjectile(projectileDefinition))
                    { MarkFault("Invalid projectile definition"); break; }
                    if (actors.ContainsKey(command.Source))
                        TrySpawnProjectileInternal(Ref(command.Source), effect.LinkedId, command.Target,
                            command.Position, command.Direction, null, false, command.Offense, command.RangeDelta,
                            command.RangeMultiplier, command.PassiveDerived, command.InstigatorActorId);
                    else if (projectiles.TryGetValue(command.Source, out var parentProjectile) &&
                             IsValid(parentProjectile.Source))
                        TrySpawnProjectileInternal(parentProjectile.Source, effect.LinkedId, command.Target,
                            command.Position, command.Direction, command.Position, true, command.Offense, command.RangeDelta,
                            command.RangeMultiplier, command.PassiveDerived, command.InstigatorActorId);
                    break;
                case CombatEffectKind.ApplyBuff:
                    if (!catalog.Buffs.TryGetValue(effect.LinkedId, out var buffDefinition) || !ValidBuff(buffDefinition))
                    { MarkFault("Invalid buff definition"); break; }
                    if (command.Target.Id != 0 || actors.ContainsKey(command.Source))
                        ApplyBuff(command.Target.Id != 0 ? command.Target : Ref(command.Source), effect.LinkedId,
                            command.InstigatorActorId, command.Offense, command.RangeDelta, command.RangeMultiplier, command.PassiveDerived);
                    break;
                case CombatEffectKind.ModifyAttribute:
                    ApplyAttributeBonus(command.Target.Id != 0 ? command.Target : Ref(command.Source), effect.Attribute, effect.Value);
                    break;
                case CombatEffectKind.Heal:
                    ExecuteHeal(command);
                    break;
            }
        }

        /// <summary>
        /// 对明确目标或形状查询结果执行伤害。
        /// </summary>
        /// <param name="command">伤害效果上下文。</param>
        private void ExecuteDamage(EffectCommand command)
        {
            if (command.Target.Id != 0)
            {
                if (IsValid(command.Target) && actors.TryGetValue(command.Target.Id, out var target))
                    ApplyDamage(command.Target.Id, target, command.InstigatorActorId, command.Definition, command.Offense, command.PassiveDerived);
                return;
            }
            var hasActorSource = actors.TryGetValue(command.Source, out var source);
            var hasProjectileSource = projectiles.TryGetValue(command.Source, out var projectile);
            if (!hasActorSource && !hasProjectileSource) return;
            var roomId = hasActorSource ? source.Spec.RoomId : projectile.RoomId;
            var origin = hasActorSource ? source.Position : command.Position;
            var sourceTeam = hasActorSource ? source.Spec.Team : projectile.SourceTeam;
            var effect = command.Definition;
            var actorIds = new List<int>(actors.Keys);
            actorIds.Sort();
            foreach (var id in actorIds)
            {
                var target = actors[id];
                if (target.DeadPending || target.Spec.RoomId != roomId || target.Spec.ActorId == command.InstigatorActorId ||
                    (sourceTeam != 0 && target.Spec.Team == sourceTeam) ||
                    !InsideShape(origin, command.Direction, target.Position, effect)) continue;
                if (effect.RequireLineOfSight && HasWallBetween(roomId, origin, target.Position)) continue;
                ApplyDamage(id, target, command.InstigatorActorId, effect, command.Offense, command.PassiveDerived);
            }
        }

        /// <summary>
        /// 结算闪避、暴击和防御后写入有效伤害。
        /// </summary>
        /// <param name="targetId">受击角色槽位。</param>
        /// <param name="target">受击角色。</param>
        /// <param name="sourceActorId">归因角色编号。</param>
        /// <param name="effect">伤害效果定义。</param>
        /// <param name="offense">效果出生时冻结的来源属性。</param>
        /// <param name="passiveDerived">本次伤害是否由被动衍生。</param>
        private void ApplyDamage(int targetId, ActorComponent target, int sourceActorId, CombatEffectDefinition effect,
            CombatOffense offense, bool passiveDerived)
        {
            double raw = Math.Max(0d, (double)effect.Value + (double)offense.Attack * effect.AttackCoefficient);
            if (target.DeadPending || raw <= 0d) return;
            if (effect.CanDodge && target.Spec.DodgeChance > 0f && random.NextDouble() < target.Spec.DodgeChance)
            {
                events.Add(new CombatEvent(CombatEventKind.Dodged, TickIndex, target.Spec.RoomId, sourceActorId,
                    target.Spec.ActorId, effect.DefinitionId, 0f, target.Position));
                return;
            }
            double criticalChance = offense.CriticalRating / ((double)offense.CriticalRating + 100d);
            bool critical = effect.CanCritical && criticalChance > 0d && random.NextDouble() < criticalChance;
            double damage = raw * (1d + offense.DamageIncrease) * (critical ? offense.CriticalDamage : 1d);
            damage *= 100d / (100d + target.Spec.Defense);
            damage *= 1d - target.Spec.DamageReduction;
            damage *= offense.DamageMultiplier;
            float actual = (float)Math.Min(target.Health, Math.Min(float.MaxValue, Math.Max(0d, damage)));
            if (actual > 0f) target.HitClock = GetBuffClock(target);
            target.Health -= actual;
            if (target.Health <= 0f)
            {
                target.Health = 0f;
                target.DeadPending = true;
                target.LastInstigatorActorId = sourceActorId;
            }
            events.Add(new CombatEvent(CombatEventKind.Damage, TickIndex, target.Spec.RoomId, sourceActorId,
                target.Spec.ActorId, effect.DefinitionId, actual, target.Position, critical));
            if (actual <= 0f || passiveDerived) return;
            var sourceId = FindActorSlot(sourceActorId);
            if (sourceId != 0 && sourceId != targetId)
            {
                TriggerAugments(sourceId, targetId, CombatTriggerKind.Hit);
                if (critical) TriggerAugments(sourceId, targetId, CombatTriggerKind.CriticalHit);
                if (target.DeadPending) TriggerAugments(sourceId, targetId, CombatTriggerKind.Kill);
            }
            if (!target.DeadPending && sourceId != 0 && sourceId != targetId)
                TriggerAugments(targetId, sourceId, CombatTriggerKind.Damaged);
        }

        /// <summary>
        /// 判断目标是否落入配置的二维形状和高度范围。
        /// </summary>
        /// <param name="origin">判定原点。</param>
        /// <param name="forward">判定正方向。</param>
        /// <param name="target">目标位置。</param>
        /// <param name="effect">形状定义。</param>
        /// <returns>目标位于形状内则为真。</returns>
        private static bool InsideShape(Vector3 origin, Vector3 forward, Vector3 target, CombatEffectDefinition effect)
        {
            var delta = target - origin;
            if (Mathf.Abs(delta.y) > effect.Height) return false;
            delta.y = 0f;
            var radius = Mathf.Max(0f, effect.Radius);
            if (effect.Shape == CombatHitShape.Circle) return delta.sqrMagnitude <= radius * radius;
            var direction = new Vector3(forward.x, 0f, forward.z).normalized;
            if (direction.sqrMagnitude < 0.001f) direction = Vector3.forward;
            if (effect.Shape == CombatHitShape.Sector)
                return delta.sqrMagnitude <= radius * radius && Vector3.Angle(direction, delta) <= effect.Angle * 0.5f;
            var ahead = Vector3.Dot(delta, direction);
            var side = Vector3.Dot(delta, new Vector3(-direction.z, 0f, direction.x));
            if (effect.Shape == CombatHitShape.Box)
                return Mathf.Abs(ahead) <= radius && Mathf.Abs(side) <= effect.Width * 0.5f;
            return Mathf.Clamp(ahead, 0f, radius) >= 0f &&
                (delta - direction * Mathf.Clamp(ahead, 0f, radius)).sqrMagnitude <= effect.Width * effect.Width * 0.25f;
        }

        /// <summary>
        /// 判断两点之间是否有实体墙阻挡。
        /// </summary>
        /// <param name="roomId">地图编号。</param>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <returns>存在障碍交点则为真。</returns>
        private bool HasWallBetween(int roomId, Vector3 start, Vector3 end)
        {
            if (!obstacles.TryGetValue(roomId, out var walls)) return false;
            foreach (var wall in walls)
                if (SweepAabb(start, end, wall, 0f, out _, out _)) return true;
            return false;
        }

        /// <summary>
        /// 使用线段与扩张矩形计算第一次接触。
        /// </summary>
        /// <param name="start">线段起点。</param>
        /// <param name="end">线段终点。</param>
        /// <param name="rect">XZ 平面的实体矩形。</param>
        /// <param name="radius">待扫掠物体的半径。</param>
        /// <param name="t">第一次接触的线段比例。</param>
        /// <param name="normal">接触面法线。</param>
        /// <returns>线段接触矩形则为真。</returns>
        private static bool SweepAabb(Vector3 start, Vector3 end, Rect rect, float radius, out float t, out Vector3 normal)
        {
            t = 0f;
            normal = Vector3.zero;
            var dx = end.x - start.x;
            var dz = end.z - start.z;
            var near = 0f;
            var far = 1f;
            if (Mathf.Abs(dx) < 0.000001f)
            {
                if (start.x < rect.xMin - radius || start.x > rect.xMax + radius) return false;
            }
            else
            {
                var a = (rect.xMin - radius - start.x) / dx;
                var b = (rect.xMax + radius - start.x) / dx;
                var enter = Mathf.Min(a, b);
                var leave = Mathf.Max(a, b);
                if (enter >= near) { near = enter; normal = new Vector3(dx > 0f ? -1f : 1f, 0f, 0f); }
                far = Mathf.Min(far, leave);
                if (near > far) return false;
            }
            if (Mathf.Abs(dz) < 0.000001f)
            {
                if (start.z < rect.yMin - radius || start.z > rect.yMax + radius) return false;
            }
            else
            {
                var a = (rect.yMin - radius - start.z) / dz;
                var b = (rect.yMax + radius - start.z) / dz;
                var enter = Mathf.Min(a, b);
                var leave = Mathf.Max(a, b);
                if (enter >= near) { near = enter; normal = new Vector3(0f, 0f, dz > 0f ? -1f : 1f); }
                far = Mathf.Min(far, leave);
                if (near > far) return false;
            }
            t = near;
            return near <= 1f && far >= 0f;
        }

        /// <summary>
        /// 读取当前来源的进攻属性供公开效果入口冻结。
        /// </summary>
        /// <param name="actorId">稳定来源角色编号。</param>
        /// <returns>来源存在时为当前属性，否则为不含加成的默认属性。</returns>
        private CombatOffense CaptureOffense(int actorId)
        {
            foreach (ActorComponent actor in actors.Values)
                if (actor.Spec.ActorId == actorId) return new CombatOffense(actor.Spec);
            return new CombatOffense(new CombatActorSpec { CriticalDamage = 1.5f });
        }

        /// <summary>
        /// 为目标添加或合并一个周期 Buff。
        /// </summary>
        /// <param name="targetRef">目标角色引用。</param>
        /// <param name="buffId">Buff 定义编号。</param>
        /// <param name="sourceActorId">稳定来源角色编号。</param>
        /// <returns>配置与目标有效则为真。</returns>
        public bool ApplyBuff(CombatEntityRef targetRef, int buffId, int sourceActorId) =>
            ApplyBuff(targetRef, buffId, sourceActorId, CaptureOffense(sourceActorId), 0f, 1f, false);

        /// <summary>
        /// 使用已冻结的来源属性添加或合并周期 Buff。
        /// </summary>
        /// <param name="targetRef">目标角色引用。</param>
        /// <param name="buffId">Buff 定义编号。</param>
        /// <param name="sourceActorId">稳定来源角色编号。</param>
        /// <param name="offense">施加时的来源进攻属性。</param>
        /// <param name="rangeDelta">施加时攻击范围相对目录初值的差值。</param>
        /// <param name="rangeMultiplier">施加时冻结的技能范围倍率。</param>
        /// <param name="passiveDerived">Buff 是否由被动衍生。</param>
        /// <returns>配置与目标有效则为真。</returns>
        private bool ApplyBuff(CombatEntityRef targetRef, int buffId, int sourceActorId, CombatOffense offense, float rangeDelta, float rangeMultiplier, bool passiveDerived)
        {
            if (!IsValid(targetRef) || !actors.TryGetValue(targetRef.Id, out var target) || target.DeadPending ||
                !catalog.Buffs.TryGetValue(buffId, out var definition) || !ValidBuff(definition)) return false;
            var now = GetBuffClock(target);
            var existing = 0;
            foreach (var pair in buffs)
                if (pair.Value.Owner == targetRef.Id && pair.Value.BuffId == buffId &&
                    pair.Value.SourceActorId == sourceActorId && pair.Value.PassiveDerived == passiveDerived)
                { existing = pair.Key; break; }
            if (existing != 0)
            {
                var current = buffs[existing];
                current.Offense = offense;
                current.RangeDelta = rangeDelta;
                current.RangeMultiplier = rangeMultiplier;
                current.PassiveDerived = passiveDerived;
                if (definition.Merge == CombatBuffMerge.AddStack)
                    current.Stacks = Mathf.Min(definition.MaxStacks, current.Stacks + 1);
                else if (definition.Merge == CombatBuffMerge.ResetDuration)
                    current.ExpireTick = now + definition.DurationTicks;
                else RemoveBuff(existing, false);
                if (definition.Merge != CombatBuffMerge.Replace)
                {
                    RecalculateBonuses(targetRef.Id);
                    return true;
                }
            }
            var entity = Allocate();
            buffs.Add(entity.Id, new BuffComponent
            {
                Owner = targetRef.Id, BuffId = buffId, SourceActorId = sourceActorId, Offense = offense, RangeDelta = rangeDelta, RangeMultiplier = rangeMultiplier,
                PassiveDerived = passiveDerived, Stacks = 1,
                ExpireTick = now + definition.DurationTicks, NextTick = now + definition.IntervalTicks
            });
            RecalculateBonuses(targetRef.Id);
            foreach (var effect in definition.OnApply)
                Enqueue(effect, targetRef.Id, targetRef, target.Position, target.Forward, sourceActorId, offense, rangeDelta, rangeMultiplier, passiveDerived);
            events.Add(new CombatEvent(CombatEventKind.BuffApplied, TickIndex, target.Spec.RoomId,
                sourceActorId, target.Spec.ActorId, buffId, 1f, target.Position));
            return true;
        }

        /// <summary>
        /// 收束待死亡角色、取消局部动作并发布一次死亡事实。
        /// </summary>
        private void PublishDeathsAndCleanup()
        {
            var actorIds = new List<int>(actors.Keys);
            actorIds.Sort();
            foreach (var id in actorIds)
            {
                var actor = actors[id];
                if (!actor.DeadPending || actor.DeathPublished || actor.Spec.RoomId != activeRoomId) continue;
                actor.DeathPublished = true;
                actor.DeathClock = GetBuffClock(actor);
                CancelOwnedCast(id, false);
                RemoveOwnedDisplacement(id);
                var buffIds = new List<int>();
                foreach (var pair in buffs)
                    if (pair.Value.Owner == id) buffIds.Add(pair.Key);
                buffIds.Sort();
                foreach (var buffId in buffIds) RemoveBuff(buffId, false);
                events.Add(new CombatEvent(CombatEventKind.Death, TickIndex, actor.Spec.RoomId, actor.LastInstigatorActorId,
                    actor.Spec.ActorId, ++nextDeathId, 0f, actor.Position));
                if (actor.Spec.IsPlayer || clearedRooms.Contains(actor.Spec.RoomId)) continue;
                var aliveEnemy = false;
                foreach (var other in actors.Values)
                    if (!other.Spec.IsPlayer && !other.DeadPending && other.Spec.RoomId == actor.Spec.RoomId)
                    { aliveEnemy = true; break; }
                if (aliveEnemy) continue;
                clearedRooms.Add(actor.Spec.RoomId);
                events.Add(new CombatEvent(CombatEventKind.RoomCleared, TickIndex, actor.Spec.RoomId, 0, 0, 0, 0f,
                    actor.Position));
            }
            var projectileIds = new List<int>();
            foreach (var pair in projectiles)
                if (pair.Value.Terminal) projectileIds.Add(pair.Key);
            projectileIds.Sort();
            foreach (var id in projectileIds) { projectiles.Remove(id); Release(id); }
        }
    }
}
