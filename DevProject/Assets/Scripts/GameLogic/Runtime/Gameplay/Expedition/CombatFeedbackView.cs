using System;
using System.Collections.Generic;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 将已确认的战斗事件和只读快照显示为局内反馈。
    /// </summary>
    public sealed class CombatFeedbackView : IDisposable
    {
        private const int TextCapacity = 32;
        private const int HitCapacity = 16;
        private const int TelegraphCapacity = 24;
        private const float TextLifetime = 0.85f;
        private const float HitLifetime = 0.25f;
        private const float FlashLifetime = 0.13f;
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private readonly Transform root;
        private readonly Camera camera;
        private readonly CombatFeedbackAssets assets;
        private readonly List<CombatActorSnapshot> actors = new(32);
        private readonly List<CombatTelegraphSnapshot> telegraphs = new(16);
        private readonly List<CombatBuffSnapshot> buffs = new(16);
        private readonly Dictionary<int, StatusView> statuses = new();
        private readonly Stack<StatusView> statusPool = new();
        private readonly List<int> staleStatuses = new(16);
        private readonly Dictionary<int, int> statusRows = new();
        private readonly Dictionary<int, HealthView> health = new();
        private readonly List<int> staleHealth = new(32);
        private readonly TextView[] textPool = new TextView[TextCapacity];
        private readonly HitView[] hitPool = new HitView[HitCapacity];
        private readonly TelegraphView[] telegraphPool = new TelegraphView[TelegraphCapacity];
        private readonly Dictionary<int, FlashView> flashes = new();
        private readonly List<int> staleFlashes = new(16);
        private readonly MaterialPropertyBlock block = new();
        private IReadOnlyDictionary<int, GameObject> models;
        private int roomId = -1;
        private double visualSeconds;
        private bool disposed;

        private sealed class HealthView
        {
            public GameObject Object;
            public UnityEngine.UI.Image Fill;
            public float FillWidth;
            public bool Seen;
        }

        private sealed class StatusView
        {
            public GameObject Object;
            public UnityEngine.UI.Text Label;
            public bool Seen;
        }

        private sealed class TextView
        {
            public GameObject Object;
            public UnityEngine.UI.Text Label;
            public Vector3 Origin;
            public Color Color;
            public Vector3 BaseScale;
            public double Start;
            public bool Active;
        }

        private sealed class HitView
        {
            public GameObject Object;
            public Renderer[] Renderers;
            public Vector3 BaseScale;
            public Vector3 Origin;
            public double Start;
            public bool Active;
        }

        private sealed class TelegraphView
        {
            public GameObject Object;
            public LineRenderer Line;
        }

        private sealed class FlashView
        {
            public GameObject Model;
            public Renderer[] Renderers;
            public MaterialPropertyBlock[] Originals;
            public double Start;
        }

        /// <summary>
        /// 创建战斗反馈表现并使用已加载的热更预制体。
        /// </summary>
        /// <param name="parent">本局表现的父节点。</param>
        /// <param name="camera">俯视相机。</param>
        /// <param name="assets">场景作用域持有租约的反馈资源。</param>
        public CombatFeedbackView(Transform parent, Camera camera, CombatFeedbackAssets assets)
        {
            root = new GameObject("CombatFeedback").transform;
            root.SetParent(parent, false);
            this.camera = camera;
            this.assets = assets;
        }

        /// <summary>
        /// 显示已确认的伤害、闪避或实际治疗事件。
        /// </summary>
        /// <param name="fact">已确认的战斗事件。</param>
        public void Present(CombatEvent fact)
        {
            if (disposed || fact.RoomId != roomId) return;
            var style = ProjectCombatFeedbackConfig.Event(fact);
            if (style == null || fact.Kind == CombatEventKind.Heal && fact.Value <= 0f) return;
            string value = fact.Value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            string caption = style.Text.Replace("{value}", value);
            ShowText(fact.Position + Vector3.up * 1.8f, caption, ProjectCombatFeedbackConfig.Color(style),
                fact.Kind == CombatEventKind.Damage && fact.IsCritical);
            if (fact.Kind != CombatEventKind.Damage || fact.Value <= 0f) return;
            if (style.ShowHit) ShowHit(fact.Position + Vector3.up * 0.95f);
            if (style.ShowFlash && models != null && models.TryGetValue(fact.TargetActorId, out GameObject model) &&
                model != null && model.activeInHierarchy) Flash(fact.TargetActorId, model);
        }

        /// <summary>
        /// 同步血条、飘字、命中反馈和未执行技能节点的地面预警。
        /// </summary>
        /// <param name="world">提供只读战斗快照的世界。</param>
        /// <param name="roomId">当前可见房间编号。</param>
        /// <param name="models">当前角色编号到模型的映射。</param>
        /// <param name="visualSeconds">随战斗暂停的表现时间。</param>
        public void Refresh(CombatWorld world, int roomId, IReadOnlyDictionary<int, GameObject> models, double visualSeconds)
        {
            if (disposed) return;
            if (this.roomId != roomId)
            {
                ClearTransient();
                this.roomId = roomId;
            }
            this.models = models;
            this.visualSeconds = visualSeconds;
            foreach (HealthView view in health.Values) view.Seen = false;
            if (world != null) world.GetActorsSnapshot(actors);
            else actors.Clear();
            foreach (CombatActorSnapshot actor in actors)
            {
                if (actor.RoomId != roomId || actor.Dead || actor.Health <= 0f || models == null ||
                    !models.TryGetValue(actor.ActorId, out GameObject model) || model == null || !model.activeInHierarchy) continue;
                if (!health.TryGetValue(actor.ActorId, out HealthView view))
                {
                    if (assets.HealthBar == null) continue;
                    GameObject instance = UnityEngine.Object.Instantiate(assets.HealthBar, root, false);
                    UnityEngine.UI.Image fill = instance.transform.Find("Fill")?.GetComponent<UnityEngine.UI.Image>();
                    view = new HealthView { Object = instance, Fill = fill, FillWidth = fill != null ? fill.rectTransform.sizeDelta.x : 0f };
                    health.Add(actor.ActorId, view);
                }
                view.Seen = true;
                view.Object.SetActive(true);
                view.Object.transform.position = model.transform.position + Vector3.up * (1.25f * model.transform.localScale.y);
                FaceCamera(view.Object.transform);
                if (view.Fill != null)
                {
                    RectTransform rect = view.Fill.rectTransform;
                    Vector2 size = rect.sizeDelta;
                    size.x = view.FillWidth * (actor.MaxHealth > 0f ? Mathf.Clamp01(actor.Health / actor.MaxHealth) : 0f);
                    rect.sizeDelta = size;
                }
            }
            staleHealth.Clear();
            foreach (var pair in health)
                if (!pair.Value.Seen) staleHealth.Add(pair.Key);
            foreach (int id in staleHealth)
            {
                Release(health[id].Object);
                health.Remove(id);
            }
            UpdateStatuses(world, roomId);
            UpdateTransient();
            UpdateTelegraphs(world, roomId);
        }

        /// <summary>
        /// 释放所有实例并恢复模型原有属性块。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            ClearTransient();
            ClearStatuses(true);
            foreach (HealthView view in health.Values) Release(view.Object);
            health.Clear();
            Release(root.gameObject);
        }

        /// <summary>
        /// 按当前房间真实 Buff 快照刷新角色血条附近的状态标签。
        /// </summary>
        /// <param name="world">提供只读状态快照的战斗世界。</param>
        /// <param name="currentRoom">当前可见房间编号。</param>
        private void UpdateStatuses(CombatWorld world, int currentRoom)
        {
            if (world != null) world.GetBuffsSnapshot(buffs);
            else buffs.Clear();
            foreach (StatusView view in statuses.Values) view.Seen = false;
            statusRows.Clear();
            foreach (CombatBuffSnapshot buff in buffs)
            {
                bool persistent = buff.Lifetime == CombatBuffLifetime.Persistent;
                if (buff.RoomId != currentRoom || !persistent && buff.RemainingTicks <= 0 || !health.TryGetValue(buff.ActorId, out HealthView owner)) continue;
                var style = ProjectCombatFeedbackConfig.Status(buff.DefinitionId);
                if (style == null || assets.DamageText == null) continue;
                if (!statuses.TryGetValue(buff.InstanceId, out StatusView view))
                {
                    view = statusPool.Count > 0 ? statusPool.Pop() : CreateStatus();
                    statuses.Add(buff.InstanceId, view);
                }
                statusRows.TryGetValue(buff.ActorId, out int row);
                statusRows[buff.ActorId] = row + 1;
                view.Seen = true;
                view.Object.SetActive(true);
                view.Object.transform.position = owner.Object.transform.position + Vector3.up * (0.27f + row * 0.21f);
                FaceCamera(view.Object.transform);
                if (view.Label != null)
                {
                    float seconds = Mathf.Max(0.1f, buff.RemainingTicks * CombatWorld.TickSeconds);
                    string duration = persistent ? "常驻" : seconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + "s";
                    view.Label.text = style.Text + " ×" + buff.Stacks + " " + duration;
                    view.Label.color = ProjectCombatFeedbackConfig.Color(style);
                }
            }
            staleStatuses.Clear();
            foreach (var pair in statuses)
                if (!pair.Value.Seen) staleStatuses.Add(pair.Key);
            foreach (int id in staleStatuses)
            {
                StatusView view = statuses[id];
                statuses.Remove(id);
                view.Object.SetActive(false);
                statusPool.Push(view);
            }
        }

        /// <summary>
        /// 创建使用现有飘字预制体的持续状态标签。
        /// </summary>
        /// <returns>可复用的状态标签。</returns>
        private StatusView CreateStatus()
        {
            GameObject instance = UnityEngine.Object.Instantiate(assets.DamageText, root, false);
            instance.name = "CombatStatus";
            return new StatusView { Object = instance, Label = instance.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>() };
        }

        /// <summary>
        /// 换房时归还状态标签，销毁时释放池中的全部对象。
        /// </summary>
        /// <param name="release">是否同时销毁状态标签对象。</param>
        private void ClearStatuses(bool release)
        {
            foreach (StatusView view in statuses.Values)
            {
                view.Object.SetActive(false);
                statusPool.Push(view);
            }
            statuses.Clear();
            buffs.Clear();
            if (!release) return;
            foreach (StatusView view in statusPool) Release(view.Object);
            statusPool.Clear();
        }

        /// <summary>
        /// 从有限池取用飘字并立即显示首帧。
        /// </summary>
        /// <param name="position">文字的世界坐标。</param>
        /// <param name="value">显示内容。</param>
        /// <param name="color">文字颜色。</param>
        /// <param name="critical">是否放大暴击文字。</param>
        private void ShowText(Vector3 position, string value, Color color, bool critical)
        {
            if (assets.DamageText == null) return;
            int slot = 0;
            for (int i = 0; i < textPool.Length; i++)
                if (textPool[i] == null || !textPool[i].Active) { slot = i; break; }
                else if (textPool[i].Start < textPool[slot].Start) slot = i;
            TextView view = textPool[slot];
            if (view == null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(assets.DamageText, root, false);
                view = new TextView { Object = instance, Label = instance.transform.Find("Label")?.GetComponent<UnityEngine.UI.Text>(),
                    BaseScale = instance.transform.localScale };
                textPool[slot] = view;
            }
            view.Active = true;
            view.Start = visualSeconds;
            view.Origin = position + Vector3.right * ((slot % 4 - 1.5f) * 0.18f) + Vector3.up * ((slot / 4 % 3) * 0.13f);
            view.Color = color;
            view.Object.SetActive(true);
            view.Object.transform.position = view.Origin;
            view.Object.transform.localScale = view.BaseScale * (critical ? 1.25f : 1f);
            FaceCamera(view.Object.transform);
            if (view.Label != null) { view.Label.text = value; view.Label.color = color; }
        }

        /// <summary>
        /// 从有限池取用命中特效并立即显示首帧。
        /// </summary>
        /// <param name="position">命中特效的世界坐标。</param>
        private void ShowHit(Vector3 position)
        {
            if (assets.Hit == null) return;
            int slot = 0;
            for (int i = 0; i < hitPool.Length; i++)
                if (hitPool[i] == null || !hitPool[i].Active) { slot = i; break; }
                else if (hitPool[i].Start < hitPool[slot].Start) slot = i;
            HitView view = hitPool[slot];
            if (view == null)
            {
                GameObject instance = UnityEngine.Object.Instantiate(assets.Hit, root, false);
                view = new HitView { Object = instance, Renderers = instance.GetComponentsInChildren<Renderer>(true),
                    BaseScale = instance.transform.localScale };
                hitPool[slot] = view;
            }
            view.Active = true;
            view.Start = visualSeconds;
            view.Origin = position;
            view.Object.SetActive(true);
            view.Object.transform.position = position;
            view.Object.transform.localScale = view.BaseScale;
            block.Clear();
            block.SetColor(ColorId, Color.white);
            foreach (Renderer renderer in view.Renderers) if (renderer != null) renderer.SetPropertyBlock(block);
        }

        /// <summary>
        /// 暂时替换模型实例属性块以显示受击闪光。
        /// </summary>
        /// <param name="actorId">受击角色编号。</param>
        /// <param name="model">受击模型。</param>
        private void Flash(int actorId, GameObject model)
        {
            if (flashes.TryGetValue(actorId, out FlashView old)) RestoreFlash(old);
            Renderer[] renderers = model.GetComponentsInChildren<Renderer>(true);
            var originals = new MaterialPropertyBlock[renderers.Length];
            for (int i = 0; i < renderers.Length; i++)
            {
                originals[i] = new MaterialPropertyBlock();
                renderers[i].GetPropertyBlock(originals[i]);
                renderers[i].GetPropertyBlock(block);
                block.SetColor(ColorId, Color.white);
                renderers[i].SetPropertyBlock(block);
                block.Clear();
            }
            flashes[actorId] = new FlashView { Model = model, Renderers = renderers, Originals = originals, Start = visualSeconds };
        }

        /// <summary>
        /// 按暂停同步的表现时间更新短暂反馈并恢复到期闪光。
        /// </summary>
        private void UpdateTransient()
        {
            foreach (TextView view in textPool)
            {
                if (view == null || !view.Active) continue;
                float age = Mathf.Max(0f, (float)(visualSeconds - view.Start));
                if (age >= TextLifetime) { view.Active = false; view.Object.SetActive(false); continue; }
                view.Object.transform.position = view.Origin + Vector3.up * (age * 1.6f);
                FaceCamera(view.Object.transform);
                if (view.Label != null) { Color color = view.Color; color.a = 1f - age / TextLifetime; view.Label.color = color; }
            }
            foreach (HitView view in hitPool)
            {
                if (view == null || !view.Active) continue;
                float age = Mathf.Max(0f, (float)(visualSeconds - view.Start));
                if (age >= HitLifetime) { view.Active = false; view.Object.SetActive(false); continue; }
                view.Object.transform.localScale = view.BaseScale * (1f + age * 2f);
                block.Clear();
                block.SetColor(ColorId, new Color(1f, 1f, 1f, 1f - age / HitLifetime));
                foreach (Renderer renderer in view.Renderers) if (renderer != null) renderer.SetPropertyBlock(block);
            }
            staleFlashes.Clear();
            foreach (var pair in flashes)
                if (visualSeconds - pair.Value.Start >= FlashLifetime || pair.Value.Model == null || !pair.Value.Model.activeInHierarchy)
                    staleFlashes.Add(pair.Key);
            foreach (int id in staleFlashes) { RestoreFlash(flashes[id]); flashes.Remove(id); }
        }

        /// <summary>
        /// 根据尚未执行的时间轴节点刷新预警线。
        /// </summary>
        /// <param name="world">提供预警快照的战斗世界。</param>
        /// <param name="currentRoom">当前可见房间编号。</param>
        private void UpdateTelegraphs(CombatWorld world, int currentRoom)
        {
            if (world != null) world.GetTelegraphsSnapshot(telegraphs);
            else telegraphs.Clear();
            int count = 0;
            foreach (CombatTelegraphSnapshot snapshot in telegraphs)
            {
                if (snapshot.RoomId != currentRoom || count == telegraphPool.Length || assets.Telegraph == null) continue;
                TelegraphView view = telegraphPool[count];
                if (view == null)
                {
                    GameObject instance = UnityEngine.Object.Instantiate(assets.Telegraph, root, false);
                    view = new TelegraphView { Object = instance, Line = instance.GetComponent<LineRenderer>() };
                    telegraphPool[count] = view;
                }
                view.Object.SetActive(true);
                if (view.Line != null) DrawTelegraph(view.Line, snapshot, count);
                count++;
            }
            for (int i = count; i < telegraphPool.Length; i++)
                if (telegraphPool[i] != null && telegraphPool[i].Object.activeSelf) telegraphPool[i].Object.SetActive(false);
        }

        /// <summary>
        /// 按命中判定中的半径和宽度语义描出预警边界。
        /// </summary>
        /// <param name="line">复用的地面线组件。</param>
        /// <param name="snapshot">下一伤害节点的形状快照。</param>
        /// <param name="order">同帧预警的绘制顺序。</param>
        private void DrawTelegraph(LineRenderer line, CombatTelegraphSnapshot snapshot, int order)
        {
            Vector3 forward = Vector3.ProjectOnPlane(snapshot.Direction, Vector3.up).normalized;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            Vector3 side = new Vector3(-forward.z, 0f, forward.x);
            Vector3 origin = snapshot.Origin;
            origin.y = Mathf.Max(0.04f, origin.y + 0.05f + order * 0.002f);
            float radius = Mathf.Max(0f, snapshot.Radius);
            float width = Mathf.Max(0f, snapshot.Width) * 0.5f;
            line.useWorldSpace = true;
            Color color = snapshot.IsPlayer ? new Color(0.3f, 0.85f, 1f) : new Color(1f, 0.32f, 0.18f);
            color.a = Mathf.Lerp(0.35f, 0.95f, Mathf.Clamp01(snapshot.Progress));
            line.startColor = color;
            line.endColor = color;
            if (snapshot.Shape == CombatHitShape.Box || snapshot.Shape == CombatHitShape.ForwardBox)
            {
                Vector3 start = snapshot.Shape == CombatHitShape.ForwardBox ? origin : origin - forward * radius;
                line.positionCount = 5;
                line.SetPosition(0, start - side * width);
                line.SetPosition(1, start + side * width);
                line.SetPosition(2, origin + forward * radius + side * width);
                line.SetPosition(3, origin + forward * radius - side * width);
                line.SetPosition(4, start - side * width);
            }
            else if (snapshot.Shape == CombatHitShape.Capsule)
            {
                const int half = 12;
                line.positionCount = half * 2 + 3;
                Vector3 end = origin + forward * radius;
                for (int i = 0; i <= half; i++)
                {
                    float angle = Mathf.PI * i / half;
                    line.SetPosition(i, end + side * (Mathf.Cos(angle) * width) + forward * (Mathf.Sin(angle) * width));
                    line.SetPosition(i + half + 1, origin - side * (Mathf.Cos(angle) * width) - forward * (Mathf.Sin(angle) * width));
                }
                line.SetPosition(half * 2 + 2, line.GetPosition(0));
            }
            else if (snapshot.Shape == CombatHitShape.Sector)
            {
                const int steps = 20;
                line.positionCount = steps + 3;
                line.SetPosition(0, origin);
                for (int i = 0; i <= steps; i++)
                {
                    float angle = -snapshot.Angle * 0.5f + snapshot.Angle * i / steps;
                    line.SetPosition(i + 1, origin + Quaternion.AngleAxis(angle, Vector3.up) * forward * radius);
                }
                line.SetPosition(steps + 2, origin);
            }
            else
            {
                const int steps = 32;
                line.positionCount = steps + 1;
                for (int i = 0; i <= steps; i++)
                {
                    float angle = Mathf.PI * 2f * i / steps;
                    line.SetPosition(i, origin + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius);
                }
            }
        }

        /// <summary>
        /// 使世界空间界面面向当前相机。
        /// </summary>
        /// <param name="target">界面根节点。</param>
        private void FaceCamera(Transform target)
        {
            if (camera != null) target.rotation = camera.transform.rotation;
        }

        /// <summary>
        /// 换图或销毁时隐藏短暂反馈并恢复闪光。
        /// </summary>
        private void ClearTransient()
        {
            ClearStatuses(false);
            foreach (TextView view in textPool) if (view != null) { view.Active = false; view.Object.SetActive(false); }
            foreach (HitView view in hitPool) if (view != null) { view.Active = false; view.Object.SetActive(false); }
            foreach (TelegraphView view in telegraphPool) if (view != null) view.Object.SetActive(false);
            foreach (FlashView view in flashes.Values) RestoreFlash(view);
            flashes.Clear();
        }

        /// <summary>
        /// 恢复受击前每个渲染器的实例属性块。
        /// </summary>
        /// <param name="view">保存原始属性块的闪光记录。</param>
        private static void RestoreFlash(FlashView view)
        {
            for (int i = 0; i < view.Renderers.Length; i++)
                if (view.Renderers[i] != null) view.Renderers[i].SetPropertyBlock(view.Originals[i]);
        }

        /// <summary>
        /// 根据播放模式销毁表现对象。
        /// </summary>
        /// <param name="value">需要释放的对象。</param>
        private static void Release(GameObject value)
        {
            if (value == null) return;
            value.SetActive(false);
            if (Application.isPlaying) UnityEngine.Object.Destroy(value);
            else UnityEngine.Object.DestroyImmediate(value);
        }
    }
}
