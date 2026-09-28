using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace MiniGame.Editor
{
    /// <summary>
    /// 在 Unity Editor 中执行 Excel 数据表导出与校验。
    /// </summary>
    public static class ExcelTableExportMenu
    {
        /// <summary>
        /// 导出 Excel 数据表，并刷新 Unity 资源数据库。
        /// </summary>
        [MenuItem("Tools/Unity Mini Game Framework/Export Excel Tables")]
        public static void Export()
        {
            EnsureEditorReady();
            Run("export");
            AssetDatabase.Refresh();
            Debug.Log("Excel 数据表已导出并请求刷新资源。请等待 Unity 完成资源导入和脚本编译，再准备或构建 GamePackage。");
        }

        /// <summary>
        /// 校验 Excel 数据表的生成结果是否与源文件一致。
        /// </summary>
        internal static void Verify() => Run("verify");

        /// <summary>
        /// 确认 Unity 当前没有执行资源刷新或脚本编译。
        /// </summary>
        internal static void EnsureEditorReady()
        {
            if (EditorApplication.isUpdating || EditorApplication.isCompiling)
                throw new InvalidOperationException("Unity 正在刷新资源或编译脚本。请等待完成后重试，避免复制旧版 GameLogic.dll。");
        }

        /// <summary>
        /// 在仓库根目录执行数据表导出工具。
        /// </summary>
        /// <param name="command">导出工具命令。</param>
        private static void Run(string command)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName;
            string repositoryRoot = string.IsNullOrEmpty(projectRoot) ? null : Directory.GetParent(projectRoot)?.FullName;
            if (string.IsNullOrEmpty(repositoryRoot)) throw new InvalidOperationException("无法解析仓库根目录。");
            string script = Path.Combine(repositoryRoot, "tool", "export-excel.mjs");
            if (!File.Exists(script)) throw new FileNotFoundException($"Excel 导表工具不存在：{script}", script);

            var startInfo = new ProcessStartInfo
            {
                FileName = "node",
                Arguments = $"tool/export-excel.mjs {command}",
                WorkingDirectory = repositoryRoot,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };
            var output = new StringBuilder();
            var errors = new StringBuilder();
            using (var process = new Process { StartInfo = startInfo })
            {
                process.OutputDataReceived += (_, args) => { if (args.Data != null) output.AppendLine(args.Data); };
                process.ErrorDataReceived += (_, args) => { if (args.Data != null) errors.AppendLine(args.Data); };
                if (!process.Start()) throw new InvalidOperationException("无法启动 Node.js Excel 导表工具，请检查 Node.js 安装与 PATH。");
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidOperationException($"Excel 导表工具 {command} 失败（退出码 {process.ExitCode}）。\n{output}{errors}");
            }
            string message = output.ToString().Trim();
            if (message.Length > 0) Debug.Log(message);
            message = errors.ToString().Trim();
            if (message.Length > 0) Debug.LogWarning(message);
        }
    }
}
