using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace MiniGame.Editor
{
    /// <summary>
    /// 补齐战斗反馈使用的热更预制体、材质和短音效。
    /// </summary>
    public static class ProjectCombatFeedbackAssetsBuilder
    {
        private const string Feedback = "Assets/GameResources/Gameplay/Expedition/Feedback";
        private const string Materials = "Assets/GameResources/Art/Prototype/Materials";
        private const string Audio = "Assets/GameResources/Audio/Combat";
        private const int SampleRate = 22050;

        /// <summary>
        /// 从菜单补齐战斗反馈资源。
        /// </summary>
        [MenuItem("Tools/ProjectExtraction/Create Combat Feedback")]
        public static void CreateMissing()
        {
            if (EnsureMissing()) Debug.Log("战斗反馈资源已就绪。");
        }

        /// <summary>
        /// 仅创建缺失的战斗反馈资源，保留已有资源及其 GUID。
        /// </summary>
        /// <returns>全部目标资源可用时为真。</returns>
        public static bool EnsureMissing()
        {
            if (!EnsureFolder(Feedback) || !EnsureFolder(Materials) || !EnsureFolder(Audio)) return false;
            if (!EnsureMaterial(Materials + "/MAT_CombatHit.mat", Color.white, out Material hitMaterial)) return false;
            if (!EnsureMaterial(Materials + "/MAT_CombatTelegraph.mat", Color.white, out Material telegraphMaterial)) return false;
            if (!EnsureHealthBar() || !EnsureDamageText() || !EnsureHit(hitMaterial) || !EnsureTelegraph(telegraphMaterial)) return false;
            if (!EnsureSound(Audio + "/SFX_CombatSwing.wav", 0) || !EnsureSound(Audio + "/SFX_CombatHit.wav", 1)
                || !EnsureSound(Audio + "/SFX_CombatCritical.wav", 2)) return false;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            return true;
        }

        /// <summary>
        /// 逐级创建资源目录。
        /// </summary>
        /// <param name="path">Assets 下的目录。</param>
        /// <returns>目录存在时为真。</returns>
        private static bool EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string parent = parts[0];
            for (int index = 1; index < parts.Length; index++)
            {
                string current = parent + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(current)) AssetDatabase.CreateFolder(parent, parts[index]);
                if (!AssetDatabase.IsValidFolder(current))
                {
                    Debug.LogError("战斗反馈资源生成失败：无法创建目录 " + current);
                    return false;
                }
                parent = current;
            }
            return true;
        }

        /// <summary>
        /// 创建支持顶点颜色与透明度的材质，已有材质保持原状。
        /// </summary>
        /// <param name="path">材质路径。</param>
        /// <param name="color">新材质颜色。</param>
        /// <param name="material">现有或新建材质。</param>
        /// <returns>材质可用时为真。</returns>
        private static bool EnsureMaterial(string path, Color color, out Material material)
        {
            material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return true;
            if (AssetExists(path)) return Fail("已有资产不是材质：" + path);
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return Fail("缺少 Sprites/Default Shader：" + path);
            material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path), color = color };
            AssetDatabase.CreateAsset(material, path);
            if (AssetDatabase.LoadAssetAtPath<Material>(path) != null) return true;
            return Fail("无法保存材质：" + path);
        }

        /// <summary>
        /// 创建世界空间血条，Fill 宽度由运行时控制。
        /// </summary>
        /// <returns>预制体可用时为真。</returns>
        private static bool EnsureHealthBar()
        {
            string path = Feedback + "/PF_CombatHealthBar.prefab";
            if (PrefabExists(path, out bool valid)) return valid;
            GameObject root = CreateWorldCanvas("PF_CombatHealthBar", new Vector2(160f, 20f), 0.008f);
            RectTransform background = CreateImage(root.transform, "Background", new Color(0.07f, 0.09f, 0.1f, 0.88f));
            Stretch(background, 0f);
            RectTransform fill = CreateImage(root.transform, "Fill", new Color(0.25f, 0.85f, 0.34f, 1f));
            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(0f, 1f);
            fill.pivot = new Vector2(0f, 0.5f);
            fill.anchoredPosition = new Vector2(2f, 0f);
            fill.sizeDelta = new Vector2(156f, -4f);
            return SavePrefab(root, path);
        }

        /// <summary>
        /// 创建世界空间伤害数字。
        /// </summary>
        /// <returns>预制体可用时为真。</returns>
        private static bool EnsureDamageText()
        {
            string path = Feedback + "/PF_CombatDamageText.prefab";
            if (PrefabExists(path, out bool valid)) return valid;
            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return Fail("缺少项目 UI 使用的 LegacyRuntime.ttf 字体。");
            GameObject root = CreateWorldCanvas("PF_CombatDamageText", new Vector2(160f, 64f), 0.009f);
            GameObject node = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text), typeof(UnityEngine.UI.Outline));
            node.transform.SetParent(root.transform, false);
            RectTransform rect = node.GetComponent<RectTransform>();
            Stretch(rect, 0f);
            UnityEngine.UI.Text label = node.GetComponent<UnityEngine.UI.Text>();
            label.font = font;
            label.text = "0";
            label.fontSize = 38;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = Color.white;
            label.horizontalOverflow = HorizontalWrapMode.Overflow;
            label.verticalOverflow = VerticalWrapMode.Overflow;
            label.raycastTarget = false;
            UnityEngine.UI.Outline outline = node.GetComponent<UnityEngine.UI.Outline>();
            outline.effectColor = Color.black;
            outline.effectDistance = new Vector2(1.5f, -1.5f);
            return SavePrefab(root, path);
        }

        /// <summary>
        /// 创建没有物理组件的受击闪光网格。
        /// </summary>
        /// <param name="material">支持运行时颜色与透明度的白色材质。</param>
        /// <returns>预制体可用时为真。</returns>
        private static bool EnsureHit(Material material)
        {
            string path = Feedback + "/PF_CombatHit.prefab";
            if (PrefabExists(path, out bool valid)) return valid;
            GameObject root = new GameObject("PF_CombatHit");
            GameObject flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Flash";
            flash.transform.SetParent(root.transform, false);
            flash.transform.localScale = Vector3.one * 0.28f;
            Collider collider = flash.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            MeshRenderer renderer = flash.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return SavePrefab(root, path);
        }

        /// <summary>
        /// 创建由运行时设置点位的世界空间预警线。
        /// </summary>
        /// <param name="material">支持运行时顶点颜色与透明度的白色材质。</param>
        /// <returns>预制体可用时为真。</returns>
        private static bool EnsureTelegraph(Material material)
        {
            string path = Feedback + "/PF_CombatTelegraph.prefab";
            if (PrefabExists(path, out bool valid)) return valid;
            GameObject root = new GameObject("PF_CombatTelegraph");
            LineRenderer line = root.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.useWorldSpace = true;
            line.positionCount = 0;
            line.widthMultiplier = 0.055f;
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            return SavePrefab(root, path);
        }

        /// <summary>
        /// 创建不接收点击的世界空间 Canvas。
        /// </summary>
        /// <param name="name">预制体根节点名。</param>
        /// <param name="size">Canvas 像素尺寸。</param>
        /// <param name="scale">世界空间缩放。</param>
        /// <returns>新建的 Canvas 根节点。</returns>
        private static GameObject CreateWorldCanvas(string name, Vector2 size, float scale)
        {
            GameObject root = new GameObject(name, typeof(RectTransform), typeof(Canvas));
            RectTransform rect = root.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.localScale = Vector3.one * scale;
            Canvas canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            return root;
        }

        /// <summary>
        /// 新建不接收点击的 Image 节点。
        /// </summary>
        /// <param name="parent">父节点。</param>
        /// <param name="name">子节点名。</param>
        /// <param name="color">图像颜色。</param>
        /// <returns>新建节点的 RectTransform。</returns>
        private static RectTransform CreateImage(Transform parent, string name, Color color)
        {
            GameObject node = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image));
            node.transform.SetParent(parent, false);
            UnityEngine.UI.Image image = node.GetComponent<UnityEngine.UI.Image>();
            image.color = color;
            image.raycastTarget = false;
            return node.GetComponent<RectTransform>();
        }

        /// <summary>
        /// 将 UI 节点拉伸到父节点并设置留白。
        /// </summary>
        /// <param name="rect">待布局节点。</param>
        /// <param name="padding">四周留白像素。</param>
        private static void Stretch(RectTransform rect, float padding)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(padding, padding);
            rect.offsetMax = new Vector2(-padding, -padding);
        }

        /// <summary>
        /// 将场景临时对象保存为预制体并销毁临时对象。
        /// </summary>
        /// <param name="root">预制体根节点。</param>
        /// <param name="path">目标路径。</param>
        /// <returns>预制体保存成功时为真。</returns>
        private static bool SavePrefab(GameObject root, string path)
        {
            PrefabUtility.SaveAsPrefabAsset(root, path, out bool saved);
            UnityEngine.Object.DestroyImmediate(root);
            return saved && AssetDatabase.LoadAssetAtPath<GameObject>(path) != null || Fail("无法保存预制体：" + path);
        }

        /// <summary>
        /// 检查目标路径是否已有有效预制体。
        /// </summary>
        /// <param name="path">目标路径。</param>
        /// <param name="valid">已有资产是否为预制体。</param>
        /// <returns>目标路径已有资产时为真。</returns>
        private static bool PrefabExists(string path, out bool valid)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab != null)
            {
                valid = PrefabUtility.IsPartOfPrefabAsset(prefab);
                if (!valid) Fail("已有资产不是预制体：" + path);
                return true;
            }
            valid = false;
            if (!AssetExists(path)) return false;
            Fail("已有资产不是预制体：" + path);
            return true;
        }

        /// <summary>
        /// 检查磁盘上是否已有资源，避免覆盖未被导入的手工文件。
        /// </summary>
        /// <param name="path">Unity 资源路径。</param>
        /// <returns>文件已存在时为真。</returns>
        private static bool AssetExists(string path)
        {
            return AssetDatabase.LoadMainAssetAtPath(path) != null || File.Exists(Path.GetFullPath(path));
        }

        /// <summary>
        /// 生成一个简短的单声道 PCM 音效，并配置非流式导入。
        /// </summary>
        /// <param name="path">音效目标路径。</param>
        /// <param name="kind">挥击、命中或暴击类型编号。</param>
        /// <returns>音效已存在或生成成功时为真。</returns>
        private static bool EnsureSound(string path, int kind)
        {
            AudioClip existing = AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            if (existing != null) return true;
            if (AssetExists(path)) return Fail("已有资产不是音效：" + path);
            byte[] bytes = BuildWave(kind);
            File.WriteAllBytes(Path.GetFullPath(path), bytes);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            AudioImporter importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return Fail("无法导入音效：" + path);
            importer.forceToMono = true;
            importer.loadInBackground = false;
            importer.defaultSampleSettings = new AudioImporterSampleSettings
            {
                loadType = AudioClipLoadType.DecompressOnLoad,
                preloadAudioData = true,
                compressionFormat = AudioCompressionFormat.PCM,
                sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate
            };
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path) != null || Fail("无法保存音效：" + path);
        }

        /// <summary>
        /// 用原创合成波形写入标准 16 位单声道 WAV 数据。
        /// </summary>
        /// <param name="kind">挥击、命中或暴击类型编号。</param>
        /// <returns>完整的 WAV 文件字节。</returns>
        private static byte[] BuildWave(int kind)
        {
            float duration = kind == 0 ? 0.16f : kind == 1 ? 0.12f : 0.22f;
            int count = Mathf.CeilToInt(duration * SampleRate);
            using (MemoryStream stream = new MemoryStream(44 + count * 2))
            using (BinaryWriter writer = new BinaryWriter(stream))
            {
                writer.Write(new[] { (byte)'R', (byte)'I', (byte)'F', (byte)'F' });
                writer.Write(36 + count * 2);
                writer.Write(new[] { (byte)'W', (byte)'A', (byte)'V', (byte)'E' });
                writer.Write(new[] { (byte)'f', (byte)'m', (byte)'t', (byte)' ' });
                writer.Write(16);
                writer.Write((short)1);
                writer.Write((short)1);
                writer.Write(SampleRate);
                writer.Write(SampleRate * 2);
                writer.Write((short)2);
                writer.Write((short)16);
                writer.Write(new[] { (byte)'d', (byte)'a', (byte)'t', (byte)'a' });
                writer.Write(count * 2);
                for (int index = 0; index < count; index++)
                {
                    float time = index / (float)SampleRate;
                    float envelope = Mathf.Min(1f, time * 350f) * Mathf.Pow(1f - time / duration, kind == 2 ? 2f : 3f);
                    float frequency = kind == 0 ? 660f - 390f * time / duration : kind == 1 ? 170f - 75f * time / duration : 430f - 220f * time / duration;
                    float fundamental = Mathf.Sin(2f * Mathf.PI * (frequency * time + 0.13f * time * time));
                    float overtone = Mathf.Sin(2f * Mathf.PI * (frequency * 1.79f * time + 0.07f * time * time));
                    float shimmer = kind == 2 ? 0.18f * Mathf.Sin(2f * Mathf.PI * 950f * time) : 0f;
                    float sample = Mathf.Clamp((fundamental * 0.72f + overtone * 0.18f + shimmer) * envelope * 0.26f, -1f, 1f);
                    writer.Write((short)Mathf.RoundToInt(sample * short.MaxValue));
                }
                return stream.ToArray();
            }
        }

        /// <summary>
        /// 记录资源生成错误。
        /// </summary>
        /// <param name="reason">失败原因。</param>
        /// <returns>固定返回假。</returns>
        private static bool Fail(string reason)
        {
            Debug.LogError("战斗反馈资源生成失败：" + reason);
            return false;
        }
    }
}
