using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 一次生成的实例句柄；归还后永远失效，避免旧句柄误回收后续复用的对象。
    /// </summary>
    public sealed class PrefabInstance : IDisposable
    {
        private readonly GameObject _gameObject;

        /// <summary>
        /// 创建绑定到本轮实例的句柄。
        /// </summary>
        /// <param name="owner">持有实例的池。</param>
        /// <param name="gameObject">本轮使用的对象。</param>
        internal PrefabInstance(PrefabPool owner, GameObject gameObject)
        {
            Owner = owner;
            _gameObject = gameObject;
            SpawnScope = new LifetimeScope("PrefabSpawn");
        }

        /// <summary>
        /// 获取实例所属池。
        /// </summary>
        internal PrefabPool Owner { get; }

        /// <summary>
        /// 获取本轮句柄是否已结束。
        /// </summary>
        internal bool Released { get; private set; }

        /// <summary>
        /// 获取本轮生成专属的生命周期作用域；归还后保持已结束，不会在复用时重置。
        /// </summary>
        public LifetimeScope SpawnScope { get; }

        /// <summary>
        /// 获取句柄与 Unity 对象是否仍然可用。
        /// </summary>
        public bool IsValid => !Released && !Owner.IsDisposed && _gameObject != null;

        /// <summary>
        /// 获取本轮对象；归还、外部销毁或关闭池后返回空。
        /// </summary>
        public GameObject GameObject => IsValid ? _gameObject : null;

        /// <summary>
        /// 获取本轮对象的 Transform；句柄失效后返回空。
        /// </summary>
        public Transform Transform => GameObject != null ? _gameObject.transform : null;

        /// <summary>
        /// 将本轮使用标记为结束；此操作不会触发 Unity 生命周期。
        /// </summary>
        internal void Invalidate() => Released = true;

        /// <summary>
        /// 归还本轮实例；多次调用没有副作用。
        /// </summary>
        public void Dispose() => Owner.Despawn(this);
    }
}
