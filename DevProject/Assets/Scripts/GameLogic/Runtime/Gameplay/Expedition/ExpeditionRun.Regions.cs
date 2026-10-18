using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 读取真实续关费用，已经完成最终领奖的存活检查点只补办通关，不再收取房间费用。
        /// </summary>
        public int RequiredResumeStamina => AdmissionPending || CurrentRoomKind == ExpeditionRoomKind.Chest ||
            IsFinalRoom && CurrentRoom.BossRewardsCompleted && !IsAwaitingDeathChoice && !IsChoosingReward
            ? 0 : PrototypeRules.RoomEntryCost;

        /// <summary>
        /// 绑定当前房间资源，并从玩家检查点建立独立战斗世界。
        /// </summary>
        /// <param name="assets">场景作用域持有的资源。</param>
        /// <param name="parent">地图表现父节点。</param>
        public void AttachScene(ExpeditionVisualAssets assets, Transform parent = null)
        {
            if (disposed || stopped || RegionView != null || assets == null) return;
            visualAssets = assets;
            sceneParent = parent;
            if (!PrepareRegion(CurrentRoomIndex, out RegionRuntimeState room, out RegionMapDefinition map, out RegionMapView view)) return;
            CurrentMap = map;
            RegionView = view;
            if (!BindRegionWorld(room)) { RegionView.Dispose(); RegionView = null; return; }
            Changed?.Invoke();
        }

        /// <summary>
        /// 在实际续关时准备当前房间，NPC 房直接前往下一房，尚未完成的准入不重复计费。
        /// </summary>
        /// <param name="error">体力或房间状态错误说明。</param>
        /// <returns>可以按当前检查点进入场景时为真。</returns>
        internal bool TryPrepareResume(out string error)
        {
            error = null;
            if (AdmissionPending) return true;
            int cost = RequiredResumeStamina;
            session.RefreshClock();
            if (session.Stamina < cost) { error = "体力不足，继续本房需要 " + cost + " 点体力。"; return false; }
            if (cost > 0 && RoomAdmissionSerial == int.MaxValue) { error = "房间准入次数已达到上限。"; return false; }
            if (CurrentRoomKind == ExpeditionRoomKind.Npc)
            {
                if (IsFinalRoom) { error = "NPC 房不能作为最终房间。"; return false; }
                CurrentRoomIndex++;
            }
            if (cost > 0) RoomAdmissionSerial++;
            AdmissionPending = true;
            return true;
        }

        /// <summary>
        /// 内容和扣费均确认后结束本次准入，下一次真实续关重新计算房间费用。
        /// </summary>
        internal void ConfirmRoomAdmission() => AdmissionPending = false;

        /// <summary>
        /// 首次生成目标房间并建立完整地图表现，既有内容保持不变。
        /// </summary>
        /// <param name="index">房间序号。</param>
        /// <param name="room">固定组合与运行状态。</param>
        /// <param name="map">完整地图几何。</param>
        /// <param name="view">待激活表现。</param>
        /// <returns>所有资源及生成规则均成功时为真。</returns>
        private bool PrepareRegion(int index, out RegionRuntimeState room, out RegionMapDefinition map, out RegionMapView view)
        {
            room = regions.Find(value => value.Plan.Request.RegionIndex == index);
            view = null;
            RegionGenerationRequest request = regionRequests[index];
            if (!visualAssets.TryGetMap(request.MapId, out map) || map.Version != request.MapVersion) return false;
            if (room == null)
            {
                RegionContentDefinition content = LevelContentConfig.CreateRegionContent(request);
                if (content == null || !RegionContentGenerator.TryGenerate(request, map, content, out RegionPlan plan, out _) ||
                    !RegionRuntimeState.TryCreate(plan, out room, out _))
                { Message = "房间内容生成失败，请检查配置。"; return false; }
                regions.Add(room);
            }
            if (!room.TryValidate(map, out string validation)) { Message = validation; return false; }
            view = RegionMapView.Create(room, map, visualAssets.GetMapPrefab(request.MapId), sceneParent);
            return view != null;
        }

        /// <summary>
        /// 创建只属于指定房间的战斗容器，不复用上一个房间的敌人、攻击或飞弹。
        /// </summary>
        /// <param name="roomId">固定房间身份。</param>
        /// <returns>尚未添加角色和地图的战斗世界。</returns>
        private CombatWorld CreateRoomWorld(int roomId) => new(CombatContentConfig.CreateCatalog(augmentBaseAttackRange),
            randomSeed: Seed ^ roomId) { ErrorLogger = reason => GameEntry.Get<ILogModule>().Error("Combat", reason) };

        /// <summary>
        /// 继承玩家自身状态，在新世界建立地图边界及本房敌人。
        /// </summary>
        /// <param name="room">当前准备进入的房间。</param>
        /// <returns>地图、玩家与敌人均建立成功时为真。</returns>
        private bool BindRegionWorld(RegionRuntimeState room)
        {
            if (!World.TryCapturePlayerCheckpoint(Player, out var checkpoint, out string error))
            { Message = error; return false; }
            int roomId = room.Plan.Request.RegionId;
            CombatWorld nextWorld = CreateRoomWorld(roomId);
            nextWorld.SetRoomBounds(roomId, CurrentMap.Bounds);
            foreach (RegionMapObstacle obstacle in CurrentMap.Obstacles)
                nextWorld.AddObstacle(roomId, Rect.MinMaxRect(obstacle.Min.x, obstacle.Min.y, obstacle.Max.x, obstacle.Max.y));
            if (!nextWorld.TryRestorePlayerCheckpoint(checkpoint, roomId, room.Plan.EntryPosition, out var player, out error))
            { nextWorld.Dispose(); Message = error; return false; }
            World.Dispose();
            World = nextWorld;
            Player = player;
            nextActorId = 2;
            enemyDefinitions.Clear();
            drops.Clear();
            if (!SpawnPendingEnemies(room)) return false;
            World.SetActiveRoom(roomId, Player);
            return RefreshPermanentAttributes();
        }

        /// <summary>
        /// 为固定房间槽位建立本次敌人生命，已领取标记继续保留在业务账本。
        /// </summary>
        /// <param name="room">正在进入的房间。</param>
        /// <returns>所有待生成敌人均成功建立时为真。</returns>
        private bool SpawnPendingEnemies(RegionRuntimeState room)
        {
            room.GetSpawnRequests(spawnRequests);
            foreach (RegionSpawnRequest request in spawnRequests)
            {
                if (!LevelContentConfig.TryGetEnemy(request.EnemyId, out EnemyDefinition enemy) || nextActorId == int.MaxValue) return false;
                int actorId = nextActorId;
                CombatEntityRef actor = World.CreateActor(new CombatActorSpec
                {
                    ActorId = actorId, RoomId = room.Plan.Request.RegionId, Team = 2, Position = request.Position,
                    MaxHealth = enemy.HealthAt(request.EnemyLevel), Attack = (float)enemy.Row.Attack, Defense = (float)enemy.Row.Defense,
                    MoveSpeed = (float)enemy.Row.MoveSpeed, Radius = (float)enemy.Row.Radius, AttackRange = (float)enemy.Row.AttackRange,
                    AggroRange = (float)enemy.Row.AggroRange, BasicSkillId = enemy.SkillID,
                    EnemySkills = CombatEnemySkillConfig.Create(enemy.ID), DamageVulnerabilities = enemy.CreateVulnerabilities()
                });
                if (actor.Id == 0) return false;
                nextActorId++;
                if (!room.CommitInitialSpawn(request.SlotId, actorId)) return false;
                enemyDefinitions.Add(actorId, enemy);
            }
            return true;
        }

        /// <summary>
        /// 完成当前房间后单向推进，房间体力和检查点确认后切换可见地图。
        /// </summary>
        /// <returns>转换已进入保存流程时为真。</returns>
        public bool TryContinueRoom()
        {
            if (!CanManagePanel || ActivePanel != ExpeditionPanelKind.ExtractionConfirmation || !IsPlayerAtExtraction() || IsFinalRoom) return false;
            session.RefreshClock();
            if (!session.CanAffordRoomEntry) return ReportInventoryFailure("体力不足，进入下一房需要 " + PrototypeRules.RoomEntryCost + " 点体力。");
            if (RoomAdmissionSerial == int.MaxValue) return ReportInventoryFailure("房间准入次数已达到上限。");
            int destination = CurrentRoomIndex + 1;
            if (!PrepareRegion(destination, out RegionRuntimeState next, out RegionMapDefinition map, out RegionMapView prepared)) return false;
            prepared.Root.SetActive(false);
            RegionMapView previous = RegionView;
            CurrentMap = map;
            CurrentRoomIndex = destination;
            RoomAdmissionSerial++;
            AdmissionPending = true;
            if (!BindRegionWorld(next)) { prepared.Dispose(); HandleFault("目标房间战斗状态建立失败。"); return false; }
            ActivePanel = ExpeditionPanelKind.None;
            ClearInteractionTargets();
            committing = true;
            CommitRoomTransitionAsync(previous, prepared).Forget();
            return true;
        }

        /// <summary>
        /// 分别确认扣费意图和已就绪房间，重试时复用同一次准入流水。
        /// </summary>
        /// <param name="previous">等待释放的上一个房间表现。</param>
        /// <param name="prepared">已经准备好的目标房间表现。</param>
        /// <returns>房间切换保存任务。</returns>
        private async UniTask CommitRoomTransitionAsync(RegionMapView previous, RegionMapView prepared)
        {
            if (!await ProjectServices.Coordinator.SaveRunBoundaryAsync(this, ProjectRunPhase.Playing) || disposed) return;
            ConfirmRoomAdmission();
            if (!await ProjectServices.Coordinator.SaveRunBoundaryAsync(this, ProjectRunPhase.Playing) || disposed) return;
            RegionView = prepared;
            prepared.Root.SetActive(true);
            previous.Dispose();
            committing = false;
            Message = IsFinalRoom ? "进入最终 Boss 房间。" : "进入房间 " + (CurrentRoomIndex + 1) + "。";
            PublishRewardState();
            Changed?.Invoke();
        }
    }
}
