using GameLogic.Project;
using GameLogic.Tables;
using GameFramework;
using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 由 ECS 快照更新热更 Prefab 实例、战利品和俯视相机。
    /// </summary>
    public sealed class ExpeditionPresentation : IDisposable
    {
        private readonly ExpeditionRun run;
        private readonly ExpeditionVisualAssets assets;
        private readonly GameObject root;
        private readonly Camera camera;
        private readonly Dictionary<int, GameObject> actors = new();
        private readonly Dictionary<int, CombatCharacterAnimation> animations = new();
        private readonly HashSet<int> visibleActors = new();
        private readonly List<int> staleActors = new();
        private int presentedRegionId = -1;
        private readonly Dictionary<string, GameObject> regionObjects = new();
        private readonly List<AnimatorPlayback> ambientAnimations = new();
        private readonly Dictionary<string, GameObject> loot = new();
        private readonly List<CombatActorSnapshot> snapshots = new();
        private readonly Dictionary<CombatEntityRef, GameObject> projectiles = new();
        private readonly List<CombatProjectileSnapshot> projectileSnapshots = new();
        private readonly HashSet<CombatEntityRef> visibleProjectiles = new();
        private readonly List<CombatEntityRef> staleProjectiles = new();
        private readonly MaterialPropertyBlock tint = new();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<CombatCueView> cues = new();
        private readonly List<CombatTelegraphSnapshot> cueTelegraphs = new();
        private bool disposed;
        private readonly CombatFeedbackView feedback;
        private readonly CombatFeedbackAudio feedbackAudio;

        /// <summary>
        /// 本局场景与模型实例的共同父节点。
        /// </summary>
        public Transform Root => root.transform;

        /// <summary>
        /// 时间轴表现提示，只读取模拟时间而不参与伤害判定。
        /// </summary>
        private sealed class CombatCueView
        {
            public GameObject Object;
            public int ActorId;
            public int RoomId;
            public long StartTick;
            public int DurationTicks;
            public Vector3 Direction;
            public float Offset;
            public float Angle;
            public bool Thrust;
        }

        /// <summary>
        /// 从已经加载的场景 Prefab 创建本局根节点、相机和灯光。
        /// </summary>
        /// <param name="run">读取快照的单局。</param>
        /// <param name="assets">由场景作用域持有租约的完整资源集。</param>
        public ExpeditionPresentation(ExpeditionRun run, ExpeditionVisualAssets assets)
        {
            this.run = run;
            this.assets = assets;
            root = UnityEngine.Object.Instantiate(assets.Scene);
            root.name = "ExpeditionPresentation";
            foreach (Camera candidate in root.GetComponentsInChildren<Camera>(true))
            {
                if (!candidate.isActiveAndEnabled) continue;
                camera = candidate;
                break;
            }
            feedback = new CombatFeedbackView(root.transform, camera, assets.Feedback);
            feedbackAudio = new CombatFeedbackAudio();
            run.CombatEventPublished += PresentEvent;
        }

        /// <summary>
        /// 同步当前地图的角色与战利品，隐藏冻结地图的表现。
        /// </summary>
        public void Refresh()
        {
            if (disposed) return;
            if (run.IsSavingRun)
            {
                feedbackAudio.Refresh(true, run.CurrentRoom.Plan.Request.RegionId);
                return;
            }
            if (presentedRegionId != run.CurrentRoom.Plan.Request.RegionId)
            {
                foreach (CombatCharacterAnimation animation in animations.Values) animation.Dispose();
                foreach (GameObject actor in actors.Values) ReleaseObject(actor);
                foreach (GameObject projectile in projectiles.Values) ReleaseObject(projectile);
                foreach (CombatCueView cue in cues) ReleaseObject(cue.Object);
                foreach (GameObject item in loot.Values) ReleaseObject(item);
                animations.Clear();
                actors.Clear();
                projectiles.Clear();
                cues.Clear();
                loot.Clear();
            }
            visibleActors.Clear();
            run.World.GetActorsSnapshot(snapshots);
            foreach (CombatActorSnapshot snapshot in snapshots)
            {
                if (snapshot.RoomId != run.CurrentRoom.Plan.Request.RegionId || !run.World.TryGetAnimationSnapshot(snapshot.Entity, out var motion)) continue;
                if (snapshot.Dead && snapshot.ActorId != 1 && motion.DeathElapsedTicks * CombatWorld.TickSeconds +
                    run.DeathPresentationElapsed >= CombatCharacterAnimation.DeathDurationTicks * CombatWorld.TickSeconds) continue;
                if (!actors.TryGetValue(snapshot.ActorId, out GameObject actor))
                {
                    bool isPlayer = snapshot.ActorId == 1;
                    run.TryGetEnemyDefinition(snapshot.ActorId, out EnemyDefinition enemy);
                    GameObject prefab = isPlayer ? assets.Player : assets.GetEnemy(enemy.ID);
                    actor = CreateModel("Actor_" + snapshot.ActorId, prefab);
                    var player = ProjectContentConfig.Player;
                    actor.transform.localScale = isPlayer ? new Vector3((float)player.ScaleX, (float)player.ScaleY, (float)player.ScaleZ) :
                        enemy.ModelScale;
                    actors.Add(snapshot.ActorId, actor);
                    animations.Add(snapshot.ActorId, new CombatCharacterAnimation(actor));
                }
                visibleActors.Add(snapshot.ActorId);
                if (!actor.activeSelf) actor.SetActive(true);
                actor.transform.position = snapshot.Position + Vector3.up * actor.transform.localScale.y;
                animations[snapshot.ActorId].Sample(motion, run.DeathPresentationElapsed);
                if (snapshot.ActorId == 1)
                {
                    camera.transform.position = snapshot.Position + new Vector3(0, 18, -11.25f);
                    camera.orthographicSize = Mathf.Max(10, 7 / Mathf.Max(0.4f, camera.aspect));
                }
            }
            staleActors.Clear();
            foreach (var pair in actors) if (!visibleActors.Contains(pair.Key)) staleActors.Add(pair.Key);
            foreach (int actorId in staleActors)
            {
                animations[actorId].Dispose();
                animations.Remove(actorId);
                ReleaseObject(actors[actorId]);
                actors.Remove(actorId);
            }
            RefreshRegionObjects();
            foreach (GameObject item in loot.Values) item.SetActive(false);
            foreach (ExpeditionDrop drop in run.Drops)
            {
                if (drop.RoomId != run.CurrentRoom.Plan.Request.RegionId || drop.Collected) continue;
                if (!loot.TryGetValue(drop.Item.Id, out GameObject item))
                {
                    item = CreateModel("Loot_" + drop.Item.Id, assets.Loot);
                    ApplyTint(item, ProjectEquipmentRules.QualityColor(drop.Item.Quality));
                    item.transform.localScale = Vector3.one * 0.55f;
                    loot.Add(drop.Item.Id, item);
                }
                item.SetActive(true);
                bool selected = drop.Item.Id == run.SelectedLootId;
                item.transform.localScale = Vector3.one * (selected ? 0.72f : 0.55f);
                item.transform.position = drop.Position + Vector3.up * (selected ? 0.7f : 0.5f);
            }
            RefreshProjectiles();
            RefreshCues();
            feedback.Refresh(run.World, run.CurrentRoom.Plan.Request.RegionId, actors, run.PresentationSeconds);
            feedbackAudio.Refresh(run.FeedbackPaused, run.CurrentRoom.Plan.Request.RegionId);
        }

        /// <summary>
        /// 释放表现资源，销毁不会触发任何战斗效果。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            run.CombatEventPublished -= PresentEvent;
            feedback.Dispose();
            feedbackAudio.Dispose();
            foreach (CombatCharacterAnimation animation in animations.Values) animation.Dispose();
            animations.Clear();
            foreach (AnimatorPlayback animation in ambientAnimations) animation.Dispose();
            ambientAnimations.Clear();
            regionObjects.Clear();
            ReleaseObject(root);
            actors.Clear();
            loot.Clear();
            projectiles.Clear();
            cues.Clear();
        }

        /// <summary>
        /// 从当前冻结计划建立箱子、商人和入口出口，切区后释放上一组对象。
        /// </summary>
        private void RefreshRegionObjects()
        {
            RegionRuntimeState region = run.CurrentRoom;
            RegionPlan plan = region.Plan;
            if (presentedRegionId != plan.Request.RegionId)
            {
                foreach (AnimatorPlayback animation in ambientAnimations) animation.Dispose();
                ambientAnimations.Clear();
                foreach (GameObject instance in regionObjects.Values) ReleaseObject(instance);
                regionObjects.Clear();
                presentedRegionId = plan.Request.RegionId;
                foreach (RegionChestPlan chest in plan.Chests)
                {
                    GameObject instance = CreateModel("Chest_" + chest.Id, assets.Chest);
                    instance.transform.position = chest.Position + Vector3.up * 0.5f;
                    regionObjects.Add(chest.Id, instance);
                }
                foreach (RegionNpcPlan npc in plan.Npcs)
                {
                    GameObject instance = CreateModel("Npc_" + npc.Id, assets.Npc);
                    instance.transform.position = npc.Position + Vector3.up;
                    regionObjects.Add(npc.Id, instance);
                    ambientAnimations.Add(new AnimatorPlayback(instance.GetComponent<Animator>()));
                }
                regionObjects.Add("entry", CreateRing("Entry", plan.EntryPosition, plan.InteractionRadius, new Color(0.2f, 0.8f, 1f)));
                regionObjects.Add("exit", CreateRing("Exit", plan.ExitPosition, plan.InteractionRadius, new Color(0.5f, 0.5f, 0.5f)));
            }
            foreach (RegionChestPlan chest in plan.Chests)
            {
                RegionChestState state = region.Chests.Find(value => value.Id == chest.Id);
                GameObject instance = regionObjects[chest.Id];
                bool bossReward = chest.Kind == RegionChestKind.BossReward;
                instance.SetActive(!bossReward || region.BossDefeated);
                bool received = bossReward ? region.BossRewardsCompleted : state.Opened;
                ApplyTint(instance, received ? new Color(0.45f, 0.5f, 0.5f) : new Color(1f, 0.8f, 0.3f));
                instance.transform.localScale = Vector3.one * (received ? 0.7f : 1f);
            }
            foreach (RegionNpcPlan npc in plan.Npcs)
            {
                RegionNpcState state = region.Npcs.Find(value => value.Id == npc.Id);
                ApplyTint(regionObjects[npc.Id], state.SoldOfferIds.Count == npc.Offers.Count ? new Color(0.4f, 0.5f, 0.55f) : Color.white);
            }
            SetRingColor(regionObjects["exit"], region.ExitEnabled ? new Color(0.3f, 1f, 0.5f) : new Color(0.5f, 0.5f, 0.5f));
            var settings = GameTables.CfgCombatAnimationSettings.GetById(1);
            float idlePhase = Mathf.Repeat((float)(run.World.TickIndex * CombatWorld.TickSeconds) / (float)settings.IdleSeconds, 1f);
            foreach (AnimatorPlayback animation in ambientAnimations) animation.TrySample(settings.IdleState, idlePhase);

        }

        /// <summary>
        /// 创建只负责提示的原生描边圈，不添加物理组件。
        /// </summary>
        /// <param name="name">提示名称。</param>
        /// <param name="position">冻结业务位置。</param>
        /// <param name="radius">提示范围。</param>
        /// <param name="color">提示颜色。</param>
        /// <returns>归属于表现根节点的提示对象。</returns>
        private GameObject CreateRing(string name, Vector3 position, float radius, Color color)
        {
            var instance = new GameObject(name, typeof(LineRenderer));
            instance.transform.SetParent(root.transform, false);
            instance.transform.position = position + Vector3.up * 0.05f;
            LineRenderer line = instance.GetComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = 32;
            line.widthMultiplier = 0.1f;
            line.sharedMaterial = assets.Loot.GetComponentInChildren<Renderer>().sharedMaterial;
            for (int index = 0; index < line.positionCount; index++)
            {
                float angle = index * Mathf.PI * 2f / line.positionCount;
                line.SetPosition(index, new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius));
            }
            SetRingColor(instance, color);
            return instance;
        }

        /// <summary>
        /// 更新提示描边颜色，不创建材质副本。
        /// </summary>
        /// <param name="instance">描边圈对象。</param>
        /// <param name="color">目标颜色。</param>
        private void SetRingColor(GameObject instance, Color color)
        {
            LineRenderer line = instance.GetComponent<LineRenderer>();
            line.startColor = color;
            line.endColor = color;
            ApplyTint(instance, color);
        }

        /// <summary>
        /// 更新飞弹快照对应的占位表现，结束的实体不遗留模型。
        /// </summary>
        private void RefreshProjectiles()
        {
            run.World.GetProjectilesSnapshot(projectileSnapshots);
            visibleProjectiles.Clear();
            foreach (CombatProjectileSnapshot snapshot in projectileSnapshots)
            {
                if (snapshot.RoomId != run.CurrentRoom.Plan.Request.RegionId) continue;
                visibleProjectiles.Add(snapshot.Entity);
                CfgCombatProjectilesRow definition = GameTables.CfgCombatProjectiles.GetById(snapshot.ProjectileId);
                if (!projectiles.TryGetValue(snapshot.Entity, out GameObject model))
                {
                    model = CreateModel("Projectile_" + snapshot.Entity.Id, assets.Projectile);
                    model.transform.localScale = new Vector3((float)definition.VisualScaleX, (float)definition.VisualScaleY, (float)definition.VisualScaleZ);
                    ApplyTint(model, new Color((float)definition.VisualRed, (float)definition.VisualGreen, (float)definition.VisualBlue));
                    projectiles.Add(snapshot.Entity, model);
                }
                model.transform.position = snapshot.Position + Vector3.up * (float)definition.VisualHeight;
                if (snapshot.Direction.sqrMagnitude > 0.001f) model.transform.rotation = Quaternion.LookRotation(snapshot.Direction);
            }
            staleProjectiles.Clear();
            foreach (var pair in projectiles)
                if (!visibleProjectiles.Contains(pair.Key)) staleProjectiles.Add(pair.Key);
            foreach (CombatEntityRef id in staleProjectiles)
            {
                ReleaseObject(projectiles[id]);
                projectiles.Remove(id);
            }
        }

        /// <summary>
        /// 由技能时间轴提示驱动挥击占位动画，不从动画回调生成伤害。
        /// </summary>
        /// <param name="fact">已经确认的战斗事件。</param>
        private void PresentEvent(CombatEvent fact)
        {
            if (disposed) return;
            if (fact.RoomId == run.CurrentRoom.Plan.Request.RegionId)
            {
                feedback.Present(fact);
                feedbackAudio.Refresh(run.FeedbackPaused, run.CurrentRoom.Plan.Request.RegionId);
                feedbackAudio.Present(fact);
            }
            if (fact.Kind == CombatEventKind.Death || fact.Kind == CombatEventKind.CastCancelled)
            {
                int owner = fact.Kind == CombatEventKind.Death ? fact.TargetActorId : fact.ActorId;
                for (int index = cues.Count - 1; index >= 0; index--)
                {
                    if (cues[index].ActorId != owner) continue;
                    ReleaseObject(cues[index].Object);
                    cues.RemoveAt(index);
                }
                return;
            }
            if (fact.RoomId != run.CurrentRoom.Plan.Request.RegionId || fact.Kind != CombatEventKind.Cue) return;
            if (GameTables.CfgCombatAnimationCues.GetById(fact.DefinitionId)?.IsAttack != true) return;
            Vector3 direction = Vector3.forward;
            int duration = 0;
            run.World.GetActorsSnapshot(snapshots);
            foreach (CombatActorSnapshot actor in snapshots)
            {
                if (actor.ActorId != fact.ActorId || !run.World.TryGetAnimationSnapshot(actor.Entity, out var motion)) continue;
                if (motion.Dead) return;
                direction = motion.CueDirection;
                duration = Mathf.Max(1, motion.CueDurationTicks);
                break;
            }
            if (duration <= 0) return;
            GameObject model = CreateModel("SkillCue", assets.MeleeCue);
            ApplyTint(model, fact.ActorId == 1 ? new Color(0.7f, 1, 1) : new Color(1, 0.55f, 0.2f));
            bool playerAttack = TryGetPlayerAttackCue(fact, out CombatTelegraphSnapshot telegraph);
            float attackRange = playerAttack ? telegraph.Radius : 1.7f;
            bool thrust = playerAttack && telegraph.Shape == CombatHitShape.ForwardBox;
            model.transform.localScale = new Vector3(0.14f, 0.14f, attackRange);
            cues.Add(new CombatCueView { Object = model, ActorId = fact.ActorId, RoomId = fact.RoomId,
                StartTick = fact.Tick, DurationTicks = duration, Direction = direction, Offset = playerAttack ? attackRange * 0.5f : 1f,
                Angle = playerAttack ? telegraph.Angle : 130f, Thrust = thrust });
        }

        /// <summary>
        /// 识别玩家攻击提示并读取对应技能已经冻结的真实命中形状。
        /// </summary>
        /// <param name="fact">当前提示事件。</param>
        /// <param name="result">匹配时返回本次攻击的预警快照。</param>
        /// <returns>提示属于玩家攻击且存在对应命中快照时为真。</returns>
        private bool TryGetPlayerAttackCue(CombatEvent fact, out CombatTelegraphSnapshot result)
        {
            result = default;
            if (fact.ActorId != 1) return false;
            run.World.GetTelegraphsSnapshot(cueTelegraphs);
            foreach (CombatTelegraphSnapshot telegraph in cueTelegraphs)
            {
                if (telegraph.ActorId != fact.ActorId || !telegraph.IsPlayer || telegraph.IsProjectile) continue;
                foreach (CfgCombatTimelineNodesRow node in GameTables.CfgCombatTimelineNodes.GetAll())
                {
                    if (node.SkillID != telegraph.SkillId) continue;
                    foreach (int effectId in node.EffectIDs)
                    {
                        CfgCombatEffectsRow effect = GameTables.CfgCombatEffects.GetById(effectId);
                        if (effect.Kind != (int)CombatEffectKind.Cue || effect.DefinitionID != fact.DefinitionId) continue;
                        result = telegraph;
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// 让提示跟随角色并按战斗帧旋转，换图即清理旧提示。
        /// </summary>
        private void RefreshCues()
        {
            for (int index = cues.Count - 1; index >= 0; index--)
            {
                CombatCueView cue = cues[index];
                float progress = (run.World.TickIndex - cue.StartTick) / (float)cue.DurationTicks;
                if (progress >= 1 || cue.RoomId != run.CurrentRoom.Plan.Request.RegionId || !actors.TryGetValue(cue.ActorId, out GameObject actor) || !actor.activeSelf)
                {
                    ReleaseObject(cue.Object);
                    cues.RemoveAt(index);
                    continue;
                }
                Vector3 direction = cue.Thrust ? cue.Direction :
                    Quaternion.Euler(0, Mathf.Lerp(-cue.Angle * 0.5f, cue.Angle * 0.5f, progress), 0) * cue.Direction;
                float offset = cue.Thrust ? cue.Offset * Mathf.Sin(Mathf.PI * progress) : cue.Offset;
                cue.Object.transform.position = actor.transform.position + direction * offset;
                cue.Object.transform.rotation = Quaternion.LookRotation(direction);
            }
        }

        /// <summary>
        /// 根据当前运行模式释放 Unity 表现对象。
        /// </summary>
        /// <param name="value">需要释放的对象。</param>
        private static void ReleaseObject(GameObject value)
        {
            if (value == null) return;
            value.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        /// <summary>
        /// 实例化已加载模型，资产租约由场景保持到所有实例释放后。
        /// </summary>
        /// <param name="name">模型名称。</param>
        /// <param name="prefab">热更资源 Prefab。</param>
        /// <returns>归属于本局表现根节点的对象。</returns>
        private GameObject CreateModel(string name, GameObject prefab)
        {
            GameObject instance = UnityEngine.Object.Instantiate(prefab, root.transform, false);
            instance.name = name;
            return instance;
        }

        /// <summary>
        /// 设置实例颜色，不修改共享资源材质或创建材质副本。
        /// </summary>
        /// <param name="instance">需要染色的模型实例。</param>
        /// <param name="color">品质或阵营颜色。</param>
        private void ApplyTint(GameObject instance, Color color)
        {
            tint.Clear();
            tint.SetColor(ColorId, color);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true)) renderer.SetPropertyBlock(tint);
        }
    }
}
