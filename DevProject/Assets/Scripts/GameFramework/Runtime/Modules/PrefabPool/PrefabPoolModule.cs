using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 持有 Prefab 池的模块生命周期，在资源模块之后初始化、之前关闭。
    /// </summary>
    public sealed class PrefabPoolModule : ModuleBase, IPrefabPoolModule
    {
        private readonly HashSet<PrefabPool> _pools = new();
        private CancellationTokenSource _lifetime;
        private IResourceModule _resources;
        private ILogModule _log;

        /// <summary>
        /// 资源模块之后初始化，输入和业务模块之前就绪。
        /// </summary>
        public override int Priority => -40;

        /// <summary>
        /// 获取尚未关闭的池数量。
        /// </summary>
        public int Count
        {
            get
            {
                int count = 0;
                foreach (PrefabPool pool in _pools) if (!pool.IsDisposed) count++;
                return count;
            }
        }

        /// <summary>
        /// 统计仍由实例池管理的活动与闲置实例。
        /// </summary>
        /// <param name="section">待填充的诊断区域。</param>
        internal void CollectDiagnostics(DiagnosticSection section)
        {
            int active = 0, inactive = 0;
            foreach (PrefabPool pool in _pools)
            {
                if (pool.IsDisposed) continue;
                pool.GetDiagnosticCounts(out int poolActive, out int poolInactive);
                active += poolActive;
                inactive += poolInactive;
            }
            section.Add("ActiveInstances", active.ToString());
            section.Add("InactiveInstances", inactive.ToString());
        }
        /// <summary>
        /// 取得通用资源和日志服务，并开启本次模块生命周期。
        /// </summary>
        public override void OnInit()
        {
            if (_lifetime != null) return;
            _resources = GameEntry.Get<IResourceModule>();
            _log = GameEntry.Get<ILogModule>();
            _lifetime = new CancellationTokenSource();
        }

        /// <summary>
        /// 取消未完成加载并关闭全部池；Unity 对象在本帧末完成销毁。
        /// </summary>
        public override void OnShutdown()
        {
            CancellationTokenSource lifetime = _lifetime;
            _lifetime = null;
            lifetime?.Cancel();
            var pools = new List<PrefabPool>(_pools);
            foreach (PrefabPool pool in pools) pool.ReleaseForShutdown();
            _pools.Clear();
            lifetime?.Dispose();
            _resources = null;
            _log = null;
        }

        /// <summary>
        /// 创建独立池，持有一份资源租约直到该池的实例全部销毁。
        /// </summary>
        /// <param name="location">Prefab 资源地址。</param>
        /// <param name="options">池的预热和闲置上限设置。</param>
        /// <param name="packageName">指定包名；为空时使用当前默认包。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>创建完成的池；模块不可用时返回空，资源错误及取消向调用方传播。</returns>
        public async UniTask<PrefabPool> CreatePoolAsync(string location, PrefabPoolOptions options = null, string packageName = null,
            CancellationToken ct = default)
        {
            CancellationTokenSource lifetime = _lifetime;
            if (lifetime == null || _resources == null)
            {
                _log?.Error("PrefabPool", "实例池模块尚未就绪或资源模块未注册。");
                return null;
            }

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token, ct);
            string resolvedPackage = packageName ?? _resources.DefaultPackageName;
            ResourceLease<GameObject> lease = null;
            try
            {
                lease = await _resources.AcquireAsync<GameObject>(resolvedPackage, location, ResourceLoadOptions.Transient, ct: linked.Token);
                linked.Token.ThrowIfCancellationRequested();
                var pool = new PrefabPool(lease, options ?? new PrefabPoolOptions(), _log, OnPoolDisposed);
                lease = null;
                if (pool.IsDisposed)
                {
                    pool.ReleaseForShutdown();
                    return null;
                }
                _pools.Add(pool);
                return pool;
            }
            finally
            {
                lease?.Dispose();
            }
        }

        /// <summary>
        /// 从模块中解除已经归还资源租约的池登记。
        /// </summary>
        /// <param name="pool">已经关闭的池。</param>
        private void OnPoolDisposed(PrefabPool pool) => _pools.Remove(pool);
    }
}
