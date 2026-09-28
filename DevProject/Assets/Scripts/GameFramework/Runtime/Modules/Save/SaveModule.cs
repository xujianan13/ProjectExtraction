using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 正式存档模块，统一持有会话、平台后端与进程内存档身份占用。
    /// </summary>
    public sealed partial class SaveModule : ModuleBase, ISaveModule
    {
        private static readonly HashSet<(string backend, string identity)> Owners = new();
        private readonly List<ISaveSession> _sessions = new();
        private readonly Func<double> _clock;
        private int _ownedCount, _generation;
        private bool _shutdown;
        public ISaveBackend Backend { get; private set; }
        public override int Priority => -290;

        /// <summary>
        /// 创建正式存档模块。
        /// </summary>
        /// <param name="clock">可注入的单调时钟；默认使用 Unity 实时时钟。</param>
        public SaveModule(Func<double> clock = null) => _clock = clock ?? (() => Time.realtimeSinceStartupAsDouble);

        /// <summary>
        /// 注册前后台通知。
        /// </summary>
        public override void OnInit()
        {
            _shutdown = false;
            GameEntry.ApplicationBackgroundChanged -= OnBackground;
            GameEntry.ApplicationBackgroundChanged += OnBackground;
        }

        /// <summary>
        /// 驱动不受游戏缩放影响的保存调度。
        /// </summary>
        /// <param name="deltaTime">游戏时间增量，保存调度不使用该值。</param>
        public override void OnUpdate(float deltaTime)
        {
            foreach (ISaveSession session in _sessions.ToArray()) session.Tick();
        }

        /// <summary>
        /// 停止新操作并同步释放调度，在途操作继续持有身份直到真正收束。
        /// </summary>
        public override void OnShutdown()
        {
            _shutdown = true;
            _generation++;
            GameEntry.ApplicationBackgroundChanged -= OnBackground;
            foreach (ISaveSession session in _sessions.ToArray()) session.Dispose();
        }

        /// <summary>
        /// 在没有会话或在途操作时配置后端。
        /// </summary>
        /// <param name="backend">平台持久化后端。</param>
        /// <returns>配置结果。</returns>
        public SaveResult ConfigureBackend(ISaveBackend backend)
        {
            if (_ownedCount != 0) return SaveResult.Fail(SaveError.Conflict, "仍有存档会话或物理操作，不能更换后端。");
            if (backend == null || string.IsNullOrWhiteSpace(backend.StorageIdentity))
                return SaveResult.Fail(SaveError.InvalidArgument, "存档后端和持久化身份不能为空。");
            Backend = backend;
            return SaveResult.Ok();
        }

        /// <summary>
        /// 打开存档，取消等待后仍负责收束底层读取和释放占用。
        /// </summary>
        /// <typeparam name="T">游戏数据类型。</typeparam>
        /// <param name="identity">存档身份。</param>
        /// <param name="definition">数据定义。</param>
        /// <param name="policy">保存策略。</param>
        /// <param name="ct">调用方等待取消令牌。</param>
        /// <returns>未激活会话或失败结果。</returns>
        public UniTask<SaveOpenResult<T>> OpenAsync<T>(SaveIdentity identity, SaveDefinition<T> definition,
            SavePolicy policy = null, CancellationToken ct = default)
        {
            SavePolicy copy = (policy ?? new SavePolicy()).Copy();
            SaveResult valid = Validate(identity, definition, copy);
            if (!valid.Success) return UniTask.FromResult(new SaveOpenResult<T>(valid));
            var repository = new SaveRepository<T>(Backend, identity, definition, copy.MaxRecordBytes);
            return OpenCoreAsync(identity, repository, repository.LoadAsync, copy, false, ct);
        }

        /// <summary>
        /// 检查指定存档可供恢复的记录；不创建可写会话。
        /// </summary>
        /// <typeparam name="T">目标游戏数据类型。</typeparam>
        /// <param name="identity">来源身份。</param>
        /// <param name="definition">用于验证兼容性的数据定义。</param>
        /// <param name="ct">等待取消令牌。</param>
        /// <returns>恢复候选列表。</returns>
        public async UniTask<SaveRecoveryInspection> InspectRecoveryAsync<T>(SaveIdentity identity, SaveDefinition<T> definition,
            CancellationToken ct = default)
        {
            var policy = new SavePolicy();
            SaveResult valid = Validate(identity, definition, policy);
            if (!valid.Success) return new SaveRecoveryInspection(valid, Array.Empty<SaveRecoveryCandidate>());
            if (!TryOwn(identity, out Action release))
                return new SaveRecoveryInspection(SaveResult.Fail(SaveError.Conflict, "存档身份已被占用。"), Array.Empty<SaveRecoveryCandidate>());
            var state = new Pending<SaveRecoveryInspection>();
            var repository = new SaveRepository<T>(Backend, identity, definition, policy.MaxRecordBytes);
            InspectPhysicalAsync(repository, state, release).Forget();
            double start = _clock();
            int generation = _generation;
            while (!state.Done)
            {
                if (ct.IsCancellationRequested || _generation != generation || _shutdown || _clock() - start >= policy.OperationTimeoutSeconds)
                    return new SaveRecoveryInspection(SaveResult.Fail(ct.IsCancellationRequested ? SaveError.Cancelled : SaveError.ReadFailed,
                        "恢复检查等待已结束，底层读取继续收束。"), Array.Empty<SaveRecoveryCandidate>());
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            return state.Value;
        }

        /// <summary>
        /// 将明确选定的恢复候选读入新身份，等待激活后才写目标。
        /// </summary>
        /// <typeparam name="T">游戏数据类型。</typeparam>
        /// <param name="candidate">检查得到的恢复候选。</param>
        /// <param name="targetIdentity">新的空存档身份。</param>
        /// <param name="definition">数据定义。</param>
        /// <param name="policy">保存策略。</param>
        /// <param name="ct">调用方等待取消令牌。</param>
        /// <returns>已接受恢复但尚未激活的目标会话。</returns>
        public UniTask<SaveOpenResult<T>> RestoreToNewSlotAsync<T>(SaveRecoveryCandidate candidate, SaveIdentity targetIdentity,
            SaveDefinition<T> definition, SavePolicy policy = null, CancellationToken ct = default)
        {
            SavePolicy copy = (policy ?? new SavePolicy()).Copy();
            SaveResult valid = Validate(targetIdentity, definition, copy);
            if (!valid.Success) return UniTask.FromResult(new SaveOpenResult<T>(valid));
            if (candidate == null || !candidate.Identity.IsValid || candidate.Identity.KeyPrefix == targetIdentity.KeyPrefix)
                return UniTask.FromResult(new SaveOpenResult<T>(SaveResult.Fail(SaveError.InvalidArgument, "恢复目标必须是新的存档身份。")));
            if (!TryOwn(candidate.Identity, out Action releaseSource))
                return UniTask.FromResult(new SaveOpenResult<T>(SaveResult.Fail(SaveError.Conflict, "来源存档正在使用。")));
            var source = new SaveRepository<T>(Backend, candidate.Identity, definition, copy.MaxRecordBytes);
            var target = new SaveRepository<T>(Backend, targetIdentity, definition, copy.MaxRecordBytes);
            return OpenCoreAsync(targetIdentity, target, () => source.RestoreToNewSlotAsync(candidate, target), copy, true, ct, releaseSource);
        }

        /// <summary>
        /// 校验操作的公共前提。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="identity">存档身份。</param>
        /// <param name="definition">数据定义。</param>
        /// <param name="policy">保存策略。</param>
        /// <returns>校验结果。</returns>
        private SaveResult Validate<T>(SaveIdentity identity, SaveDefinition<T> definition, SavePolicy policy)
        {
            if (_shutdown) return SaveResult.Fail(SaveError.Closed, "存档模块已关闭。");
            if (Backend == null) return SaveResult.Fail(SaveError.Unsupported, "尚未配置平台存档后端。");
            if (!identity.IsValid || definition == null || !definition.IsValid || !policy.IsValid())
                return SaveResult.Fail(SaveError.InvalidArgument, "存档身份、数据定义或保存策略无效。");
            return SaveResult.Ok();
        }

        /// <summary>
        /// 占用身份，使用稳定后端标识阻止不同后端实例同时写同一存档。
        /// </summary>
        /// <param name="identity">存档身份。</param>
        /// <param name="release">幂等释放方法。</param>
        /// <returns>是否成功占用。</returns>
        private bool TryOwn(SaveIdentity identity, out Action release)
        {
            var key = (Backend.StorageIdentity, identity.KeyPrefix);
            if (!Owners.Add(key)) { release = null; return false; }
            _ownedCount++;
            bool released = false;
            release = () =>
            {
                if (released) return;
                released = true;
                Owners.Remove(key);
                _ownedCount--;
            };
            return true;
        }

        /// <summary>
        /// 执行统一的异步打开流程。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="identity">目标身份。</param>
        /// <param name="repository">目标仓库。</param>
        /// <param name="load">读取或恢复操作。</param>
        /// <param name="policy">保存策略。</param>
        /// <param name="acceptRecovery">是否已显式选择恢复候选。</param>
        /// <param name="ct">等待取消令牌。</param>
        /// <param name="releaseSource">恢复来源的额外占用。</param>
        /// <returns>打开结果。</returns>
        private async UniTask<SaveOpenResult<T>> OpenCoreAsync<T>(SaveIdentity identity, SaveRepository<T> repository,
            Func<UniTask<SaveLoadResult<T>>> load, SavePolicy policy, bool acceptRecovery, CancellationToken ct, Action releaseSource = null)
        {
            if (ct.IsCancellationRequested) { releaseSource?.Invoke(); return new SaveOpenResult<T>(SaveResult.Fail(SaveError.Cancelled, "已取消打开存档。")); }
            if (!TryOwn(identity, out Action release))
            {
                releaseSource?.Invoke();
                return new SaveOpenResult<T>(SaveResult.Fail(SaveError.Conflict, "存档身份已被另一会话占用。"));
            }
            var state = new Pending<SaveOpenResult<T>>();
            int generation = _generation;
            OpenPhysicalAsync(repository, load, policy, acceptRecovery, state, release, releaseSource, generation).Forget();
            double start = _clock();
            while (!state.Done)
            {
                if (ct.IsCancellationRequested || _generation != generation || _shutdown || _clock() - start >= policy.OperationTimeoutSeconds)
                {
                    state.Abandoned = true;
                    return new SaveOpenResult<T>(SaveResult.Fail(ct.IsCancellationRequested ? SaveError.Cancelled : SaveError.ReadFailed,
                        "打开存档等待已结束，底层读取继续收束。"));
                }
                await UniTask.Yield(PlayerLoopTiming.Update);
            }
            return state.Value;
        }

        /// <summary>
        /// 持有打开操作直到初始化与读取真正结束。
        /// </summary>
        /// <typeparam name="T">数据类型。</typeparam>
        /// <param name="repository">目标仓库。</param>
        /// <param name="load">实际加载操作。</param>
        /// <param name="policy">保存策略。</param>
        /// <param name="acceptRecovery">是否已经选择恢复。</param>
        /// <param name="state">调用状态。</param>
        /// <param name="release">释放目标占用。</param>
        /// <param name="releaseSource">释放来源占用。</param>
        /// <param name="generation">模块生命周期编号。</param>
        /// <returns>物理操作任务。</returns>
        private async UniTask OpenPhysicalAsync<T>(SaveRepository<T> repository, Func<UniTask<SaveLoadResult<T>>> load,
            SavePolicy policy, bool acceptRecovery, Pending<SaveOpenResult<T>> state, Action release, Action releaseSource, int generation)
        {
            SaveSession<T> session = null;
            try
            {
                SaveResult initialized = await Backend.InitializeAsync();
                SaveLoadResult<T> loaded = initialized.Success ? await load() : null;
                await UniTask.SwitchToMainThread();
                SaveResult result = initialized.Success ? loaded.Result : initialized;
                if (result.Success && !state.Abandoned && !_shutdown && generation == _generation)
                {
                    session = new SaveSession<T>(repository, loaded, policy, _clock, () => { _sessions.Remove(session); release(); });
                    if (acceptRecovery) session.AcceptRecovery();
                    _sessions.Add(session);
                    state.Value = new SaveOpenResult<T>(SaveResult.Ok(), session);
                }
                else state.Value = new SaveOpenResult<T>(result.Success ? SaveResult.Fail(SaveError.Closed, "存档模块已结束。") : result);
            }
            catch (Exception error)
            {
                await UniTask.SwitchToMainThread();
                state.Value = new SaveOpenResult<T>(SaveResult.Fail(SaveError.ReadFailed, "打开存档失败：" + error.Message));
                GameEntry.Get<ILogModule>()?.Warn("Save", state.Value.Result.Message);
            }
            finally
            {
                if (session == null) release();
                releaseSource?.Invoke();
                state.Done = true;
            }
        }

        /// <summary>
        /// 在恢复检查结束时释放占用，即使调用方已经停止等待。
        /// </summary>
        /// <typeparam name="T">游戏数据类型。</typeparam>
        /// <param name="repository">来源仓库。</param>
        /// <param name="state">异步结果容器。</param>
        /// <param name="release">释放身份占用。</param>
        /// <returns>检查任务。</returns>
        private async UniTask InspectPhysicalAsync<T>(SaveRepository<T> repository, Pending<SaveRecoveryInspection> state, Action release)
        {
            try
            {
                SaveResult initialized = await Backend.InitializeAsync();
                state.Value = initialized.Success ? await repository.InspectRecoveryAsync() :
                    new SaveRecoveryInspection(initialized, Array.Empty<SaveRecoveryCandidate>());
            }
            catch (Exception error)
            {
                state.Value = new SaveRecoveryInspection(SaveResult.Fail(SaveError.ReadFailed, error.Message), Array.Empty<SaveRecoveryCandidate>());
            }
            finally
            {
                await UniTask.SwitchToMainThread();
                release();
                state.Done = true;
            }
        }

        /// <summary>
        /// 转发去重后的前后台事件。
        /// </summary>
        /// <param name="background">是否进入后台。</param>
        private void OnBackground(bool background)
        {
            foreach (ISaveSession session in _sessions.ToArray()) session.OnBackground(background);
        }

        /// <summary>
        /// 持有调用方可能提前结束等待的物理操作结果。
        /// </summary>
        /// <typeparam name="T">结果类型。</typeparam>
        private sealed class Pending<T>
        {
            internal bool Done, Abandoned;
            internal T Value;
        }
    }
}
