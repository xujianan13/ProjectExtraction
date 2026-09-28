using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using YooAsset;

namespace GameFramework.Tests.Modules.Audio
{
    /// <summary>
    /// 验证音量边界与异步加载后的音源槽位复用。
    /// </summary>
    [TestFixture]
    public sealed class AudioModuleTests
    {
        private AudioModule _module;
        private FakeResourceBackend _backend;
        private readonly List<AudioClip> _clips = new();

        /// <summary>
        /// 注册真实资源模块与可控底层加载操作。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            _backend = new FakeResourceBackend();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<ITimerModule>(new TimerModule());
            GameEntry.Register<IResourceModule>(new ResourceModule(_backend, new ResourceCacheSettings(), FakeResourceBackend.PackageName));
            _module = new AudioModule();
            GameEntry.Register<IAudioModule>(_module);
            GameEntry.StartFramework();
        }

        /// <summary>
        /// 关闭框架并释放测试音频片段。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
            foreach (AudioClip clip in _clips) UnityEngine.Object.DestroyImmediate(clip);
            _clips.Clear();
        }

        /// <summary>
        /// 主音量应限制在零到一之间。
        /// </summary>
        [Test]
        public void MasterVolume_ClampsToZeroOne()
        {
            _module.MasterVolume = 2f;
            Assert.AreEqual(1f, _module.MasterVolume);
            _module.MasterVolume = -1f;
            Assert.AreEqual(0f, _module.MasterVolume);
        }

        /// <summary>
        /// 通道音量应限制在零到一之间。
        /// </summary>
        [Test]
        public void SetChannelVolume_ClampsToZeroOne()
        {
            _module.SetChannelVolume(AudioChannel.Sfx, 1.5f);
            Assert.AreEqual(1f, _module.GetChannelVolume(AudioChannel.Sfx));
            _module.SetChannelVolume(AudioChannel.Sfx, -0.5f);
            Assert.AreEqual(0f, _module.GetChannelVolume(AudioChannel.Sfx));
        }

        /// <summary>
        /// 通道音量默认值为一。
        /// </summary>
        [Test]
        public void GetChannelVolume_DefaultIsOne()
        {
            Assert.AreEqual(1f, _module.GetChannelVolume(AudioChannel.Music));
        }

        /// <summary>
        /// 旧请求因停止而取消后，复用的槽位仍能完成新请求。
        /// </summary>
        [Test]
        public void StaleLoadCompletion_DoesNotReleaseReusedSlot()
        {
            UniTask<AudioHandle> oldTask = _module.PlayAsync("old", AudioChannel.Sfx);
            _module.StopChannel(AudioChannel.Sfx);
            UniTask<AudioHandle> newTask = _module.PlayAsync("new", AudioChannel.Sfx);
            Assert.IsFalse(oldTask.GetAwaiter().GetResult().IsValid);
            _backend.GetLatest("old").Succeed(null);

            AudioClip clip = AudioClip.Create("new", 44100, 1, 44100, false);
            _clips.Add(clip);
            _backend.GetLatest("new").Succeed(clip);
            AudioHandle handle = newTask.GetAwaiter().GetResult();
            Assert.IsTrue(handle.IsValid, "旧请求不得清除新请求的句柄 ID。");
            _module.Stop(handle);
        }
        private sealed class FakeResourceBackend : IResourceAssetBackend
        {
            internal const string PackageName = "AudioTestPackage";
            private readonly Dictionary<string, FakeResourceOperation> _operations = new(StringComparer.Ordinal);

            /// <summary>
            /// 判断测试资源包是否可用。
            /// </summary>
            /// <param name="packageName">资源包名。</param>
            /// <returns>资源包是否匹配测试包。</returns>
            public bool IsPackageReady(string packageName) => packageName == PackageName;

            /// <summary>
            /// 创建由测试控制完成时间的加载操作。
            /// </summary>
            /// <typeparam name="T">资源类型。</typeparam>
            /// <param name="packageName">资源包名。</param>
            /// <param name="location">资源地址。</param>
            /// <returns>待完成的加载操作。</returns>
            public IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object
            {
                var operation = new FakeResourceOperation();
                _operations.Add(location, operation);
                return operation;
            }

            /// <summary>
            /// 模拟清理测试资源包。
            /// </summary>
            /// <param name="packageName">资源包名。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成任务。</returns>
            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct) => UniTask.CompletedTask;

            /// <summary>
            /// 获取指定地址创建的加载操作。
            /// </summary>
            /// <param name="location">资源地址。</param>
            /// <returns>加载操作。</returns>
            public FakeResourceOperation GetLatest(string location) => _operations[location];
        }

        private sealed class FakeResourceOperation : IResourceAssetOperation
        {
            private readonly UniTaskCompletionSource _completion = new();

            /// <summary>
            /// 操作释放前始终有效。
            /// </summary>
            public bool IsValid => !Released;

            /// <summary>
            /// 加载是否完成。
            /// </summary>
            public bool IsDone { get; private set; }

            /// <summary>
            /// 加载进度。
            /// </summary>
            public float Progress { get; private set; }

            /// <summary>
            /// 操作状态。
            /// </summary>
            public EOperationStatus Status { get; private set; }

            /// <summary>
            /// 错误说明。
            /// </summary>
            public string Error { get; private set; } = string.Empty;

            /// <summary>
            /// 加载的资源。
            /// </summary>
            public UnityEngine.Object Asset { get; private set; }

            /// <summary>
            /// 操作是否已释放。
            /// </summary>
            public bool Released { get; private set; }

            /// <summary>
            /// 等待测试控制操作完成。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>加载等待任务。</returns>
            public UniTask WaitAsync(CancellationToken ct) => ct.CanBeCanceled ? _completion.Task.AttachExternalCancellation(ct) : _completion.Task;

            /// <summary>
            /// 标记操作为已释放。
            /// </summary>
            public void Release() => Released = true;

            /// <summary>
            /// 完成资源加载。
            /// </summary>
            /// <param name="asset">加载的资源。</param>
            public void Succeed(UnityEngine.Object asset)
            {
                Asset = asset;
                Progress = 1f;
                Status = EOperationStatus.Succeeded;
                IsDone = true;
                _completion.TrySetResult();
            }

            /// <summary>
            /// 将资源加载标记为失败。
            /// </summary>
            /// <param name="error">失败说明。</param>
            public void Fail(string error)
            {
                Error = error;
                Progress = 1f;
                Status = EOperationStatus.Failed;
                IsDone = true;
                _completion.TrySetResult();
            }
        }
    }
}
