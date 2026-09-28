using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Cysharp.Threading.Tasks;
using UnityEngine.TestTools;
using NUnit.Framework;

namespace GameFramework.Tests
{
    /// <summary>
    /// 文件、内存和平台后端的持久化边界测试。
    /// </summary>
    public sealed class SaveBackendTests
    {
        private string root;

        /// <summary>
        /// 为每个测试创建独立文件根目录。
        /// </summary>
        [SetUp]
        public void SetUp() => root = Path.Combine(Path.GetTempPath(), "GameFrameworkSaveTests", Guid.NewGuid().ToString("N"));

        /// <summary>
        /// 清理本次测试创建的文件。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        /// <summary>
        /// 验证缺档与不同实例的实际文件读回。
        /// </summary>
        /// <returns>等待异步文件操作的协程。</returns>
        [UnityTest]
        public IEnumerator FileBackendDistinguishesMissingAndPersistsAcrossInstances()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                var first = new FileSaveBackend(root);
                Assert.IsTrue((await first.InitializeAsync()).Success);
                Assert.AreEqual(SaveReadStatus.Missing, (await first.ReadAsync("saves/v1/game/dev/guest/main/a")).Status);
                Assert.IsTrue((await first.WriteAsync("saves/v1/game/dev/guest/main/a", "记录😀")).Success);

                var second = new FileSaveBackend(root);
                Assert.AreEqual(first.StorageIdentity, second.StorageIdentity);
                Assert.AreEqual("记录😀", (await second.ReadAsync("saves/v1/game/dev/guest/main/a")).Record);
            });
        }

        /// <summary>
        /// 验证路径边界、记录上限与单槽替换隔离。
        /// </summary>
        /// <returns>等待异步文件操作的协程。</returns>
        [UnityTest]
        public IEnumerator FileBackendRejectsTraversalAndOversizeWithoutDamagingOtherRecord()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                var backend = new FileSaveBackend(root, 16);
                Assert.IsTrue((await backend.InitializeAsync()).Success);
                Assert.IsTrue((await backend.WriteAsync("slot/a", "old")).Success);
                Assert.IsTrue((await backend.WriteAsync("slot/b", "safe")).Success);
                Assert.AreEqual(SaveError.InvalidArgument, (await backend.WriteAsync("../escape", "bad")).Error);
                Assert.IsTrue((await backend.WriteAsync("slot/a", "new")).Success);
                Assert.AreEqual(SaveError.TooLarge, (await backend.WriteAsync("slot/a", new string('x', 17))).Error);
                Assert.AreEqual("new", (await backend.ReadAsync("slot/a")).Record);
                Assert.AreEqual("safe", (await backend.ReadAsync("slot/b")).Record);
            });
        }

        /// <summary>
        /// 验证临时文件、非法编码和根目录丢失的读取结果。
        /// </summary>
        /// <returns>等待异步文件操作的协程。</returns>
        [UnityTest]
        public IEnumerator FileBackendIgnoresIncompleteTemporaryFile()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                var backend = new FileSaveBackend(root);
                Assert.IsTrue((await backend.InitializeAsync()).Success);
                using var sha = SHA256.Create();
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes("slot/a"))).Replace("-", string.Empty).ToLowerInvariant();
                File.WriteAllText(Path.Combine(root, hash + ".save.abcd.tmp"), "uncommitted");
                Assert.AreEqual(SaveReadStatus.Missing, (await backend.ReadAsync("slot/a")).Status);
                var recordPath = Path.Combine(root, hash + ".save");
                File.WriteAllBytes(recordPath, new byte[] { 0xC3, 0x28 });
                var corrupt = await backend.ReadAsync("slot/a");
                Assert.AreEqual(SaveReadStatus.Failed, corrupt.Status);
                Assert.AreEqual(SaveError.Corrupt, corrupt.Result.Error);
                File.Delete(recordPath);
                Directory.Delete(root, true);
                var missingRoot = await backend.ReadAsync("slot/a");
                Assert.AreEqual(SaveReadStatus.Failed, missingRoot.Status);
                Assert.AreEqual(SaveError.ReadFailed, missingRoot.Result.Error);
            });
        }

        /// <summary>
        /// 验证内存上限及未注入 SDK 时的明确失败。
        /// </summary>
        /// <returns>等待异步操作的协程。</returns>
        [UnityTest]
        public IEnumerator MemoryAndMissingPlatformDoNotReportFalseSuccess()
        {
            yield return UniTask.ToCoroutine(async () =>
            {
                var memory = new MemorySaveBackend(maxRecordBytes: 4);
                Assert.AreEqual(SaveReadStatus.Missing, (await memory.ReadAsync("a")).Status);
                Assert.AreEqual(SaveError.TooLarge, (await memory.WriteAsync("a", "abcde")).Error);
                Assert.IsTrue((await memory.WriteAsync("a", "abc")).Success);
                Assert.AreEqual("abc", (await memory.ReadAsync("a")).Record);
                var platform = new PlatformSaveBackend(null);
                Assert.AreEqual(SaveError.Unsupported, (await platform.InitializeAsync()).Error);
                Assert.AreEqual(SaveReadStatus.Failed, (await platform.ReadAsync("a")).Status);
                Assert.AreEqual(SaveError.Unsupported, (await platform.WriteAsync("a", "record")).Error);
            });
        }
    }
}
