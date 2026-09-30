using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 地面战利品记录，地图视图销毁不改变领取状态。
    /// </summary>
    public sealed class ExpeditionDrop
    {
        public int RoomId;
        public Vector3 Position;
        public ProjectItem Item;
        public bool Collected;
    }

    /// <summary>
    /// 一局的地图、战斗和物品协调器，不复制 ECS 角色状态。
    /// </summary>
    public sealed class ExpeditionRun : IDisposable
    {
        private readonly ProjectSession session;
        private readonly System.Random random;
        private readonly List<CombatEvent> events = new();
        private readonly HashSet<int> rewardedDeaths = new();
        private readonly List<ProjectItem> inventory = new();
        private readonly HashSet<string> protectedItems = new();
        private readonly List<ExpeditionDrop> drops = new();
        private bool disposed;
        private bool interactionRequested;
        private Vector2 touchMove;

        public event Action Changed;
        public event Action<CombatEvent> CombatEventPublished;
        public string RunId { get; }
        public int LevelId { get; }
        public LevelRunState Maps { get; }
        public CombatWorld World { get; }
        public CombatEntityRef Player { get; }
        public LevelRoomView RoomView { get; private set; }
        public LevelRoomLayout Layout { get; } = new();
        public bool IsRunning { get; private set; } = true;
        public int KillExperience { get; private set; }
        public int GoldSpent { get; private set; }
        public int AvailableGold => session.Gold - GoldSpent;
        public string Message { get; private set; } = "移动靠近敌人自动攻击；靠近出口或物品后交互。";
        public IReadOnlyList<ProjectItem> Inventory => inventory;
        public IReadOnlyList<ExpeditionDrop> Drops => drops;
        public int SafeCount => protectedItems.Count;
        public string InteractionLabel { get; private set; } = "交互 / E";
        public LevelRoomState CurrentRoom { get { Maps.TryGetRoom(Maps.CurrentRoomId, out LevelRoomState room); return room; } }

        /// <summary>
        /// 创建一局纯逻辑状态和玩家实体，表现由战斗场景另行绑定。
        /// </summary>
        /// <param name="session">局外唯一会话。</param>
        /// <param name="runId">稳定单局身份。</param>
        /// <param name="levelId">关卡等级配置。</param>
        /// <param name="maps">已经验证的连通地图。</param>
        /// <param name="items">本局入场装备。</param>
        private ExpeditionRun(ProjectSession session, string runId, int levelId, LevelRunState maps, List<ProjectItem> items)
        {
            this.session = session;
            RunId = runId;
            LevelId = levelId;
            Maps = maps;
            random = new System.Random(maps.Seed ^ 0x5162);
            inventory.AddRange(items.ConvertAll(item => item.Copy()));
            CombatCatalog catalog = CreateCatalog(items, session.PowerRank);
            World = new CombatWorld(catalog) { ErrorLogger = reason => GameEntry.Get<ILogModule>()?.Error("Combat", reason) };
            foreach (LevelRoomState room in maps.Rooms)
            {
                float extent = Layout.HalfExtent - PrototypeLevelConfig.WallThickness * 0.5f;
                World.SetRoomBounds(room.RoomId, new Rect(-extent, -extent, extent * 2, extent * 2));
                foreach (LevelObstacle obstacle in Layout.Obstacles)
                    World.AddObstacle(room.RoomId, Rect.MinMaxRect(obstacle.Min.x, obstacle.Min.y, obstacle.Max.x, obstacle.Max.y));
            }
            ProjectItem weapon = items.Find(item => item.Type == ProjectItemType.Weapon);
            ProjectItem armor = items.Find(item => item.Type == ProjectItemType.Equipment);
            Player = World.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = maps.StartRoomId, Team = 1, IsPlayer = true, Position = new Vector3(0, 0, -8),
                MaxHealth = 100 + session.VitalityRank * 15 + (armor?.BonusHealth ?? 0), MoveSpeed = 5 * (1 + session.AgilityRank * 0.05f),
                Radius = 0.45f, AttackRange = weapon?.Range ?? 1.5f, BasicSkillId = 1, AggroRange = 0
            });
            World.SetActiveRoom(maps.StartRoomId, Player);
        }

        /// <summary>
        /// 生成关卡并准备可进入的单局，失败不扣体力。
        /// </summary>
        /// <param name="session">项目会话。</param>
        /// <param name="runId">单局身份。</param>
        /// <param name="levelId">关卡配置。</param>
        /// <param name="items">入场装备快照。</param>
        /// <param name="run">创建成功的单局。</param>
        /// <returns>图和战斗玩家均创建成功时为真。</returns>
        public static bool TryCreate(ProjectSession session, string runId, int levelId, List<ProjectItem> items, out ExpeditionRun run)
        {
            run = null;
            int seed = Guid.NewGuid().GetHashCode();
            if (!LevelGenerator.TryGenerate(seed, out LevelRunState maps)) return false;
            var created = new ExpeditionRun(session, runId, levelId, maps, items);
            if (created.Player.Id == 0)
            {
                created.Dispose();
                return false;
            }
            run = created;
            return true;
        }

        /// <summary>
        /// 场景进入后建立当前地图内容，回访不重生敌人。
        /// </summary>
        public void AttachScene()
        {
            if (disposed || RoomView != null) return;
            RoomView = LevelRoomView.Create(CurrentRoom);
            SpawnRoomEnemies(CurrentRoom);
            Changed?.Invoke();
        }

        /// <summary>
        /// 在唯一玩法更新入口推进模拟，并在完整 tick 后处理物品和换图。
        /// </summary>
        /// <param name="deltaTime">本帧模拟秒数。</param>
        public void Advance(float deltaTime)
        {
            if (!IsRunning || disposed || RoomView == null || session.HasPendingSave) return;
            IInputModule input = GameEntry.Get<IInputModule>();
            Vector2 movement = input.GetVector2("Gameplay/Move");
            if (touchMove.sqrMagnitude > movement.sqrMagnitude) movement = touchMove;
            World.SetMove(Player, movement);
            World.Advance(deltaTime);
            events.Clear();
            World.DrainEvents(events);
            bool playerDied = false;
            foreach (CombatEvent fact in events)
            {
                CombatEventPublished?.Invoke(fact);
                if (fact.Kind == CombatEventKind.Death)
                {
                    if (fact.TargetActorId == 1) playerDied = true;
                    else if (rewardedDeaths.Add(fact.TargetActorId))
                    {
                        KillExperience += 10 + LevelId * 3;
                        Maps.TryGetRoom(fact.RoomId, out LevelRoomState room);
                        if (room.Type == LevelRoomType.Boss || random.NextDouble() < 0.55)
                            drops.Add(new ExpeditionDrop { RoomId = fact.RoomId, Position = fact.Position,
                                Item = PrototypeRules.CreateLoot(random, LevelId) });
                    }
                }
                else if (fact.Kind == CombatEventKind.RoomCleared && Maps.TryGetRoom(fact.RoomId, out LevelRoomState room))
                    room.MarkCleared();
            }
            RoomView.Refresh();
            if (playerDied)
            {
                Stop();
                session.FinishRunAsync(false).Forget();
                Changed?.Invoke();
                return;
            }
            if (World.Faulted)
            {
                Stop();
                Message = "战斗异常，正在恢复入场装备和体力。";
                session.RecoverInterruptedRunAsync(Message).Forget();
                Changed?.Invoke();
                return;
            }
            if (World.TryGetActor(Player, out CombatActorSnapshot player))
            {
                UpdateInteractionLabel(player.Position);
                if (RoomView.TryGetInteractionNear(player.Position, out LevelInteractionKind kind) && kind == LevelInteractionKind.Extraction)
                {
                    Stop();
                    session.FinishRunAsync(true).Forget();
                }
                else if (interactionRequested || input.GetButton("Gameplay/Interact").PressedThisFrame) Interact(player.Position);
            }
            interactionRequested = false;
            Changed?.Invoke();
        }

        /// <summary>
        /// 提交触屏移动轴，松手时传入零向量。
        /// </summary>
        /// <param name="axis">归一化摇杆输入。</param>
        public void SetTouchMove(Vector2 axis) => touchMove = Vector2.ClampMagnitude(axis, 1);

        /// <summary>
        /// 将 UI 交互请求延迟到战斗 tick 收束之后。
        /// </summary>
        public void RequestInteraction() => interactionRequested = true;

        /// <summary>
        /// 切换战利品的安全箱保护；已穿戴物品须先在后续装备界面卸下。
        /// </summary>
        /// <param name="itemId">本局物品实例身份。</param>
        /// <returns>是否已经改变保护状态。</returns>
        public bool ToggleSafe(string itemId)
        {
            if (!IsRunning) return false;
            ProjectItem item = inventory.Find(value => value.Id == itemId);
            if (item == null || item.Equipped) return false;
            if (protectedItems.Remove(itemId)) { Changed?.Invoke(); return true; }
            if (protectedItems.Count >= PrototypeRules.SafeSlots) { Message = "安全箱已满。"; return false; }
            protectedItems.Add(itemId);
            Changed?.Invoke();
            return true;
        }

        /// <summary>
        /// 查询物品当前是否受安全箱保护。
        /// </summary>
        /// <param name="itemId">物品实例身份。</param>
        /// <returns>在安全箱中时为真。</returns>
        public bool IsProtected(string itemId) => protectedItems.Contains(itemId);

        /// <summary>
        /// 生成正常撤离或死亡结算的物品计划。
        /// </summary>
        /// <param name="extracted">是否正常撤离。</param>
        /// <returns>待携出的独立实例快照。</returns>
        public IReadOnlyList<ProjectItem> GetRetainedItems(bool extracted)
        {
            var retained = new List<ProjectItem>();
            foreach (ProjectItem item in inventory)
                if (extracted || protectedItems.Contains(item.Id)) retained.Add(item.Copy());
            return retained;
        }

        /// <summary>
        /// 冻结战斗和输入，等待唯一结算结果。
        /// </summary>
        public void Stop()
        {
            IsRunning = false;
            touchMove = Vector2.zero;
            interactionRequested = false;
        }

        /// <summary>
        /// 销毁本局内容和战斗世界，不生成到期或死亡效果。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stop();
            RoomView?.Dispose();
            RoomView = null;
            World.Dispose();
            Changed = null;
            CombatEventPublished = null;
        }

        /// <summary>
        /// 在操作前显示交互类型和商店价格。
        /// </summary>
        /// <param name="position">玩家权威位置。</param>
        private void UpdateInteractionLabel(Vector3 position)
        {
            InteractionLabel = "交互 / E";
            if (RoomView.TryGetExitNear(position, out _)) { InteractionLabel = "进入相邻地图 / E"; return; }
            foreach (ExpeditionDrop drop in drops)
                if (!drop.Collected && drop.RoomId == Maps.CurrentRoomId && (drop.Position - position).sqrMagnitude <= 4f)
                { InteractionLabel = "拾取战利品 / E"; return; }
            if (!RoomView.TryGetInteractionNear(position, out LevelInteractionKind kind)) return;
            if (kind == LevelInteractionKind.Chest) InteractionLabel = "打开箱子 / E";
            if (kind == LevelInteractionKind.Npc)
            {
                bool sold = CurrentRoom.TryGetNpcStock("relic", out int stock) && stock <= 0;
                InteractionLabel = sold ? "商品已售罄" : "购买 " + PrototypeRules.NpcItemPrice + " 金币 / E";
            }
        }

        /// <summary>
        /// 执行靠近目标的单次交互，换图不受清怪状态限制。
        /// </summary>
        /// <param name="position">玩家权威位置。</param>
        private void Interact(Vector3 position)
        {
            if (RoomView.TryGetExitNear(position, out RoomDirection direction)) { ChangeRoom(direction); return; }
            foreach (ExpeditionDrop drop in drops)
            {
                if (drop.Collected || drop.RoomId != Maps.CurrentRoomId || (drop.Position - position).sqrMagnitude > 4f) continue;
                drop.Collected = true;
                inventory.Add(drop.Item);
                Message = "拾取：" + drop.Item.Name;
                return;
            }
            if (!RoomView.TryGetInteractionNear(position, out LevelInteractionKind kind)) { Message = "请靠近出口、战利品或 NPC。"; return; }
            if (kind == LevelInteractionKind.Chest && !CurrentRoom.ChestOpened)
            {
                CurrentRoom.MarkChestOpened();
                inventory.Add(PrototypeRules.CreateLoot(random, LevelId));
                Message = "已打开箱子，获得一件战利品。";
            }
            else if (kind == LevelInteractionKind.Npc)
            {
                if (!CurrentRoom.TryGetNpcStock("relic", out int count)) count = 1;
                if (count <= 0) { Message = "本地图商品已售罄。"; return; }
                if (AvailableGold < PrototypeRules.NpcItemPrice) { Message = "金币不足。"; return; }
                GoldSpent += PrototypeRules.NpcItemPrice;
                CurrentRoom.TrySetNpcStock("relic", count - 1);
                inventory.Add(PrototypeRules.CreateLoot(random, LevelId));
                Message = "购买成功，金币 -" + PrototypeRules.NpcItemPrice;
            }
            RoomView.Refresh();
        }

        /// <summary>
        /// 在目标准备完成后一次提交地图、入口和战斗分区。
        /// </summary>
        /// <param name="direction">当前出口方向。</param>
        private void ChangeRoom(RoomDirection direction)
        {
            if (!Maps.TryGetDestination(direction, out int destination) || !Maps.TryGetRoom(destination, out LevelRoomState room)) return;
            LevelRoomView prepared = LevelRoomView.Create(room);
            if (prepared == null) { Message = "目标地图准备失败。"; return; }
            RoomDirection entrySide = LevelGenerator.Opposite(direction);
            Vector3 spawn = prepared.GetSpawnPosition(entrySide);
            if (Layout.IsBlocked(spawn, 0.45f)) { prepared.Dispose(); Message = "目标入口不可用。"; return; }
            if (!World.SetActiveRoom(destination, Player)) { prepared.Dispose(); return; }
            if (!World.TrySetPosition(Player, spawn) || !Maps.TryCommitTransition(destination))
            {
                prepared.Dispose();
                Stop();
                session.RecoverInterruptedRunAsync("地图切换失败，已恢复入场装备和体力。").Forget();
                return;
            }
            LevelRoomView previous = RoomView;
            RoomView = prepared;
            previous.Dispose();
            SpawnRoomEnemies(room);
            Message = "进入地图 " + destination + "，可随时沿出口继续探索。";
        }

        /// <summary>
        /// 首次激活战斗地图时创建其敌人实体。
        /// </summary>
        /// <param name="room">激活的地图。</param>
        private void SpawnRoomEnemies(LevelRoomState room)
        {
            if (room.EnemiesSpawned || (room.Type != LevelRoomType.Mob && room.Type != LevelRoomType.Boss)) return;
            bool boss = room.Type == LevelRoomType.Boss;
            int count = boss ? 1 : room.IsStart ? 2 : 3;
            for (int index = 0; index < count; index++)
            {
                Vector3 position = new Vector3((index - (count - 1) * 0.5f) * 3, 0, 3 + index % 2 * 2);
                World.CreateActor(new CombatActorSpec
                {
                    ActorId = room.RoomId * 100 + index + 1, RoomId = room.RoomId, Team = 2,
                    Position = position, MaxHealth = boss ? 180 + LevelId * 30 : 35 + LevelId * 10,
                    MoveSpeed = boss ? 1.5f : 2.2f, Radius = boss ? 0.9f : 0.45f,
                    AttackRange = boss ? 7f : 1.6f, AggroRange = 10, BasicSkillId = boss ? 3 : 2
                });
            }
            room.MarkEnemiesSpawned();
        }

        /// <summary>
        /// 组合首版普攻、敌人攻击和飞弹附带 Buff 的验证内容。
        /// </summary>
        /// <param name="items">入场装备。</param>
        /// <param name="powerRank">局外伤害成长等级。</param>
        /// <returns>仅本局持有的战斗配置。</returns>
        private static CombatCatalog CreateCatalog(List<ProjectItem> items, int powerRank)
        {
            CombatCatalog catalog = CombatCatalog.CreatePrototype();
            ProjectItem weapon = items.Find(item => item.Type == ProjectItemType.Weapon);
            CombatSkillDefinition player = catalog.Skills[1];
            player.StartRange = (weapon?.Range ?? 1.5f) + 0.6f;
            player.Nodes[1].Effects[0].Value = 0.35f;
            CombatEffectDefinition hit = player.Nodes[2].Effects[0];
            hit.Value = (weapon?.Damage ?? 10) * (1 + powerRank * 0.1f);
            hit.Radius = weapon?.Range ?? 1.5f;
            hit.Shape = CombatHitShape.Circle;
            var enemy = new CombatSkillDefinition { CombatSkillId = 2, DurationTicks = 38, CooldownTicks = 65, StartRange = 2.1f };
            var cue = new CombatTimelineNode { Tick = 0 };
            cue.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Cue, DefinitionId = 20 });
            var strike = new CombatTimelineNode { Tick = 25 };
            strike.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.Damage, DefinitionId = 21, Value = 8,
                Radius = 1.7f, Shape = CombatHitShape.Sector, Angle = 100, Height = 2, RequireLineOfSight = true });
            enemy.Nodes.Add(cue);
            enemy.Nodes.Add(strike);
            catalog.Skills.Add(2, enemy);
            var boss = new CombatSkillDefinition { CombatSkillId = 3, DurationTicks = 48, CooldownTicks = 90, StartRange = 8 };
            var launch = new CombatTimelineNode { Tick = 30 };
            launch.Effects.Add(new CombatEffectDefinition { Kind = CombatEffectKind.SpawnProjectile, DefinitionId = 30, LinkedId = 1 });
            boss.Nodes.Add(cue);
            boss.Nodes.Add(launch);
            catalog.Skills.Add(3, boss);
            catalog.Projectiles[1].OnHit.Add(new CombatEffectDefinition { Kind = CombatEffectKind.ApplyBuff, DefinitionId = 31, LinkedId = 1 });
            return catalog;
        }
    }
}
