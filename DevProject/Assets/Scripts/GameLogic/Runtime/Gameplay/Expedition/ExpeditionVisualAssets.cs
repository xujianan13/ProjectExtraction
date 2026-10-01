using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 持有一场关卡所需的热更预制件，资源租约由场景作用域释放。
    /// </summary>
    public sealed class ExpeditionVisualAssets
    {
        private static readonly string[] roles = { "Scene", "Room", "Wall", "Obstacle", "Player", "Enemy", "Boss", "Npc", "Chest",
            "Exit", "Extraction", "Loot", "Projectile", "MeleeCue" };
        private readonly Dictionary<int, GameObject> enemies = new();

        /// <summary>
        /// 读取完整资源组中的关卡预制件。
        /// </summary>
        /// <param name="group">已加载的关卡资源组。</param>
        private ExpeditionVisualAssets(ResourceGroupLease group)
        {
            var locations = Locations;
            Scene = Get(group, locations[0]);
            Room = Get(group, locations[1]);
            Wall = Get(group, locations[2]);
            Obstacle = Get(group, locations[3]);
            Player = Get(group, locations[4]);
            Enemy = Get(group, locations[5]);
            Boss = Get(group, locations[6]);
            Npc = Get(group, locations[7]);
            Chest = Get(group, locations[8]);
            Exit = Get(group, locations[9]);
            Extraction = Get(group, locations[10]);
            Loot = Get(group, locations[11]);
            Projectile = Get(group, locations[12]);
            MeleeCue = Get(group, locations[13]);
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
        public GameObject Scene { get; }
        public GameObject Room { get; }
        public GameObject Wall { get; }
        public GameObject Obstacle { get; }
        public GameObject Player { get; }
        public GameObject Enemy { get; }
        public GameObject Boss { get; }
        public GameObject Npc { get; }
        public GameObject Chest { get; }
        public GameObject Exit { get; }
        public GameObject Extraction { get; }
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
                Mesh floor = assets.Room.transform.Find("Floor")?.GetComponent<MeshFilter>()?.sharedMesh;
                if (floor == null || floor.bounds.size.x <= 0f || floor.bounds.size.z <= 0f)
                {
                    LogFailure("房间预制件缺少有效 Floor 网格，无法按配置生成房间地板。");
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
