using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using HybridCLR.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 将当前宿主裁剪后的 AOT 元数据打入固定 Resources 资产。
    /// </summary>
    public static class HostAotMetadataBuild
    {
        private const string AssetDirectory = "Assets/Resources/HotUpdateMetadata";
        private const string ManifestPath = AssetDirectory + "/Manifest.json";
        private static readonly Regex AssemblyBlock = new(@"PatchedAOTAssemblyList\s*=\s*new\s+List<string>\s*\{(?<body>.*?)\};", RegexOptions.Singleline);
        private static readonly Regex AssemblyName = new("\"(?<name>[^\"]+)\"");

        /// <summary>
        /// 从 HybridCLR 生成清单复制本平台当前宿主的裁剪 AOT 程序集。
        /// </summary>
        /// <param name="target">宿主构建目标平台。</param>
        public static void Prepare(BuildTarget target)
        {
            string[] names = ReadGeneratedAssemblyNames();
            string sourceDirectory = ResolveProjectPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(target));
            if (!Directory.Exists(sourceDirectory)) throw new BuildFailedException($"缺少 HybridCLR 裁剪 AOT 目录：{sourceDirectory}");
            if (!AssetDatabase.IsValidFolder(AssetDirectory)) AssetDatabase.CreateFolder("Assets/Resources", "HotUpdateMetadata");

            string[] hashes = new string[names.Length];
            for (int index = 0; index < names.Length; index++)
            {
                string source = Path.Combine(sourceDirectory, names[index]);
                if (!File.Exists(source)) throw new BuildFailedException($"缺少当前宿主的裁剪 AOT 程序集：{source}");
                byte[] bytes = File.ReadAllBytes(source);
                if (bytes.Length == 0) throw new BuildFailedException($"裁剪 AOT 程序集为空：{source}");
                hashes[index] = Hash(bytes);
                string assetPath = AssetDirectory + "/" + names[index] + ".bytes";
                File.WriteAllBytes(ResolveProjectPath(assetPath), bytes);
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
            }

            string[] current = names.Select(name => AssetDirectory + "/" + name + ".bytes").ToArray();
            foreach (string guid in AssetDatabase.FindAssets("", new[] { AssetDirectory }))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (assetPath.EndsWith(".dll.bytes", StringComparison.OrdinalIgnoreCase) && !AssetDatabase.IsValidFolder(assetPath) &&
                    !current.Contains(assetPath, StringComparer.Ordinal))
                    AssetDatabase.DeleteAsset(assetPath);
            }

            var manifest = new MetadataManifest { assemblies = names, hashes = hashes };
            File.WriteAllText(ResolveProjectPath(ManifestPath), JsonUtility.ToJson(manifest, true), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(ManifestPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"宿主 AOT 元数据已准备：{names.Length} 个程序集，目标 {target}。");
        }

        /// <summary>
        /// 校验宿主元数据资产与当前 HybridCLR 生成清单及裁剪程序集一致。
        /// </summary>
        /// <param name="target">宿主构建目标平台。</param>
        public static void Validate(BuildTarget target)
        {
            string path = ResolveProjectPath(ManifestPath);
            if (!File.Exists(path)) throw new BuildFailedException("缺少宿主 AOT 元数据清单，请重新执行 Prepare Windows Player。");
            MetadataManifest manifest = JsonUtility.FromJson<MetadataManifest>(File.ReadAllText(path, Encoding.UTF8));
            string[] expected = ReadGeneratedAssemblyNames();
            if (manifest?.assemblies == null || manifest.hashes == null || !manifest.assemblies.SequenceEqual(expected) ||
                manifest.hashes.Length != expected.Length)
                throw new BuildFailedException("宿主 AOT 元数据清单与 HybridCLR 当前生成结果不一致，请重新准备。");

            string sourceDirectory = ResolveProjectPath(SettingsUtil.GetAssembliesPostIl2CppStripDir(target));
            for (int index = 0; index < expected.Length; index++)
            {
                string asset = ResolveProjectPath(AssetDirectory + "/" + expected[index] + ".bytes");
                string source = Path.Combine(sourceDirectory, expected[index]);
                if (!File.Exists(asset) || !File.Exists(source) || !string.Equals(Hash(File.ReadAllBytes(asset)), manifest.hashes[index], StringComparison.Ordinal) ||
                    !string.Equals(Hash(File.ReadAllBytes(source)), manifest.hashes[index], StringComparison.Ordinal))
                    throw new BuildFailedException($"宿主 AOT 元数据缺失或与裁剪程序集不一致：{expected[index]}。请重新准备。");
            }
        }

        /// <summary>
        /// 从生成代码的指定字段读取需要补充元数据的 AOT 程序集名。
        /// </summary>
        /// <returns>按生成顺序排列的程序集文件名。</returns>
        private static string[] ReadGeneratedAssemblyNames()
        {
            string generated = Path.Combine(Application.dataPath, SettingsUtil.HybridCLRSettings.outputAOTGenericReferenceFile);
            if (!File.Exists(generated)) throw new BuildFailedException($"缺少 HybridCLR AOT 引用清单：{generated}");
            Match block = AssemblyBlock.Match(File.ReadAllText(generated, Encoding.UTF8));
            if (!block.Success) throw new BuildFailedException("HybridCLR AOT 引用清单缺少 PatchedAOTAssemblyList。");
            string body = block.Groups["body"].Value;
            MatchCollection matches = AssemblyName.Matches(body);
            string[] names = matches.Cast<Match>().Select(match => match.Groups["name"].Value).ToArray();
            if (!Regex.IsMatch(AssemblyName.Replace(body, string.Empty), @"^[\s,]*$") ||
                names.Any(name => !IsSafeAssemblyName(name)) || names.Distinct(StringComparer.Ordinal).Count() != names.Length)
                throw new BuildFailedException("HybridCLR AOT 引用清单包含无效或重复的程序集文件名。");
            return names;
        }

        /// <summary>
        /// 检查程序集名只包含单层安全文件名。
        /// </summary>
        /// <param name="name">清单中的程序集名。</param>
        /// <returns>是否可用于当前元数据目录。</returns>
        private static bool IsSafeAssemblyName(string name) => Regex.IsMatch(name, @"^[A-Za-z0-9_-][A-Za-z0-9_.-]*\.dll$") &&
            !name.Contains("..") && Path.GetFileName(name) == name;

        /// <summary>
        /// 将项目相对路径解析到 Unity 工程根目录。
        /// </summary>
        /// <param name="path">项目相对路径或绝对路径。</param>
        /// <returns>完整路径。</returns>
        private static string ResolveProjectPath(string path) => Path.IsPathRooted(path) ? path : Path.Combine(SettingsUtil.ProjectDir, path);

        /// <summary>
        /// 计算内容 SHA-256 校验值。
        /// </summary>
        /// <param name="bytes">待校验字节。</param>
        /// <returns>小写十六进制摘要。</returns>
        private static string Hash(byte[] bytes)
        {
            using var sha = SHA256.Create();
            return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }

        [Serializable]
        private sealed class MetadataManifest
        {
            public string[] assemblies;
            public string[] hashes;
        }
    }
}
