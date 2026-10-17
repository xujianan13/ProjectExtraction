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
        private readonly List<ExpeditionDrop> drops = new();
        private PersistentRandom random;
        private bool disposed;
        private bool stopped;
        private bool paused;
        private bool interactionRequested;
        private bool committing;
        private int nextActorId = 2;
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
        public int CurrentRegionIndex { get; private set; }
        public int RegionCount => regionRequests.Count - 1;
        public bool IsFinalRegion => CurrentRegionIndex == regionRequests.Count - 1;
        public IReadOnlyList<RegionGenerationRequest> RegionRequests => regionRequests.ConvertAll(value => value.Copy());
        public RegionRuntimeState CurrentRegion => regions.Find(value => value.Plan.Request.RegionIndex == CurrentRegionIndex);
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
        public string Message { get; private set; } = "击杀积累强化进度；击败区域尾王后可带回结束或继续深入。";
        public IReadOnlyList<ProjectItem> Inventory => inventory.ConvertAll(item => item.Copy());
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
        /// <param name="roguePolicy">可选技能选择策略。</param>
        /// <param name="rogueAccess">可选第四选项授权。</param>
        private ExpeditionRun(ProjectSession session, string runId, LevelDefinition level, int seed,
            List<RegionGenerationRequest> requests, List<ProjectItem> items, RogueChoicePolicy roguePolicy, IRogueChoiceAccessProvider rogueAccess)
        {
            this.session = session;
            RunId = runId;
            LevelId = level.ID;
            LootLevel = level.LootLevel;
            Seed = seed;
            regionRequests.AddRange(requests.ConvertAll(value => value.Copy()));
            random = new PersistentRandom(seed ^ 0x5162);
            inventory.AddRange(items.ConvertAll(item => item.Copy()));
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
            InitializeRewardSessions(seed, level.Row.SkillPoolID, level.Row.ProgressRuleID, roguePolicy, rogueAccess);
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
        /// <param name="roguePolicy">可选技能选择策略。</param>
        /// <param name="rogueAccess">可选第四选项授权。</param>
        /// <returns>创建成功时为真。</returns>
        public static bool TryCreate(ProjectSession session, string runId, int levelId, int seed, List<ProjectItem> items, out ExpeditionRun run,
            RogueChoicePolicy roguePolicy = null, IRogueChoiceAccessProvider rogueAccess = null)
        {
            run = null;
            if (session == null || items == null || string.IsNullOrWhiteSpace(runId) ||
                !LevelContentConfig.TryGetLevel(levelId, out LevelDefinition level) ||
                !LevelContentConfig.TryCreateRegionRequests(runId, seed, level, out List<RegionGenerationRequest> requests, out _)) return false;
            var created = new ExpeditionRun(session, runId, level, seed, requests, items, roguePolicy, rogueAccess);
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
            if (value) { CancelReviveAd(); ClearPlayerInput(); }
            PublishRewardState();
            Changed?.Invoke();
        }

        /// <summary>
        /// 释放场景表现；完整关卡状态由单局生命周期持有。
        /// </summary>
        public void DetachScene()
        {
            CancelReviveAd();
            EndNavigation(null);
            IsRunning = false;
            IsReady = false;
            RegionView?.Dispose();
            RegionView = null;
            visualAssets = null;
            sceneParent = null;
        }

        /// <summary>
        /// 从唯一循环推进有效战斗时间，每个完整逻辑帧形成独立保存候选。
        /// </summary>
        /// <param name="deltaTime">当前帧真实经过秒数。</param>
        public void Advance(float deltaTime)
        {
            AdvanceDeathPresentation(deltaTime);
            if (!CanInteract) { World.ClearPendingTime(); return; }
            IInputModule input = GameEntry.Get<IInputModule>();
            Vector2 movement = input.GetVector2("Gameplay/Move");
            if (touchMove.sqrMagnitude > movement.sqrMagnitude) movement = touchMove;
            if (movement.sqrMagnitude > 0f && IsNavigating) CancelNavigation();
            if (!CanAdvance) { World.ClearPendingTime(); return; }
            PreparePlayerMovement(movement);
            if (input.GetButton("Gameplay/CycleInteraction").PressedThisFrame) CycleInteraction();
            if (input.GetButton("Gameplay/Interact").PressedThisFrame) RequestInteraction();
            bool interact = interactionRequested;
            World.Advance(deltaTime, () =>
            {
                bool proceed = CompleteTick(interact);
                ObserveNavigationTick();
                interact = false;
                interactionRequested = false;
                if (proceed) PreparePlayerMovement(movement);
                return proceed;
            });
            if (World.Faulted && IsRunning) HandleFault("战斗发生错误，已暂停并保留最近完整存档。");
        }

        /// <summary>
        /// 在同一边界消费死亡收益、个体复活与交互，并形成连续保存修订。
        /// </summary>
        /// <param name="interact">本帧是否提交一次交互。</param>
        /// <returns>本帧之后仍可推进时为真。</returns>
        private bool CompleteTick(bool interact)
        {
            events.Clear();
            World.DrainEvents(events);
            int playerDeathId = 0;
            foreach (CombatEvent fact in events)
            {
                RecordStatistics(fact);
                CombatEventPublished?.Invoke(fact);
                if (fact.Kind != CombatEventKind.Death) continue;
                if (fact.TargetActorId == 1) { playerDeathId = fact.DefinitionId; continue; }
                RegionSpawnSlotState slot = CurrentRegion.Slots.Find(value => value.CurrentActorId == fact.TargetActorId);
                if (slot == null || !CurrentRegion.TryRecordDeath(slot.SlotId, fact.TargetActorId, slot.LifeSequence)) continue;
                RegionSpawnSlotPlan plan = CurrentRegion.Plan.SpawnSlots.Find(value => value.SlotId == slot.SlotId);
                EnemyDefinition enemy = enemyDefinitions[fact.TargetActorId];
                float multiplier = slot.LifeSequence == 1 || plan.Kind == RegionEnemyKind.Boss ? 1f :
                    (plan.Kind == RegionEnemyKind.Elite ? CurrentRegion.Plan.EliteRespawn : CurrentRegion.Plan.NormalRespawn).ExperienceMultiplier;
                int previousExperience = KillExperience;
                KillExperience = (int)Math.Min(int.MaxValue, (long)KillExperience + Mathf.FloorToInt(enemy.ExperienceAt(plan.EnemyLevel) * multiplier));
                int gainedProgress = RecordProgressDeath(plan.Kind);
                if (plan.Kind == RegionEnemyKind.Boss) QueueBossReward(CurrentRegion.Plan.Request.RegionId);
                if (!CurrentRegion.TryConfirmDeathProcessed(slot.SlotId, slot.LifeSequence))
                { HandleFault("死亡收益确认失败，已保留存档。"); return false; }
                RecordEnemyKill(plan, slot.LifeSequence, KillExperience - previousExperience, gainedProgress);
            }
            if (playerDeathId > 0) { BeginDeathChoice(playerDeathId); return false; }
            CurrentRegion.AdvanceTick(EvaluateSpawnSafety);
            if (!SpawnPendingEnemies(CurrentRegion, false)) { HandleFault("敌人复活状态创建失败。"); return false; }
            if (TryBeginPendingReward()) return false;
            if (World.TryGetActor(Player, out CombatActorSnapshot player))
            {
                UpdateInteractionLabel(player.Position);
                if (interact) Interact(player.Position);
            }
            if (!committing && IsRunning && !QueueTickSnapshot()) return false;
            Changed?.Invoke();
            return CanAdvance;
        }

        /// <summary>
        /// 先冻结完整终局事实，随后由统一流程保存并结算。
        /// </summary>
        /// <param name="extracted">是否正常带回物品。</param>
        private void EndRun(bool extracted)
        {
            if (ProjectServices.Coordinator.TryEndRun(this, extracted)) return;
            HandleFault("终局未被流程接受，请重试恢复本关。");
        }

        /// <summary>
        /// 暂停故障状态，保持最近完整修订供同局恢复。
        /// </summary>
        /// <param name="reason">玩家可读的故障说明。</param>
        private void HandleFault(string reason)
        {
            EndNavigation(null);
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
            if (IsNavigating) CancelNavigation();
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
            EndNavigation(null);
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
