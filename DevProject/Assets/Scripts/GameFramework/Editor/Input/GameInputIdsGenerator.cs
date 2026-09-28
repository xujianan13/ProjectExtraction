using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFramework.Editor.Input
{
    /// <summary>
    /// 校验玩法输入资产并生成稳定的动作 GUID 常量。
    /// </summary>
    public static class GameInputIdsGenerator
    {
        private const string AssetPath = "Assets/GameResources/Input/GameInputActions.inputactions";
        private const string OutputPath = "Assets/Scripts/GameLogic/Runtime/Input/Gen/GameInputIds.Gen.cs";

        [Serializable]
        private sealed class ActionDocument
        {
            public ActionMap[] maps;
        }

        [Serializable]
        private sealed class ActionMap
        {
            public string name;
            public string id;
            public ActionEntry[] actions;
            public BindingEntry[] bindings;
        }

        [Serializable]
        private sealed class ActionEntry
        {
            public string name;
            public string id;
        }

        [Serializable]
        private sealed class BindingEntry
        {
            public string action;
            public string id;
        }

        /// <summary>
        /// 从玩法动作资产生成常量代码。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Input/Generate Game Input IDs")]
        public static void Generate()
        {
            if (!File.Exists(AssetPath))
            {
                Debug.LogError($"缺少玩法输入资产：{AssetPath}");
                return;
            }

            ActionDocument document = JsonUtility.FromJson<ActionDocument>(File.ReadAllText(AssetPath));
            if (!Validate(document, out string error))
            {
                Debug.LogError($"玩法输入资产无效：{error}");
                return;
            }

            var code = new StringBuilder();
            code.AppendLine("namespace GameLogic.Input");
            code.AppendLine("{");
            code.AppendLine("    /// <summary>");
            code.AppendLine("    /// 玩法输入动作的稳定 GUID。");
            code.AppendLine("    /// </summary>");
            code.AppendLine("    public static class GameInputIds");
            code.AppendLine("    {");
            foreach (ActionMap map in document.maps)
            {
                foreach (ActionEntry action in map.actions)
                {
                    code.AppendLine("        /// <summary>");
                    code.AppendLine("        /// 玩法输入动作 GUID。");
                    code.AppendLine("        /// </summary>");
                    code.AppendLine($"        public const string {ToIdentifier(action.name)} = \"{action.id}\";");
                }
            }
            code.AppendLine("    }");
            code.AppendLine("}");
            Directory.CreateDirectory(Path.GetDirectoryName(OutputPath));
            File.WriteAllText(OutputPath, code.ToString(), new UTF8Encoding(false));
            AssetDatabase.ImportAsset(OutputPath);
            Debug.Log($"已生成玩法输入动作 GUID：{OutputPath}");
        }

        /// <summary>
        /// 检查动作与绑定的名称和 GUID。
        /// </summary>
        /// <param name="document">输入动作资产的 JSON 数据。</param>
        /// <param name="error">失败时的错误说明。</param>
        /// <returns>动作资产是否有效。</returns>
        private static bool Validate(ActionDocument document, out string error)
        {
            error = string.Empty;
            if (document?.maps == null || document.maps.Length == 0)
            {
                error = "缺少 Action Map。";
                return false;
            }

            var ids = new HashSet<Guid>();
            var identifiers = new HashSet<string>(StringComparer.Ordinal);
            foreach (ActionMap map in document.maps)
            {
                if (map == null || string.IsNullOrWhiteSpace(map.name) || !AddId(map.id, ids))
                {
                    error = "Action Map 名称或 GUID 无效。";
                    return false;
                }
                if (map.actions == null || map.actions.Length == 0)
                {
                    error = $"{map.name} 缺少 Action。";
                    return false;
                }

                var actionNames = new HashSet<string>(StringComparer.Ordinal);
                foreach (ActionEntry action in map.actions)
                {
                    if (action == null || string.IsNullOrWhiteSpace(action.name) || !AddId(action.id, ids) ||
                        !actionNames.Add(action.name) || !identifiers.Add(ToIdentifier(action.name)))
                    {
                        error = $"{map.name} 的 Action 名称或 GUID 无效或重复。";
                        return false;
                    }
                }

                if (map.bindings == null) continue;
                foreach (BindingEntry binding in map.bindings)
                {
                    if (binding == null || !AddId(binding.id, ids) || !actionNames.Contains(binding.action))
                    {
                        error = $"{map.name} 的 Binding GUID 无效或 Action 引用不存在。";
                        return false;
                    }
                }
            }
            return true;
        }

        /// <summary>
        /// 校验 GUID 并加入唯一性集合。
        /// </summary>
        /// <param name="value">待检查的 GUID 文本。</param>
        /// <param name="ids">已经使用的 GUID 集合。</param>
        /// <returns>GUID 有效且此前未出现时为 true。</returns>
        private static bool AddId(string value, HashSet<Guid> ids)
        {
            return Guid.TryParse(value, out Guid id) && id != Guid.Empty && ids.Add(id);
        }

        /// <summary>
        /// 将动作名转换为 C# 标识符。
        /// </summary>
        /// <param name="name">动作名。</param>
        /// <returns>可写入 C# 源码的标识符。</returns>
        private static string ToIdentifier(string name)
        {
            var result = new StringBuilder();
            if (name.Length == 0 || char.IsDigit(name[0])) result.Append('_');
            foreach (char character in name) result.Append(char.IsLetterOrDigit(character) || character == '_' ? character : '_');
            return result.ToString();
        }
    }
}
