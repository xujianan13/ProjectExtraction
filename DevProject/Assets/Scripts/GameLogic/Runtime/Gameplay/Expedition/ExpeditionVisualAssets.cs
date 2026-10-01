using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 持有一场关卡所需的热更预制件，资源租约由场景作用域释放。
    /// </summary>
    public sealed class ExpeditionVisualAssets
    {
        private static readonly string[] LocationNames =
        {
            "SC_Expedition", "RM_Prototype", "PF_TestWall", "PF_TestObstacle", "PF_TestPlayer", "PF_TestEnemy", "PF_TestBoss",
            "PF_TestNpc", "PF_TestChest", "PF_TestExit", "PF_TestExtraction", "PF_TestLoot", "PF_TestProjectile", "PF_TestMeleeCue"
        };
        private static readonly IReadOnlyList<string> ReadOnlyLocations = Array.AsReadOnly(LocationNames);
        private static readonly ResourceGroupDefinition Definition = CreateDefinition();

        /// <summary>
        /// 读取完整资源组中的关卡预制件。
        /// </summary>
        /// <param name="group">已加载的关卡资源组。</param>
        private ExpeditionVisualAssets(ResourceGroupLease group)
        {
            Scene = Get(group, LocationNames[0]);
            Room = Get(group, LocationNames[1]);
            Wall = Get(group, LocationNames[2]);
            Obstacle = Get(group, LocationNames[3]);
            Player = Get(group, LocationNames[4]);
            Enemy = Get(group, LocationNames[5]);
            Boss = Get(group, LocationNames[6]);
            Npc = Get(group, LocationNames[7]);
            Chest = Get(group, LocationNames[8]);
            Exit = Get(group, LocationNames[9]);
            Extraction = Get(group, LocationNames[10]);
            Loot = Get(group, LocationNames[11]);
            Projectile = Get(group, LocationNames[12]);
            MeleeCue = Get(group, LocationNames[13]);
        }

        public static IReadOnlyList<string> Locations => ReadOnlyLocations;
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
                group = await resources.AcquireGroupAsync(Definition, ct: linked.Token);
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
                if (!HasActiveCamera(assets.Scene))
                {
                    LogFailure("SC_Expedition 缺少 Camera，拒绝进入关卡。");
                    return null;
                }
                foreach (GameObject character in new[] { assets.Player, assets.Enemy, assets.Boss, assets.Npc })
                {
                    Animator animator = character.GetComponent<Animator>();
                    if (animator != null && animator.runtimeAnimatorController != null) continue;
                    LogFailure("角色预制件缺少 Animator 或动画控制器：" + character.name);
                    return null;
                }
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
            var items = new ResourceGroupDefinition.Item[LocationNames.Length];
            for (int index = 0; index < items.Length; index++)
                items[index] = new ResourceGroupDefinition.Item(LocationNames[index], LocationNames[index], ResourceGroupDefinition.AssetKind.GameObject);
            return new ResourceGroupDefinition("ExpeditionVisuals", items);
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
