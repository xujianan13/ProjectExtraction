using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;

namespace GameFramework
{
    /// <summary>
    /// 复用有并发上限的音源，统一管理异步资源租约、空间播放、淡变和生命周期。
    /// 所有公开操作须在 Unity 主线程调用。
    /// </summary>
    public sealed class AudioModule : ModuleBase, IAudioModule
    {
        private const string Tag = "Audio";
        private const int ChannelCount = 3;
        private static readonly int[] InitialSlotCount = { 2, 8, 4 };
        private static readonly AnimationCurve DefaultRolloff = AnimationCurve.Linear(0f, 1f, 1f, 0f);
        private readonly List<Slot>[] _slots = new List<Slot>[ChannelCount];
        private readonly float[] _channelVolumes = { 1f, 1f, 1f };
        private readonly int[] _voiceLimits = { 2, 32, 8 };
        private readonly AudioVoiceOverflowPolicy[] _overflowPolicies =
        {
            AudioVoiceOverflowPolicy.StealOldest, AudioVoiceOverflowPolicy.StealOldest, AudioVoiceOverflowPolicy.StealOldest
        };
        private readonly bool[] _stoppingChannels = new bool[ChannelCount];
        private bool _stoppingAll;
        private readonly AudioMixerGroup[] _mixerGroups = new AudioMixerGroup[ChannelCount];
        private float _masterVolume = 1f;
        private int _nextHandleId = 1;
        private long _nextSequence;
        private bool _isPaused;
        private bool _isApplicationPaused;
        private bool _sourcesPaused;
        private GameObject _host;
        private ILogModule _log;
        private IResourceModule _resource;

        /// <summary>
        /// 在资源模块之后初始化。
        /// </summary>
        public override int Priority => 30;

        /// <summary>
        /// 业务是否主动暂停全部音频。
        /// </summary>
        public bool IsPaused => _isPaused;

        /// <summary>
        /// 应用是否因切后台暂停全部音频。
        /// </summary>
        public bool IsApplicationPaused => _isApplicationPaused;

