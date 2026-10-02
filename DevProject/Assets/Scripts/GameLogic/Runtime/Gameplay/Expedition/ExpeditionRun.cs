using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Gameplay.Roguelike;
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
    public sealed partial class ExpeditionRun : IDisposable
    {
        private readonly ProjectSession session;
        private readonly Dictionary<int, EnemyDefinition> enemyDefinitions = new();
        private readonly Dictionary<int, LevelRoomLayout> roomLayouts = new();
        private readonly System.Random random;
        private readonly List<CombatEvent> events = new();
        private readonly HashSet<int> rewardedDeaths = new();
        private readonly List<ProjectItem> inventory = new();
        private readonly HashSet<string> protectedItems = new();
        private readonly List<ExpeditionDrop> drops = new();
        private bool disposed;
        private bool stopped;
        private bool paused;
        private bool interactionRequested;
        private ExpeditionVisualAssets visualAssets;
        private Transform sceneParent;
        private Vector2 touchMove;

        public event Action Changed;
        public event Action<CombatEvent> CombatEventPublished;
        public string RunId { get; }
        public int LevelId { get; }
        public int Seed => Maps.Seed;
        public LevelRunState Maps { get; }
        public CombatWorld World { get; }
        public CombatEntityRef Player { get; }
        public LevelRoomView RoomView { get; private set; }
        public LevelRoomLayout Layout => roomLayouts[Maps.CurrentRoomId];
        public bool IsReady { get; private set; }
        public bool IsRunning { get; private set; }
        public int KillExperience { get; private set; }
        public int GoldSpent { get; private set; }
        public int AvailableGold => session.Gold - GoldSpent;
        private bool CanInteract => IsRunning && IsReady && !paused && !IsChoosingRogue && !disposed && !stopped && !session.HasPendingSave && !GameEntry.IsInBackground;
        /// <summary>
        /// 当前是否允许玩家输入，供局内触屏控件门禁使用。
        /// </summary>
        public bool CanAcceptPlayerInput => CanInteract;
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
        /// <param name="level">已经校验的关卡配置。</param>
        /// <param name="maps">已经验证的连通地图。</param>
        /// <param name="items">本局入场装备。</param>
        /// <param name="roguePolicy">三选一扩展功能开关。</param>
        /// <param name="rogueAccess">第四选项的外部授权提供器。</param>
        private ExpeditionRun(ProjectSession session, string runId, LevelDefinition level, LevelRunState maps, List<ProjectItem> items,
            RogueChoicePolicy roguePolicy, IRogueChoiceAccessProvider rogueAccess)
        {
            this.session = session;
            RunId = runId;
            LevelId = level.ID;
            Maps = maps;
            rogueChoices = new RogueChoiceSession(runId, maps.Seed ^ 0x5247, roguePolicy);
            rogueAccessProvider = rogueAccess;
            rogueLootRandom = new System.Random(maps.Seed ^ 0x5291);
            random = new System.Random(maps.Seed ^ 0x5162);
            inventory.AddRange(items.ConvertAll(item => item.Copy()));
            ProjectCharacterStats attributes = ProjectEquipmentRules.Calculate(items, session.PowerRank, session.VitalityRank, session.AgilityRank);
            CombatCatalog catalog = CombatContentConfig.CreateCatalog(attributes.AttackRange);
            World = new CombatWorld(catalog, randomSeed: maps.Seed) { ErrorLogger = reason => GameEntry.Get<ILogModule>()?.Error("Combat", reason) };
            foreach (LevelRoomState room in maps.Rooms)
            {
                var layout = new LevelRoomLayout(room.Plan.Definition);
                roomLayouts.Add(room.RoomId, layout);
                float extent = layout.HalfExtent - layout.WallThickness * 0.5f;
                World.SetRoomBounds(room.RoomId, new Rect(-extent, -extent, extent * 2, extent * 2));
                foreach (LevelObstacle obstacle in layout.Obstacles)
                    World.AddObstacle(room.RoomId, Rect.MinMaxRect(obstacle.Min.x, obstacle.Min.y, obstacle.Max.x, obstacle.Max.y));
            }
            Player = World.CreateActor(new CombatActorSpec
            {
                ActorId = 1, RoomId = maps.StartRoomId, Team = 1, IsPlayer = true, Position = new Vector3((float)ProjectContentConfig.Player.SpawnX, 0, (float)ProjectContentConfig.Player.SpawnZ),
                MaxHealth = attributes.MaxHealth, Attack = attributes.Attack, Defense = attributes.Defense,
                CriticalRating = attributes.CriticalRating, CriticalDamage = attributes.CriticalDamage, DodgeChance = attributes.DodgeChance,
                DamageIncrease = attributes.DamageIncrease, DamageReduction = attributes.DamageReduction, MoveSpeed = attributes.MoveSpeed,
                Radius = (float)ProjectContentConfig.Player.Radius, AttackRange = attributes.AttackRange, BasicSkillId = ProjectContentConfig.Player.SkillID, AggroRange = 0
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
        public static bool TryCreate(ProjectSession session, string runId, int levelId, List<ProjectItem> items, out ExpeditionRun run) =>
            TryCreate(session, runId, levelId, Guid.NewGuid().GetHashCode(), items, out run);

        /// <summary>
        /// 使用指定种子生成关卡并准备未激活单局。
        /// </summary>
        /// <param name="session">项目会话。</param>
        /// <param name="runId">单局身份。</param>
        /// <param name="levelId">关卡配置。</param>
        /// <param name="seed">本局随机种子。</param>
        /// <param name="items">入场装备快照。</param>
        /// <param name="run">创建成功的单局。</param>
        /// <param name="roguePolicy">可选扩展配置，默认禁用刷新和第四选项。</param>
        /// <param name="rogueAccess">可选的第四选项授权适配器。</param>
        /// <returns>图和战斗玩家均创建成功时为真。</returns>
        public static bool TryCreate(ProjectSession session, string runId, int levelId, int seed, List<ProjectItem> items, out ExpeditionRun run,
            RogueChoicePolicy roguePolicy = null, IRogueChoiceAccessProvider rogueAccess = null)
        {
            run = null;
            if (session == null || items == null || string.IsNullOrWhiteSpace(runId) ||
                !LevelContentConfig.TryGetLevel(levelId, out LevelDefinition definition) ||
                !LevelGenerator.TryGenerate(seed, definition, out LevelRunState maps)) return false;
            var created = new ExpeditionRun(session, runId, definition, maps, items, roguePolicy, rogueAccess);
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
        /// <param name="assets">场景作用域持有的关卡预制件。</param>
        /// <param name="parent">可选的场景父节点。</param>
        public void AttachScene(ExpeditionVisualAssets assets, Transform parent = null)
        {
            if (disposed || stopped || RoomView != null || assets == null) return;
            visualAssets = assets;
            sceneParent = parent;
            RoomView = LevelRoomView.Create(CurrentRoom, assets, CurrentRoom.Plan.Definition, parent);
            if (RoomView == null) return;
            if (!SpawnRoomEnemies(CurrentRoom))
            {
                RoomView.Dispose();
                RoomView = null;
                Stop();
                return;
            }
            Changed?.Invoke();
        }

        /// <summary>
        /// 在地图、玩法和界面全部建立后声明场景就绪。
        /// </summary>
        /// <returns>场景内容完整且可以等待激活时为真。</returns>
        public bool MarkReady()
        {
            if (disposed || stopped || RoomView == null) return false;
            IsReady = true;
            return true;
        }

        /// <summary>
        /// 在入场 Running 记录确认后开放玩法模拟。
        /// </summary>
        /// <returns>当前单局满足激活条件时为真。</returns>
        public bool Activate()
        {
            if (disposed || stopped || !IsReady) return false;
            IsRunning = true;
            paused = false;
            World.ClearPendingTime();
            return true;
        }

        /// <summary>
        /// 暂停或恢复本局模拟，暂停时间不进入战斗累计器。
        /// </summary>
        /// <param name="value">是否暂停。</param>
        public void SetPaused(bool value)
        {
            if (disposed || stopped || paused == value) return;
            paused = value;
            if (value)
            {
                touchMove = Vector2.zero;
                interactionRequested = false;
                World.ClearPendingTime();
                World.SetMove(Player, Vector2.zero);
            }
            RogueChoiceChanged?.Invoke();
            Changed?.Invoke();
        }

        /// <summary>
        /// 释放场景表现和地图视图，不释放单局战斗世界。
        /// </summary>
        public void DetachScene()
        {
            Stop();
            IsReady = false;
            RoomView?.Dispose();
            RoomView = null;
            visualAssets = null;
            sceneParent = null;
        }

        /// <summary>
        /// 在唯一玩法更新入口推进模拟，每个完整 tick 后立即仲裁终局。
        /// </summary>
        /// <param name="deltaTime">本帧模拟秒数。</param>
        public void Advance(float deltaTime)
        {
            AdvanceDeathPresentation(deltaTime);
            if (!IsRunning || !IsReady || paused || IsChoosingRogue || disposed || RoomView == null || session.HasPendingSave || GameEntry.IsInBackground)
            {
                World.ClearPendingTime();
                if (IsRunning && GameEntry.IsInBackground) SetPaused(true);
                return;
            }
            IInputModule input = GameEntry.Get<IInputModule>();
            Vector2 movement = input.GetVector2("Gameplay/Move");
            if (touchMove.sqrMagnitude > movement.sqrMagnitude) movement = touchMove;
            World.SetMove(Player, movement);
            bool interact = interactionRequested || input.GetButton("Gameplay/Interact").PressedThisFrame;
            World.Advance(deltaTime, () =>
            {
                bool continueRunning = CompleteTick(interact);
                interact = false;
                interactionRequested = false;
                return continueRunning;
            });
            if (World.Faulted && IsRunning) HandleFault("战斗世界异常，正在处理中断的单局。");
        }

        /// <summary>
        /// 处理一个完整 tick 的事实、终局和玩家交互。
        /// </summary>
        /// <param name="interact">该 tick 是否接收玩家交互。</param>
        /// <returns>可以继续执行本帧剩余 tick 时为真。</returns>
        private bool CompleteTick(bool interact)
        {
            events.Clear();
            World.DrainEvents(events);
            bool playerDied = false;
            foreach (CombatEvent fact in events)
            {
                CombatEventPublished?.Invoke(fact);
                if (fact.Kind == CombatEventKind.Death)
                {
                    if (fact.TargetActorId == 1) playerDied = true;
                    else if (rewardedDeaths.Add(fact.TargetActorId) && enemyDefinitions.TryGetValue(fact.TargetActorId, out EnemyDefinition enemy))
                    {
                        KillExperience = (int)Math.Min(int.MaxValue, (long)KillExperience + enemy.ExperienceAt(LevelId));
                        if (random.NextDouble() < enemy.DropChance)
                            drops.Add(new ExpeditionDrop { RoomId = fact.RoomId, Position = fact.Position,
                                Item = ProjectEquipmentRules.CreateLootFromPool(random, LevelId, enemy.DropPoolID) });
                    }
                }
                else if (fact.Kind == CombatEventKind.RoomCleared && Maps.TryGetRoom(fact.RoomId, out LevelRoomState room))
                    room.MarkCleared();
            }
            RoomView.Refresh();
            if (World.Faulted) { HandleFault("战斗世界异常，正在处理中断的单局。"); return false; }
            if (playerDied) { EndRun(false); return false; }
            if (World.TryGetActor(Player, out CombatActorSnapshot player))
            {
                UpdateInteractionLabel(player.Position);
                if (RoomView.TryGetInteractionNear(player.Position, out LevelInteractionKind kind) && kind == LevelInteractionKind.Extraction)
                { EndRun(true); return false; }
                if (interact) Interact(player.Position);
            }
            Changed?.Invoke();
            return IsRunning && !IsChoosingRogue;
        }

        /// <summary>
        /// 冻结正常终局并请求统一流程接收。
        /// </summary>
        /// <param name="extracted">是否成功撤离。</param>
        private void EndRun(bool extracted)
        {
            Stop();
            Changed?.Invoke();
            if (ProjectServices.Coordinator?.TryEndRun(this, extracted) != true)
            {
                string reason = "终局未被流程接收，正在处理中断的单局。";
                GameEntry.Get<ILogModule>()?.Error("Expedition", reason);
                RecoverAfterTickAsync(reason).Forget();
            }
        }

        /// <summary>
        /// 冻结故障状态，并在战斗调用栈退出后转交中断恢复。
        /// </summary>
        /// <param name="reason">可展示的故障原因。</param>
        private void HandleFault(string reason)
        {
            Stop();
            Message = reason;
            Changed?.Invoke();
            RecoverAfterTickAsync(reason).Forget();
        }

        /// <summary>
        /// 等待模拟调用栈退出后通知流程处理故障。
        /// </summary>
        /// <param name="reason">故障原因。</param>
        /// <returns>中断恢复任务。</returns>
        private async UniTask RecoverAfterTickAsync(string reason)
        {
            await UniTask.Yield();
            if (disposed || !ReferenceEquals(ProjectServices.Session?.CurrentRun, this) ||
                ProjectServices.Session.ActiveRunId != RunId) return;
            if (ProjectServices.Coordinator != null) await ProjectServices.Coordinator.RecoverInterruptedRunAsync(reason);
        }

        /// <summary>
        /// 提交触屏移动轴，松手时传入零向量。
        /// </summary>
        /// <param name="axis">归一化摇杆输入。</param>
        public void SetTouchMove(Vector2 axis)
        {
            if (axis == Vector2.zero || float.IsNaN(axis.x) || float.IsInfinity(axis.x) || float.IsNaN(axis.y) || float.IsInfinity(axis.y))
            {
                touchMove = Vector2.zero;
                return;
            }
            if (!CanInteract) return;
            touchMove = Vector2.ClampMagnitude(axis, 1);
        }

        /// <summary>
        /// 将 UI 交互请求延迟到战斗 tick 收束之后。
        /// </summary>
        public void RequestInteraction()
        {
            if (CanInteract) interactionRequested = true;
        }

        /// <summary>
        /// 切换战利品的安全箱保护；已穿戴物品须先在后续装备界面卸下。
        /// </summary>
        /// <param name="itemId">本局物品实例身份。</param>
        /// <returns>是否已经改变保护状态。</returns>
        public bool ToggleSafe(string itemId)
        {
            if (!CanInteract) return false;
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
        /// 为已停止的单局创建独立的终局数据快照。
        /// </summary>
        /// <param name="extracted">是否成功撤离。</param>
        /// <returns>包含全部持有物品和安全箱身份的终局快照。</returns>
        public ProjectRunOutcome CreateOutcome(bool extracted)
        {
            if (!stopped || disposed) return null;
            var outcome = new ProjectRunOutcome
            {
                RunId = RunId, LevelId = LevelId, Seed = Seed, RulesVersion = ProjectRunRecordRules.RulesVersion,
                Tick = World.TickIndex, ElapsedSeconds = World.TickIndex * CombatWorld.TickSeconds,
                Extracted = extracted, KillExperience = KillExperience,
                AwardedKillExperience = extracted || PrototypeRules.KeepKillExperienceOnDeath ? KillExperience : 0,
                ExtractionExperience = extracted ? PrototypeRules.ExtractionExperience : 0,
                GoldSpent = GoldSpent, Items = new List<ProjectItem>(), SafeItemIds = new List<string>(protectedItems)
            };
            foreach (ProjectItem item in inventory) outcome.Items.Add(item.Copy());
            outcome.SafeItemIds.Sort(StringComparer.Ordinal);
            return outcome;
        }

        /// <summary>
        /// 冻结战斗和输入，等待唯一结算结果。
        /// </summary>
        public void Stop()
        {
            stopped = true;
            IsRunning = false;
            StopRogueChoices();
            World.ClearPendingTime();
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
            DetachScene();
            World.Dispose();
            Changed = null;
            CombatEventPublished = null;
            RogueChoiceChanged = null;
            rogueLifetime.Dispose();
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
                if (!drop.Collected && drop.RoomId == Maps.CurrentRoomId && (drop.Position - position).sqrMagnitude <= Layout.ObjectInteractionRadius * Layout.ObjectInteractionRadius)
                { InteractionLabel = "拾取战利品 / E"; return; }
            if (!RoomView.TryGetInteractionNear(position, out LevelInteractionKind kind)) return;
            if (kind == LevelInteractionKind.Chest) InteractionLabel = "打开箱子 / E";
            if (kind == LevelInteractionKind.Npc)
            {
                bool sold = CurrentRoom.TryGetNpcStock("relic", out int stock) && stock <= 0;
                InteractionLabel = sold ? "商品已售罄" : "购买 " + CurrentRoom.Plan.NpcPrice + " 金币 / E";
            }
        }

        /// <summary>
        /// 执行靠近目标的单次交互，换图不受清怪状态限制。
        /// </summary>
        /// <param name="position">玩家权威位置。</param>
        private void Interact(Vector3 position)
        {
            if (!CanInteract) return;
            if (RoomView.TryGetExitNear(position, out RoomDirection direction)) { ChangeRoom(direction); return; }
            foreach (ExpeditionDrop drop in drops)
            {
                if (drop.Collected || drop.RoomId != Maps.CurrentRoomId || (drop.Position - position).sqrMagnitude > Layout.ObjectInteractionRadius * Layout.ObjectInteractionRadius) continue;
                drop.Collected = true;
                inventory.Add(drop.Item);
                Message = "拾取：" + drop.Item.Name;
                return;
            }
            if (!RoomView.TryGetInteractionNear(position, out LevelInteractionKind kind)) { Message = "请靠近出口、战利品或 NPC。"; return; }
            if (kind == LevelInteractionKind.Chest && !CurrentRoom.ChestOpened)
            {
                CurrentRoom.MarkChestOpened();
                inventory.Add(ProjectEquipmentRules.CreateLootFromPool(random, LevelId, CurrentRoom.Plan.LootPoolID));
                Message = "已打开箱子，获得一件战利品。";
                TryBeginRogueChoice("chest:" + CurrentRoom.RoomId);
            }
            else if (kind == LevelInteractionKind.Npc)
            {
                if (!CurrentRoom.TryGetNpcStock("relic", out int count)) count = CurrentRoom.Plan.NpcStock;
                if (count <= 0) { Message = "本地图商品已售罄。"; return; }
                if (AvailableGold < CurrentRoom.Plan.NpcPrice) { Message = "金币不足。"; return; }
                GoldSpent += CurrentRoom.Plan.NpcPrice;
                CurrentRoom.TrySetNpcStock("relic", count - 1);
                inventory.Add(ProjectEquipmentRules.CreateLootFromPool(random, LevelId, CurrentRoom.Plan.LootPoolID));
                Message = "购买成功，金币 -" + CurrentRoom.Plan.NpcPrice;
            }
            RoomView.Refresh();
        }

        /// <summary>
        /// 在目标准备完成后一次提交地图、入口和战斗分区。
        /// </summary>
        /// <param name="direction">当前出口方向。</param>
        private void ChangeRoom(RoomDirection direction)
        {
            if (!CanInteract) return;
            if (!Maps.TryGetDestination(direction, out int destination) || !Maps.TryGetRoom(destination, out LevelRoomState room)) return;
            LevelRoomView prepared = LevelRoomView.Create(room, visualAssets, room.Plan.Definition, sceneParent);
            if (prepared == null) { Message = "目标地图准备失败。"; return; }
            RoomDirection entrySide = LevelGenerator.Opposite(direction);
            Vector3 spawn = prepared.GetSpawnPosition(entrySide);
            if (prepared.Layout.IsBlocked(spawn, (float)ProjectContentConfig.Player.Radius)) { prepared.Dispose(); Message = "目标入口不可用。"; return; }
            if (!World.TryTransferActor(Player, destination, spawn))
            {
                prepared.Dispose();
                Message = "目标入口被占用，请稍后重试。";
                return;
            }
            if (!Maps.TryCommitTransition(destination))
            {
                prepared.Dispose();
                Stop();
                RecoverAfterTickAsync("地图切换失败，正在处理中断的单局。").Forget();
                return;
            }
            LevelRoomView previous = RoomView;
            RoomView = prepared;
            previous.Dispose();
            if (!SpawnRoomEnemies(room))
            {
                HandleFault("房间敌人创建失败，正在处理中断的单局。");
                return;
            }
            Message = "进入地图 " + destination + "，可随时沿出口继续探索。";
        }

        /// <summary>
        /// 首次激活战斗地图时创建其敌人实体。
        /// </summary>
        /// <param name="room">激活的地图。</param>
        /// <returns>全部计划敌人创建成功，或无需再次创建时为真。</returns>
        private bool SpawnRoomEnemies(LevelRoomState room)
        {
            if (room.EnemiesSpawned || (room.Type != LevelRoomType.Mob && room.Type != LevelRoomType.Boss)) return true;
            int sequence = 0;
            foreach (SpawnDefinition spawn in room.Plan.Spawns)
            {
                if (!LevelContentConfig.TryGetEnemy(spawn.EnemyID, out EnemyDefinition enemy))
                {
                    GameEntry.Get<ILogModule>()?.Error("Expedition", "房间计划引用的敌人不存在：" + spawn.EnemyID);
                    return false;
                }
                for (int index = 0; index < spawn.Count; index++)
                {
                    int actorId = room.RoomId * 1000 + ++sequence;
                    var entity = World.CreateActor(new CombatActorSpec
                    {
                        ActorId = actorId, RoomId = room.RoomId, Team = 2, Position = spawn.PositionAt(index),
                        MaxHealth = enemy.HealthAt(LevelId), Attack = (float)enemy.Row.Attack, MoveSpeed = (float)enemy.Row.MoveSpeed,
                        Radius = (float)enemy.Row.Radius, AttackRange = (float)enemy.Row.AttackRange,
                        AggroRange = (float)enemy.Row.AggroRange, BasicSkillId = enemy.SkillID,
                        EnemySkills = CombatEnemySkillConfig.Create(enemy.ID)
                    });
                    if (entity.Id == 0)
                    {
                        GameEntry.Get<ILogModule>()?.Error("Expedition", "房间计划敌人创建失败：" + room.RoomId + "/" + spawn.EnemyID);
                        return false;
                    }
                    enemyDefinitions.Add(actorId, enemy);
                }
            }
            room.MarkEnemiesSpawned();
            return true;
        }

        /// <summary>
        /// 根据实体身份取得本局敌人的内容定义，表现和奖励共用同一来源。
        /// </summary>
        /// <param name="actorId">战斗角色身份。</param>
        /// <param name="definition">对应敌人定义。</param>
        /// <returns>角色由配置刷怪创建时为真。</returns>
        public bool TryGetEnemyDefinition(int actorId, out EnemyDefinition definition) => enemyDefinitions.TryGetValue(actorId, out definition);
    }
}
