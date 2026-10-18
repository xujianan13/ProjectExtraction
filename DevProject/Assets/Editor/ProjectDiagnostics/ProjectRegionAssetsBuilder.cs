using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GameLogic.Gameplay.Level;
using GameLogic.Tables;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 一套完整地图和几何资源的确定构建清单。
    /// </summary>
    public sealed class RegionAssetBuildEntry
    {
        public int MapId;
        public string Version;
        public string Name;
        public string PrefabAddress;
        public string GeometryAddress;
    }

    /// <summary>
    /// 用 Unity 原生资产接口制作 V3 长条房间资源及同源几何。
    /// </summary>
    public static class ProjectRegionAssetsBuilder
    {
        public const string MapsFolder = "Assets/GameResources/Scenes/Expedition/Regions";
        public const string MaterialsFolder = "Assets/GameResources/Art/Prototype/Materials";
        private static RegionAssetBuildEntry[] entries => GetBuildManifest();

        /// <summary>
        /// 公开供配置层引用的地址及版本，不执行资产写入。
        /// </summary>
        /// <returns>不会共享可写清单成员的副本。</returns>
        public static RegionAssetBuildEntry[] GetBuildManifest()
        {
            if (!TryReadRows("CfgRoomMaps", out CfgRoomMapsRow[] maps)) return Array.Empty<RegionAssetBuildEntry>();
            return Array.ConvertAll(maps, value => new RegionAssetBuildEntry
            {
                MapId = value.ID, Version = value.Version, Name = value.Name, PrefabAddress = value.PrefabAddress,
                GeometryAddress = value.GeometryAddress
            });
        }

        /// <summary>
        /// 显式生成或更新配置的长条房间，失败保持对应资源未发布。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Build Room Maps")]
        public static void CreateOrUpdate()
        {
            bool success = BuildAll();
            if (success) Debug.Log("V3 房间地图和几何已同源生成。");
        }

        /// <summary>
        /// 生成全部资源供工程准备入口调用。
        /// </summary>
        /// <returns>房间地图均校验且生成成功时为真。</returns>
        public static bool BuildAll()
        {
            if (!ValidateDefinitions(out string error)) { Debug.LogError("V3 地图几何校验失败：" + error); return false; }
            EnsureFolder(MapsFolder);
            EnsureFolder(MaterialsFolder);
            Material floor = Material("MAT_RegionFloor", new Color(0.27f, 0.35f, 0.31f));
            Material walls = Material("MAT_RegionRuins", new Color(0.42f, 0.39f, 0.32f));
            Material arena = Material("MAT_RegionArena", new Color(0.37f, 0.31f, 0.41f));
            if (floor == null || walls == null || arena == null) return false;
            foreach (RegionAssetBuildEntry entry in entries)
            {
                RegionMapDefinition map = CreateDefinition(entry.MapId);
                GameObject root = new GameObject(entry.PrefabAddress);
                var identity = new GameObject("RegionMap_" + map.MapId + "_" + map.Version);
                identity.transform.SetParent(root.transform, false);
                MeshPart(root.transform, "Floor", new Vector3(map.Bounds.center.x, -0.15f, map.Bounds.center.y),
                    new Vector3(map.Bounds.width, 0.3f, map.Bounds.height), entry.MapId == 3 ? arena : floor);
                for (int index = 0; index < map.Obstacles.Count; index++)
                {
                    RegionMapObstacle obstacle = map.Obstacles[index];
                    Vector2 size = obstacle.Max - obstacle.Min, center = (obstacle.Min + obstacle.Max) * 0.5f;
                    MeshPart(root.transform, "Obstacle_" + index, new Vector3(center.x, 1.5f, center.y), new Vector3(size.x, 3f, size.y), walls);
                }
                Transform anchors = new GameObject("Anchors").transform;
                anchors.SetParent(root.transform, false);
                foreach (RegionMapAnchor anchor in map.Anchors)
                {
                    Transform marker = new GameObject(anchor.Id).transform;
                    marker.SetParent(anchors, false);
                    marker.localPosition = anchor.Position;
                }
                if (!ValidatePrefabGeometry(root, map, out error))
                {
                    UnityEngine.Object.DestroyImmediate(root);
                    Debug.LogError("V3 地图表现与几何不一致：" + error);
                    return false;
                }
                string prefabPath = MapsFolder + "/" + entry.PrefabAddress + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                UnityEngine.Object.DestroyImmediate(root);
                if (!saved) { Debug.LogError("V3 地图保存失败：" + prefabPath); return false; }
                File.WriteAllText(MapsFolder + "/" + entry.GeometryAddress + ".json", JsonUtility.ToJson(map, true));
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// 在不写入资产的情况下检查房间地图所有关键锚点通行。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>描述完整且所有锚点可达时为真。</returns>
        public static bool ValidateDefinitions(out string error)
        {
            error = null;
            RegionAssetBuildEntry[] manifest = GetBuildManifest();
            if (manifest.Length == 0 || !TryReadRows("CfgRooms", out CfgRoomsRow[] rooms) ||
                !TryReadRows("CfgPlayerCombat", out CfgPlayerCombatRow[] players) || players.Length != 1)
            { error = "房间地图、预算或玩家配置缺失。"; return false; }
            foreach (RegionAssetBuildEntry entry in manifest)
            {
                RegionMapDefinition map = CreateDefinition(entry.MapId);
                if (map == null || !map.TryValidate(out error)) return false;
                var targets = new List<Vector3>();
                foreach (RegionMapAnchor anchor in map.Anchors) targets.Add(anchor.Position);
                foreach (CfgRoomsRow room in rooms)
                    if (room.MapID == map.MapId && !new RegionMapGeometry(map).TryValidateReachability(targets, (float)players[0].Radius,
                        (float)room.ReachabilityGridSpacing, room.ReachabilityMaxCells, out error)) return false;
            }
            return true;
        }

        /// <summary>
        /// 构建指定地图的唯一几何来源，视觉读取此描述。
        /// </summary>
        /// <param name="mapId">清单地图编号。</param>
        /// <returns>地图描述，编号不存在时为空。</returns>
        public static RegionMapDefinition CreateDefinition(int mapId)
        {
            if (!TryReadRows("CfgRoomMaps", out CfgRoomMapsRow[] maps) ||
                !TryReadRows("CfgRoomAnchors", out CfgRoomAnchorsRow[] anchors) ||
                !TryReadRows("CfgRoomObstacles", out CfgRoomObstaclesRow[] obstacles)) return null;
            CfgRoomMapsRow row = Array.Find(maps, value => value.ID == mapId);
            if (LevelContentConfig.TryCreateRoomMap(row, anchors, obstacles, out RegionMapDefinition map, out string error)) return map;
            Debug.LogError("房间地图配置无效：" + error);
            return null;
        }

        /// <summary>
        /// 校验构建中的视觉矩形与几何字段逐项一致。
        /// </summary>
        /// <param name="root">尚未保存的完整地图。</param>
        /// <param name="map">几何来源。</param>
        /// <param name="error">失败原因。</param>
        /// <returns>仅有原生无物理组件且视觉尺寸一致时为真。</returns>
        private static bool ValidatePrefabGeometry(GameObject root, RegionMapDefinition map, out string error)
        {
            error = null;
            if (root.GetComponentsInChildren<Collider>(true).Length != 0 || root.GetComponentsInChildren<Rigidbody>(true).Length != 0)
            { error = "地图不能包含独立物理组件。"; return false; }
            for (int index = 0; index < map.Obstacles.Count; index++)
            {
                RegionMapObstacle obstacle = map.Obstacles[index];
                Transform visual = root.transform.Find("Obstacle_" + index);
                Vector2 size = obstacle.Max - obstacle.Min, center = (obstacle.Min + obstacle.Max) * 0.5f;
                if (visual == null || visual.localPosition.x != center.x || visual.localPosition.z != center.y ||
                    visual.localScale.x != size.x || visual.localScale.z != size.y)
                { error = "障碍表现与空间描述不同：" + index; return false; }
            }
            return true;
        }

        /// <summary>
        /// 取得或创建构建器专用材质。
        /// </summary>
        /// <param name="name">材质资源名。</param>
        /// <param name="color">基础颜色。</param>
        /// <returns>有效原生材质，Shader 缺失时为空。</returns>
        private static Material Material(string name, Color color)
        {
            string path = MaterialsFolder + "/" + name + ".mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            Shader shader = Shader.Find("Standard");
            if (shader == null) { Debug.LogError("区域材质缺少 Standard Shader。"); return null; }
            material = new Material(shader) { name = name, color = color };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        /// <summary>
        /// 创建无 Collider 的原生矩形网格。
        /// </summary>
        /// <param name="parent">地图根节点。</param>
        /// <param name="name">层级名称。</param>
        /// <param name="position">局部位置。</param>
        /// <param name="scale">几何尺寸。</param>
        /// <param name="material">原生共享材质。</param>
        private static void MeshPart(Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(PrimitiveType.Cube);
            UnityEngine.Object.DestroyImmediate(part.GetComponent<Collider>());
            part.name = name;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = position;
            part.transform.localScale = scale;
            part.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        /// <summary>
        /// 逐级创建原生资源目录。
        /// </summary>
        /// <param name="path">Assets 下的资源目录。</param>
        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string parent = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string next = parent + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(parent, parts[index]);
                parent = next;
            }
        }

        /// <summary>
        /// 读取导出的唯一配置源，避免编辑器烘焙修改运行时全局表集。
        /// </summary>
        /// <typeparam name="T">配置行类型。</typeparam>
        /// <param name="tableName">表名。</param>
        /// <param name="rows">读取的配置行。</param>
        /// <returns>导出文件及行数组存在时为真。</returns>
        private static bool TryReadRows<T>(string tableName, out T[] rows)
        {
            rows = null;
            string path = Path.Combine(Application.dataPath, "GameResources", "Config", "Tables", "Table_" + tableName + ".json");
            if (!File.Exists(path)) { Debug.LogError("房间资产所需导表文件不存在：" + path); return false; }
            Type tableType = typeof(GameTables).GetProperty(tableName, BindingFlags.Static | BindingFlags.Public)?.PropertyType;
            Type envelopeType = tableType?.GetNestedType("Envelope", BindingFlags.NonPublic);
            FieldInfo rowsField = envelopeType?.GetField("rows", BindingFlags.Instance | BindingFlags.Public);
            if (envelopeType == null || rowsField == null) { Debug.LogError("房间配置生成结构无效：" + tableName); return false; }
            object envelope = JsonUtility.FromJson(File.ReadAllText(path), envelopeType);
            rows = envelope == null ? null : rowsField.GetValue(envelope) as T[];
            if (rows == null) { Debug.LogError("房间配置缺少 rows：" + tableName); return false; }
            return true;
        }
    }
}
