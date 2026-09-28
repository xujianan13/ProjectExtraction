using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace GameFramework.Editor.Scaffolding
{
    /// <summary>
    /// Unity batchmode 的脚手架命令入口。
    /// </summary>
    public static class ScaffoldBatch
    {
        /// <summary>
        /// 从命令行请求文件执行，并写入结构化结果。
        /// </summary>
        public static void RunFromCommandLine()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            string requestPath = ReadArgument(arguments, "-scaffoldRequest");
            string resultPath = ReadArgument(arguments, "-scaffoldResult");
            ScaffoldResult result;
            try
            {
                if (string.IsNullOrWhiteSpace(requestPath) || !File.Exists(requestPath))
                    result = new ScaffoldResult { phase = "request", errors = new[] { $"请求文件不存在：{requestPath}" } };
                else
                {
                    ScaffoldRequest request = JsonUtility.FromJson<ScaffoldRequest>(File.ReadAllText(requestPath));
                    result = ScaffoldGenerator.Run(request);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"脚手架命令失败：{exception}");
                result = new ScaffoldResult { phase = "command", errors = new[] { exception.Message } };
            }

            if (!result.success && string.IsNullOrEmpty(result.nextStep)) result.nextStep = "修正错误后重试。";
            if (!string.IsNullOrWhiteSpace(resultPath))
            {
                string directory = Path.GetDirectoryName(Path.GetFullPath(resultPath));
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllText(resultPath, JsonUtility.ToJson(result, true) + Environment.NewLine, new UTF8Encoding(false));
            }
            if (!result.success) EditorApplication.Exit(1);
        }

        /// <summary>
        /// 读取命令行中的单值参数。
        /// </summary>
        /// <param name="arguments">完整参数数组。</param>
        /// <param name="key">参数名。</param>
        /// <returns>参数值；缺失时为空。</returns>
        private static string ReadArgument(string[] arguments, string key)
        {
            for (int index = 0; index < arguments.Length - 1; index++)
                if (arguments[index] == key) return arguments[index + 1];
            return string.Empty;
        }
    }
}
