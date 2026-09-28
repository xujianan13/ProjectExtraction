using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace GameFramework.Editor.Scaffolding
{
    /// <summary>
    /// 记录本次写入的文件和目录，在失败时恢复已有文件并移除新产物。
    /// </summary>
    internal sealed class ScaffoldWriteTransaction
    {
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _newDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _newDirectoryMetas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// 在首次写入前记录文件内容或新文件身份。
        /// </summary>
        /// <param name="path">文件绝对路径。</param>
        internal void TrackFile(string path)
        {
            if (_files.ContainsKey(path)) return;
            bool exists = File.Exists(path);
            _files.Add(path, exists ? File.ReadAllBytes(path) : null);
            if (!exists) TrackNewDirectory(Path.GetDirectoryName(path));
        }

        /// <summary>
        /// 在首次创建 Unity 资产前记录资产及元数据路径。
        /// </summary>
        /// <param name="path">资产绝对路径。</param>
        internal void TrackNewAsset(string path)
        {
            TrackFile(path);
            TrackMetaIfNew(path + ".meta");
        }

        /// <summary>
        /// 将新源码的 Unity 元数据加入回滚范围。
        /// </summary>
        /// <param name="path">源码绝对路径。</param>
        internal void TrackNewSourceMeta(string path) => TrackMetaIfNew(path + ".meta");

        /// <summary>
        /// 恢复已有文件，并删除本次新建的文件、元数据和空目录。
        /// </summary>
        internal void Rollback()
        {
            foreach (KeyValuePair<string, byte[]> file in _files)
            {
                if (file.Value == null)
                {
                    if (File.Exists(file.Key)) File.Delete(file.Key);
                }
                else File.WriteAllBytes(file.Key, file.Value);
            }
            foreach (string directory in _newDirectories.OrderByDescending(path => path.Length))
            {
                if (!Directory.Exists(directory) || Directory.EnumerateFileSystemEntries(directory).Any()) continue;
                Directory.Delete(directory);
                string meta = directory + ".meta";
                if (_newDirectoryMetas.Contains(meta) && File.Exists(meta)) File.Delete(meta);
            }
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// 记录原先不存在的 Unity 元数据路径。
        /// </summary>
        /// <param name="path">元数据绝对路径。</param>
        private void TrackMetaIfNew(string path)
        {
            if (!File.Exists(path)) _files[path] = null;
        }

        /// <summary>
        /// 记录目标文件所需的新目录及其元数据。
        /// </summary>
        /// <param name="directory">文件父目录。</param>
        private void TrackNewDirectory(string directory)
        {
            string current = directory;
            while (!string.IsNullOrEmpty(current) && !Directory.Exists(current))
            {
                _newDirectories.Add(current);
                string meta = current + ".meta";
                if (!File.Exists(meta)) _newDirectoryMetas.Add(meta);
                current = Path.GetDirectoryName(current);
            }
        }
    }
}
