using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using GameLogic.Gameplay.Expedition;
using GameLogic.Gameplay.Level;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace ProjectExtraction.Tests
{
    /// <summary>
    /// 验证关卡热更地址对应真实预制体及其几何与依赖契约。
    /// </summary>
    public sealed class ExpeditionPrototypeAssetTests
    {
        private const string Root = "Assets/GameResources";
        private static readonly Dictionary<string, string> Paths = new()
        {
            ["SC_Expedition"] = "Scenes/Expedition",
            ["RM_Prototype"] = "Scenes/Expedition/Rooms",
            ["PF_TestWall"] = "Gameplay/Expedition/Environment",
            ["PF_TestObstacle"] = "Gameplay/Expedition/Environment",
            ["PF_TestPlayer"] = "Gameplay/Expedition/Characters",
            ["PF_TestEnemy"] = "Gameplay/Expedition/Characters",
            ["PF_TestBoss"] = "Gameplay/Expedition/Characters",
            ["PF_TestNpc"] = "Gameplay/Expedition/Characters",
            ["PF_TestChest"] = "Gameplay/Expedition/Interactables",
            ["PF_TestExit"] = "Gameplay/Expedition/Markers",
            ["PF_TestExtraction"] = "Gameplay/Expedition/Markers",
            ["PF_TestLoot"] = "Gameplay/Expedition/Loot",
            ["PF_TestProjectile"] = "Gameplay/Expedition/Effects",
            ["PF_TestMeleeCue"] = "Gameplay/Expedition/Effects"
        };

        /// <summary>
        /// 每个运行时地址均唯一，且能从热更目录加载预制体与所需材质。
        /// </summary>
        [Test]
        public void LocationsResolveToRealPrefabsWithHotUpdateDependencies()
        {
            Assert.That(ExpeditionVisualAssets.Locations.Count, Is.EqualTo(14));
            Assert.That(ExpeditionVisualAssets.Locations.Distinct().Count(), Is.EqualTo(14));
            CollectionAssert.AreEquivalent(Paths.Keys, ExpeditionVisualAssets.Locations);
            foreach (string location in ExpeditionVisualAssets.Locations)
            {
                string path = PathFor(location);
                Assert.That(AssetDatabase.LoadMainAssetAtPath(path), Is.TypeOf<GameObject>(), location);
                Assert.That(PrefabUtility.GetPrefabAssetType(AssetDatabase.LoadAssetAtPath<GameObject>(path)), Is.EqualTo(PrefabAssetType.Regular), location);
                string[] matches = AssetDatabase.FindAssets("t:Prefab", new[] { Root })
                    .Select(AssetDatabase.GUIDToAssetPath).Where(candidate => Path.GetFileNameWithoutExtension(candidate) == location).ToArray();
                Assert.That(matches, Is.EquivalentTo(new[] { path }), "地址存在重复或未落在约定目录：" + location);
                foreach (string dependency in AssetDatabase.GetDependencies(path, true))
                    if (dependency.StartsWith("Assets/", StringComparison.Ordinal))
                        Assert.That(dependency, Does.StartWith(Root + "/"), "热更预制体引用了宿主资产：" + dependency);
            }
        }

        /// <summary>
        /// 视觉预制体只包含网格子节点和持久化材质，不携带物理或热更脚本组件。
        /// </summary>
        [Test]
        public void VisualPrefabsHaveIdentityRootsAndNoPhysicsOrScripts()
        {
            foreach (string location in ExpeditionVisualAssets.Locations)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(location));
                Assert.That(prefab, Is.Not.Null, location);
                Assert.That(prefab.transform.localPosition, Is.EqualTo(Vector3.zero), location);
                Assert.That(prefab.transform.localRotation, Is.EqualTo(Quaternion.identity), location);
                Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one), location);
                Assert.That(prefab.GetComponentsInChildren<Collider>(true), Is.Empty, location);
                Assert.That(prefab.GetComponentsInChildren<Rigidbody>(true), Is.Empty, location);
                Assert.That(prefab.GetComponentsInChildren<MonoBehaviour>(true), Is.Empty, location);
                if (location == "SC_Expedition") continue;
                MeshRenderer[] renderers = prefab.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(renderers, Is.Not.Empty, location);
                foreach (MeshRenderer renderer in renderers)
                {
                    string materialPath = AssetDatabase.GetAssetPath(renderer.sharedMaterial);
                    Assert.That(materialPath, Does.StartWith(Root + "/Art/Prototype/Materials/"), location);
                    Assert.That(AssetDatabase.LoadAssetAtPath<Material>(materialPath), Is.SameAs(renderer.sharedMaterial), location);
                }
            }
            Assert.That(AssetDatabase.LoadAssetAtPath<GameObject>(PathFor("SC_Expedition")).GetComponentInChildren<Camera>(true), Is.Not.Null);
            foreach (string location in new[] { "PF_TestPlayer", "PF_TestEnemy", "PF_TestBoss", "PF_TestNpc" })
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PathFor(location));
                MeshFilter mesh = prefab.GetComponentInChildren<MeshFilter>(true);
                Assert.That(mesh.sharedMesh.name, Is.EqualTo("Capsule"), location);
                Animator animator = prefab.GetComponent<Animator>();
                Assert.That(animator, Is.Not.Null, location);
                Assert.That(animator.applyRootMotion, Is.False, location);
                Assert.That(AssetDatabase.GetAssetPath(animator.runtimeAnimatorController),
                    Is.EqualTo(Root + "/Art/Prototype/Animations/Controllers/AC_TestCharacter.controller"), location);
                Assert.That(animator.runtimeAnimatorController.animationClips.Length, Is.EqualTo(5));
                foreach (AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                {
                    Assert.That(clip.events, Is.Empty, clip.name);
                    Assert.That(AssetDatabase.GetAssetPath(clip), Does.StartWith(Root + "/Art/Prototype/Animations/Clips/"));
                    Assert.That(AnimationUtility.GetCurveBindings(clip).All(binding => binding.path == "Mesh"), Is.True);
                }
            }
        }

        /// <summary>
        /// 房间地板和障碍网格符合地图运行时尺寸坐标约定。
        /// </summary>
        [Test]
        public void RoomFloorAndObstacleMatchLayoutDimensions()
        {
            GameObject room = AssetDatabase.LoadAssetAtPath<GameObject>(PathFor("RM_Prototype"));
            Transform floor = room.transform.Find("Floor");
            Assert.That(floor, Is.Not.Null);
            float span = PrototypeLevelConfig.RoomHalfExtent * 2f;
            Assert.That(floor.localPosition, Is.EqualTo(new Vector3(0f, -0.15f, 0f)));
            Assert.That(floor.localScale, Is.EqualTo(new Vector3(span, 0.3f, span)));
            GameObject obstacle = AssetDatabase.LoadAssetAtPath<GameObject>(PathFor("PF_TestObstacle"));
            Transform mesh = obstacle.transform.Find("Mesh");
            Assert.That(mesh, Is.Not.Null);
            Assert.That(mesh.localPosition, Is.EqualTo(Vector3.zero));
            Assert.That(mesh.localScale, Is.EqualTo(Vector3.one));
            Assert.That(mesh.GetComponent<MeshFilter>()?.sharedMesh.name, Is.EqualTo("Cube"));
        }

        /// <summary>
        /// 将运行时地址映射到约定热更预制体路径。
        /// </summary>
        /// <param name="location">唯一资源地址。</param>
        /// <returns>Assets 下的预制体路径。</returns>
        private static string PathFor(string location) => Root + "/" + Paths[location] + "/" + location + ".prefab";
    }
}