        /// <summary>
        /// 主音量，立即与通道音量和单次播放音量相乘。
        /// </summary>
        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = ClampFinite(value, 0f, 1f, 1f);
                for (int c = 0; c < ChannelCount; c++) ApplyVolumeChannel((AudioChannel)c);
            }
        }

        /// <summary>
        /// 创建跨场景音源宿主和初始音源池。
        /// </summary>
        public override void OnInit()
        {
            _log = GameEntry.Get<ILogModule>();
            _resource = GameEntry.Get<IResourceModule>();
            if (_resource == null) _log?.Error(Tag, "资源模块未注册，无法加载音频。");
            _host = new GameObject("[AudioHost]");
            UnityEngine.Object.DontDestroyOnLoad(_host);
            for (int c = 0; c < ChannelCount; c++)
            {
                var list = new List<Slot>(InitialSlotCount[c]);
                for (int i = 0; i < Mathf.Min(InitialSlotCount[c], _voiceLimits[c]); i++) list.Add(CreateSlot((AudioChannel)c));
                _slots[c] = list;
            }
        }

        /// <summary>
        /// 跟随空间目标，使用非缩放时间驱动淡变，并根据音源实际播放状态回收短音频。
        /// </summary>
        /// <param name="deltaTime">框架缩放时间增量；音频改用 Unity 非缩放时间。</param>
        public override void OnUpdate(float deltaTime)
        {
            bool paused = _sourcesPaused || AudioListener.pause;
            float elapsed = Time.unscaledDeltaTime;
            for (int c = 0; c < ChannelCount; c++)
            {
                var list = _slots[c];
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    var slot = list[i];
                    if (slot.State == SlotState.Idle) continue;
                    if (slot.Source == null || (slot.Options.Owner != null && !slot.Options.Owner.IsActive) || !UpdatePosition(slot))
                    {
                        ReleaseSlot(slot);
                        continue;
                    }
                    if (slot.State != SlotState.Playing || paused) continue;
                    if (slot.FadeDuration > 0f)
                    {
                        slot.FadeElapsed += elapsed;
                        float progress = Mathf.Clamp01(slot.FadeElapsed / slot.FadeDuration);
                        slot.Volume = Mathf.Lerp(slot.FadeStart, slot.FadeTarget, progress);
                        ApplyVolume(slot);
                        if (progress >= 1f)
                        {
                            slot.FadeDuration = 0f;
                            if (slot.StopAfterFade)
                            {
                                ReleaseSlot(slot);
                                continue;
                            }
                        }
                    }
                    if (!slot.Source.loop && Time.frameCount > slot.StartFrame && !slot.Source.isPlaying) ReleaseSlot(slot);
                }
            }
        }

        /// <summary>
        /// 取消加载，停止播放，释放租约和作用域登记并销毁音源宿主。
        /// </summary>
        public override void OnShutdown()
        {
            _resource = null;
            StopAll();
            _isPaused = false;
            _isApplicationPaused = false;
            _sourcesPaused = false;
            if (_host != null) UnityEngine.Object.Destroy(_host);
            _host = null;
            for (int c = 0; c < ChannelCount; c++)
            {
                _slots[c] = null;
                _mixerGroups[c] = null;
            }
            _log = null;
        }

        /// <summary>
        /// 获取通道音量。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <returns>通道音量；无效通道返回零。</returns>
        public float GetChannelVolume(AudioChannel channel) => IsChannelValid(channel) ? _channelVolumes[(int)channel] : 0f;

        /// <summary>
        /// 设置通道音量，并立即更新正在播放的音源。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <param name="volume">零到一的通道音量。</param>
        public void SetChannelVolume(AudioChannel channel, float volume)
        {
            if (!IsChannelValid(channel)) return;
            _channelVolumes[(int)channel] = ClampFinite(volume, 0f, 1f, 1f);
            ApplyVolumeChannel(channel);
        }

        /// <summary>
        /// 获取通道默认混音组。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <returns>默认混音组；空表示直接输出。</returns>
        public AudioMixerGroup GetChannelMixerGroup(AudioChannel channel) => IsChannelValid(channel) ? _mixerGroups[(int)channel] : null;

        /// <summary>
        /// 更新通道混音组，不覆盖单次播放显式指定的混音组。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <param name="group">混音组；空表示直接输出。</param>
        public void SetChannelMixerGroup(AudioChannel channel, AudioMixerGroup group)
        {
            if (!IsChannelValid(channel)) return;
            _mixerGroups[(int)channel] = group;
            var list = _slots[(int)channel];
            if (list == null) return;
            foreach (var slot in list)
                if (slot.State == SlotState.Playing && slot.Options.OutputGroup == null) slot.Source.outputAudioMixerGroup = group;
        }

        /// <summary>
        /// 获取加载中与播放中音频的总数。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <returns>正在占用通道名额的数量。</returns>
        public int GetActiveCount(AudioChannel channel)
        {
            if (!IsChannelValid(channel) || _slots[(int)channel] == null) return 0;
            int count = 0;
            foreach (var slot in _slots[(int)channel])
                if (slot.State != SlotState.Idle) count++;
            return count;
        }

        /// <summary>
        /// 获取通道并发上限。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <returns>上限；无效通道返回零。</returns>
        public int GetVoiceLimit(AudioChannel channel) => IsChannelValid(channel) ? _voiceLimits[(int)channel] : 0;

        /// <summary>
        /// 设置并发上限；调低时立即停止超出的最早请求。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <param name="maxVoices">大于零的并发上限。</param>
        /// <param name="policy">满额时拒绝新请求或替换最早请求。</param>
        /// <returns>配置是否有效并已应用。</returns>
        public bool SetVoiceLimit(AudioChannel channel, int maxVoices, AudioVoiceOverflowPolicy policy = AudioVoiceOverflowPolicy.StealOldest)
        {
            if (!IsChannelValid(channel) || maxVoices <= 0 ||
                (policy != AudioVoiceOverflowPolicy.RejectNew && policy != AudioVoiceOverflowPolicy.StealOldest)) return false;
            _voiceLimits[(int)channel] = maxVoices;
            _overflowPolicies[(int)channel] = policy;
            while (GetActiveCount(channel) > maxVoices) ReleaseSlot(FindOldest(channel));
            return true;
        }

        /// <summary>
        /// 使用原有二维播放方式加载音频。
        /// </summary>
        /// <param name="address">默认资源包中的音频地址。</param>
        /// <param name="channel">音频通道。</param>
        /// <param name="loop">是否循环播放。</param>
        /// <param name="ct">本次加载的取消令牌。</param>
        /// <returns>播放句柄；加载失败或请求被停止时返回无效句柄。</returns>
        public UniTask<AudioHandle> PlayAsync(string address, AudioChannel channel, bool loop = false, CancellationToken ct = default)
            => PlayAsync(address, channel, new AudioPlayOptions { Loop = loop }, ct);

        /// <summary>
        /// 快照播放配置，并在资源就绪后播放。调用方取消抛出取消异常，内部停止返回无效句柄。
        /// </summary>
        /// <param name="address">默认资源包中的音频地址。</param>
        /// <param name="channel">音频通道。</param>
        /// <param name="options">空间、混音和所有权配置；空表示默认二维播放。</param>
        /// <param name="ct">仅控制加载等待的取消令牌。</param>
        /// <returns>播放句柄；失败、满额或内部停止时返回无效句柄。</returns>
        public async UniTask<AudioHandle> PlayAsync(string address, AudioChannel channel, AudioPlayOptions options, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (_resource == null || _host == null || !IsChannelValid(channel)) return default;
            if (_stoppingAll || _stoppingChannels[(int)channel]) return default;
            if (string.IsNullOrWhiteSpace(address))
            {
                _log?.Warn(Tag, "音频地址不能为空。");
                return default;
            }
            var settings = Normalize(options);
            settings.Owner?.CheckThread();
            if (settings.Owner != null && !settings.Owner.IsActive) return default;
            bool following = !ReferenceEquals(settings.FollowTarget, null);
            if (following && settings.FollowTarget == null && settings.StopWhenTargetDestroyed) return default;
            var slot = AcquireSlot(channel);
            if (slot == null) return default;
            int handleId = NextHandleId();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(ct);
            slot.HandleId = handleId;
            slot.Sequence = ++_nextSequence;
            slot.State = SlotState.Loading;
            slot.Options = settings;
            slot.Following = following;
            slot.LoadCancellation = request;
            slot.Source.transform.position = settings.Position;
            UpdatePosition(slot);
            if (settings.Owner != null)
            {
                var ownership = settings.Owner.Defer(() => ReleaseSlotIfCurrent(slot, handleId));
                if (slot.HandleId != handleId) return default;
                slot.Ownership = ownership;
                var cancellation = settings.Owner.Token.Register(() => ReleaseSlotIfCurrent(slot, handleId));
                if (slot.HandleId == handleId) slot.OwnerCancellation = cancellation;
                else cancellation.Dispose();
            }

            try
            {
                if (slot.HandleId != handleId) return default;
                var lease = await _resource.AcquireAsync<AudioClip>(address, ct: request.Token);
                if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
                if (slot.HandleId != handleId || ct.IsCancellationRequested)
                {
                    lease?.Dispose();
                    ct.ThrowIfCancellationRequested();
                    return default;
                }
                slot.ClipLease = lease;
                if (lease?.Asset == null || (settings.Owner != null && !settings.Owner.IsActive) || !UpdatePosition(slot))
                {
                    ReleaseSlot(slot);
                    return default;
                }
                ConfigureSource(slot, lease.Asset);
                slot.State = SlotState.Playing;
                slot.StartFrame = Time.frameCount;
                slot.Source.Play();
                if (_sourcesPaused) slot.Source.Pause();
                return new AudioHandle(handleId, channel);
            }
            catch (OperationCanceledException)
            {
                if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
                ReleaseSlotIfCurrent(slot, handleId);
                ct.ThrowIfCancellationRequested();
                return default;
            }
            catch (Exception error)
            {
                if (!PlayerLoopHelper.IsMainThread) await UniTask.SwitchToMainThread();
                _log?.Error(Tag, $"音频加载或播放失败：{address}，{error.Message}");
                ReleaseSlotIfCurrent(slot, handleId);
                return default;
            }
            finally
            {
                if (slot.HandleId == handleId) slot.LoadCancellation = null;
            }
        }

        /// <summary>
        /// 查询句柄是否仍在播放或暂停。
        /// </summary>
        /// <param name="handle">音频句柄。</param>
        /// <returns>句柄仍持有播放中的音源时返回真，包括暂停状态。</returns>
        public bool IsPlaying(AudioHandle handle) => FindSlot(handle)?.State == SlotState.Playing;

        /// <summary>
        /// 立即停止音频并归还资源租约。
        /// </summary>
        /// <param name="handle">音频句柄。</param>
        public void Stop(AudioHandle handle)
        {
            var slot = FindSlot(handle);
            if (slot != null) ReleaseSlot(slot);
        }

        /// <summary>
        /// 淡出后停止音频；淡出期间仍占并发名额。
        /// </summary>
        /// <param name="handle">音频句柄。</param>
        /// <param name="fadeOutSeconds">淡出秒数；非正数立即停止。</param>
        public void Stop(AudioHandle handle, float fadeOutSeconds)
        {
            if (!FadeTo(handle, 0f, fadeOutSeconds, stopWhenComplete: true)) Stop(handle);
        }

        /// <summary>
        /// 从当前单次播放音量开始淡变，覆盖尚未完成的淡变。
        /// </summary>
        /// <param name="handle">音频句柄。</param>
        /// <param name="volume">零到一的目标播放音量。</param>
        /// <param name="duration">非缩放时间秒数；非正数立即应用。</param>
        /// <param name="stopWhenComplete">达到目标时是否释放音频。</param>
        /// <returns>句柄仍在播放或暂停且操作已接受时返回真。</returns>
        public bool FadeTo(AudioHandle handle, float volume, float duration, bool stopWhenComplete = false)
        {
            var slot = FindSlot(handle);
            if (slot == null || slot.State != SlotState.Playing) return false;
            slot.FadeStart = slot.Volume;
            slot.FadeTarget = ClampFinite(volume, 0f, 1f, 1f);
            slot.FadeDuration = ClampFinite(duration, 0f, float.MaxValue, 0f);
            slot.FadeElapsed = 0f;
            slot.StopAfterFade = stopWhenComplete;
            if (slot.FadeDuration <= 0f)
            {
                slot.Volume = slot.FadeTarget;
                if (stopWhenComplete) ReleaseSlot(slot);
                else ApplyVolume(slot);
            }
            return true;
        }

        /// <summary>
        /// 停止指定通道中的播放和加载。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        public void StopChannel(AudioChannel channel)
        {
            if (!IsChannelValid(channel) || _stoppingChannels[(int)channel]) return;
            var list = _slots[(int)channel];
            if (list == null) return;
            _stoppingChannels[(int)channel] = true;
            try
            {
                for (int i = 0; i < list.Count; i++)
                    if (list[i].State != SlotState.Idle) ReleaseSlot(list[i]);
            }
            finally { _stoppingChannels[(int)channel] = false; }
        }

        /// <summary>
        /// 停止全部通道中的播放和加载。
        /// </summary>
        public void StopAll()
        {
            if (_stoppingAll) return;
            _stoppingAll = true;
            try
            {
                for (int c = 0; c < ChannelCount; c++) StopChannel((AudioChannel)c);
            }
            finally { _stoppingAll = false; }
        }

        /// <summary>
        /// 设置业务暂停状态，同时冻结所有音频淡变。
        /// </summary>
        public void PauseAll()
        {
            _isPaused = true;
            ApplyPauseState();
        }

        /// <summary>
        /// 清除业务暂停状态；应用仍在后台时不恢复声音。
        /// </summary>
        public void ResumeAll()
        {
            _isPaused = false;
            ApplyPauseState();
        }

        /// <summary>
        /// 设置应用后台暂停状态，独立保留业务暂停状态。
        /// </summary>
        /// <param name="paused">应用是否进入后台。</param>
        public void SetApplicationPaused(bool paused)
        {
            _isApplicationPaused = paused;
            ApplyPauseState();
        }

        /// <summary>
        /// 将组合暂停状态应用到所有播放中的音源。
        /// </summary>
        private void ApplyPauseState()
        {
            bool shouldPause = _isPaused || _isApplicationPaused;
            if (_sourcesPaused == shouldPause) return;
            _sourcesPaused = shouldPause;
            for (int c = 0; c < ChannelCount; c++)
            {
                var list = _slots[c];
                if (list == null) continue;
                foreach (var slot in list)
                {
                    if (slot.State != SlotState.Playing || slot.Source == null) continue;
                    if (shouldPause) slot.Source.Pause();
                    else
                    {
                        slot.Source.UnPause();
                        slot.StartFrame = Time.frameCount;
                    }
                }
            }
        }

        /// <summary>
        /// 初始化每次播放的全部可配置音源属性，避免复用串音或残留空间设置。
        /// </summary>
        /// <param name="slot">音源槽位。</param>
        /// <param name="clip">已经加载的片段。</param>
        private void ConfigureSource(Slot slot, AudioClip clip)
        {
            var settings = slot.Options;
            var source = slot.Source;
            source.clip = clip;
            source.loop = settings.Loop;
            source.pitch = settings.Pitch;
            source.spatialBlend = settings.SpatialBlend;
            source.minDistance = settings.MinDistance;
            source.maxDistance = settings.MaxDistance;
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, settings.CustomRolloff ?? DefaultRolloff);
            source.rolloffMode = settings.RolloffMode;
            source.dopplerLevel = settings.DopplerLevel;
            source.spread = settings.Spread;
            source.priority = settings.Priority;
            source.outputAudioMixerGroup = settings.OutputGroup != null ? settings.OutputGroup : _mixerGroups[(int)slot.Channel];
            slot.Volume = settings.FadeInSeconds > 0f ? 0f : settings.Volume;
            slot.FadeStart = 0f;
            slot.FadeTarget = settings.Volume;
            slot.FadeDuration = settings.FadeInSeconds;
            slot.FadeElapsed = 0f;
            slot.StopAfterFade = false;
            ApplyVolume(slot);
        }

        /// <summary>
        /// 更新跟随位置；目标销毁时按配置停止或保留最后位置。
        /// </summary>
        /// <param name="slot">音源槽位。</param>
        /// <returns>请求是否可以继续保留。</returns>
        private static bool UpdatePosition(Slot slot)
        {
            if (!slot.Following) return true;
            if (slot.Options.FollowTarget != null)
            {
                slot.Source.transform.position = slot.Options.FollowTarget.TransformPoint(slot.Options.FollowOffset);
                return true;
            }
            if (slot.Options.StopWhenTargetDestroyed) return false;
            slot.Following = false;
            slot.Options.FollowTarget = null;
            return true;
        }

        /// <summary>
        /// 获取空闲槽位，满额时按策略拒绝或回收最早请求。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <returns>可用槽位；拒绝请求时返回空。</returns>
        private Slot AcquireSlot(AudioChannel channel)
        {
            var list = _slots[(int)channel];
            if (GetActiveCount(channel) >= _voiceLimits[(int)channel])
            {
                if (_overflowPolicies[(int)channel] == AudioVoiceOverflowPolicy.RejectNew) return null;
                var oldest = FindOldest(channel);
                ReleaseSlot(oldest);
                if (_resource == null || _host == null || _slots[(int)channel] != list || GetActiveCount(channel) >= _voiceLimits[(int)channel])
                    return null;
            }
            foreach (var slot in list)
            {
                if (slot.State != SlotState.Idle) continue;
                if (slot.Source == null) slot.Source = CreateSlot(channel).Source;
                return slot;
            }
            var created = CreateSlot(channel);
            list.Add(created);
            return created;
        }

        /// <summary>
        /// 找到通道中发起时间最早的活动请求。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        /// <returns>最早请求的槽位；没有活动请求时为空。</returns>
        private Slot FindOldest(AudioChannel channel)
        {
            Slot oldest = null;
            foreach (var slot in _slots[(int)channel])
                if (slot.State != SlotState.Idle && (oldest == null || slot.Sequence < oldest.Sequence)) oldest = slot;
            return oldest;
        }

        /// <summary>
        /// 查找句柄当前占用的槽位。
        /// </summary>
        /// <param name="handle">音频句柄。</param>
        /// <returns>匹配的槽位；过期或无效句柄返回空。</returns>
        private Slot FindSlot(AudioHandle handle)
        {
            if (!handle.IsValid || !IsChannelValid(handle.Channel)) return null;
            var list = _slots[(int)handle.Channel];
            if (list == null) return null;
            foreach (var slot in list)
                if (slot.HandleId == handle.Id && slot.State != SlotState.Idle) return slot;
            return null;
        }

        /// <summary>
        /// 仅释放仍属于原异步请求的槽位。
        /// </summary>
        /// <param name="slot">原请求的槽位。</param>
        /// <param name="handleId">原请求的标识。</param>
        private void ReleaseSlotIfCurrent(Slot slot, int handleId)
        {
            if (slot.HandleId == handleId) ReleaseSlot(slot);
        }

        /// <summary>
        /// 先清空所有权，再取消等待，防止同步取消回调清理已复用的槽位。
        /// </summary>
        /// <param name="slot">需要释放的槽位。</param>
        private void ReleaseSlot(Slot slot)
        {
            if (slot == null || slot.State == SlotState.Idle) return;
            var request = slot.LoadCancellation;
            var lease = slot.ClipLease;
            var ownership = slot.Ownership;
            var cancellation = slot.OwnerCancellation;
            slot.State = SlotState.Idle;
            slot.HandleId = 0;
            slot.LoadCancellation = null;
            slot.ClipLease = null;
            slot.Ownership = null;
            slot.OwnerCancellation = default;
            slot.Options = null;
            slot.Following = false;
            slot.FadeDuration = 0f;
            slot.StopAfterFade = false;
            if (slot.Source != null)
            {
                slot.Source.Stop();
                slot.Source.clip = null;
                slot.Source.outputAudioMixerGroup = null;
            }
            ownership?.Forget();
            cancellation.Dispose();
            lease?.Dispose();
            request?.Cancel();
        }

        /// <summary>
        /// 创建由跨场景宿主持有的二维空闲音源。
        /// </summary>
        /// <param name="channel">音源所属通道。</param>
        /// <returns>新建的音源槽位。</returns>
        private Slot CreateSlot(AudioChannel channel)
        {
            var go = new GameObject($"AudioSource_{channel}");
            go.transform.SetParent(_host.transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.velocityUpdateMode = AudioVelocityUpdateMode.Dynamic;
            return new Slot { Source = source, Channel = channel };
        }

        /// <summary>
        /// 更新通道中全部音源的组合音量。
        /// </summary>
        /// <param name="channel">音频通道。</param>
        private void ApplyVolumeChannel(AudioChannel channel)
        {
            var list = _slots[(int)channel];
            if (list == null) return;
            foreach (var slot in list)
                if (slot.State == SlotState.Playing) ApplyVolume(slot);
        }

        /// <summary>
        /// 应用主音量、通道音量和播放音量的乘积。
        /// </summary>
        /// <param name="slot">播放中的槽位。</param>
        private void ApplyVolume(Slot slot)
        {
            if (slot.Source != null) slot.Source.volume = _masterVolume * _channelVolumes[(int)slot.Channel] * slot.Volume;
        }

        /// <summary>
        /// 为新请求分配非零句柄标识。
        /// </summary>
        /// <returns>新的句柄标识。</returns>
        private int NextHandleId()
        {
            int id = _nextHandleId++;
            if (id == 0) id = _nextHandleId++;
            return id;
        }

        /// <summary>
        /// 校验通道是否在模块支持的范围内。
        /// </summary>
        /// <param name="channel">待检查通道。</param>
        /// <returns>通道是否合法。</returns>
        private static bool IsChannelValid(AudioChannel channel) => (uint)channel < ChannelCount;

        /// <summary>
        /// 限制有限数值范围，对非有限值使用默认值。
        /// </summary>
        /// <param name="value">输入数值。</param>
        /// <param name="min">下限。</param>
        /// <param name="max">上限。</param>
        /// <param name="fallback">非有限值的默认值。</param>
        /// <returns>经过规范化的数值。</returns>
        private static float ClampFinite(float value, float min, float max, float fallback)
            => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        /// <summary>
        /// 深复制请求配置并规范化 Unity 音源参数范围。
        /// </summary>
        /// <param name="options">调用方提供的配置。</param>
        /// <returns>本次请求独占的规范化配置。</returns>
        private static AudioPlayOptions Normalize(AudioPlayOptions options)
        {
            var value = options?.Clone() ?? new AudioPlayOptions();
            value.Volume = ClampFinite(value.Volume, 0f, 1f, 1f);
            value.Pitch = ClampFinite(value.Pitch, 0.01f, 3f, 1f);
            value.SpatialBlend = ClampFinite(value.SpatialBlend, 0f, 1f, 0f);
            value.MinDistance = ClampFinite(value.MinDistance, 0.01f, float.MaxValue, 1f);
            value.MaxDistance = ClampFinite(value.MaxDistance, value.MinDistance, float.MaxValue, Mathf.Max(value.MinDistance, 50f));
            value.DopplerLevel = ClampFinite(value.DopplerLevel, 0f, 5f, 1f);
            value.Spread = ClampFinite(value.Spread, 0f, 360f, 0f);
            value.Priority = Mathf.Clamp(value.Priority, 0, 256);
            value.FadeInSeconds = ClampFinite(value.FadeInSeconds, 0f, float.MaxValue, 0f);
            if ((uint)value.RolloffMode > (uint)AudioRolloffMode.Custom) value.RolloffMode = AudioRolloffMode.Logarithmic;
            return value;
        }

        /// <summary>
        /// 音源槽位的占用阶段。
        /// </summary>
        private enum SlotState : byte { Idle, Loading, Playing }

        /// <summary>
        /// 持有一轮播放的配置、资源、取消与淡变状态。
        /// </summary>
        private sealed class Slot
        {
            public AudioSource Source;
            public AudioChannel Channel;
            public int HandleId;
            public long Sequence;
            public SlotState State;
            public AudioPlayOptions Options;
            public ResourceLease<AudioClip> ClipLease;
            public CancellationTokenSource LoadCancellation;
            public ScopeRegistration Ownership;
            public CancellationTokenRegistration OwnerCancellation;
            public bool Following;
            public int StartFrame;
            public float Volume;
            public float FadeStart;
            public float FadeTarget;
            public float FadeDuration;
            public float FadeElapsed;
            public bool StopAfterFade;
        }
    }
}
