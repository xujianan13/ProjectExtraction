using System;
using System.Collections.Generic;

namespace GameFramework.Editor.Scaffolding
{
    /// <summary>
    /// 项目脚手架的持久配置。
    /// </summary>
    [Serializable]
    public sealed class ScaffoldProject
    {
        public int schemaVersion = 1;
        public string rootNamespace = "GameLogic";
        public string sourceRoot = "Assets/Scripts/GameLogic/Runtime";
        public string resourceRoot = "Assets/GameResources";
        public string startupScene = "";
        public int[] reservedWindowIds = { 1001, 1100 };
        public List<ScaffoldEntry> entries = new List<ScaffoldEntry>();
    }

    /// <summary>
    /// 一项玩法、窗口或场景的生成描述。
    /// </summary>
    [Serializable]
    public sealed class ScaffoldEntry
    {
        public string kind;
        public string name;
        public string address;
        public string assetPath;
        public string createdShape;
        public string preset = "Page";
        public string contentMode = "Prefab";
        public string gameplay = "";
        public string window = "";
        public int windowId;
        public bool includeModel;
        public string scope = "Scene";
        public string cacheMode = "Immediate";
        public bool closeOnOutsideClick;
        public int layer = -1;
        public float maskAlpha = 0.65f;
        public float destroyDelaySeconds = 30f;
    }

    /// <summary>
    /// 模板生成的一份文本文件。
    /// </summary>
    public sealed class ScaffoldTextFile
    {
        public string Path { get; }
        public string Text { get; }
        public bool Generated { get; }

        /// <summary>
        /// 创建文本文件计划。
        /// </summary>
        /// <param name="path">Unity 项目相对路径。</param>
        /// <param name="text">文件完整内容。</param>
        /// <param name="generated">是否允许由生成器更新。</param>
        public ScaffoldTextFile(string path, string text, bool generated)
        {
            Path = path;
            Text = text;
            Generated = generated;
        }
    }

    /// <summary>
    /// 一次脚手架命令请求。
    /// </summary>
    [Serializable]
    public sealed class ScaffoldRequest
    {
        public string operation = "generate";
        public ScaffoldEntry[] entries = Array.Empty<ScaffoldEntry>();
        public bool setStartup;
        public string startupScene = "";
        public bool dryRun;
    }

    /// <summary>
    /// 一次脚手架命令的结构化结果。
    /// </summary>
    [Serializable]
    public sealed class ScaffoldResult
    {
        public bool success;
        public string phase;
        public string[] files = Array.Empty<string>();
        public ScaffoldEntry[] entries = Array.Empty<ScaffoldEntry>();
        public string nextStep = "";
        public string[] errors = Array.Empty<string>();
    }
}
