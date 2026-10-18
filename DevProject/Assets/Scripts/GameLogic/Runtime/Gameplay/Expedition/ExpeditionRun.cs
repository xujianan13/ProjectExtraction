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
    /// 地面战利品的唯一归属与领取记录。
    /// </summary>
    [Serializable]
    public sealed class ExpeditionDrop
    {
        public int RoomId;
        public Vector3 Position;
        public ProjectItem Item;
        public bool Collected;

        /// <summary>
        /// 创建独立物品及地面状态副本。
        /// </summary>
        /// <returns>不共享物品的副本。</returns>
        public ExpeditionDrop Copy() => new() { RoomId = RoomId, Position = Position, Item = Item.Copy(), Collected = Collected };
    }

    /// <summary>
    /// 同一关卡的区域计划、完整战斗、交互与奖励协调器。
    /// </summary>
    public sealed partial class ExpeditionRun : IDisposable
    {
        private readonly ProjectSession session;
        private readonly Dictionary<int, EnemyDefinition> enemyDefinitions = new();
        private readonly List<RegionGenerationRequest> regionRequests = new();
        private readonly List<RegionRuntimeState> regions = new();
        private readonly List<CombatEvent> events = new();
        private readonly List<CombatActorSnapshot> actorSnapshots = new();
        private readonly List<RegionSpawnRequest> spawnRequests = new();
        private readonly List<ProjectItem> inventory = new();
        private readonly List<ProjectItem> entryEquipment = new();
        private readonly List<ExpeditionDrop> drops = new();
        private PersistentRandom random;
        private bool disposed;
        private bool stopped;
        private bool paused;
        private bool interactionRequested;
        private bool committing;
        private int nextActorId = 2;
        private long elapsedTicks;
        private ExpeditionVisualAssets visualAssets;
        private Transform sceneParent;
        private Vector2 touchMove;
        private ProjectRunOutcome pendingOutcome;

        public event Action Changed;
        public event Action<CombatEvent> CombatEventPublished;
        public string RunId { get; }
        public int LevelId { get; }
        public int LootLevel { get; }
        public int Seed { get; }
        public int CurrentRoomIndex { get; private set; }
        public int RoomCount => regionRequests.Count;
        public bool IsFinalRoom => CurrentRoomIndex == regionRequests.Count - 1;
        public ExpeditionRoomKind CurrentRoomKind => regionRequests[CurrentRoomIndex].RoomKind;
        public bool IsCurrentRoomComplete => CurrentRoom != null && CurrentRoom.ExitEnabled;
        public IReadOnlyList<RegionGenerationRequest> RegionRequests => regionRequests.ConvertAll(value => value.Copy());
        public RegionRuntimeState CurrentRoom => regions.Find(value => value.Plan.Request.RegionIndex == CurrentRoomIndex);
        public RegionMapDefinition CurrentMap { get; private set; }
        public RegionMapView RegionView { get; private set; }
        public CombatWorld World { get; private set; }
        public CombatEntityRef Player { get; private set; }
        public bool IsReady { get; private set; }
        public bool IsRunning { get; private set; }
        public int KillExperience { get; private set; }
        public int GoldSpent { get; private set; }
        public int AvailableGold => session.Gold - GoldSpent;
        public bool IsSavingRun => committing || session.RunSaveInteractionBlocked;
        private bool CanInteract => IsRunning && IsReady && !paused && ActivePanel == ExpeditionPanelKind.None && !IsChoosingReward &&
            !disposed && !stopped && !IsAwaitingDeathChoice && !IsSavingRun && !session.HasUncommittedSave && !GameEntry.IsInBackground;
        /// <summary>
        /// 完整战斗帧只在保存队列未达到预算时继续推进，等待期间仍保留玩家输入。
        /// </summary>
        private bool CanAdvance => CanInteract && !session.RunSaveBlocked;
        public bool CanAcceptPlayerInput => CanInteract;
        public string Message { get; private set; } = "完成当前房间后向上前进，击败最终 Boss 并领取奖励后带回物品。";
        public IReadOnlyList<ProjectItem> Inventory => inventory.ConvertAll(item => item.Copy());
        public IReadOnlyList<ProjectItem> EntryEquipment => entryEquipment.ConvertAll(item => item.Copy());
        public IReadOnlyList<ExpeditionDrop> Drops => drops;
        public string InteractionLabel { get; private set; } = "交互 / E";

        /// <summary>
        /// 冻结整关Boss与地图身份，区域内容在首次绑定资源时生成。
        /// </summary>
        /// <param name="session">统一账号与保存会话。</param>
        /// <param name="runId">本局稳定身份。</param>
        /// <param name="level">已经校验的关卡配置。</param>
        /// <param name="seed">整关初始随机种子。</param>
        /// <param name="requests">开局冻结的区域请求。</param>
        /// <param name="items">本局携入装备。</param>
        private ExpeditionRun(ProjectSession session, string runId, LevelDefinition level, int seed,
            List<RegionGenerationRequest> requests, List<ProjectItem> items)
        {
            this.session = session;
            RunId = runId;
            LevelId = level.ID;
            LootLevel = level.LootLevel;
            Seed = seed;
            regionRequests.AddRange(requests.ConvertAll(value => value.Copy()));
            random = new PersistentRandom(seed ^ 0x5162);
            entryEquipment.AddRange(items.ConvertAll(item => item.Copy()));
            InitializeStatistics(items);
            CombatActorSpec spec = EquipmentSpec(items);
            augmentBaseAttackRange = spec.AttackRange;
            World = new CombatWorld(CombatContentConfig.CreateCatalog(augmentBaseAttackRange), randomSeed: seed)
            { ErrorLogger = reason => GameEntry.Get<ILogModule>().Error("Combat", reason) };
            spec.ActorId = 1;
            spec.RoomId = requests[0].RegionId;
            spec.Team = 1;
            spec.IsPlayer = true;
            spec.Radius = (float)ProjectContentConfig.Player.Radius;
            spec.BasicSkillId = ProjectContentConfig.Player.SkillID;
            Player = World.CreateActor(spec);
            World.SetActiveRoom(requests[0].RegionId, Player);
            InitializeRewardSessions(seed);
        }

        /// <summary>
        /// 用新种子创建尚未入场的关卡，创建失败不扣体力。
        /// </summary>
        /// <param name="session">账号会话。</param>
        /// <param name="runId">单局身份。</param>
        /// <param name="levelId">关卡定义。</param>
        /// <param name="items">入场装备。</param>
        /// <param name="run">创建的单局。</param>
        /// <returns>全部计划和玩家初始化成功时为真。</returns>
        public static bool TryCreate(ProjectSession session, string runId, int levelId, List<ProjectItem> items, out ExpeditionRun run) =>
            TryCreate(session, runId, levelId, Guid.NewGuid().GetHashCode(), items, out run);

        /// <summary>
        /// 用指定种子冻结本关的全部区域Boss与对应资源。
        /// </summary>
        /// <param name="session">账号会话。</param>
        /// <param name="runId">单局身份。</param>
        /// <param name="levelId">关卡定义。</param>
        /// <param name="seed">确定性初始种子。</param>
        /// <param name="items">入场装备。</param>
        /// <param name="run">创建的单局。</param>
        /// <returns>创建成功时为真。</returns>
        public static bool TryCreate(ProjectSession session, string runId, int levelId, int seed, List<ProjectItem> items, out ExpeditionRun run)
        {
            run = null;
            if (session == null || items == null || string.IsNullOrWhiteSpace(runId) ||
                !LevelContentConfig.TryGetLevel(levelId, out LevelDefinition level) ||
                !LevelContentConfig.TryCreateRoomRequests(runId, seed, level, out List<RegionGenerationRequest> requests, out _)) return false;
            var created = new ExpeditionRun(session, runId, level, seed, requests, items);
            if (created.Player.Id == 0 || !created.InitializeCombatSkills() || !created.InitializeEquipmentAugments())
            { created.Dispose(); return false; }
            run = created;
            return true;
        }

        /// <summary>
        /// 场景内容与界面建立后标记就绪，仍等待持久激活确认。
        /// </summary>
        /// <returns>地图完整时为真。</returns>
        public bool MarkReady()
        {
            if (disposed || stopped || RegionView == null) return false;
            IsReady = true;
            return true;
        }

        /// <summary>
        /// 完整初始修订与入场记录确认后开放模拟。
        /// </summary>
        /// <returns>单局已就绪时为真。</returns>
        public bool Activate()
        {
            if (disposed || stopped || !IsReady) return false;
            IsRunning = true;
            paused = false;
            World.ClearPendingTime();
            if (IsAwaitingDeathChoice) RestoreDeathChoice();
            PublishRewardState();
            return true;
        }

        /// <summary>
        /// 调整应用暂停原因，并清除跨帧输入。
        /// </summary>
        /// <param name="value">是否暂停。</param>
        public void SetPaused(bool value)
        {
            if (disposed || stopped || paused == value) return;
            paused = value;
            if (value) { CancelReviveAd(); CancelExtractionAd(); ClearPlayerInput(); }
            PublishRewardState();
            Changed?.Invoke();
        }

        /// <summary>
        /// 释放场景表现；完整关卡状态由单局生命周期持有。
        /// </summary>
        public void DetachScene()
        {
            CancelReviveAd();
            CancelExtractionAd();
            IsRunning = false;
            IsReady = false;
            RegionView?.Dispose();
            RegionView = null;
            visualAssets = null;
            sceneParent = null;
        }

        /// <summary>
        /// 从唯一循环推进有效战斗时间，在击杀或关键操作后提交业务检查点。
        /// </summary>
        /// <param name="deltaTime">当前帧真实经过秒数。</param>
        public void Advance(float deltaTime)
        {
            AdvanceDeathPresentation(deltaTime);
            if (!CanInteract) { World.ClearPendingTime(); return; }
            IInputModule input = GameEntry.Get<IInputModule>();
            Vector2 movement = input.GetVector2("Gameplay/Move");
            if (touchMove.sqrMagnitude > movement.sqrMagnitude) movement = touchMove;
            if (!CanAdvance) { World.ClearPendingTime(); return; }
            World.SetMove(Player, movement);
            if (input.GetButton("Gameplay/CycleInteraction").PressedThisFrame) CycleInteraction();
            if (input.GetButton("Gameplay/Interact").PressedThisFrame) RequestInteraction();
            bool interact = interactionRequested;
            World.Advance(deltaTime, () =>
            {
                bool proceed = CompleteTick(interact);
                interact = false;
                interactionRequested = false;
                if (proceed) World.SetMove(Player, movement);
                return proceed;
            });
            if (World.Faulted && IsRunning) HandleFault("战斗发生错误，已暂停并保留最近完整存档。");
        }

        /// <summary>
        /// 在同一边界消费死亡收益与交互，在业务事实改变后提交保存。
        /// </summary>
        /// <param name="interact">本帧是否提交一次交互。</param>
        /// <returns>本帧之后仍可推进时为真。</returns>
        private bool CompleteTick(bool interact)
        {
            elapsedTicks++;
            CurrentRoom.AdvanceTick();
            events.Clear();
            World.DrainEvents(events);
            int playerDeathId = 0;
            bool progressionChanged = false;
            foreach (CombatEvent fact in events)
            {
                RecordStatistics(fact);
                CombatEventPublished?.Invoke(fact);
                if (fact.Kind != CombatEventKind.Death) continue;
                if (fact.TargetActorId == 1) { playerDeathId = fact.DefinitionId; continue; }
                RegionSpawnSlotState slot = CurrentRoom.Slots.Find(value => value.CurrentActorId == fact.TargetActorId);
                if (slot == null || !CurrentRoom.TryRecordDeath(slot.SlotId, fact.TargetActorId, slot.LifeSequence)) continue;
                RegionSpawnSlotPlan plan = CurrentRoom.Plan.SpawnSlots.Find(value => value.SlotId == slot.SlotId);
                EnemyDefinition enemy = enemyDefinitions[fact.TargetActorId];
                int previousExperience = KillExperience;
                bool firstReward = !slot.RewardClaimed;
                if (firstReward)
                {
                    KillExperience = (int)Math.Min(int.MaxValue, (long)KillExperience + enemy.ExperienceAt(plan.EnemyLevel));
                    if (plan.Kind == RegionEnemyKind.Boss) QueueBossReward(CurrentRoom.Plan.Request.RegionId);
                    RecordEnemyKill(plan, slot.LifeSequence, KillExperience - previousExperience, 0);
                }
                if (!CurrentRoom.TryConfirmDeathProcessed(slot.SlotId, slot.LifeSequence))
                { HandleFault("死亡收益确认失败，已保留存档。"); return false; }
                progressionChanged = true;
            }
            if (playerDeathId > 0) { BeginDeathChoice(playerDeathId); return false; }
            if (TryBeginPendingReward()) return false;
            if (World.TryGetActor(Player, out CombatActorSnapshot player))
            {
                UpdateInteractionLabel(player.Position);
                if (interact) Interact(player.Position);
            }
            if (!committing && IsRunning && progressionChanged)
            {
                SaveMutation(CurrentRoom.ExitEnabled ? "房间已完成，前往上方出口。" : "击杀收益已确认。");
                return false;
            }
            Changed?.Invoke();
            return CanAdvance;
        }

        /// <summary>
        /// 先冻结完整终局事实，随后由统一流程保存并结算。
        /// </summary>
        /// <param name="exitKind">已经确认的离场类型。</param>
        private void EndRun(ProjectRunExitKind exitKind)
        {
            if (ProjectServices.Coordinator.TryEndRun(this, exitKind)) return;
            HandleFault("终局未被流程接受，请重试恢复本关。");
        }

        /// <summary>
        /// 暂停故障状态，保持最近完整修订供同局恢复。
        /// </summary>
        /// <param name="reason">玩家可读的故障说明。</param>
        private void HandleFault(string reason)
        {
            paused = true;
            Message = reason;
            ClearPlayerInput();
            Changed?.Invoke();
            ProjectServices.Coordinator.RecoverInterruptedRunAsync(reason).Forget();
        }

        /// <summary>
        /// 提交触屏移动轴，暂停或非法输入时清空。
        /// </summary>
        /// <param name="axis">归一化移动轴。</param>
        public void SetTouchMove(Vector2 axis)
        {
            if (axis == Vector2.zero || !CanInteract || float.IsNaN(axis.x) || float.IsInfinity(axis.x) || float.IsNaN(axis.y) || float.IsInfinity(axis.y))
            { touchMove = Vector2.zero; return; }
            touchMove = Vector2.ClampMagnitude(axis, 1);
        }

        /// <summary>
        /// 锁定按下时的具体目标，交互只在完整战斗帧后执行。
        /// </summary>
        public void RequestInteraction()
        {
            if (!CanInteract || !World.TryGetActor(Player, out CombatActorSnapshot player) || player.Dead) return;
            UpdateInteractionLabel(player.Position);
            requestedInteractionId = SelectedInteractionId;
            interactionRequested = HasInteraction;
        }

        /// <summary>
        /// 清除输入与未推进时间，不改变应用或面板暂停原因。
        /// </summary>
        private void ClearPlayerInput()
        {
            touchMove = Vector2.zero;
            interactionRequested = false;
            World.ClearPendingTime();
            World.SetMove(Player, Vector2.zero);
        }

        /// <summary>
        /// 冻结战斗与输入，保留所有奖励事实供终局和异常恢复使用。
        /// </summary>
        public void Stop()
        {
            CancelReviveAd();
            CancelExtractionAd();
            stopped = true;
            IsRunning = false;
            ActivePanel = ExpeditionPanelKind.None;
            chestLootItems.Clear();
            ClearInteractionTargets();
            ClearPlayerInput();
            Changed?.Invoke();
        }

        /// <summary>
        /// 释放关卡表现、授权等待和战斗世界，不重放任何效果。
        /// </summary>
        public void Dispose()
        {
            if (disposed) return;
            Stop();
            disposed = true;
            DetachScene();
            World.Dispose();
            Changed = null;
            CombatEventPublished = null;
            DisposeRewardSessions();
        }

        /// <summary>
        /// 根据本局生命身份取得配置定义，供表现读取名称与模型。
        /// </summary>
        /// <param name="actorId">全局递增角色身份。</param>
        /// <param name="definition">对应敌人定义。</param>
        /// <returns>角色来自固定生成槽位时为真。</returns>
        public bool TryGetEnemyDefinition(int actorId, out EnemyDefinition definition) => enemyDefinitions.TryGetValue(actorId, out definition);
    }
}
