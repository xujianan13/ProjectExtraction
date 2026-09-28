using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using YooAsset;

namespace GameFramework.Tests.Modules.Prefabs
{
    /// <summary>
    /// 验证池实例每轮作用域的停止、复用和外部销毁边界。
    /// </summary>
    [TestFixture]
    public sealed class PrefabPoolLifetimeTests
    {
        private const string PackageName = "PrefabPoolLifetimeTest";
        private readonly List<GameObject> _objects = new();
        private PrefabPoolModule _module;
        private TemplateBackend _backend;

        /// <summary>
        /// 启动可控模板后端及对象池模块。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            LifetimeProbe.OnDespawn = null;
            _backend = new TemplateBackend();
            _module = new PrefabPoolModule();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IResourceModule>(new ResourceModule(_backend, new ResourceCacheSettings(), PackageName));
            GameEntry.Register<IPrefabPoolModule>(_module);
            GameEntry.StartFramework();
        }

        /// <summary>
        /// 关闭框架并清理测试模板。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
            LifetimeProbe.OnDespawn = null;
            foreach (GameObject gameObject in _objects)
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        /// <summary>
        /// 同一对象复用时创建新作用域，旧轮次的延迟工作不能写入新轮次。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator Reuse_CreatesNewScopeAndRejectsOldWork()
        {
            PrefabPool pool = CreatePool();
            PrefabInstance first = pool.Spawn();
            GameObject reused = first.GameObject;
            LifetimeScope oldScope = first.SpawnScope;
            bool oldTaskWrote = false;
            UniTask oldTask = WriteAfterYield(oldScope, () => oldTaskWrote = true);
            Assert.IsTrue(oldScope.IsActive);
            Assert.IsTrue(pool.Despawn(first));
            Assert.IsFalse(oldScope.IsActive);
            Assert.IsTrue(oldScope.Token.IsCancellationRequested);
            PrefabInstance second = pool.Spawn();
            Assert.AreSame(reused, second.GameObject);
            Assert.AreNotSame(oldScope, second.SpawnScope);
            Assert.IsTrue(second.SpawnScope.IsActive);
            yield return oldTask.ToCoroutine();
            Assert.IsFalse(oldTaskWrote);
            Assert.IsTrue(second.IsValid);
            yield return pool.DisposeAsync().ToCoroutine();
            Assert.IsFalse(second.SpawnScope.IsActive);
        }

        /// <summary>
        /// 外部销毁应由 OnDestroy 即时结束本轮，不依赖计数查询触发剔除。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator ExternalDestroy_StopsScopeWithoutPrune()
        {
            PrefabPool pool = CreatePool();
            PrefabInstance instance = pool.Spawn();
            LifetimeScope scope = instance.SpawnScope;
            int cleaned = 0;
            scope.Defer(() => cleaned++);
            UnityEngine.Object.Destroy(instance.GameObject);
            yield return null;
            Assert.IsFalse(instance.IsValid);
            Assert.IsFalse(scope.IsActive);
            Assert.AreEqual(1, cleaned);
            Assert.AreEqual(0, pool.CountActive);
            yield return pool.DisposeAsync().ToCoroutine();
        }

        /// <summary>
        /// 关闭池和回调异常均应按先 Stop、回调、再 Dispose 的顺序收尾。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator DisposeAndCallbackFault_StillCleanAllScopes()
        {
            PrefabPool pool = CreatePool();
            PrefabInstance first = pool.Spawn();
            LifetimeScope firstScope = first.SpawnScope;
            int cleaned = 0;
            firstScope.Defer(() => cleaned++);
            LifetimeProbe.OnDespawn = () =>
            {
                Assert.IsFalse(firstScope.IsActive);
                Assert.AreEqual(0, cleaned);
                throw new InvalidOperationException("expected callback fault");
            };
            Assert.IsTrue(pool.Despawn(first));
            Assert.AreEqual(1, cleaned);
            Assert.IsFalse(firstScope.IsActive);
            LifetimeProbe.OnDespawn = null;
            PrefabInstance second = pool.Spawn();
            LifetimeScope secondScope = second.SpawnScope;
            secondScope.Defer(() => cleaned++);
            pool.Dispose();
            Assert.IsFalse(second.IsValid);
            Assert.IsFalse(secondScope.IsActive);
            Assert.AreEqual(2, cleaned);
            yield return pool.DisposeAsync().ToCoroutine();
            Assert.IsTrue(_backend.Operation.Released);
        }

