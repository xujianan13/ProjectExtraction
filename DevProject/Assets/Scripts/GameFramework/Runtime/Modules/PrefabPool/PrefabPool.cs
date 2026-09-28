using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace GameFramework
{
    /// <summary>
    /// 持有一份预制体资源租约的实例池；每次生成都会发放独立句柄。
    /// </summary>
    public sealed class PrefabPool : IDisposable
    {
        private sealed class Entry
        {
            internal readonly GameObject GameObject;
            internal readonly PrefabPoolReset Reset;
            internal readonly MonoBehaviour[] Callbacks;
            internal readonly PrefabPoolDestroyBridge DestroyBridge;
            internal bool Dirty;

            /// <summary>
            /// 保存一个可复用实例及其原始状态。
            /// </summary>
            /// <param name="gameObject">预制体实例。</param>
            internal Entry(GameObject gameObject)
            {
                GameObject = gameObject;
                Reset = new PrefabPoolReset(gameObject);
                Callbacks = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
                DestroyBridge = gameObject.AddComponent<PrefabPoolDestroyBridge>();
            }
        }

        private readonly ResourceLease<GameObject> _lease;
        private readonly PrefabPoolOptions _options;
        private readonly ILogModule _log;
        private readonly Action<PrefabPool> _onDisposed;
        private readonly Dictionary<PrefabInstance, Entry> _active = new();
        private readonly List<Entry> _inactive = new();
        private readonly List<GameObject> _pendingDestroy = new();
        private readonly List<PrefabInstance> _lost = new();
        private readonly UniTaskCompletionSource _completion = new();
        private GameObject _root;
        private bool _disposed;
        private bool _leaseReleased;
        private bool _pruning;

        /// <summary>
        /// 创建池并预热闲置实例。
        /// </summary>
        /// <param name="lease">预制体资源的独占使用租约。</param>
        /// <param name="options">池的不可变配置。</param>
        /// <param name="log">记录回调及创建错误的日志模块。</param>
        /// <param name="onDisposed">租约最终释放后的通知。</param>
        internal PrefabPool(ResourceLease<GameObject> lease, PrefabPoolOptions options, ILogModule log, Action<PrefabPool> onDisposed)
        {
            _lease = lease;
            _options = options;
            _log = log;
            _onDisposed = onDisposed;
            if (_lease?.Asset == null)
            {
                _disposed = true;
                ReleaseLease();
                _completion.TrySetResult();
                return;
            }

            _root = new GameObject($"PrefabPool:{_lease.Location}");
            UnityEngine.Object.DontDestroyOnLoad(_root);
            _root.SetActive(false);
            Prewarm(_options.PrewarmCount);
        }

        /// <summary>
        /// 获取资源包名。
        /// </summary>
        public string PackageName => _lease.PackageName;

        /// <summary>
        /// 获取预制体逻辑地址。
        /// </summary>
        public string Location => _lease.Location;

        /// <summary>
        /// 获取池是否已停止生成与归还。
        /// </summary>
        public bool IsDisposed => _disposed;

        /// <summary>
        /// 获取仍由池管理的实例总数。
        /// </summary>
        public int CountAll { get { PruneDestroyed(); return _active.Count + _inactive.Count; } }

        /// <summary>
        /// 获取仍在使用的实例数量。
        /// </summary>
        public int CountActive { get { PruneDestroyed(); return _active.Count; } }

        /// <summary>
        /// 获取可再次使用的闲置实例数量。
        /// </summary>
        public int CountInactive { get { PruneDestroyed(); return _inactive.Count; } }

        /// <summary>
        /// 读取内部集合计数，不触发损坏实例清理。
        /// </summary>
        /// <param name="active">占用中的实例数量。</param>
        /// <param name="inactive">可复用实例数量。</param>
        internal void GetDiagnosticCounts(out int active, out int inactive)
        {
            active = _active.Count;
            inactive = _inactive.Count;
        }

        /// <summary>
        /// 从池中生成一个实例，保持预制体的原始局部位置。
        /// </summary>
        /// <param name="parent">可选的实例父节点。</param>
        /// <returns>本轮实例句柄；池关闭或生成失败时返回空。</returns>
        public PrefabInstance Spawn(Transform parent = null) => SpawnCore(parent, false, default, default);

        /// <summary>
        /// 从池中生成一个实例并设置世界坐标姿态。
        /// </summary>
        /// <param name="position">世界坐标位置。</param>
        /// <param name="rotation">世界坐标旋转。</param>
        /// <param name="parent">可选的实例父节点。</param>
        /// <returns>本轮实例句柄；池关闭或生成失败时返回空。</returns>
        public PrefabInstance Spawn(Vector3 position, Quaternion rotation, Transform parent = null) => SpawnCore(parent, true, position, rotation);

        /// <summary>
        /// 结束本轮使用并将实例归还池中。
        /// </summary>
        /// <param name="instance">本轮实例句柄。</param>
        /// <returns>本次是否成功结束一个仍活跃的实例。</returns>
        public bool Despawn(PrefabInstance instance)
        {
            if (_disposed || instance == null || instance.Owner != this || instance.Released || !_active.TryGetValue(instance, out Entry entry))
                return false;
            _active.Remove(instance);
            instance.Invalidate();
            instance.SpawnScope.Stop();
            if (entry.GameObject == null)
            {
                instance.SpawnScope.Dispose();
                ScheduleEntryDestroy(entry);
                return false;
            }

            bool clean;
            try
            {
                clean = InvokeCallbacks(entry, false, null);
            }
            finally
            {
                instance.SpawnScope.Dispose();
            }
            if (_disposed || entry.GameObject == null || !clean || entry.Dirty)
            {
                ScheduleEntryDestroy(entry);
                return true;
            }

            entry.GameObject.transform.SetParent(_root.transform, false);
            entry.GameObject.SetActive(false);
            entry.Reset.Restore(entry.GameObject);
            if (_disposed || _inactive.Count >= _options.MaxInactive) ScheduleEntryDestroy(entry);
            else _inactive.Add(entry);
            return true;
        }

        /// <summary>
        /// 将闲置实例补足到指定数量，受池的闲置上限约束。
        /// </summary>
        /// <param name="count">希望池中保有的闲置实例数量。</param>
        /// <returns>本次成功创建的实例数量。</returns>
        public int Prewarm(int count)
        {
            if (_disposed || count <= 0) return 0;
            PruneDestroyed();
            int target = Math.Min(count, _options.MaxInactive);
            int created = 0;
            while (!_disposed && _inactive.Count < target)
            {
                Entry entry = CreateEntry();
                if (entry == null) break;
                _inactive.Add(entry);
                created++;
            }
            return created;
        }

        /// <summary>
        /// 销毁全部闲置实例。
        /// </summary>
        public void ClearInactive()
        {
            var entries = _inactive.ToArray();
            _inactive.Clear();
            foreach (Entry entry in entries) ScheduleEntryDestroy(entry);
        }

        /// <summary>
        /// 停止池并安排销毁实例；租约在 Unity 完成销毁后释放。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var active = new List<KeyValuePair<PrefabInstance, Entry>>(_active);
            _active.Clear();
            foreach (var pair in active)
            {
                pair.Key.Invalidate();
                pair.Key.SpawnScope.Stop();
            }
            foreach (var pair in active)
            {
                try
                {
                    InvokeCallbacks(pair.Value, false, null);
                }
                finally
                {
                    pair.Key.SpawnScope.Dispose();
                }
                ScheduleEntryDestroy(pair.Value);
            }
            ClearInactive();
            ScheduleDestroy(_root);
            _root = null;
            FinishDisposeAsync().Forget();
        }

        /// <summary>
        /// 等待所有已安排销毁的 Unity 对象消失后释放资源租约。
        /// </summary>
        /// <returns>租约已释放时完成的异步操作。</returns>
        public UniTask DisposeAsync()
        {
            Dispose();
            return _completion.Task;
        }

        /// <summary>
        /// 模块同步停机时立即归还租约，避免资源模块将池报告为泄漏。
        /// </summary>
        internal void ReleaseForShutdown()
        {
            Dispose();
            ReleaseLease();
        }

        /// <summary>
        /// 从闲置队列或资源预制体生成对象并执行本轮回调。
        /// </summary>
        /// <param name="parent">目标父节点。</param>
        /// <param name="hasPose">是否覆盖世界坐标姿态。</param>
        /// <param name="position">目标世界坐标位置。</param>
        /// <param name="rotation">目标世界坐标旋转。</param>
        /// <returns>成功生成的句柄，失败时为空。</returns>
        private PrefabInstance SpawnCore(Transform parent, bool hasPose, Vector3 position, Quaternion rotation)
        {
            if (_disposed) return null;
            PruneDestroyed();
            Entry entry = null;
            while (_inactive.Count > 0 && entry == null)
            {
                int last = _inactive.Count - 1;
                entry = _inactive[last];
                _inactive.RemoveAt(last);
                if (entry.GameObject == null) entry = null;
            }
            entry ??= CreateEntry();
            if (entry == null) return null;

            GameObject gameObject = entry.GameObject;
            var instance = new PrefabInstance(this, gameObject);
            _active.Add(instance, entry);
            Transform transform = gameObject.transform;
            gameObject.SetActive(false);
            if (parent == null)
            {
                transform.SetParent(null, false);
                Scene scene = SceneManager.GetActiveScene();
                if (scene.IsValid() && scene.isLoaded) SceneManager.MoveGameObjectToScene(gameObject, scene);
            }
            else transform.SetParent(parent, false);
            if (hasPose) transform.SetPositionAndRotation(position, rotation);
            gameObject.SetActive(true);
            if (instance.Released || _disposed || gameObject == null)
            {
                if (!instance.Released) EndLostInstance(instance, entry);
                return null;
            }
            if (!InvokeCallbacks(entry, true, instance))
            {
                entry.Dirty = true;
                if (!instance.Released) Despawn(instance);
                else
                {
                    _inactive.Remove(entry);
                    PrefabInstance reused = null;
                    foreach (var pair in _active)
                        if (ReferenceEquals(pair.Value, entry)) { reused = pair.Key; break; }
                    if (reused != null)
                    {
                        reused.Invalidate();
                        reused.SpawnScope.Stop();
                        reused.SpawnScope.Dispose();
                        _active.Remove(reused);
                    }
                    ScheduleEntryDestroy(entry);
                }
                return null;
            }
            if (!instance.Released && (_disposed || gameObject == null)) EndLostInstance(instance, entry);
            return instance.Released ? null : instance;
        }

        /// <summary>
        /// 在不可激活的持久根节点下复制预制体。
        /// </summary>
        /// <returns>可用的新实例，创建失败时为空。</returns>
        private Entry CreateEntry()
        {
            if (_disposed || _root == null || _lease.Asset == null) return null;
            GameObject gameObject = null;
            try
            {
                gameObject = UnityEngine.Object.Instantiate(_lease.Asset, _root.transform);
                if (gameObject == null) return null;
                gameObject.SetActive(false);
                var entry = new Entry(gameObject);
                entry.DestroyBridge.Bind(() => OnEntryDestroyed(entry));
                return entry;
            }
            catch (Exception error)
            {
                _log?.Error("PrefabPool", $"创建实例失败：{error}");
                ScheduleDestroy(gameObject);
                Dispose();
                return null;
            }
        }

        /// <summary>
        /// 逐个执行实例上的生命周期回调；异常会将本轮对象标为脏实例。
        /// </summary>
        /// <param name="entry">回调所属实例。</param>
        /// <param name="spawning">是否执行生成回调。</param>
        /// <param name="instance">生成回调使用的本轮句柄。</param>
        /// <returns>全部回调是否正常完成。</returns>
        private bool InvokeCallbacks(Entry entry, bool spawning, PrefabInstance instance)
        {
            if (entry.GameObject == null) return false;
            bool clean = true;
            foreach (MonoBehaviour component in entry.Callbacks)
            {
                if (component == null || !(component is IPrefabPoolCallbacks callback)) continue;
                if (spawning && (instance.Released || _disposed || entry.GameObject == null)) return clean;
                try
                {
                    if (spawning) callback.OnSpawned(instance);
                    else callback.OnDespawned();
                }
                catch (Exception error)
                {
                    clean = false;
                    _log?.Error("PrefabPool", $"实例生命周期回调失败：{error}");
                }
            }
            return clean;
        }

        /// <summary>
        /// 销毁实例及仍由该实例拥有的原始子节点。
        /// </summary>
        /// <param name="entry">待销毁的实例记录。</param>
        private void ScheduleEntryDestroy(Entry entry)
        {
            entry.Reset.DestroyDetachedNodes(entry.GameObject, ScheduleDestroy);
            ScheduleDestroy(entry.GameObject);
        }

        /// <summary>
        /// 安排销毁对象，并保留引用直到 Unity 完成销毁。
        /// </summary>
        /// <param name="gameObject">需要销毁的对象。</param>
        private void ScheduleDestroy(GameObject gameObject)
        {
            if (gameObject == null || _pendingDestroy.Contains(gameObject)) return;
            gameObject.SetActive(false);
            _pendingDestroy.Add(gameObject);
            UnityEngine.Object.Destroy(gameObject);
        }

        /// <summary>
        /// 在 Unity 对象销毁的当刻结束活跃轮次，并清理脱离根对象的原始子节点。
        /// </summary>
        /// <param name="entry">已销毁的池记录。</param>
        private void OnEntryDestroyed(Entry entry)
        {
            PrefabInstance found = null;
            foreach (var pair in _active)
                if (ReferenceEquals(pair.Value, entry)) { found = pair.Key; break; }
            if (found != null) EndLostInstance(found, entry);
            else
            {
                _inactive.Remove(entry);
                entry.Reset.DestroyDetachedNodes(entry.GameObject, ScheduleDestroy);
            }
        }

        /// <summary>
        /// 结束外部销毁或生成途中失效的轮次。
        /// </summary>
        /// <param name="instance">本轮句柄。</param>
        /// <param name="entry">对应的池记录。</param>
        private void EndLostInstance(PrefabInstance instance, Entry entry)
        {
            if (!_active.Remove(instance)) return;
            instance.Invalidate();
            instance.SpawnScope.Stop();
            instance.SpawnScope.Dispose();
            entry.Reset.DestroyDetachedNodes(entry.GameObject, ScheduleDestroy);
        }

        /// <summary>
        /// 剔除已被调用方或场景外部销毁的对象和失效句柄。
        /// </summary>
        private void PruneDestroyed()
        {
            if (_pruning) return;
            _pruning = true;
            try
            {
                for (int i = _inactive.Count - 1; i >= 0; i--)
                {
                    if (i >= _inactive.Count || _inactive[i].GameObject != null) continue;
                    Entry entry = _inactive[i];
                    _inactive.RemoveAt(i);
                    ScheduleEntryDestroy(entry);
                }
                _lost.Clear();
                foreach (var pair in _active) if (pair.Value.GameObject == null) _lost.Add(pair.Key);
                foreach (PrefabInstance instance in _lost)
                    if (_active.TryGetValue(instance, out Entry entry)) EndLostInstance(instance, entry);
                for (int i = _pendingDestroy.Count - 1; i >= 0; i--)
                    if (_pendingDestroy[i] == null) _pendingDestroy.RemoveAt(i);
            }
            finally
            {
                _lost.Clear();
                _pruning = false;
            }
        }

        /// <summary>
        /// 等待已安排销毁的对象消失，并在此后释放租约。
        /// </summary>
        /// <returns>释放完成后的异步操作。</returns>
        private async UniTask FinishDisposeAsync()
        {
            PruneDestroyed();
            while (_pendingDestroy.Count > 0)
            {
                await UniTask.Yield();
                PruneDestroyed();
            }
            ReleaseLease();
            _completion.TrySetResult();
        }

        /// <summary>
        /// 恰好一次地归还资源租约，并通知所属模块。
        /// </summary>
        private void ReleaseLease()
        {
            if (_leaseReleased) return;
            _leaseReleased = true;
            _lease?.Dispose();
            _onDisposed?.Invoke(this);
        }
    }
}
