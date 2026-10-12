using System;
using System.Collections.Generic;
using System.IO;
using GameLogic.Gameplay.Level;
using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 一套完整地图、几何和底图资源的确定构建清单。
    /// </summary>
    public sealed class RegionAssetBuildEntry
    {
        public int MapId;
        public string Version;
        public string Name;
        public string PrefabAddress;
        public string GeometryAddress;
        public string MinimapAddress;
    }

    /// <summary>
    /// 用 Unity 原生资产接口制作 v2 完整区域资源及同源地图底图。
    /// </summary>
    public static class ProjectRegionAssetsBuilder
    {
        public const string MapVersion = "v2-region-1";
        public const string MapsFolder = "Assets/GameResources/Scenes/Expedition/Regions";
        public const string MaterialsFolder = "Assets/GameResources/Art/Prototype/Materials";
        private static readonly RegionAssetBuildEntry[] entries =
        {
            Entry(1, "RG_AncientCrossroads", "古代交汇地"), Entry(2, "RG_ShatteredCourtyard", "破碎庭院"), Entry(3, "RG_FinalArena", "最终竞技场")
        };

        /// <summary>
        /// 公开供配置层引用的地址及版本，不执行资产写入。
        /// </summary>
        /// <returns>不会共享可写清单成员的副本。</returns>
        public static RegionAssetBuildEntry[] GetBuildManifest() => Array.ConvertAll(entries, value => new RegionAssetBuildEntry
        {
            MapId = value.MapId, Version = value.Version, Name = value.Name, PrefabAddress = value.PrefabAddress,
            GeometryAddress = value.GeometryAddress, MinimapAddress = value.MinimapAddress
        });

        /// <summary>
        /// 显式生成或更新三套完整地图，失败保持对应资源未发布。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Build Region Maps")]
        public static void CreateOrUpdate()
        {
            bool success = BuildAll();
            if (success) Debug.Log("v2 区域地图、几何和底图已同源生成。");
        }

        /// <summary>
        /// 生成全部资源供工程准备入口调用。
        /// </summary>
        /// <returns>三张地图均校验且生成成功时为真。</returns>
        public static bool BuildAll()
        {
            if (!ValidateDefinitions(out string error)) { Debug.LogError("v2 地图几何校验失败：" + error); return false; }
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
                    Debug.LogError("v2 地图表现与几何不一致：" + error);
                    return false;
                }
                string prefabPath = MapsFolder + "/" + entry.PrefabAddress + ".prefab";
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool saved);
                UnityEngine.Object.DestroyImmediate(root);
                if (!saved) { Debug.LogError("v2 地图保存失败：" + prefabPath); return false; }
                File.WriteAllText(MapsFolder + "/" + entry.GeometryAddress + ".json", JsonUtility.ToJson(map, true));
                WriteMinimap(entry, map);
            }
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            foreach (RegionAssetBuildEntry entry in entries) ConfigureMinimap(entry);
            return true;
        }

        /// <summary>
        /// 在不写入资产的情况下检查三张地图所有关键锚点通行。
        /// </summary>
        /// <param name="error">失败原因。</param>
        /// <returns>描述完整且所有锚点可达时为真。</returns>
        public static bool ValidateDefinitions(out string error)
        {
            error = null;
            foreach (RegionAssetBuildEntry entry in entries)
            {
                RegionMapDefinition map = CreateDefinition(entry.MapId);
                if (!map.TryValidate(out error)) return false;
                var targets = new List<Vector3>();
                var geometry = new RegionMapGeometry(map);
                foreach (RegionMapAnchor anchor in map.Anchors)
                {
                    targets.Add(anchor.Position);
                    if ((anchor.Kind == RegionAnchorKind.Monster || anchor.Kind == RegionAnchorKind.Elite) &&
                        (geometry.IsBlocked(anchor.Position, 3f) || geometry.IsSpawnForbidden(anchor.Position, 3f)))
                    { error = entry.PrefabAddress + " 候选点无法容纳小范围怪群：" + anchor.Id; return false; }
                }
                if (!geometry.TryValidateReachability(targets, 1f, 1f, 20000, out error)) return false;
            }
            return true;
        }

        /// <summary>
        /// 构建指定地图的唯一几何来源，视觉和底图均读取此描述。
        /// </summary>
        /// <param name="mapId">清单地图编号。</param>
        /// <returns>地图描述，编号不存在时为空。</returns>
        public static RegionMapDefinition CreateDefinition(int mapId)
        {
            if (mapId < 1 || mapId > 3) return null;
            var map = new RegionMapDefinition
            {
                MapId = mapId, Version = MapVersion, Bounds = mapId == 3 ? new Rect(-24, -28, 48, 56) : new Rect(-50, -60, 100, 120)
            };
            if (mapId == 3)
            {
                AddObstacle(map, -20, -12, -15, 12);
                AddObstacle(map, 15, -12, 20, 12);
                AddAnchor(map, "entry", RegionAnchorKind.Entry, "", 0, -20);
                AddAnchor(map, "boss", RegionAnchorKind.Boss, "", 0, 12);
                AddAnchor(map, "exit", RegionAnchorKind.Exit, "", 0, 22);
                AddNoSpawn(map, 0, -20, 6);
                AddNoSpawn(map, 0, 22, 6);
                return map;
            }
            if (mapId == 1)
            {
                AddObstacle(map, -5, -34, 5, -12);
                AddObstacle(map, -5, -4, 5, 24);
                AddObstacle(map, -35, -22, -31, -6);
                AddObstacle(map, 31, -22, 35, -6);
                AddObstacle(map, -50, 34, -9, 37);
                AddObstacle(map, 9, 34, 50, 37);
            }
            else
            {
                AddObstacle(map, -7, -28, 7, -16);
                AddObstacle(map, -7, 4, 7, 18);
                AddObstacle(map, -39, -35, -30, -22);
                AddObstacle(map, 30, -35, 39, -22);
                AddObstacle(map, -40, -4, -34, 16);
                AddObstacle(map, 34, -4, 40, 16);
                AddObstacle(map, -50, 34, -10, 37);
                AddObstacle(map, 10, 34, 50, 37);
            }
            AddAnchor(map, "entry", RegionAnchorKind.Entry, "", 0, -52);
            AddAnchor(map, "boss", RegionAnchorKind.Boss, "", 0, 46);
            AddAnchor(map, "exit", RegionAnchorKind.Exit, "", 0, 54);
            float[] monsterZ = { -38, -20, 0, 22 };
            for (int index = 0; index < monsterZ.Length; index++)
            {
                AddAnchor(map, "monster-" + (index * 2 + 1), RegionAnchorKind.Monster, "monster", -22, monsterZ[index], 5);
                AddAnchor(map, "monster-" + (index * 2 + 2), RegionAnchorKind.Monster, "monster", 22, monsterZ[index], 5);
            }
            if (mapId == 1)
            {
                AddAnchor(map, "elite-1", RegionAnchorKind.Elite, "elite", -40, -27);
                AddAnchor(map, "elite-2", RegionAnchorKind.Elite, "elite", 40, -27);
                AddAnchor(map, "elite-3", RegionAnchorKind.Elite, "elite", -38, 16);
                AddAnchor(map, "elite-4", RegionAnchorKind.Elite, "elite", 38, 16);
                AddAnchor(map, "chest-1", RegionAnchorKind.Chest, "chest", -40, -44);
                AddAnchor(map, "chest-2", RegionAnchorKind.Chest, "chest", 40, -44);
                AddAnchor(map, "chest-3", RegionAnchorKind.Chest, "chest", -36, 28);
                AddAnchor(map, "chest-4", RegionAnchorKind.Chest, "chest", 36, 28);
                AddAnchor(map, "npc-1", RegionAnchorKind.Npc, "npc", -12, -46);
                AddAnchor(map, "npc-2", RegionAnchorKind.Npc, "npc", 12, 29);
                AddNoSpawn(map, -12, -46, 6);
                AddNoSpawn(map, 12, 29, 6);
            }
            else
            {
                AddAnchor(map, "elite-1", RegionAnchorKind.Elite, "elite", -39, -46);
                AddAnchor(map, "elite-2", RegionAnchorKind.Elite, "elite", 39, -46);
                AddAnchor(map, "elite-3", RegionAnchorKind.Elite, "elite", -36, 26);
                AddAnchor(map, "elite-4", RegionAnchorKind.Elite, "elite", 36, 26);
                AddAnchor(map, "chest-1", RegionAnchorKind.Chest, "chest", -43, -12);
                AddAnchor(map, "chest-2", RegionAnchorKind.Chest, "chest", 43, -12);
                AddAnchor(map, "chest-3", RegionAnchorKind.Chest, "chest", -12, 29);
                AddAnchor(map, "chest-4", RegionAnchorKind.Chest, "chest", 12, 29);
                AddAnchor(map, "npc-1", RegionAnchorKind.Npc, "npc", -12, -42);
                AddAnchor(map, "npc-2", RegionAnchorKind.Npc, "npc", 12, 25);
                AddNoSpawn(map, -12, -42, 6);
                AddNoSpawn(map, 12, 25, 6);
            }
            AddNoSpawn(map, 0, -52, 6);
            AddNoSpawn(map, 0, 31, 5);
            AddNoSpawn(map, 0, 54, 6);
            return map;
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
        /// 输出与世界边界保持相同比例及北向的底图。
        /// </summary>
        /// <param name="entry">构建地址。</param>
        /// <param name="map">几何来源。</param>
        private static void WriteMinimap(RegionAssetBuildEntry entry, RegionMapDefinition map)
        {
            const int pixelsPerUnit = 4;
            int width = Mathf.RoundToInt(map.Bounds.width * pixelsPerUnit), height = Mathf.RoundToInt(map.Bounds.height * pixelsPerUnit);
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            var colors = new Color32[width * height];
            var geometry = new RegionMapGeometry(map);
            Color32 ground = new Color32(68, 90, 81, 255), wall = new Color32(142, 129, 102, 255), border = new Color32(30, 39, 37, 255);
            for (int z = 0; z < height; z++)
                for (int x = 0; x < width; x++)
                {
                    Vector3 point = new Vector3(map.Bounds.xMin + (x + 0.5f) * map.Bounds.width / width, 0,
                        map.Bounds.yMin + (z + 0.5f) * map.Bounds.height / height);
                    colors[z * width + x] = x == 0 || z == 0 || x == width - 1 || z == height - 1 ? border :
                        geometry.IsBlocked(point, 0f) ? wall : ground;
                }
            texture.SetPixels32(colors);
            texture.Apply();
            File.WriteAllBytes(MapsFolder + "/" + entry.MinimapAddress + ".png", texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
        }

        /// <summary>
        /// 设置底图为无损夹边纹理，供 UI RawImage 直接使用。
        /// </summary>
        /// <param name="entry">构建地址。</param>
        private static void ConfigureMinimap(RegionAssetBuildEntry entry)
        {
            string path = MapsFolder + "/" + entry.MinimapAddress + ".png";
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) { Debug.LogError("地图底图导入失败：" + path); return; }
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
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
        /// 创建固定地址清单。
        /// </summary>
        /// <param name="mapId">地图编号。</param>
        /// <param name="address">地图 Prefab 地址。</param>
        /// <param name="name">玩家可见名称。</param>
        /// <returns>确定的资源清单条目。</returns>
        private static RegionAssetBuildEntry Entry(int mapId, string address, string name) => new RegionAssetBuildEntry
        {
            MapId = mapId, Version = MapVersion, Name = name, PrefabAddress = address,
            GeometryAddress = "Geometry_" + address, MinimapAddress = "Minimap_" + address
        };

        /// <summary>
        /// 写入一块几何矩形。
        /// </summary>
        /// <param name="map">地图描述。</param>
        /// <param name="minX">最小水平坐标。</param>
        /// <param name="minZ">最小纵向坐标。</param>
        /// <param name="maxX">最大水平坐标。</param>
        /// <param name="maxZ">最大纵向坐标。</param>
        private static void AddObstacle(RegionMapDefinition map, float minX, float minZ, float maxX, float maxZ) =>
            map.Obstacles.Add(new RegionMapObstacle { Min = new Vector2(minX, minZ), Max = new Vector2(maxX, maxZ) });

        /// <summary>
        /// 写入一个空间候选锚点。
        /// </summary>
        /// <param name="map">地图描述。</param>
        /// <param name="id">稳定锚点身份。</param>
        /// <param name="kind">用途。</param>
        /// <param name="tag">内容匹配标签。</param>
        /// <param name="x">水平坐标。</param>
        /// <param name="z">纵向坐标。</param>
        /// <param name="capacity">可容纳成员数量。</param>
        private static void AddAnchor(RegionMapDefinition map, string id, RegionAnchorKind kind, string tag, float x, float z, int capacity = 1) =>
            map.Anchors.Add(new RegionMapAnchor { Id = id, Kind = kind, Tag = tag, Position = new Vector3(x, 0, z), Capacity = capacity });

        /// <summary>
        /// 写入入口和交互附近的禁止出生范围。
        /// </summary>
        /// <param name="map">地图描述。</param>
        /// <param name="x">中心水平坐标。</param>
        /// <param name="z">中心纵向坐标。</param>
        /// <param name="radius">禁止出生半径。</param>
        private static void AddNoSpawn(RegionMapDefinition map, float x, float z, float radius) =>
            map.NoSpawnZones.Add(new RegionNoSpawnZone { Center = new Vector3(x, 0, z), Radius = radius });
    }
}
