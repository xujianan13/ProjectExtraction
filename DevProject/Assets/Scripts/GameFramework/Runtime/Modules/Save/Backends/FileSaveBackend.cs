using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 原生平台的异步文件存档后端。
    /// </summary>
    public sealed class FileSaveBackend : ISaveBackend
    {
        private static readonly UTF8Encoding Utf8 = new(false, true);
        private readonly string rootPath;
        private readonly int maxRecordBytes;
        private readonly string configurationError;

        public string StorageIdentity { get; }
        public int MaxRecordBytes => maxRecordBytes;

        /// <summary>
        /// 创建独立根目录的文件后端。
        /// </summary>
        /// <param name="rootPath">持久存储根目录。</param>
        /// <param name="maxRecordBytes">单条记录最大 UTF-8 字节数。</param>
        public FileSaveBackend(string rootPath, int maxRecordBytes = 262144)
        {
            this.maxRecordBytes = maxRecordBytes;
            if (string.IsNullOrWhiteSpace(rootPath))
            {
                configurationError = "Save root path is empty.";
                StorageIdentity = "file://invalid";
                return;
            }

            try
            {
                var fullPath = Path.GetFullPath(rootPath);
                this.rootPath = fullPath == Path.GetPathRoot(fullPath) ? fullPath : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var identityPath = Path.DirectorySeparatorChar == (char)92 ? this.rootPath.ToLowerInvariant() : this.rootPath;
                StorageIdentity = "file://" + identityPath.Replace((char)92, '/');
            }
            catch (ArgumentException)
            {
                configurationError = "Save root path is invalid.";
                StorageIdentity = "file://invalid";
            }
            catch (NotSupportedException)
            {
                configurationError = "Save root path is invalid.";
                StorageIdentity = "file://invalid";
            }
            catch (PathTooLongException)
            {
                configurationError = "Save root path is too long.";
                StorageIdentity = "file://invalid";
            }
        }

        /// <summary>
        /// 检查目录和平台能力。
        /// </summary>
        /// <returns>初始化结果。</returns>
        public UniTask<SaveResult> InitializeAsync()
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return UniTask.FromResult(SaveResult.Fail(SaveError.Unsupported, "File persistence is not supported on WebGL."));
#else
            if (configurationError != null || maxRecordBytes <= 0)
                return UniTask.FromResult(SaveResult.Fail(SaveError.InvalidArgument, configurationError ?? "Invalid record limit."));
            try
            {
                Directory.CreateDirectory(rootPath);
                return UniTask.FromResult(SaveResult.Ok());
            }
            catch (IOException error) { return UniTask.FromResult(FailWrite(error)); }
            catch (UnauthorizedAccessException error) { return UniTask.FromResult(FailWrite(error)); }
#endif
        }

        /// <summary>
        /// 异步读取指定文件。
        /// </summary>
        /// <param name="key">受限的逻辑键。</param>
        /// <returns>读取结果。</returns>
        public UniTask<SaveReadResult> ReadAsync(string key)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return UniTask.FromResult(SaveReadResult.Failed(SaveResult.Fail(SaveError.Unsupported, "File persistence is not supported on WebGL.")));
