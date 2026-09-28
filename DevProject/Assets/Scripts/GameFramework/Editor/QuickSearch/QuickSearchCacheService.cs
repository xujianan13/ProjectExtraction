#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 快速搜索使用的资源缓存条目。
    /// </summary>
    internal sealed class QuickSearchCachedAsset
    {
        private UnityEngine.Object _asset;
        private Texture2D _icon;

        internal QuickSearchCachedAsset(string path)
        {
            UpdatePath(path);
        }

        internal string Path { get; private set; }

        internal string Name { get; private set; }

        internal string NameLower { get; private set; }

        internal string FuzzyNameLower { get; private set; }

        internal string AssetType { get; private set; }

        internal UnityEngine.Object Asset
        {
            get
            {
                EnsureAssetLoaded();
                return _asset;
            }
        }

        internal Texture2D Icon
        {
            get
            {
                EnsureAssetLoaded();
                return _icon;
            }
        }

        internal void UpdatePath(string path)
        {
            Path = path;
            int separatorIndex = path.LastIndexOf('/');
            Name = separatorIndex >= 0 ? path.Substring(separatorIndex + 1) : path;
            NameLower = Name.ToLowerInvariant();
            FuzzyNameLower = QuickSearchCacheService.NormalizeForFuzzySearch(Name);
            AssetType = GetAssetType(path);
            _asset = null;
            _icon = null;
        }

        private void EnsureAssetLoaded()
        {
            if (_asset != null)
            {
                return;
            }

            _asset = AssetDatabase.LoadMainAssetAtPath(Path);
            if (_asset != null)
            {
                _icon = AssetPreview.GetMiniThumbnail(_asset);
            }
        }

        private static string GetAssetType(string path)
        {
            int extensionIndex = path.LastIndexOf('.');
            string extension = extensionIndex >= 0 ? path.Substring(extensionIndex).ToLowerInvariant() : string.Empty;
            switch (extension)
            {
                case ".cs": return "C# Script";
                case ".prefab": return "Prefab";
                case ".unity": return "Scene";
                case ".asset": return "Asset";
                case ".mat": return "Material";
                case ".png":
                case ".jpg":
                case ".jpeg":
                case ".tga":
                case ".psd": return "Texture";
                case ".anim": return "Animation";
                case ".controller": return "Animator Controller";
                case ".fbx":
                case ".obj": return "Model";
                case ".wav":
                case ".mp3":
                case ".ogg": return "Audio";
                default: return "Other";
            }
        }
    }

    /// <summary>
    /// 为快速搜索分帧建立资源索引，并在资源变化后增量更新。
    /// </summary>
    [InitializeOnLoad]
    internal static class QuickSearchCacheService
    {
        private const int AssetsPerFrame = 600;

        private static readonly List<QuickSearchCachedAsset> AssetsInternal = new List<QuickSearchCachedAsset>(8192);
        private static readonly Dictionary<string, int> PathToIndex =
            new Dictionary<string, int>(8192, StringComparer.OrdinalIgnoreCase);
        private static readonly StringBuilder FuzzySearchBuilder = new StringBuilder(64);

        private static string[] _buildGuids;
        private static int _buildIndex;
        private static bool _showProgress;

        static QuickSearchCacheService()
        {
            EditorApplication.delayCall += EnsureLoaded;
        }

        internal static event Action CacheUpdated;

        internal static IReadOnlyList<QuickSearchCachedAsset> Assets => AssetsInternal;

        internal static int AssetCount => AssetsInternal.Count;

        internal static bool IsReady { get; private set; }

        internal static bool IsBuilding { get; private set; }

        internal static string LastUpdateTime { get; private set; } = string.Empty;

        internal static void EnsureLoaded()
        {
            if (!IsReady && !IsBuilding)
            {
                RequestFullRebuild(false);
            }
        }

        internal static void RequestFullRebuild(bool showProgress)
        {
            if (IsBuilding)
            {
                return;
            }

            AssetsInternal.Clear();
            PathToIndex.Clear();
            _buildGuids = AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });
            _buildIndex = 0;
            _showProgress = showProgress;
            IsReady = false;
            IsBuilding = true;
            EditorApplication.update += BuildNextBatch;
            CacheUpdated?.Invoke();
        }

        internal static void ApplyAssetChanges(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (IsBuilding)
            {
                return;
            }

            bool changed = RemovePaths(deletedAssets) | RemovePaths(movedFromAssetPaths);
            changed |= UpsertPaths(importedAssets) | UpsertPaths(movedAssets);
            if (changed)
            {
                LastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                CacheUpdated?.Invoke();
            }
        }

        internal static string NormalizeForFuzzySearch(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return string.Empty;
            }

            FuzzySearchBuilder.Length = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char character = text[i];
                if (character == '_' || character == '-' || character == '.' || character == ' ')
                {
                    continue;
                }

                FuzzySearchBuilder.Append(char.ToLowerInvariant(character));
            }

            return FuzzySearchBuilder.ToString();
        }

        private static void BuildNextBatch()
        {
            if (_buildGuids == null)
            {
                FinishBuild();
                return;
            }

            int endIndex = Math.Min(_buildIndex + AssetsPerFrame, _buildGuids.Length);
            for (; _buildIndex < endIndex; _buildIndex++)
            {
                UpsertPath(AssetDatabase.GUIDToAssetPath(_buildGuids[_buildIndex]));
            }

            if (_showProgress && _buildGuids.Length > 0)
            {
                bool cancelled = EditorUtility.DisplayCancelableProgressBar(
                    "快速搜索",
                    $"正在建立资源索引 {_buildIndex}/{_buildGuids.Length}",
                    _buildIndex / (float)_buildGuids.Length);
                if (cancelled)
                {
                    FinishBuild();
                    return;
                }
            }

            if (_buildIndex >= _buildGuids.Length)
            {
                FinishBuild();
            }
        }

        private static void FinishBuild()
        {
            EditorApplication.update -= BuildNextBatch;
            EditorUtility.ClearProgressBar();
            _buildGuids = null;
            _buildIndex = 0;
            _showProgress = false;
            IsBuilding = false;
            IsReady = true;
            LastUpdateTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            CacheUpdated?.Invoke();
        }

        private static bool RemovePaths(string[] paths)
        {
            bool changed = false;
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                changed |= RemovePath(paths[i]);
            }

            return changed;
        }

        private static bool UpsertPaths(string[] paths)
        {
            bool changed = false;
            if (paths == null)
            {
                return false;
            }

            for (int i = 0; i < paths.Length; i++)
            {
                changed |= UpsertPath(paths[i]);
            }

            return changed;
        }

        private static bool UpsertPath(string path)
        {
            if (!ShouldIncludePath(path))
            {
                return RemovePath(path);
            }

            if (PathToIndex.TryGetValue(path, out int index))
            {
                AssetsInternal[index].UpdatePath(path);
                return true;
            }

            PathToIndex.Add(path, AssetsInternal.Count);
            AssetsInternal.Add(new QuickSearchCachedAsset(path));
            return true;
        }

        private static bool RemovePath(string path)
        {
            if (string.IsNullOrEmpty(path) || !PathToIndex.TryGetValue(path, out int index))
            {
                return false;
            }

            int lastIndex = AssetsInternal.Count - 1;
            if (index != lastIndex)
            {
                QuickSearchCachedAsset lastAsset = AssetsInternal[lastIndex];
                AssetsInternal[index] = lastAsset;
                PathToIndex[lastAsset.Path] = index;
            }

            AssetsInternal.RemoveAt(lastIndex);
            PathToIndex.Remove(path);
            return true;
        }

        private static bool ShouldIncludePath(string path)
        {
            return !string.IsNullOrEmpty(path)
                   && path.StartsWith("Assets/", StringComparison.Ordinal)
                   && !AssetDatabase.IsValidFolder(path)
                   && !path.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                   && !path.EndsWith(".DS_Store", StringComparison.OrdinalIgnoreCase)
                   && path.IndexOf("Editor Default Resources", StringComparison.Ordinal) < 0;
        }
    }

    /// <summary>
    /// 监听资源导入、删除与移动，并同步快速搜索索引。
    /// </summary>
    internal sealed class QuickSearchAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            QuickSearchCacheService.ApplyAssetChanges(
                importedAssets,
                deletedAssets,
                movedAssets,
                movedFromAssetPaths);
        }
    }
}
#endif
