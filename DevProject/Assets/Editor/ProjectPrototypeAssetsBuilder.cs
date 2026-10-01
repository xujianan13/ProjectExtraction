using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 以编辑器原生资产接口补齐可热更的关卡测试预制体与材质。
    /// </summary>
    public static class ProjectPrototypeAssetsBuilder
    {
        private const string Scenes = "Assets/GameResources/Scenes/Expedition";
        private const string Gameplay = "Assets/GameResources/Gameplay/Expedition";
        private const string Materials = "Assets/GameResources/Art/Prototype/Materials";
        private static readonly List<string> Created = new List<string>();
        private static readonly List<string> Skipped = new List<string>();
        private static readonly List<string> Failed = new List<string>();

        /// <summary>
        /// 创建尚不存在的测试材质与预制体，并在控制台列出生成结果。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Create Test Prefabs")]
        public static void CreateMissing()
        {
            Created.Clear();
            Skipped.Clear();
            Failed.Clear();
            if (!TryGetRoomHalfExtent(out float roomHalfExtent)) return;

            EnsureFolder(Scenes + "/Rooms");
            EnsureFolder(Gameplay + "/Environment");
            EnsureFolder(Gameplay + "/Characters");
            EnsureFolder(Gameplay + "/Interactables");
            EnsureFolder(Gameplay + "/Markers");
            EnsureFolder(Gameplay + "/Loot");
            EnsureFolder(Gameplay + "/Effects");
            EnsureFolder(Materials);

            Material floor = EnsureMaterial("MAT_TestFloor", new Color(0.36f, 0.4f, 0.44f));
            Material wall = EnsureMaterial("MAT_TestWall", new Color(0.35f, 0.39f, 0.48f));
            Material obstacle = EnsureMaterial("MAT_TestObstacle", new Color(0.48f, 0.43f, 0.34f));
            Material player = EnsureMaterial("MAT_TestPlayer", new Color(0.1f, 0.85f, 0.82f));
            Material enemy = EnsureMaterial("MAT_TestEnemy", new Color(0.9f, 0.2f, 0.24f));
            Material boss = EnsureMaterial("MAT_TestBoss", new Color(0.62f, 0.25f, 0.8f));
            Material npc = EnsureMaterial("MAT_TestNpc", new Color(0.18f, 0.44f, 0.92f));
            Material chest = EnsureMaterial("MAT_TestChest", new Color(0.92f, 0.7f, 0.18f));
            Material exit = EnsureMaterial("MAT_TestExit", new Color(0.2f, 0.48f, 0.95f));
            Material extraction = EnsureMaterial("MAT_TestExtraction", new Color(0.14f, 0.82f, 0.46f));
            Material loot = EnsureMaterial("MAT_TestLoot", Color.white);
            Material projectile = EnsureMaterial("MAT_TestProjectile", new Color(1f, 0.48f, 0.1f));
            Material meleeCue = EnsureMaterial("MAT_TestMeleeCue", Color.white);

            EnsureScenePrefab();
            EnsureRoomPrefab(roomHalfExtent, floor);
            EnsurePrimitivePrefab(Gameplay + "/Environment/PF_TestWall.prefab", PrimitiveType.Cube, wall);
            EnsurePrimitivePrefab(Gameplay + "/Environment/PF_TestObstacle.prefab", PrimitiveType.Cube, obstacle);
            EnsurePrimitivePrefab(Gameplay + "/Characters/PF_TestPlayer.prefab", PrimitiveType.Capsule, player);
            EnsurePrimitivePrefab(Gameplay + "/Characters/PF_TestEnemy.prefab", PrimitiveType.Capsule, enemy);
            EnsurePrimitivePrefab(Gameplay + "/Characters/PF_TestBoss.prefab", PrimitiveType.Capsule, boss);
            EnsurePrimitivePrefab(Gameplay + "/Characters/PF_TestNpc.prefab", PrimitiveType.Capsule, npc);
            EnsurePrimitivePrefab(Gameplay + "/Interactables/PF_TestChest.prefab", PrimitiveType.Cube, chest);
            EnsurePrimitivePrefab(Gameplay + "/Markers/PF_TestExit.prefab", PrimitiveType.Cube, exit);
            EnsurePrimitivePrefab(Gameplay + "/Markers/PF_TestExtraction.prefab", PrimitiveType.Cylinder, extraction);
            EnsurePrimitivePrefab(Gameplay + "/Loot/PF_TestLoot.prefab", PrimitiveType.Cube, loot);
            EnsurePrimitivePrefab(Gameplay + "/Effects/PF_TestProjectile.prefab", PrimitiveType.Sphere, projectile);
            EnsurePrimitivePrefab(Gameplay + "/Effects/PF_TestMeleeCue.prefab", PrimitiveType.Cube, meleeCue);

            if (!ProjectCharacterAnimationBuilder.EnsureMissing()) Failed.Add("角色动画资源");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            string report = $"新增 {Created.Count}，已存在 {Skipped.Count}，失败 {Failed.Count}。\n新增：{string.Join(", ", Created)}\n已存在：{string.Join(", ", Skipped)}\n失败：{string.Join(", ", Failed)}";
            if (Failed.Count > 0) Debug.LogError("测试资源生成未完成：" + report);
            else Debug.Log("测试资源生成完成：" + report);
        }

        /// <summary>
        /// 从热更程序集的编译后常量读取原型房间半边长。
        /// </summary>
        /// <param name="halfExtent">读取成功后的房间半边长。</param>
        /// <returns>常量存在且有效时为真。</returns>
        private static bool TryGetRoomHalfExtent(out float halfExtent)
        {
            Type config = Type.GetType("GameLogic.Gameplay.Level.PrototypeLevelConfig, GameLogic");
            FieldInfo field = config?.GetField("RoomHalfExtent", BindingFlags.Public | BindingFlags.Static);
            if (field != null && field.IsLiteral && field.GetRawConstantValue() is float value && value > 0f)
            {
                halfExtent = value;
                return true;
            }
            halfExtent = 0f;
            Debug.LogError("测试资源生成失败：无法读取 GameLogic.Gameplay.Level.PrototypeLevelConfig.RoomHalfExtent，请先完成 GameLogic 编译。");
            return false;
        }

        /// <summary>
        /// 逐级创建 Unity 资源目录。
        /// </summary>
        /// <param name="path">Assets 下的目标目录。</param>
        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string parent = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string current = parent + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(current)) AssetDatabase.CreateFolder(parent, parts[index]);
                parent = current;
            }
        }

        /// <summary>
        /// 补齐 Standard 材质，已有资产保持原状。
        /// </summary>
        /// <param name="name">材质资产名。</param>
        /// <param name="color">新材质的基础颜色。</param>
        /// <returns>现有或新建材质，失败时为空。</returns>
        private static Material EnsureMaterial(string name, Color color)
        {
            string path = Materials + "/" + name + ".mat";
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing != null)
            {
                if (existing is Material material) { Skipped.Add(path); return material; }
                Failed.Add(path + " (不是材质)");
                return null;
            }
            Shader shader = Shader.Find("Standard");
            if (shader == null) { Failed.Add(path + " (缺少 Standard Shader)"); return null; }
            Material created = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(created, path);
            Created.Add(path);
            return created;
        }

        /// <summary>
        /// 创建只包含俯视相机和方向光的场景预制体。
        /// </summary>
        private static void EnsureScenePrefab()
        {
            string path = Scenes + "/SC_Expedition.prefab";
            if (SkipExisting(path)) return;
            GameObject root = new GameObject("SC_Expedition");
            GameObject cameraObject = new GameObject("ExpeditionCamera");
            cameraObject.transform.SetParent(root.transform, false);
            cameraObject.transform.localPosition = new Vector3(0f, 18f, -11.25f);
            cameraObject.transform.localRotation = Quaternion.Euler(58f, 0f, 0f);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.depth = 10f;
            camera.orthographic = true;
            camera.orthographicSize = 11f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.055f, 0.12f);

            GameObject lightObject = new GameObject("DirectionalLight");
            lightObject.transform.SetParent(root.transform, false);
            lightObject.transform.localRotation = Quaternion.Euler(55f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            SavePrefab(root, path);
        }

        /// <summary>
        /// 创建地板顶面位于零高度的房间预制体。
        /// </summary>
        /// <param name="halfExtent">运行时房间半边长。</param>
        /// <param name="floorMaterial">地板材质。</param>
        private static void EnsureRoomPrefab(float halfExtent, Material floorMaterial)
        {
            string path = Scenes + "/Rooms/RM_Prototype.prefab";
            if (SkipExisting(path) || floorMaterial == null) return;
            GameObject root = new GameObject("RM_Prototype");
            GameObject floor = CreateMesh("Floor", PrimitiveType.Cube, floorMaterial, root.transform);
            floor.transform.localPosition = new Vector3(0f, -0.15f, 0f);
            floor.transform.localScale = new Vector3(halfExtent * 2f, 0.3f, halfExtent * 2f);
            SavePrefab(root, path);
        }

        /// <summary>
        /// 用单位尺寸的基础网格创建视觉预制体。
        /// </summary>
        /// <param name="path">目标预制体路径。</param>
        /// <param name="primitive">基础网格类型。</param>
        /// <param name="material">持久化材质。</param>
        private static void EnsurePrimitivePrefab(string path, PrimitiveType primitive, Material material)
        {
            if (SkipExisting(path) || material == null) return;
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            GameObject root = new GameObject(name);
            CreateMesh("Mesh", primitive, material, root.transform);
            SavePrefab(root, path);
        }

        /// <summary>
        /// 创建无物理组件的基础网格子物件。
        /// </summary>
        /// <param name="name">子物件名称。</param>
        /// <param name="primitive">基础网格类型。</param>
        /// <param name="material">持久化材质。</param>
        /// <param name="parent">预制体根节点。</param>
        /// <returns>新建的网格子物件。</returns>
        private static GameObject CreateMesh(string name, PrimitiveType primitive, Material material, Transform parent)
        {
            GameObject mesh = GameObject.CreatePrimitive(primitive);
            mesh.name = name;
            Collider collider = mesh.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            mesh.transform.SetParent(parent, false);
            mesh.GetComponent<MeshRenderer>().sharedMaterial = material;
            return mesh;
        }

        /// <summary>
        /// 保留已经存在的资产而不作覆盖。
        /// </summary>
        /// <param name="path">待创建资产路径。</param>
        /// <returns>路径已有资产、需要跳过创建时为真。</returns>
        private static bool SkipExisting(string path)
        {
            UnityEngine.Object existing = AssetDatabase.LoadMainAssetAtPath(path);
            if (existing == null) return false;
            if (existing is GameObject prefab && PrefabUtility.IsPartOfPrefabAsset(prefab)) Skipped.Add(path);
            else Failed.Add(path + " (已有资产不是 Prefab GameObject)");
            return true;
        }

        /// <summary>
        /// 保存新建预制体并清理临时场景对象。
        /// </summary>
        /// <param name="root">待保存的预制体根节点。</param>
        /// <param name="path">目标预制体路径。</param>
        private static void SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            UnityEngine.Object.DestroyImmediate(root);
            if (saved) Created.Add(path);
            else Failed.Add(path + " (保存失败)");
        }
    }
}
