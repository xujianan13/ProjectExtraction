using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using GameLogic.Project;
using GameLogic.Tables;
using GameLogic.Gameplay.Level;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 持有一场关卡所需的热更预制件，资源租约由场景作用域释放。
    /// </summary>
    public sealed class ExpeditionVisualAssets
    {
        private static readonly string[] roles = { "Scene", "Player", "Npc", "Chest", "Loot", "Projectile", "MeleeCue" };
        private readonly Dictionary<int, GameObject> enemies = new();
        private readonly Dictionary<int, RegionMapDefinition> maps = new();
        private readonly Dictionary<int, GameObject> mapPrefabs = new();
        private readonly Dictionary<int, Texture2D> mapTextures = new();

        /// <summary>
        /// 读取完整资源组中的关卡预制件。
        /// </summary>
        /// <param name="group">已加载的关卡资源组。</param>
        private ExpeditionVisualAssets(ResourceGroupLease group)
        {
            Scene = Get(group, ProjectContentConfig.Asset("Scene"));
            Player = Get(group, ProjectContentConfig.Asset("Player"));
            Npc = Get(group, ProjectContentConfig.Asset("Npc"));
            Chest = Get(group, ProjectContentConfig.Asset("Chest"));
            Loot = Get(group, ProjectContentConfig.Asset("Loot"));
            Projectile = Get(group, ProjectContentConfig.Asset("Projectile"));
            MeleeCue = Get(group, ProjectContentConfig.Asset("MeleeCue"));
            foreach (CfgEnemiesRow row in GameTables.CfgEnemies.GetAll()) enemies.Add(row.ID, Get(group, row.PrefabAddress));
        }

        public static IReadOnlyList<string> Locations
        {
            get
            {
                var locations = new string[roles.Length];
                for (int i = 0; i < roles.Length; i++) locations[i] = ProjectContentConfig.Asset(roles[i]);
                return Array.AsReadOnly(locations);
            }
        }

        /// <summary>
        /// 取得指定敌人配置加载的模型。
        /// </summary>
        /// <param name="definitionId">敌人配置编号。</param>
        /// <returns>对应模型，未加载时为空。</returns>
        public GameObject GetEnemy(int definitionId) => enemies.TryGetValue(definitionId, out GameObject prefab) ? prefab : null;

        /// <summary>
        /// 取得已经验证版本和几何结构的完整地图定义。
        /// </summary>
        /// <param name="mapId">地图配置编号。</param>
        /// <param name="map">对应的地图定义。</param>
        /// <returns>存在该地图时为真。</returns>
        public bool TryGetMap(int mapId, out RegionMapDefinition map) => maps.TryGetValue(mapId, out map);

        /// <summary>
        /// 取得地图绑定的完整原生组件预制件。
        /// </summary>
        /// <param name="mapId">地图配置编号。</param>
        /// <returns>地图预制件；未加载时为空。</returns>
        public GameObject GetMapPrefab(int mapId) => mapPrefabs.TryGetValue(mapId, out GameObject prefab) ? prefab : null;

        /// <summary>
        /// 取得同一几何生成的固定北向地图底图。
        /// </summary>
        /// <param name="mapId">地图配置编号。</param>
        /// <returns>地图底图；未加载时为空。</returns>
        public Texture2D GetMapTexture(int mapId) => mapTextures.TryGetValue(mapId, out Texture2D texture) ? texture : null;
        public GameObject Scene { get; }
        public GameObject Player { get; }
        public GameObject Npc { get; }
        public GameObject Chest { get; }
        public GameObject Loot { get; }
        public GameObject Projectile { get; }
        public GameObject MeleeCue { get; }
        public CombatFeedbackAssets Feedback { get; private set; }

        /// <summary>
        /// 完整加载关卡预制件，并交由场景作用域持有资源组租约。
        /// </summary>
        /// <param name="scope">关卡场景作用域。</param>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">本次加载取消令牌。</param>
        /// <returns>完整有效的关卡资源；预期资源失败时返回空。</returns>
        public static async UniTask<ExpeditionVisualAssets> LoadAsync(LifetimeScope scope, IResourceModule resources, CancellationToken ct = default)
        {
            if (scope == null || resources == null)
            {
                LogFailure("关卡资源加载缺少场景作用域或资源模块。");
                return null;
            }
            if (!scope.IsActive || ct.IsCancellationRequested) throw new OperationCanceledException(ct.IsCancellationRequested ? ct : scope.Token);

            ResourceGroupLease group = null;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(scope.Token, ct);
            try
            {
                group = await resources.AcquireGroupAsync(CreateDefinition(), ct: linked.Token);
                if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
                linked.Token.ThrowIfCancellationRequested();
                if (!scope.IsActive) throw new OperationCanceledException(scope.Token);
                if (group == null)
                {
                    LogFailure("关卡资源组未返回有效租约。");
                    return null;
                }

                foreach (string location in Locations)
                    if (!group.TryGet<GameObject>(location, out _))
                    {
                        LogFailure("关卡预制件缺失或类型错误：" + location);
                        return null;
                    }
                var assets = new ExpeditionVisualAssets(group);
                if (!assets.TryLoadMaps(group, out string mapError))
                {
                    LogFailure(mapError);
                    return null;
                }
                if (!HasActiveCamera(assets.Scene))
                {
                    LogFailure("SC_Expedition 缺少 Camera，拒绝进入关卡。");
                    return null;
                }
                var characters = new List<GameObject>(assets.enemies.Values) { assets.Player, assets.Npc };
                foreach (GameObject character in characters)
                {
                    Animator animator = character != null ? character.GetComponent<Animator>() : null;
                    if (animator != null && animator.runtimeAnimatorController != null) continue;
                    LogFailure("角色预制件缺少 Animator 或动画控制器：" + (character != null ? character.name : "未加载"));
                    return null;
                }
                assets.Feedback = await CombatFeedbackAssets.LoadAsync(scope, resources, linked.Token);
                if (assets.Feedback == null) return null;
                if (!scope.TryOwn(group)) throw new OperationCanceledException(scope.Token);
                group = null;
                return assets;
            }
            catch (ResourceGroupLoadException error)
            {
                foreach (ResourceLoadError failure in error.Failures) LogFailure(failure.Message);
                return null;
            }
            finally
            {
                group?.Dispose();
            }
        }

        /// <summary>
        /// 校验并绑定每张完整地图及同版本几何和底图。
        /// </summary>
        /// <param name="group">完整资源组。</param>
        /// <param name="error">首个资源错误说明。</param>
        /// <returns>全部地图可用时为真。</returns>
        private bool TryLoadMaps(ResourceGroupLease group, out string error)
        {
            foreach (CfgRegionMapsRow row in GameTables.CfgRegionMaps.GetAll())
            {
                if (!group.TryGet(row.PrefabAddress, out GameObject prefab) ||
                    !group.TryGet(row.GeometryAddress, out TextAsset geometry) ||
                    !group.TryGet(row.MinimapAddress, out Texture2D texture) || texture.width <= 0 || texture.height <= 0)
                { error = "区域地图资源缺失或类型错误：" + row.ID; return false; }
                RegionMapDefinition map;
                try
                {
                    map = JsonUtility.FromJson<RegionMapDefinition>(geometry.text);
                }
                catch (ArgumentException)
                {
                    error = "区域几何 JSON 无法解析：" + row.ID;
                    return false;
                }
                if (map == null || map.MapId != row.ID || map.Version != row.Version)
                { error = "区域几何身份或版本不匹配：" + row.ID; return false; }
                if (!map.TryValidate(out error)) return false;
                if (!MatchesPrefab(prefab, map))
                { error = "区域预制件与几何定义不一致：" + row.ID; return false; }
                maps.Add(row.ID, map);
                mapPrefabs.Add(row.ID, prefab);
                mapTextures.Add(row.ID, texture);
            }
            error = null;
            return true;
        }

        /// <summary>
        /// 检查原生地图的身份、地面、矩形障碍和无物理组件约束。
        /// </summary>
        /// <param name="prefab">完整地图预制件。</param>
        /// <param name="map">同地址组的几何。</param>
        /// <returns>地图表现与 ECS 几何一致时为真。</returns>
        private static bool MatchesPrefab(GameObject prefab, RegionMapDefinition map)
        {
            if (prefab.transform.Find("RegionMap_" + map.MapId + "_" + map.Version) == null ||
                prefab.GetComponentsInChildren<Collider>(true).Length != 0 ||
                prefab.GetComponentsInChildren<Rigidbody>(true).Length != 0) return false;
            Transform floor = prefab.transform.Find("Floor");
            if (floor == null ||
                !Mathf.Approximately(floor.localPosition.x, map.Bounds.center.x) ||
                !Mathf.Approximately(floor.localPosition.z, map.Bounds.center.y) ||
                !Mathf.Approximately(floor.localScale.x, map.Bounds.width) ||
                !Mathf.Approximately(floor.localScale.z, map.Bounds.height)) return false;
            for (int index = 0; index < map.Obstacles.Count; index++)
            {
                RegionMapObstacle obstacle = map.Obstacles[index];
                Transform visual = prefab.transform.Find("Obstacle_" + index);
                Vector2 center = (obstacle.Min + obstacle.Max) * 0.5f;
                Vector2 size = obstacle.Max - obstacle.Min;
                if (visual == null || !Mathf.Approximately(visual.localPosition.x, center.x) || !Mathf.Approximately(visual.localPosition.z, center.y) ||
                    !Mathf.Approximately(visual.localScale.x, size.x) || !Mathf.Approximately(visual.localScale.z, size.y)) return false;
            }
            return true;
        }

        /// <summary>
        /// 验证场景预制件中存在启用的相机。
        /// </summary>
        /// <param name="scene">场景预制件。</param>
        /// <returns>至少一台相机在启用的层级中生效时为真。</returns>
        private static bool HasActiveCamera(GameObject scene)
        {
            foreach (Camera camera in scene.GetComponentsInChildren<Camera>(true))
            {
                if (!camera.enabled) continue;
                Transform node = camera.transform;
                bool active = true;
                while (node != null)
                {
                    if (!node.gameObject.activeSelf) { active = false; break; }
                    if (node == scene.transform) break;
                    node = node.parent;
                }
                if (active) return true;
            }
            return false;
        }

        /// <summary>
        /// 建立完整预制件资源组定义。
        /// </summary>
        /// <returns>全部条目都必须成功的资源组。</returns>
        private static ResourceGroupDefinition CreateDefinition()
        {
            var addresses = new HashSet<string>(Locations);
            foreach (CfgEnemiesRow row in GameTables.CfgEnemies.GetAll()) addresses.Add(row.PrefabAddress);
            var items = new List<ResourceGroupDefinition.Item>();
            foreach (string address in addresses)
                items.Add(new ResourceGroupDefinition.Item(address, address, ResourceGroupDefinition.AssetKind.GameObject));
            foreach (CfgRegionMapsRow row in GameTables.CfgRegionMaps.GetAll())
            {
                items.Add(new ResourceGroupDefinition.Item(row.PrefabAddress, row.PrefabAddress, ResourceGroupDefinition.AssetKind.GameObject));
                items.Add(new ResourceGroupDefinition.Item(row.GeometryAddress, row.GeometryAddress, ResourceGroupDefinition.AssetKind.TextAsset));
                items.Add(new ResourceGroupDefinition.Item(row.MinimapAddress, row.MinimapAddress, ResourceGroupDefinition.AssetKind.Texture2D));
            }
            return new ResourceGroupDefinition("ExpeditionVisuals", items.ToArray());
        }

        /// <summary>
        /// 从仍有效的资源组中取得预制件。
        /// </summary>
        /// <param name="group">关卡资源组租约。</param>
        /// <param name="location">资源逻辑地址。</param>
        /// <returns>对应预制件。</returns>
        private static GameObject Get(ResourceGroupLease group, string location)
        {
            group.TryGet(location, out GameObject prefab);
            return prefab;
        }

        /// <summary>
        /// 记录可预期的关卡资源问题。
        /// </summary>
        /// <param name="message">错误说明。</param>
        private static void LogFailure(string message) => GameEntry.Get<ILogModule>()?.Error("Expedition", message);
    }
}
