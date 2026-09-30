using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 由 ECS 快照更新占位角色、战利品和俯视相机。
    /// </summary>
    public sealed class ExpeditionPresentation : IDisposable
    {
        private readonly ExpeditionRun run;
        private readonly GameObject root;
        private readonly Camera camera;
        private readonly Dictionary<int, GameObject> actors = new();
        private readonly Dictionary<string, GameObject> loot = new();
        private readonly List<CombatActorSnapshot> snapshots = new();
        private readonly Dictionary<CombatEntityRef, GameObject> projectiles = new();
        private readonly List<CombatProjectileSnapshot> projectileSnapshots = new();
        private readonly HashSet<CombatEntityRef> visibleProjectiles = new();
        private readonly List<CombatEntityRef> staleProjectiles = new();
        private readonly Dictionary<Color, Material> materials = new();
        private readonly List<CombatCueView> cues = new();
        private bool disposed;

        /// <summary>
        /// 时间轴表现提示，只读取模拟时间而不参与伤害判定。
        /// </summary>
        private sealed class CombatCueView
        {
            public GameObject Object;
            public int ActorId;
            public int RoomId;
            public long StartTick;
            public Vector3 Direction;
        }

        /// <summary>
        /// 创建本局独立表现根节点与相机。
        /// </summary>
        /// <param name="run">读取快照的单局。</param>
        public ExpeditionPresentation(ExpeditionRun run)
        {
            this.run = run;
            root = new GameObject("ExpeditionPresentation");
            var cameraObject = new GameObject("ExpeditionCamera", typeof(Camera));
            cameraObject.transform.SetParent(root.transform, false);
            camera = cameraObject.GetComponent<Camera>();
            camera.depth = 10;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.06f, 0.09f, 0.13f);
            camera.orthographic = true;
            camera.orthographicSize = 11;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100;
            camera.transform.rotation = Quaternion.Euler(58, 0, 0);
            var lightObject = new GameObject("ExpeditionLight", typeof(Light));
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.rotation = Quaternion.Euler(55, -30, 0);
            Light light = lightObject.GetComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            run.CombatEventPublished += PresentEvent;
            Refresh();
        }

        /// <summary>
        /// 同步当前地图的角色与战利品，隐藏冻结地图的表现。
        /// </summary>
        public void Refresh()
        {
            if (disposed) return;
            foreach (GameObject actor in actors.Values) actor.SetActive(false);
            run.World.GetActorsSnapshot(snapshots);
            foreach (CombatActorSnapshot snapshot in snapshots)
            {
                if (snapshot.RoomId != run.Maps.CurrentRoomId || snapshot.Dead) continue;
                if (!actors.TryGetValue(snapshot.ActorId, out GameObject actor))
                {
                    actor = CreatePrimitive("Actor_" + snapshot.ActorId, PrimitiveType.Capsule,
                        snapshot.ActorId == 1 ? new Color(0.25f, 0.8f, 0.85f) : new Color(0.86f, 0.29f, 0.24f));
                    actors.Add(snapshot.ActorId, actor);
                }
                actor.SetActive(true);
                actor.transform.position = snapshot.Position + Vector3.up * 1.1f;
                if (snapshot.ActorId == 1)
                {
                    camera.transform.position = snapshot.Position + new Vector3(0, 18, -11.25f);
                    camera.orthographicSize = Mathf.Max(10, 7 / Mathf.Max(0.4f, camera.aspect));
                }
            }
            foreach (GameObject item in loot.Values) item.SetActive(false);
            foreach (ExpeditionDrop drop in run.Drops)
            {
                if (drop.RoomId != run.Maps.CurrentRoomId || drop.Collected) continue;
                if (!loot.TryGetValue(drop.Item.Id, out GameObject item))
                {
                    item = CreatePrimitive("Loot_" + drop.Item.Id, PrimitiveType.Cube, new Color(0.96f, 0.8f, 0.25f));
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
            ReleaseObject(root);
            foreach (Material material in materials.Values) ReleaseObject(material);
            actors.Clear();
            loot.Clear();
            projectiles.Clear();
            cues.Clear();
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
                    model = CreatePrimitive("Projectile_" + snapshot.Entity.Id, PrimitiveType.Sphere, new Color(1, 0.45f, 0.13f));
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
            if (disposed || fact.RoomId != run.Maps.CurrentRoomId || fact.Kind != CombatEventKind.Cue) return;
            Vector3 direction = Vector3.forward;
            run.World.GetActorsSnapshot(snapshots);
            foreach (CombatActorSnapshot actor in snapshots)
            {
                if (actor.ActorId != fact.TargetActorId) continue;
                direction = actor.Position - fact.Position;
                direction.y = 0;
                if (direction.sqrMagnitude > 0.001f) direction.Normalize();
                else direction = Vector3.forward;
                break;
            }
            GameObject model = CreatePrimitive("SkillCue", PrimitiveType.Cube,
                fact.ActorId == 1 ? new Color(0.7f, 1, 1) : new Color(1, 0.55f, 0.2f));
            model.transform.localScale = new Vector3(0.14f, 0.14f, 1.7f);
            cues.Add(new CombatCueView { Object = model, ActorId = fact.ActorId, RoomId = fact.RoomId,
                StartTick = fact.Tick, Direction = direction });
        }

        /// <summary>
        /// 让提示跟随角色并按战斗帧旋转，换图即清理旧提示。
        /// </summary>
        private void RefreshCues()
        {
            for (int index = cues.Count - 1; index >= 0; index--)
            {
                CombatCueView cue = cues[index];
                float progress = (run.World.TickIndex - cue.StartTick) / 18f;
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
        private static void ReleaseObject(UnityEngine.Object value)
        {
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }

        /// <summary>
        /// 创建不参与权威碰撞的占位模型。
        /// </summary>
        /// <param name="name">模型名称。</param>
        /// <param name="type">基础几何体类型。</param>
        /// <param name="color">材质颜色。</param>
        /// <returns>归属于本局表现根节点的对象。</returns>
        private GameObject CreatePrimitive(string name, PrimitiveType type, Color color)
        {
            GameObject instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(root.transform, false);
            Collider collider = instance.GetComponent<Collider>();
            if (collider != null) { collider.enabled = false; ReleaseObject(collider); }
            Shader shader = Shader.Find("Standard");
            if (shader != null)
            {
                if (!materials.TryGetValue(color, out Material material))
                {
                    material = new Material(shader) { color = color };
                    materials.Add(color, material);
                }
                instance.GetComponent<Renderer>().sharedMaterial = material;
            }
            return instance;
        }
    }
}