        /// <summary>
        /// 创建含生命周期探针的测试模板并完成资源加载。
        /// </summary>
        /// <returns>新建的池。</returns>
        private PrefabPool CreatePool()
        {
            GameObject container = new GameObject("PrefabPoolLifetime-container");
            container.SetActive(false);
            _objects.Add(container);
            GameObject template = new GameObject("PrefabPoolLifetime-template");
            template.transform.SetParent(container.transform, false);
            template.AddComponent<LifetimeProbe>();
            _objects.Add(template);
            _backend.Template = template;
            return _module.CreatePoolAsync("lifetime").GetAwaiter().GetResult();
        }

        /// <summary>
        /// 模拟跨越一帧后尝试修改状态的旧异步操作。
        /// </summary>
        /// <param name="scope">任务创建时的作用域。</param>
        /// <param name="write">仍存活时执行的写入。</param>
        /// <returns>异步操作。</returns>
        private static async UniTask WriteAfterYield(LifetimeScope scope, Action write)
        {
            await UniTask.Yield();
            if (scope.IsActive) write();
        }

        private sealed class TemplateBackend : IResourceAssetBackend
        {
            /// <summary>
            /// 加载所用的模板。
            /// </summary>
            public GameObject Template { get; set; }

            /// <summary>
            /// 最近一次底层操作。
            /// </summary>
            public TemplateOperation Operation { get; private set; }

            /// <summary>
            /// 判断测试包是否可用。
            /// </summary>
            /// <param name="packageName">资源包名。</param>
            /// <returns>包是否已就绪。</returns>
            public bool IsPackageReady(string packageName) => packageName == PackageName;

            /// <summary>
            /// 返回立即完成的模板加载操作。
            /// </summary>
            /// <typeparam name="T">资源类型。</typeparam>
            /// <param name="packageName">资源包名。</param>
            /// <param name="location">资源地址。</param>
            /// <returns>测试加载操作。</returns>
            public IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object
            {
                Operation = new TemplateOperation(Template);
                return Operation;
            }

            /// <summary>
            /// 测试后端没有额外待卸载资源。
            /// </summary>
            /// <param name="packageName">资源包名。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成的操作。</returns>
            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct) => UniTask.CompletedTask;
        }

        private sealed class TemplateOperation : IResourceAssetOperation
        {
            /// <summary>
            /// 创建即时完成的资源操作。
            /// </summary>
            /// <param name="asset">返回的模板。</param>
            public TemplateOperation(UnityEngine.Object asset) => Asset = asset;

            /// <summary>
            /// 资源是否仍可使用。
            /// </summary>
            public bool IsValid => !Released;

            /// <summary>
            /// 加载是否完成。
            /// </summary>
            public bool IsDone => true;

            /// <summary>
            /// 加载进度。
            /// </summary>
            public float Progress => 1f;

            /// <summary>
            /// 加载状态。
            /// </summary>
            public EOperationStatus Status => EOperationStatus.Succeeded;

            /// <summary>
            /// 错误文本。
            /// </summary>
            public string Error => string.Empty;

            /// <summary>
            /// 返回的测试模板。
            /// </summary>
            public UnityEngine.Object Asset { get; }

            /// <summary>
            /// 操作是否已释放。
            /// </summary>
            public bool Released { get; private set; }

            /// <summary>
            /// 等待已完成的操作。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成的任务。</returns>
            public UniTask WaitAsync(CancellationToken ct) => UniTask.CompletedTask;

            /// <summary>
            /// 标记底层操作已经释放。
            /// </summary>
            public void Release() => Released = true;
        }
    }

    /// <summary>
    /// 注入池回收回调行为的测试组件。
    /// </summary>
    public sealed class LifetimeProbe : MonoBehaviour, IPrefabPoolCallbacks
    {
        /// <summary>
        /// 回收时执行的测试动作。
        /// </summary>
        public static Action OnDespawn { get; set; }

        /// <summary>
        /// 生成时无需额外动作。
        /// </summary>
        /// <param name="instance">本轮实例句柄。</param>
        public void OnSpawned(PrefabInstance instance)
        {
            Assert.IsTrue(instance.SpawnScope.IsActive);
            Assert.IsTrue(instance.GameObject.activeSelf);
        }

        /// <summary>
        /// 执行可控的回收动作。
        /// </summary>
        public void OnDespawned() => OnDespawn?.Invoke();
    }
}
