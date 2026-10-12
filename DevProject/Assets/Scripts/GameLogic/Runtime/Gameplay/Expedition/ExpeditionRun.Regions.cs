using System;
using GameLogic.Gameplay.Combat;
using GameLogic.Gameplay.Level;
using GameLogic.Project;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    public sealed partial class ExpeditionRun
    {
        /// <summary>
        /// 绑定当前完整地图资源，恢复时复用已保存的组合与战斗状态。
        /// </summary>
        /// <param name="assets">场景作用域持有的资源。</param>
        /// <param name="parent">地图表现父节点。</param>
        public void AttachScene(ExpeditionVisualAssets assets, Transform parent = null)
        {
            if (disposed || stopped || RegionView != null || assets == null) return;
            visualAssets = assets;
            sceneParent = parent;
            bool restoring = CurrentRegion != null;
            if (!PrepareRegion(CurrentRegionIndex, out RegionRuntimeState region, out RegionMapDefinition map, out RegionMapView view)) return;
            CurrentMap = map;
            RegionView = view;
            if (!BindRegionWorld(region, restoring)) { RegionView.Dispose(); RegionView = null; return; }
            Changed?.Invoke();
        }

        /// <summary>
        /// 首次生成目标区域并建立完整地图表现，已生成组合不再抽取。
        /// </summary>
        /// <param name="index">区域序号。</param>
        /// <param name="region">固定组合与运行状态。</param>
        /// <param name="map">完整地图几何。</param>
        /// <param name="view">待激活表现。</param>
        /// <returns>所有资源及生成规则均成功时为真。</returns>
        private bool PrepareRegion(int index, out RegionRuntimeState region, out RegionMapDefinition map, out RegionMapView view)
        {
            region = regions.Find(value => value.Plan.Request.RegionIndex == index);
            view = null;
            RegionGenerationRequest request = regionRequests[index];
            if (!visualAssets.TryGetMap(request.MapId, out map) || map.Version != request.MapVersion) return false;
            if (region == null)
            {
                RegionContentDefinition content = LevelContentConfig.CreateRegionContent(request);
                if (content == null || !RegionContentGenerator.TryGenerate(request, map, content, out RegionPlan plan, out _) ||
                    !RegionRuntimeState.TryCreate(plan, out region, out _))
                { Message = "区域内容生成失败，请检查配置。"; return false; }
                regions.Add(region);
            }
            if (!region.TryValidate(map, out string validation)) { Message = validation; return false; }
            view = RegionMapView.Create(region, map, visualAssets.GetMapPrefab(request.MapId), sceneParent);
            return view != null;
        }

        /// <summary>
        /// 设置区域边界并创建初始敌人，恢复已有区域时保留完整世界。
        /// </summary>
        /// <param name="region">当前区域。</param>
        /// <param name="restoring">是否已经存在战斗状态。</param>
        /// <returns>区域可被激活时为真。</returns>
        private bool BindRegionWorld(RegionRuntimeState region, bool restoring)
        {
            if (restoring) return true;
            int regionId = region.Plan.Request.RegionId;
            World.SetRoomBounds(regionId, CurrentMap.Bounds);
            foreach (RegionMapObstacle obstacle in CurrentMap.Obstacles)
                World.AddObstacle(regionId, Rect.MinMaxRect(obstacle.Min.x, obstacle.Min.y, obstacle.Max.x, obstacle.Max.y));
            if (!World.TryTransferActor(Player, regionId, region.Plan.EntryPosition) || !SpawnPendingEnemies(region, true)) return false;
            World.SetActiveRoom(regionId, Player);
            return true;
        }

        /// <summary>
        /// 结合动态占位、玩家距离和相机范围检查复活安全条件。
        /// </summary>
        /// <param name="slot">固定出生槽位。</param>
        /// <returns>三个独立安全条件的结果。</returns>
        private RegionSpawnSafety EvaluateSpawnSafety(RegionSpawnSlotPlan slot)
        {
            World.TryGetActor(Player, out CombatActorSnapshot player);
            float safeDistance = slot.Kind == RegionEnemyKind.Elite ? CurrentRegion.Plan.EliteRespawn.SafeDistance : CurrentRegion.Plan.NormalRespawn.SafeDistance;
            bool available = true;
            World.GetActorsSnapshot(actorSnapshots);
            foreach (CombatActorSnapshot actor in actorSnapshots)
            {
                if (actor.Dead || actor.RoomId != CurrentRegion.Plan.Request.RegionId) continue;
                float actorRadius = actor.ActorId == 1 ? (float)ProjectContentConfig.Player.Radius : (float)enemyDefinitions[actor.ActorId].Row.Radius;
                float radius = actorRadius + slot.Radius;
                if ((actor.Position - slot.HomePosition).sqrMagnitude < radius * radius) { available = false; break; }
            }
            Camera camera = Camera.main;
            Vector3 screen = camera != null ? camera.WorldToViewportPoint(slot.HomePosition) : new Vector3(-1, -1, -1);
            bool visible = screen.z > 0 && screen.x >= -0.05f && screen.x <= 1.05f && screen.y >= -0.05f && screen.y <= 1.05f;
            return new RegionSpawnSafety { PositionAvailable = available, OutsideSafeDistance =
                (player.Position - slot.HomePosition).sqrMagnitude >= safeDistance * safeDistance, OutsideCamera = !visible };
        }

        /// <summary>
        /// 为准备好的稳定槽位创建新的全局生命身份。
        /// </summary>
        /// <param name="region">所属区域。</param>
        /// <param name="initial">是否正在建立初始区域。</param>
        /// <returns>初始生成完整且复活请求均有效时为真。</returns>
        private bool SpawnPendingEnemies(RegionRuntimeState region, bool initial)
        {
            region.GetSpawnRequests(spawnRequests);
            foreach (RegionSpawnRequest request in spawnRequests)
            {
                RegionSpawnSlotPlan plan = region.Plan.SpawnSlots.Find(value => value.SlotId == request.SlotId);
                RegionSpawnSlotState state = region.Slots.Find(value => value.SlotId == request.SlotId);
                if (!initial && !EvaluateSpawnSafety(plan).IsSafe) continue;
                if (!LevelContentConfig.TryGetEnemy(request.EnemyId, out EnemyDefinition enemy) || nextActorId == int.MaxValue) return false;
                if (state.CurrentActorId > 0)
                {
                    if (!World.TryFindActor(state.CurrentActorId, out CombatEntityRef oldActor) || !World.TryRetireDeadActor(oldActor)) return false;
                    enemyDefinitions.Remove(state.CurrentActorId);
                }
                int actorId = nextActorId;
                CombatEntityRef actor = World.CreateActor(new CombatActorSpec
                {
                    ActorId = actorId, RoomId = region.Plan.Request.RegionId, Team = 2, Position = request.Position,
                    MaxHealth = enemy.HealthAt(request.EnemyLevel), Attack = (float)enemy.Row.Attack, Defense = (float)enemy.Row.Defense,
                    MoveSpeed = (float)enemy.Row.MoveSpeed, Radius = (float)enemy.Row.Radius, AttackRange = (float)enemy.Row.AttackRange,
                    AggroRange = (float)enemy.Row.AggroRange, BasicSkillId = enemy.SkillID,
                    EnemySkills = CombatEnemySkillConfig.Create(enemy.ID), DamageVulnerabilities = enemy.CreateVulnerabilities()
                });
                if (actor.Id == 0) return false;
                nextActorId++;
                bool accepted = request.IsInitial ? region.CommitInitialSpawn(request.SlotId, actorId) :
                    region.CommitRespawn(request.SlotId, request.ExpectedLifeSequence, actorId);
                if (!accepted) return false;
                enemyDefinitions.Add(actorId, enemy);
            }
            return true;
        }

        /// <summary>
        /// 取得当前区域的实际位置与固定目标投影，普通怪不进入关键点地图。
        /// </summary>
        /// <returns>本区域地图标记；未绑定地图时为空。</returns>
        public RegionMapProjection GetMapProjection()
        {
            if (CurrentRegion == null || CurrentMap == null || !World.TryGetActor(Player, out CombatActorSnapshot player)) return null;
            World.TryGetAnimationSnapshot(Player, out CombatAnimationSnapshot animation);
            return RegionMapProjection.Build(CurrentRegion, CurrentMap, player.Position, animation.Forward, actorId =>
            {
                World.TryFindActor(actorId, out CombatEntityRef actor);
                World.TryGetActor(actor, out CombatActorSnapshot snapshot);
                return snapshot.Position;
            });
        }

        /// <summary>
        /// 取得资源提供的北向底图。
        /// </summary>
        /// <returns>当前区域小地图纹理。</returns>
        public Texture2D MapTexture => visualAssets != null ? visualAssets.GetMapTexture(regionRequests[CurrentRegionIndex].MapId) : null;

        /// <summary>
        /// 展开地图时采用独立面板暂停。
        /// </summary>
        /// <returns>地图已打开时为真。</returns>
        public bool TryOpenMap()
        {
            if (ActivePanel == ExpeditionPanelKind.Map && CanManagePanel) return true;
            if (!CanInteract || !CanManagePanel) return false;
            SetActivePanel(ExpeditionPanelKind.Map);
            return true;
        }

        /// <summary>
        /// 追踪具体目标并保存，不执行自动移动。
        /// </summary>
        /// <param name="targetId">稳定标记身份，空字符串表示取消。</param>
        /// <returns>目标存在且状态已接纳时为真。</returns>
        public bool TryTrackMapTarget(string targetId)
        {
            if (ActivePanel != ExpeditionPanelKind.Map || !CanManagePanel || !CurrentRegion.TrySetTrackingTarget(targetId)) return false;
            SaveMutation(string.IsNullOrEmpty(targetId) ? "已取消追踪。" : "已更新地图追踪。");
            return true;
        }

        /// <summary>
        /// 区域领奖完成后单向进入下一域，完整转换保存后才切换可见地图。
        /// </summary>
        /// <returns>转换已进入保存时为真。</returns>
        public bool TryContinueRegion()
        {
            if (!CanManagePanel || ActivePanel != ExpeditionPanelKind.ExtractionConfirmation || !IsPlayerAtExtraction() || IsFinalRegion) return false;
            int destination = CurrentRegionIndex + 1;
            if (!PrepareRegion(destination, out RegionRuntimeState next, out RegionMapDefinition map, out RegionMapView prepared)) return false;
            prepared.Root.SetActive(false);
            RegionMapView previous = RegionView;
            EndNavigation(null);
            CurrentMap = map;
            CurrentRegionIndex = destination;
            if (!BindRegionWorld(next, false)) { prepared.Dispose(); HandleFault("目标区域战斗状态建立失败。"); return false; }
            ActivePanel = ExpeditionPanelKind.None;
            ClearInteractionTargets();
            SaveMutation(IsFinalRegion ? "进入最终Boss场地。" : "进入区域 " + (CurrentRegionIndex + 1) + "。", () =>
            {
                RegionView = prepared;
                prepared.Root.SetActive(true);
                previous.Dispose();
            });
            return true;
        }
    }
}
