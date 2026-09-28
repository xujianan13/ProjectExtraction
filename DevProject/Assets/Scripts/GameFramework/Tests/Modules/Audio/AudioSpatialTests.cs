using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace GameFramework.Tests.Modules.Audio
{
    /// <summary>
    /// 验证空间音频参数、音源复用、并发加载与生命周期所有权。
    /// </summary>
    [TestFixture]
    public sealed class AudioSpatialTests
    {
        private AudioModule _module;
        private FakeResourceBackend _backend;
        private readonly List<AudioClip> _clips = new();
        private readonly List<GameObject> _targets = new();

        /// <summary>
        /// 注册真实音频模块和由测试控制完成时间的资源后端。
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
        /// 释放测试创建的音频片段与跟随目标。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
            foreach (GameObject target in _targets)
                if (target != null) UnityEngine.Object.DestroyImmediate(target);
            foreach (AudioClip clip in _clips)
                if (clip != null) UnityEngine.Object.DestroyImmediate(clip);
            _targets.Clear();
            _clips.Clear();
        }

        /// <summary>
        /// 加载中的设置应在请求发起时快照，并完整应用到真实音源。
        /// </summary>
        [Test]
        public void PlayAsync_SnapshotsSpatialOptionsAndAppliesSourceParameters()
        {
            var curve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.2f));
            var options = new AudioPlayOptions
            {
                Loop = true, Volume = 0.4f, Pitch = 1.25f, SpatialBlend = 0.8f,
                Position = new Vector3(2f, 3f, 4f), MinDistance = 3f, MaxDistance = 30f, RolloffMode = AudioRolloffMode.Custom,
                CustomRolloff = curve, DopplerLevel = 0.3f, Spread = 45f, Priority = 42
            };
            Vector3 position = new Vector3(2f, 3f, 4f);
            UniTask<AudioHandle> task = _module.PlayAsync("spatial", AudioChannel.Sfx, options);
            options.Volume = 0.9f;
            options.SpatialBlend = 0f;
            options.MinDistance = 9f;
            curve.MoveKey(1, new Keyframe(1f, 0.9f));

            AudioClip clip = CreateClip("spatial");
            _backend.GetLatest("spatial").Succeed(clip);
            AudioHandle handle = task.GetAwaiter().GetResult();
            AudioSource source = FindSource(clip);
            Assert.IsTrue(handle.IsValid);
            Assert.IsNotNull(source);
            Assert.AreEqual(position, source.transform.position);
            Assert.AreEqual(0.8f, source.spatialBlend, 0.001f);
            Assert.AreEqual(0.4f, source.volume, 0.001f);
            Assert.AreEqual(1.25f, source.pitch, 0.001f);
            Assert.AreEqual(3f, source.minDistance, 0.001f);
            Assert.AreEqual(30f, source.maxDistance, 0.001f);
            Assert.AreEqual(AudioRolloffMode.Custom, source.rolloffMode);
            Assert.AreEqual(0.2f, source.GetCustomCurve(AudioSourceCurveType.CustomRolloff).Evaluate(1f), 0.001f);
            Assert.AreEqual(0.3f, source.dopplerLevel, 0.001f);
            Assert.AreEqual(45f, source.spread, 0.001f);
            Assert.AreEqual(42, source.priority);
            _module.Stop(handle);
        }

        /// <summary>
        /// 跟随偏移应按目标本地坐标转换，目标销毁应结束默认播放。
        /// </summary>
        [Test]
        public void FollowTarget_TracksLocalOffsetAndStopsWhenDestroyed()
        {
            GameObject target = CreateTarget("follow");
            target.transform.position = new Vector3(3f, 0f, 2f);
            target.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var options = new AudioPlayOptions { Loop = true, FollowOffset = new Vector3(1f, 0f, 0f) };
            UniTask<AudioHandle> task = _module.PlayFollowAsync("follow", target.transform, options: options);
            AudioClip clip = CreateClip("follow");
            _backend.GetLatest("follow").Succeed(clip);
            AudioHandle handle = task.GetAwaiter().GetResult();
            AudioSource source = FindSource(clip);
            Assert.AreEqual(target.transform.TransformPoint(options.FollowOffset), source.transform.position);

            target.transform.position = new Vector3(7f, 1f, -2f);
            _module.OnUpdate(0.016f);
            Assert.AreEqual(target.transform.TransformPoint(options.FollowOffset), source.transform.position);
            UnityEngine.Object.DestroyImmediate(target);
            _module.OnUpdate(0.016f);
            Assert.IsFalse(_module.IsPlaying(handle));
            Assert.AreEqual(0, _module.GetActiveCount(AudioChannel.Sfx));
        }

        /// <summary>
        /// 目标销毁后保留最后位置的配置应允许播放继续。
        /// </summary>
        [Test]
        public void FollowTarget_OptionalTargetLossKeepsLastPosition()
        {
            GameObject target = CreateTarget("persistent");
            target.transform.position = new Vector3(4f, 5f, 6f);
            var options = new AudioPlayOptions { Loop = true, StopWhenTargetDestroyed = false };
            UniTask<AudioHandle> task = _module.PlayFollowAsync("persistent", target.transform, options: options);
            AudioClip clip = CreateClip("persistent");
            _backend.GetLatest("persistent").Succeed(clip);
            AudioHandle handle = task.GetAwaiter().GetResult();
            AudioSource source = FindSource(clip);
            Vector3 lastPosition = source.transform.position;
            UnityEngine.Object.DestroyImmediate(target);
            _module.OnUpdate(0.016f);
            Assert.IsTrue(_module.IsPlaying(handle));
            Assert.AreEqual(lastPosition, source.transform.position);
            _module.Stop(handle);
        }

        /// <summary>
        /// 复用空间音源播放旧接口的二维音效时必须清除上一轮的设置。
        /// </summary>
        [Test]
        public void ReusedSpatialSource_ResetsToLegacyTwoDimensionalDefaults()
        {
            var options = new AudioPlayOptions
            {
                Loop = true, Volume = 0.3f, Pitch = 1.5f, SpatialBlend = 1f,
                Position = new Vector3(9f, 8f, 7f), MinDistance = 5f, MaxDistance = 60f,
                RolloffMode = AudioRolloffMode.Linear, DopplerLevel = 0f, Spread = 120f, Priority = 10
            };
            AudioClip firstClip = CreateClip("first");
            UniTask<AudioHandle> firstTask = _module.PlayAsync("first", AudioChannel.Sfx, options);
            _backend.GetLatest("first").Succeed(firstClip);
            AudioHandle first = firstTask.GetAwaiter().GetResult();
            AudioSource reused = FindSource(firstClip);
            _module.Stop(first);

            AudioClip secondClip = CreateClip("second");
            UniTask<AudioHandle> secondTask = _module.PlayAsync("second", AudioChannel.Sfx);
            _backend.GetLatest("second").Succeed(secondClip);
            AudioHandle second = secondTask.GetAwaiter().GetResult();
            AudioSource source = FindSource(secondClip);
            Assert.AreSame(reused, source);
            Assert.AreEqual(0f, source.spatialBlend, 0.001f);
            Assert.IsFalse(source.loop);
            Assert.AreEqual(1f, source.pitch, 0.001f);
            Assert.AreEqual(1f, source.volume, 0.001f);
            Assert.AreEqual(AudioRolloffMode.Logarithmic, source.rolloffMode);
            Assert.AreEqual(1f, source.minDistance, 0.001f);
            Assert.AreEqual(50f, source.maxDistance, 0.001f);
            Assert.AreEqual(1f, source.dopplerLevel, 0.001f);
            Assert.AreEqual(0f, source.spread, 0.001f);
            Assert.AreEqual(128, source.priority);
            Assert.IsNull(source.outputAudioMixerGroup);
            _module.Stop(second);
        }

        /// <summary>
        /// 语音上限应计入加载请求，抢占后的旧完成不得清除新槽位。
        /// </summary>
        [Test]
        public void VoiceLimit_IncludesLoadingAndLateStolenCompletionCannotReplaceNewVoice()
        {
            _module.SetVoiceLimit(AudioChannel.Sfx, 1, AudioVoiceOverflowPolicy.StealOldest);
            UniTask<AudioHandle> oldTask = _module.PlayAsync("old", AudioChannel.Sfx, new AudioPlayOptions { Loop = true });
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));
            UniTask<AudioHandle> newTask = _module.PlayAsync("new", AudioChannel.Sfx, new AudioPlayOptions { Loop = true });
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));
            Assert.IsFalse(oldTask.GetAwaiter().GetResult().IsValid);
            _backend.GetLatest("old").Succeed(CreateClip("old"));

            AudioClip clip = CreateClip("new");
            _backend.GetLatest("new").Succeed(clip);
            AudioHandle current = newTask.GetAwaiter().GetResult();
            Assert.IsTrue(_module.IsPlaying(current));
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));
            Assert.AreSame(clip, FindSource(clip).clip);
            _module.Stop(current);
        }

        /// <summary>
        /// 抢占取消同步唤醒旧请求时，重入的新请求应保留唯一槽位。
        /// </summary>
        [Test]
        public void VoiceLimit_ReentrantPlayDuringStealKeepsSingleOwner()
        {
            _module.SetVoiceLimit(AudioChannel.Sfx, 1, AudioVoiceOverflowPolicy.StealOldest);
            UniTask<AudioHandle> oldTask = _module.PlayAsync("reentrant-old", AudioChannel.Sfx);
            UniTask<AudioHandle> chained = PlayAfterStoppedLoadAsync(oldTask, "reentrant-next");
            AudioHandle outer = _module.PlayAsync("reentrant-outer", AudioChannel.Sfx).GetAwaiter().GetResult();
            Assert.IsFalse(outer.IsValid);
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));

            _backend.GetLatest("reentrant-old").Succeed(CreateClip("reentrant-old"));
            AudioClip clip = CreateClip("reentrant-next");
            _backend.GetLatest("reentrant-next").Succeed(clip);
            AudioHandle current = chained.GetAwaiter().GetResult();
            Assert.IsTrue(_module.IsPlaying(current));
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));
            Assert.AreSame(clip, FindSource(clip).clip);
            _module.Stop(current);
        }

        /// <summary>
        /// StopAll 应清除加载中的作用域登记；迟到的资源完成不得恢复音源。
        /// </summary>
        [Test]
        public void StopAll_DuringOwnedLoadForgetsRegistrationAndLateCompletion()
        {
            using var scope = new LifetimeScope("stop all audio");
            UniTask<AudioHandle> task = scope.PlayAudioAsync(_module, "stop-all", AudioChannel.Sfx);
            UniTask<AudioHandle> reentrant = PlayAfterStoppedLoadAsync(task, "stop-all-reentrant");
            Assert.AreEqual(1, scope.RegistrationCount);
            _module.StopAll();
            Assert.IsFalse(reentrant.GetAwaiter().GetResult().IsValid);
            Assert.AreEqual(0, scope.RegistrationCount);
            Assert.AreEqual(0, _module.GetActiveCount(AudioChannel.Sfx));
            _backend.GetLatest("stop-all").Succeed(CreateClip("stop-all"));
            Assert.AreEqual(0, _module.GetActiveCount(AudioChannel.Sfx));
        }

        /// <summary>
        /// 拒绝策略应保留先进入加载的请求，调用方取消应抛出取消异常并释放槽位。
        /// </summary>
        [Test]
        public void VoiceLimit_RejectsNewAndCallerCancellationReleasesLoadingSlot()
        {
            _module.SetVoiceLimit(AudioChannel.Sfx, 1, AudioVoiceOverflowPolicy.RejectNew);
            using var cancellation = new CancellationTokenSource();
            UniTask<AudioHandle> pending = _module.PlayAsync("pending", AudioChannel.Sfx, ct: cancellation.Token);
            AudioHandle rejected = _module.PlayAsync("rejected", AudioChannel.Sfx).GetAwaiter().GetResult();
            Assert.IsFalse(rejected.IsValid);
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            Assert.AreEqual(0, _module.GetActiveCount(AudioChannel.Sfx));
        }

        /// <summary>
        /// 作用域停止应取消加载，释放已播放音源，并清除对应登记。
        /// </summary>
        [Test]
        public void ScopeOwner_StopsLoadingAndPlayingAndRemovesRegistrations()
        {
            using var loadingScope = new LifetimeScope("loading audio");
            UniTask<AudioHandle> loading = loadingScope.PlayAudioAsync(_module, "loading", AudioChannel.Sfx, options: new AudioPlayOptions { Loop = true });
            Assert.AreEqual(1, _module.GetActiveCount(AudioChannel.Sfx));
            loadingScope.Stop();
            Assert.IsFalse(loading.GetAwaiter().GetResult().IsValid);
            Assert.AreEqual(0, _module.GetActiveCount(AudioChannel.Sfx));
            Assert.AreEqual(0, loadingScope.RegistrationCount);

            using var playingScope = new LifetimeScope("playing audio");
            AudioClip clip = CreateClip("owned");
            UniTask<AudioHandle> task = playingScope.PlayAudioAsync(_module, "owned", AudioChannel.Sfx, options: new AudioPlayOptions { Loop = true });
            _backend.GetLatest("owned").Succeed(clip);
            AudioHandle handle = task.GetAwaiter().GetResult();
            Assert.IsTrue(_module.IsPlaying(handle));
            Assert.AreEqual(1, playingScope.RegistrationCount);
            playingScope.Dispose();
            Assert.IsFalse(_module.IsPlaying(handle));
            Assert.AreEqual(0, playingScope.RegistrationCount);
        }

        /// <summary>
        /// 短音效自然结束后应释放句柄和作用域登记。
        /// </summary>
        [UnityTest]
        public IEnumerator CompletedOneShot_ReleasesVoiceAndScopeRegistration()
        {
            using var scope = new LifetimeScope("one shot");
            AudioClip clip = CreateClip("short", 0.03f);
            UniTask<AudioHandle> task = scope.PlayAudioAsync(_module, "short", AudioChannel.Sfx);
            _backend.GetLatest("short").Succeed(clip);
            AudioHandle handle = task.GetAwaiter().GetResult();
            Assert.IsTrue(handle.IsValid);
            yield return new WaitForSecondsRealtime(0.15f);
            _module.OnUpdate(0.016f);
            Assert.IsFalse(_module.IsPlaying(handle));
            Assert.AreEqual(0, _module.GetActiveCount(AudioChannel.Sfx));
            Assert.AreEqual(0, scope.RegistrationCount);
        }

        /// <summary>
        /// 主音量与通道音量应叠加在播放音量上，淡变期间的暂停应冻结进度。
        /// </summary>
        [UnityTest]
        public IEnumerator Fade_UsesMixedVolumeAndFreezesWhilePaused()
        {
            _module.MasterVolume = 0.8f;
            _module.SetChannelVolume(AudioChannel.Sfx, 0.5f);
            using var scope = new LifetimeScope("fading audio");
            AudioClip clip = CreateClip("fade");
            var options = new AudioPlayOptions { Loop = true, Volume = 0.5f, FadeInSeconds = 0.1f, Owner = scope };
            UniTask<AudioHandle> task = _module.PlayAsync("fade", AudioChannel.Sfx, options);
            _backend.GetLatest("fade").Succeed(clip);
            AudioHandle handle = task.GetAwaiter().GetResult();
            AudioSource source = FindSource(clip);
            Assert.AreEqual(0f, source.volume, 0.001f);
            yield return TickAudioFor(0.15f);
            Assert.AreEqual(0.2f, source.volume, 0.001f);

            _module.FadeTo(handle, 0f, 0.2f);
            _module.PauseAll();
            yield return new WaitForSecondsRealtime(0.25f);
            _module.OnUpdate(0.016f);
            Assert.IsTrue(_module.IsPlaying(handle));
            Assert.AreEqual(0.2f, source.volume, 0.01f);
            _module.SetApplicationPaused(true);
            _module.ResumeAll();
            yield return TickAudioFor(0.1f);
            Assert.AreEqual(0.2f, source.volume, 0.001f);
            Assert.IsTrue(_module.IsPlaying(handle));
            _module.SetApplicationPaused(false);
            yield return TickAudioFor(0.25f);
            Assert.AreEqual(0f, source.volume, 0.001f);
            Assert.IsTrue(_module.IsPlaying(handle));

            _module.FadeTo(handle, 0.5f, 0f);
            _module.Stop(handle, 0.1f);
            Assert.IsTrue(_module.IsPlaying(handle));
            yield return TickAudioFor(0.15f);
            Assert.IsFalse(_module.IsPlaying(handle));
            Assert.IsNull(source.clip);
            Assert.AreEqual(0, scope.RegistrationCount);
        }

        /// <summary>
        /// 按实际时间逐帧更新音频，避免无图形模式的帧率影响断言。
        /// </summary>
        /// <param name="seconds">需要推进的实际秒数。</param>
        /// <returns>逐帧推进的协程。</returns>
        private IEnumerator TickAudioFor(float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                _module.OnUpdate(0f);
            }
        }

        /// <summary>
        /// 等待被抢占的请求结束后，立即在取消回调中发起新播放。
        /// </summary>
        /// <param name="oldTask">将由抢占终止的请求。</param>
        /// <param name="address">重入请求的资源地址。</param>
        /// <returns>重入请求的播放句柄。</returns>
        private async UniTask<AudioHandle> PlayAfterStoppedLoadAsync(UniTask<AudioHandle> oldTask, string address)
        {
            AudioHandle old = await oldTask;
            Assert.IsFalse(old.IsValid);
            return await _module.PlayAsync(address, AudioChannel.Sfx);
        }

        /// <summary>
        /// 创建供测试播放的音频片段。
        /// </summary>
        /// <param name="name">片段名称。</param>
        /// <param name="seconds">片段时长。</param>
        /// <returns>需要在清理阶段释放的片段。</returns>
        private AudioClip CreateClip(string name, float seconds = 2f)
        {
            AudioClip clip = AudioClip.Create(name, Mathf.CeilToInt(seconds * 44100f), 1, 44100, false);
            _clips.Add(clip);
            return clip;
        }

        /// <summary>
        /// 创建测试拥有的跟随目标。
        /// </summary>
        /// <param name="name">目标名称。</param>
        /// <returns>跟随目标。</returns>
        private GameObject CreateTarget(string name)
        {
            var target = new GameObject(name);
            _targets.Add(target);
            return target;
        }

        /// <summary>
        /// 按播放片段寻找真实音源。
        /// </summary>
        /// <param name="clip">目标片段。</param>
        /// <returns>当前播放该片段的音源。</returns>
        private static AudioSource FindSource(AudioClip clip)
        {
            foreach (AudioSource source in UnityEngine.Object.FindObjectsOfType<AudioSource>())
                if (source.clip == clip) return source;
            return null;
        }

        private sealed class FakeResourceBackend : IResourceAssetBackend
        {
            internal const string PackageName = "AudioSpatialTestPackage";
            private readonly Dictionary<string, FakeResourceOperation> _operations = new(StringComparer.Ordinal);

            /// <summary>
            /// 判断测试资源包是否可用。
            /// </summary>
            /// <param name="packageName">资源包名称。</param>
            /// <returns>是否匹配测试包。</returns>
            public bool IsPackageReady(string packageName) => packageName == PackageName;

            /// <summary>
            /// 创建由测试控制完成时间的资源操作。
            /// </summary>
            /// <typeparam name="T">资源类型。</typeparam>
            /// <param name="packageName">资源包名称。</param>
            /// <param name="location">资源地址。</param>
            /// <returns>待完成的资源操作。</returns>
            public IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object
            {
                var operation = new FakeResourceOperation();
                _operations.Add(location, operation);
                return operation;
            }

            /// <summary>
            /// 模拟清理资源包。
            /// </summary>
            /// <param name="packageName">资源包名称。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成任务。</returns>
            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct) => UniTask.CompletedTask;

            /// <summary>
            /// 获取指定资源地址的加载操作。
            /// </summary>
            /// <param name="location">资源地址。</param>
            /// <returns>加载操作。</returns>
            public FakeResourceOperation GetLatest(string location) => _operations[location];
        }

        private sealed class FakeResourceOperation : IResourceAssetOperation
        {
            private readonly UniTaskCompletionSource _completion = new();

            /// <summary>
            /// 资源操作尚未释放时有效。
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
            /// 加载状态。
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
            /// 标记操作已释放。
            /// </summary>
            public void Release() => Released = true;

            /// <summary>
            /// 完成资源加载。
            /// </summary>
            /// <param name="asset">加载到的资源。</param>
            public void Succeed(UnityEngine.Object asset)
            {
                Asset = asset;
                Progress = 1f;
                Status = EOperationStatus.Succeeded;
                IsDone = true;
                _completion.TrySetResult();
            }
        }
    }
}
