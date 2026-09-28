#if UNITY_EDITOR
using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 为编辑器工具生成按项目隔离的用户设置键。
    /// </summary>
    internal static class FrameworkEditorUserStorage
    {
        private static string _projectId;

        internal static string ProjectId
        {
            get
            {
                if (string.IsNullOrEmpty(_projectId))
                {
                    string projectRoot = Application.dataPath
                        .Replace('\\', '/')
                        .TrimEnd('/');
                    const string AssetsSuffix = "/Assets";
                    if (projectRoot.EndsWith(AssetsSuffix, StringComparison.OrdinalIgnoreCase))
                    {
                        projectRoot = projectRoot.Substring(0, projectRoot.Length - AssetsSuffix.Length);
                    }

                    _projectId = Mathf.Abs(projectRoot.ToLowerInvariant().GetHashCode()).ToString("X8");
                }

                return _projectId;
            }
        }

        internal static string GetPrefKey(string moduleName, string keyName)
        {
            if (string.IsNullOrEmpty(moduleName))
            {
                throw new ArgumentException("模块名不能为空", nameof(moduleName));
            }

            if (string.IsNullOrEmpty(keyName))
            {
                throw new ArgumentException("设置键不能为空", nameof(keyName));
            }

            return $"GF.{moduleName}.{ProjectId}.{keyName}";
        }
    }
}
#endif
