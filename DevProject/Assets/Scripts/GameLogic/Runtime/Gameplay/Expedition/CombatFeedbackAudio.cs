using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameLogic.Gameplay.Combat;
using GameLogic.Project;
using GameLogic.Tables;

namespace GameLogic.Gameplay.Expedition
{
    /// <summary>
    /// 通过框架音频通道播放本局短音效，并限制连击时的并发及迟到播放。
    /// </summary>
    public sealed class CombatFeedbackAudio : IDisposable
    {
        private readonly IAudioModule audio = GameEntry.Get<IAudioModule>();
        private readonly LifetimeScope scope = new("CombatFeedbackAudio");
        private readonly List<AudioHandle> handles = new();
        private readonly Dictionary<string, long> lastTicks = new();
        private bool paused;
        private int revision;
        private int pending;
        private int roomId;

        /// <summary>
        /// 同步暂停与房间状态，只停止本局拥有的声音，不暂停界面和背景音乐。
        /// </summary>
        /// <param name="shouldPause">是否暂停本局反馈。</param>
        /// <param name="currentRoom">当前房间编号。</param>
        public void Refresh(bool shouldPause, int currentRoom)
        {
            if (paused != shouldPause || roomId != currentRoom)
            {
                paused = shouldPause;
                roomId = currentRoom;
                revision++;
                foreach (AudioHandle handle in handles) audio?.Stop(handle);
                handles.Clear();
                lastTicks.Clear();
            }
            for (int i = handles.Count - 1; i >= 0; i--)
                if (audio == null || !audio.IsPlaying(handles[i])) handles.RemoveAt(i);
        }

        /// <summary>
        /// 将攻击提示和已确认伤害映射为热更短音效。
        /// </summary>
        /// <param name="fact">当前逻辑帧的战斗事实。</param>
        public void Present(CombatEvent fact)
        {
            if (audio == null || paused || !scope.IsActive || fact.RoomId != roomId) return;
            string role = fact.Kind == CombatEventKind.Cue && GameTables.CfgCombatAnimationCues.GetById(fact.DefinitionId)?.IsAttack == true ? "SwingSound" :
                fact.Kind == CombatEventKind.Damage && fact.Value > 0f ? (fact.IsCritical ? "CriticalSound" : "HitSound") : null;
            string address = role == null ? null : ProjectContentConfig.Asset(role);
            if (address == null || handles.Count + pending >= 8 ||
                lastTicks.TryGetValue(address, out long tick) && fact.Tick - tick < 3) return;
            lastTicks[address] = fact.Tick;
            pending++;
            PlayAsync(address, revision).Forget();
        }

        /// <summary>
        /// 等待音频模块获取资源，暂停、换图或退出后的迟到声音立即停止。
        /// </summary>
        /// <param name="address">音效逻辑地址。</param>
        /// <param name="generation">请求时的状态代次。</param>
        /// <returns>本次播放请求任务。</returns>
        private async UniTask PlayAsync(string address, int generation)
        {
            var result = await audio.PlayAsync(address, AudioChannel.Sfx,
                new AudioPlayOptions { Volume = 0.5f, Owner = scope }, scope.Token).SuppressCancellationThrow();
            pending--;
            if (result.IsCanceled) return;
            AudioHandle handle = result.Result;
            if (generation != revision || !scope.IsActive || paused) audio.Stop(handle);
            else if (handle.IsValid) handles.Add(handle);
        }

        /// <summary>
        /// 取消在途播放并归还本局声音，资源由音频模块和场景租约共同管理。
        /// </summary>
        public void Dispose()
        {
            revision++;
            foreach (AudioHandle handle in handles) audio?.Stop(handle);
            handles.Clear();
            scope.Dispose();
        }
    }
}
