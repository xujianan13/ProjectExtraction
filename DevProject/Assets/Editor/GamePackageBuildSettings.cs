using UnityEditor;
using UnityEngine;

namespace MiniGame.Editor
{
    /// <summary>
    /// 保存独立于宿主程序版本的 GamePackage 内容版本。
    /// </summary>
    public sealed class GamePackageBuildSettings : ScriptableObject
    {
        private const string AssetPath = "Assets/Editor/GamePackageBuildSettings.asset";

        [SerializeField] private string _packageVersion = "1.0.0";
        [SerializeField] private string[] _additionalStartupDirectories = System.Array.Empty<string>();

        /// <summary>
        /// 当前内容包版本。
        /// </summary>
        public string PackageVersion => _packageVersion;

        /// <summary>
        /// GameResources 下额外需要随启动下载的直接子目录名称。
        /// </summary>
        public string[] AdditionalStartupDirectories => _additionalStartupDirectories ?? System.Array.Empty<string>();

        /// <summary>
        /// 加载内容包构建配置，不存在时创建默认配置。
        /// </summary>
        /// <returns>可编辑的内容包构建配置。</returns>
        public static GamePackageBuildSettings LoadOrCreate()
        {
            GamePackageBuildSettings settings = AssetDatabase.LoadAssetAtPath<GamePackageBuildSettings>(AssetPath);
            if (settings != null) return settings;

            settings = CreateInstance<GamePackageBuildSettings>();
            AssetDatabase.CreateAsset(settings, AssetPath);
            AssetDatabase.SaveAssets();
            return settings;
        }

        /// <summary>
        /// 在 Inspector 中打开内容包构建配置。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Edit GamePackage Build Settings")]
        public static void OpenInInspector()
        {
            GamePackageBuildSettings settings = LoadOrCreate();
            Selection.activeObject = settings;
            EditorGUIUtility.PingObject(settings);
        }
    }
}