#else
            if (!TryGetPath(key, out var path))
                return UniTask.FromResult(SaveReadResult.Failed(SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key or root.")));
            if (!TryOpenRead(path, out var stream, out var result)) return UniTask.FromResult(result);
            return ReadOpenedAsync(stream);
#endif
        }

        /// <summary>
        /// 在异步状态机之外区分缺档和文件访问失败，规避已复现的 IL2CPP 多类型 catch 匹配崩溃。
        /// 这里只捕获基类异常并按类型全名识别文件缺失，避免重新触发具体类型的 catch 匹配。
        /// </summary>
        /// <param name="path">已校验的记录文件路径。</param>
        /// <param name="stream">成功打开的读取流。</param>
        /// <param name="result">未打开时的读取结果。</param>
        /// <returns>是否已打开读取流。</returns>
        private bool TryOpenRead(string path, out FileStream stream, out SaveReadResult result)
        {
            stream = null;
            result = default;
            try
            {
                stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 8192, true);
                if (stream.Length <= maxRecordBytes) return true;
                stream.Dispose();
                stream = null;
                result = SaveReadResult.Failed(SaveResult.Fail(SaveError.TooLarge, "Stored save record exceeds limit."));
                return false;
            }
            catch (Exception error)
            {
                var typeName = error.GetType().FullName;
                if (typeName == "System.IO.FileNotFoundException") result = SaveReadResult.Missing();
                else result = SaveReadResult.Failed(SaveResult.Fail(SaveError.ReadFailed, error.Message,
                    typeName == "System.IO.DirectoryNotFoundException" || typeName == "System.IO.IOException"));
            }
            stream?.Dispose();
            stream = null;
            return false;
        }

        /// <summary>
        /// 读取已经打开的文件流。
        /// </summary>
        /// <param name="stream">已打开的记录流。</param>
        /// <returns>读取结果。</returns>
        private static async UniTask<SaveReadResult> ReadOpenedAsync(FileStream stream)
        {
            using (stream)
            {
                var buffer = new byte[(int)stream.Length];
                var read = 0;
                try
                {
                    while (read < buffer.Length)
                    {
                        var count = await stream.ReadAsync(buffer, read, buffer.Length - read);
                        if (count == 0) return SaveReadResult.Failed(SaveResult.Fail(SaveError.ReadFailed, "Save file ended unexpectedly.", true));
                        read += count;
                    }
                }
                catch (Exception error) { return SaveReadResult.Failed(SaveResult.Fail(SaveError.ReadFailed, error.Message, true)); }
                return DecodeRecord(buffer);
            }
        }

        /// <summary>
        /// 在异步状态机之外验证 UTF-8 编码。
        /// </summary>
        /// <param name="bytes">完整文件内容。</param>
        /// <returns>解码后的读取结果。</returns>
        private static SaveReadResult DecodeRecord(byte[] bytes)
        {
            try { return SaveReadResult.Found(Utf8.GetString(bytes)); }
            catch (DecoderFallbackException) { return SaveReadResult.Failed(SaveResult.Fail(SaveError.Corrupt, "Save file is not UTF-8.")); }
        }
        /// <summary>
        /// 经同目录临时文件写入并替换单条记录。
        /// </summary>
        /// <param name="key">受限的逻辑键。</param>
        /// <param name="record">完整记录。</param>
        /// <returns>物理写入完成结果。</returns>
        public async UniTask<SaveResult> WriteAsync(string key, string record)
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return SaveResult.Fail(SaveError.Unsupported, "File persistence is not supported on WebGL.");
#else
            if (!TryGetPath(key, out var path) || record == null) return SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key, root or record.");
            byte[] bytes;
            try { bytes = Utf8.GetBytes(record); }
            catch (EncoderFallbackException) { return SaveResult.Fail(SaveError.InvalidArgument, "Save record is not valid UTF-8."); }
            if (bytes.Length > maxRecordBytes) return SaveResult.Fail(SaveError.TooLarge, "Save record exceeds limit.");

            var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            var commitStarted = false;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 8192, true))
                {
                    await stream.WriteAsync(bytes, 0, bytes.Length);
                    await stream.FlushAsync();
                    stream.Flush(true);
                }
                commitStarted = true;
                if (File.Exists(path))
                {
                    try { File.Replace(temporaryPath, path, null); }
                    catch (PlatformNotSupportedException)
                    {
                        using var destination = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
                        await destination.WriteAsync(bytes, 0, bytes.Length);
                        await destination.FlushAsync();
                        destination.Flush(true);
                    }
                }
                else File.Move(temporaryPath, path);
                return SaveResult.Ok();
            }
            catch (IOException error) { return commitStarted ? SaveResult.Fail(SaveError.Uncertain, error.Message) : FailWrite(error); }
            catch (UnauthorizedAccessException error) { return commitStarted ? SaveResult.Fail(SaveError.Uncertain, error.Message) : FailWrite(error); }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    try { File.Delete(temporaryPath); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
#endif
        }

        /// <summary>
        /// 限制逻辑键只映射到根目录之下的记录文件。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <param name="path">解析后的文件路径。</param>
        /// <returns>路径是否有效。</returns>
        private bool TryGetPath(string key, out string path)
        {
            path = null;
            if (rootPath == null || maxRecordBytes <= 0 || string.IsNullOrEmpty(key) || key.Length > 4096) return false;
            var segments = key.Split('/');
            foreach (var segment in segments)
            {
                if (segment.Length == 0 || segment == "." || segment == "..") return false;
                foreach (var character in segment)
                    if (!char.IsLetterOrDigit(character) && character != '-' && character != '_' && character != '.' && character != '%' && character != '~') return false;
            }
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Utf8.GetBytes(key));
            var relative = BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant() + ".save";
            var candidate = Path.GetFullPath(Path.Combine(rootPath, relative));
            var rootPrefix = rootPath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ? rootPath : rootPath + Path.DirectorySeparatorChar;
            var comparison = Path.DirectorySeparatorChar == (char)92 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!candidate.StartsWith(rootPrefix, comparison)) return false;
            path = candidate;
            return true;
        }

        /// <summary>
        /// 映射文件写入异常。
        /// </summary>
        /// <param name="error">底层异常。</param>
        /// <returns>失败结果。</returns>
        private static SaveResult FailWrite(Exception error)
        {
            var quota = error is IOException && ((uint)error.HResult == 0x80070070 || (uint)error.HResult == 0x80070027);
            return SaveResult.Fail(quota ? SaveError.QuotaExceeded : SaveError.WriteFailed, error.Message, !quota && error is IOException);
        }
    }
}
