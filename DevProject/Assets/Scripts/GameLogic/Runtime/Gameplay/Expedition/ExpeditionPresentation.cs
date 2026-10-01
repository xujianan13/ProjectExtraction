using GameLogic.Project;
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
        private GameObject ambientRoom;
        private AnimatorPlayback ambientAnimation;
        private readonly Dictionary<string, GameObject> loot = new();
        private readonly List<CombatActorSnapshot> snapshots = new();
        private readonly Dictionary<CombatEntityRef, GameObject> projectiles = new();
        private readonly List<CombatProjectileSnapshot> projectileSnapshots = new();
        private readonly HashSet<CombatEntityRef> visibleProjectiles = new();
        private readonly List<CombatEntityRef> staleProjectiles = new();
        private readonly MaterialPropertyBlock tint = new();
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly List<CombatCueView> cues = new();
        private bool disposed;

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
            run.CombatEventPublished += PresentEvent;
            Refresh();
        }

        /// <summary>
        /// 同步当前地图的角色与战利品，隐藏冻结地图的表现。
        /// </summary>
        public void Refresh()
        {
            if (disposed) return;
            visibleActors.Clear();
            run.World.GetActorsSnapshot(snapshots);
            foreach (CombatActorSnapshot snapshot in snapshots)
            {
                if (snapshot.RoomId != run.Maps.CurrentRoomId || !run.World.TryGetAnimationSnapshot(snapshot.Entity, out var motion)) continue;
                if (snapshot.Dead && motion.DeathElapsedTicks >= CombatCharacterAnimation.DeathDurationTicks) continue;
                if (!actors.TryGetValue(snapshot.ActorId, out GameObject actor))
                {
                    bool isPlayer = snapshot.ActorId == 1;
                    bool isBoss = !isPlayer && run.Maps.TryGetRoom(snapshot.RoomId, out LevelRoomState room) && room.Type == LevelRoomType.Boss;
                    actor = CreateModel("Actor_" + snapshot.ActorId, isPlayer ? assets.Player : isBoss ? assets.Boss : assets.Enemy);
                    actor.transform.localScale = isBoss ? new Vector3(1.8f, 1.5f, 1.8f) : new Vector3(0.9f, 1f, 0.9f);
                    actors.Add(snapshot.ActorId, actor);
                    animations.Add(snapshot.ActorId, new CombatCharacterAnimation(actor));
                }
                visibleActors.Add(snapshot.ActorId);
                if (!actor.activeSelf) actor.SetActive(true);
                actor.transform.position = snapshot.Position + Vector3.up * actor.transform.localScale.y;
                animations[snapshot.ActorId].Sample(motion);
                if (snapshot.ActorId == 1)
                {
                    camera.transform.position = snapshot.Position + new Vector3(0, 18, -11.25f);
                    camera.orthographicSize = Mathf.Max(10, 7 / Mathf.Max(0.4f, camera.aspect));
                }
            }
            foreach (var pair in actors)
                if (!visibleActors.Contains(pair.Key) && pair.Value.activeSelf) pair.Value.SetActive(false);
            RefreshAmbientAnimation();
            foreach (GameObject item in loot.Values) item.SetActive(false);
            foreach (ExpeditionDrop drop in run.Drops)
            {
                if (drop.RoomId != run.Maps.CurrentRoomId || drop.Collected) continue;
                if (!loot.TryGetValue(drop.Item.Id, out GameObject item))
                {
                    item = CreateModel("Loot_" + drop.Item.Id, assets.Loot);
                    ApplyTint(item, ProjectEquipmentRules.QualityColor(drop.Item.Quality));
                    item.transform.localScale = Vector3.one * 0.55f;
                    loot.Add(drop.Item.Id, item);
                }
                item.SetActive(true);
                item.transform.position = drop.Position + Vector3.up * 0.5f;
            }
            RefreshProjectiles();
            RefreshCues();
        }

        /// <summary>
        /// 释放表现资源，销毁不会触发任何战斗效果。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            run.CombatEventPublished -= PresentEvent;
            foreach (CombatCharacterAnimation animation in animations.Values) animation.Dispose();
            animations.Clear();
            ambientAnimation?.Dispose();
            ambientAnimation = null;
            ambientRoom = null;
            ReleaseObject(root);
            actors.Clear();
            loot.Clear();
            projectiles.Clear();
            cues.Clear();
        }

        /// <summary>
        /// 用通用播放器驱动房间内 NPC 的待机，使三选一暂停时环境角色也同步冻结。
        /// </summary>
        private void RefreshAmbientAnimation()
        {
            GameObject room = run.RoomView?.Root;
            if (ambientRoom != room)
            {
                ambientAnimation?.Dispose();
                ambientAnimation = null;
                ambientRoom = room;
                Animator npc = room != null ? room.GetComponentInChildren<Animator>() : null;
                if (npc != null) ambientAnimation = new AnimatorPlayback(npc);
            }
            ambientAnimation?.TrySample("Base Layer.Idle", Mathf.Repeat((float)(run.World.TickIndex * CombatWorld.TickSeconds), 1f));
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
                if (snapshot.RoomId != run.Maps.CurrentRoomId) continue;
                visibleProjectiles.Add(snapshot.Entity);
                if (!projectiles.TryGetValue(snapshot.Entity, out GameObject model))
                {
                    model = CreateModel("Projectile_" + snapshot.Entity.Id, assets.Projectile);
                    model.transform.localScale = Vector3.one * 0.35f;
                    projectiles.Add(snapshot.Entity, model);
                }
                model.transform.position = snapshot.Position + Vector3.up * 0.7f;
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
            if (fact.RoomId != run.Maps.CurrentRoomId || fact.Kind != CombatEventKind.Cue) return;
            if (fact.DefinitionId != 1 && fact.DefinitionId != 20 && fact.DefinitionId != 30) return;
            Vector3 direction = Vector3.forward;
            int duration = 18;
            run.World.GetActorsSnapshot(snapshots);
            foreach (CombatActorSnapshot actor in snapshots)
            {
                if (actor.ActorId != fact.ActorId || !run.World.TryGetAnimationSnapshot(actor.Entity, out var motion)) continue;
                if (motion.Dead) return;
                direction = motion.CueDirection;
                duration = Mathf.Max(1, motion.CueDurationTicks);
                break;
            }
            GameObject model = CreateModel("SkillCue", assets.MeleeCue);
            ApplyTint(model, fact.ActorId == 1 ? new Color(0.7f, 1, 1) : new Color(1, 0.55f, 0.2f));
            model.transform.localScale = new Vector3(0.14f, 0.14f, 1.7f);
            cues.Add(new CombatCueView { Object = model, ActorId = fact.ActorId, RoomId = fact.RoomId,
                StartTick = fact.Tick, DurationTicks = duration, Direction = direction });
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
                if (progress >= 1 || cue.RoomId != run.Maps.CurrentRoomId || !actors.TryGetValue(cue.ActorId, out GameObject actor) || !actor.activeSelf)
                {
                    ReleaseObject(cue.Object);
                    cues.RemoveAt(index);
                    continue;
                }
                Vector3 direction = Quaternion.Euler(0, Mathf.Lerp(-65, 65, progress), 0) * cue.Direction;
                cue.Object.transform.position = actor.transform.position + direction;
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
