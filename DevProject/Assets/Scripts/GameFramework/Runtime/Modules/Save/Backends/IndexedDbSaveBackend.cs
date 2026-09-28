#if UNITY_WEBGL && !UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 浏览器 IndexedDB 存档后端；写入只在事务完成后返回。
    /// </summary>
    public sealed class IndexedDbSaveBackend : ISaveBackend
    {
        private const string Database = "GameFramework.Save.v1";
        private const int Capacity = 262144;
        private static readonly Dictionary<int, Pending> PendingOperations = new();
        private static readonly NativeCompletion CompletionCallback = OnCompleted;
        private static int nextId;

        public string StorageIdentity => "indexeddb://GameFramework.Save.v1/records";
        public int MaxRecordBytes => Capacity;

        /// <summary>
        /// 打开稳定命名的浏览器数据库。
        /// </summary>
        /// <returns>初始化结果。</returns>
        public async UniTask<SaveResult> InitializeAsync()
        {
            var result = await Begin(false, null, null, true);
            return result.Result;
        }

        /// <summary>
        /// 读取指定逻辑键。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <returns>读取结果。</returns>
        public UniTask<SaveReadResult> ReadAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return UniTask.FromResult(SaveReadResult.Failed(SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key.")));
            return Begin(true, key, null, false);
        }

        /// <summary>
        /// 在事务完成后确认指定逻辑键的写入。
        /// </summary>
        /// <param name="key">逻辑键。</param>
        /// <param name="record">完整记录。</param>
        /// <returns>持久化事务结果。</returns>
        public async UniTask<SaveResult> WriteAsync(string key, string record)
        {
            if (string.IsNullOrWhiteSpace(key) || record == null) return SaveResult.Fail(SaveError.InvalidArgument, "Invalid save key or record.");
            if (Encoding.UTF8.GetByteCount(record) > Capacity) return SaveResult.Fail(SaveError.TooLarge, "Save record exceeds IndexedDB limit.");
            var result = await Begin(false, key, record, false);
            return result.Result;
        }

        /// <summary>
        /// 创建并登记一次 JS 操作。
        /// </summary>
        /// <param name="read">是否读取。</param>
        /// <param name="key">逻辑键。</param>
        /// <param name="record">写入记录。</param>
        /// <param name="initialize">是否只初始化。</param>
        /// <returns>操作结果。</returns>
        private static UniTask<SaveReadResult> Begin(bool read, string key, string record, bool initialize)
        {
            var id = ++nextId;
            var source = new UniTaskCompletionSource<SaveReadResult>();
            PendingOperations.Add(id, new Pending(source, read, initialize));
            if (initialize) GFSave_Init(id, Database, CompletionCallback);
            else if (read) GFSave_Read(id, Database, key, CompletionCallback);
            else GFSave_Write(id, Database, key, record, CompletionCallback);
            return source.Task;
        }

        /// <summary>
        /// 接收 JS 事务最终回调并释放在途引用。
        /// </summary>
        /// <param name="id">操作编号。</param>
        /// <param name="status">JS 状态码。</param>
        /// <param name="value">临时 UTF-8 字符串指针。</param>
        [AOT.MonoPInvokeCallback(typeof(NativeCompletion))]
        private static void OnCompleted(int id, int status, IntPtr value)
        {
            if (!PendingOperations.TryGetValue(id, out var pending)) return;
            PendingOperations.Remove(id);
            var message = value == IntPtr.Zero ? string.Empty : Marshal.PtrToStringUTF8(value);
            if (status == 0)
                pending.Source.TrySetResult(pending.Read ? SaveReadResult.Found(message) : SaveReadResult.Found(string.Empty));
            else if (status == 1 && pending.Read) pending.Source.TrySetResult(SaveReadResult.Missing());
            else
            {
                var error = status == 2 ? SaveError.TooLarge : status == 3 ? SaveError.QuotaExceeded : status == 5 ? SaveError.Unsupported :
                    status == 6 ? SaveError.Uncertain : status == 7 ? SaveError.Corrupt : pending.Read ? SaveError.ReadFailed : SaveError.WriteFailed;
                var retry = status == 4;
                pending.Source.TrySetResult(SaveReadResult.Failed(SaveResult.Fail(error, message, retry)));
            }
        }

        /// <summary>
        /// 接收浏览器存储操作的最终事件。
        /// </summary>
        /// <param name="id">操作编号。</param>
        /// <param name="status">浏览器状态码。</param>
        /// <param name="value">临时 UTF-8 内容指针。</param>
        private delegate void NativeCompletion(int id, int status, IntPtr value);

        private readonly struct Pending
        {
            public UniTaskCompletionSource<SaveReadResult> Source { get; }
            public bool Read { get; }
            public bool Initialize { get; }

            /// <summary>
            /// 保存回调上下文。
            /// </summary>
            /// <param name="source">完成源。</param>
            /// <param name="read">是否读取。</param>
            /// <param name="initialize">是否初始化。</param>
            public Pending(UniTaskCompletionSource<SaveReadResult> source, bool read, bool initialize)
            {
                Source = source;
                Read = read;
                Initialize = initialize;
            }
        }

        /// <summary>
        /// 打开浏览器数据库。
        /// </summary>
        /// <param name="id">操作编号。</param>
        /// <param name="database">数据库名称。</param>
        /// <param name="completion">最终事件回调。</param>
        [DllImport("__Internal")] private static extern void GFSave_Init(int id, string database, NativeCompletion completion);
        /// <summary>
        /// 在浏览器事务中读取记录。
        /// </summary>
        /// <param name="id">操作编号。</param>
        /// <param name="database">数据库名称。</param>
        /// <param name="key">逻辑键。</param>
        /// <param name="completion">最终事件回调。</param>
        [DllImport("__Internal")] private static extern void GFSave_Read(int id, string database, string key, NativeCompletion completion);
        /// <summary>
        /// 在浏览器事务中写入记录。
        /// </summary>
        /// <param name="id">操作编号。</param>
        /// <param name="database">数据库名称。</param>
        /// <param name="key">逻辑键。</param>
        /// <param name="record">完整记录。</param>
        /// <param name="completion">最终事件回调。</param>
        [DllImport("__Internal")] private static extern void GFSave_Write(int id, string database, string key, string record, NativeCompletion completion);
    }
}
#endif
