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
    /// 通过真实资源模块验证实例池的复用、所有权和异步释放边界。
    /// </summary>
    [TestFixture]
    public sealed class PrefabPoolModuleTests
    {
        private const string PackageName = "PrefabPoolTestPackage";
        private readonly List<GameObject> _objects = new();
        private FakeResourceBackend _backend;
        private PrefabPoolModule _module;

        /// <summary>
        /// 启动真实资源模块及可控的测试加载后端。
        /// </summary>
        [SetUp]
        public void SetUp()
        {
            FrameworkTestHelper.ResetFramework();
            PrefabPoolProbe.OnSpawnAction = null;
            PrefabPoolProbe.OnDespawnAction = null;
            _backend = new FakeResourceBackend();
            _module = new PrefabPoolModule();
            GameEntry.Register<ILogModule>(new LogModule());
            GameEntry.Register<IResourceModule>(new ResourceModule(_backend, new ResourceCacheSettings(), PackageName));
            GameEntry.Register<IPrefabPoolModule>(_module);
            GameEntry.StartFramework();
        }

        /// <summary>
        /// 停止框架并清除本测试创建的模板与外部父节点。
        /// </summary>
        [TearDown]
        public void TearDown()
        {
            FrameworkTestHelper.ResetFramework();
            PrefabPoolProbe.OnSpawnAction = null;
            PrefabPoolProbe.OnDespawnAction = null;
            foreach (GameObject gameObject in _objects)
                if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        /// <summary>
        /// 预热不能激活实例；首次和再次生成应在激活前摆好姿态，并恢复子节点结构。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator PrewarmReuse_RestoresHierarchyAndSetsPoseBeforeOnEnable()
        {
            GameObject template = CreateTemplate("reuse");
            Transform originalChild = Track(new GameObject("original")).transform;
            originalChild.SetParent(template.transform, false);
            originalChild.localPosition = new Vector3(1, 2, 3);
            var pool = CreatePool("reuse", template, new PrefabPoolOptions(1, 2));
            Assert.AreEqual(1, pool.CountInactive);
            Assert.AreEqual(0, PrefabPoolProbe.EnableCount);
            Assert.AreEqual(0, PrefabPoolProbe.SpawnCount);
            Assert.AreEqual(0, pool.Prewarm(1));
            Assert.AreEqual(1, pool.Prewarm(2));

            Vector3 position = new Vector3(10, 4, -3);
            Quaternion rotation = Quaternion.Euler(0, 73, 0);
            PrefabInstance first = pool.Spawn(position, rotation);
            GameObject reused = first.GameObject;
            Assert.AreEqual(position, first.Transform.position);
            Assert.That(Quaternion.Angle(rotation, first.Transform.rotation), Is.LessThan(0.01f));
            Assert.AreEqual(position, PrefabPoolProbe.LastEnabledPosition);
            Assert.IsTrue(PrefabPoolProbe.SpawnSawValidHandle);
            Transform child = reused.transform.Find("original");
            child.localPosition = new Vector3(8, 9, 10);
            child.SetParent(null);
            first.Dispose();
            Assert.IsFalse(first.IsValid);
            Assert.IsFalse(reused.activeSelf);
            Assert.AreEqual(1, PrefabPoolProbe.DespawnCount);
            Assert.AreSame(reused.transform, child.parent);
            Assert.AreEqual(new Vector3(1, 2, 3), child.localPosition);

            PrefabInstance second = pool.Spawn(new Vector3(-5, 0, 2), Quaternion.identity);
            Assert.AreSame(reused, second.GameObject);
            Assert.AreEqual(new Vector3(-5, 0, 2), PrefabPoolProbe.LastEnabledPosition);
            Assert.AreEqual(1, pool.CountActive);
            yield return pool.DisposeAsync().ToCoroutine();
            Assert.IsFalse(second.IsValid);
            Assert.IsTrue(_backend.GetLatest("reuse").Released);
        }

        /// <summary>
        /// 旧句柄、重复归还和跨池归还不能影响新一轮或其他池的实例。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator Handles_AreOneUseAndPoolOwned()
        {
            GameObject template = CreateTemplate("handles");
            var firstPool = CreatePool("handles", template);
            var secondPool = CreatePool("handles", template);
            Assert.AreEqual(1, _backend.LoadCount);
            PrefabInstance first = firstPool.Spawn();
            GameObject reused = first.GameObject;
            Assert.IsFalse(secondPool.Despawn(first));
            PrefabPoolProbe.OnDespawnAction = () => Assert.IsFalse(first.IsValid, "回收回调必须先使旧句柄失效。");
            Assert.IsTrue(firstPool.Despawn(first));
            PrefabPoolProbe.OnDespawnAction = null;
            Assert.IsFalse(firstPool.Despawn(first));
            Assert.IsNull(first.GameObject);
            PrefabInstance second = firstPool.Spawn();
            Assert.AreSame(reused, second.GameObject);
            first.Dispose();
            Assert.IsTrue(second.IsValid);
            Assert.AreEqual(1, firstPool.CountActive);
            Assert.AreEqual(2, _module.Count);
            yield return firstPool.DisposeAsync().ToCoroutine();
            Assert.IsFalse(_backend.GetLatest("handles").Released, "第二个独立池仍持有资源租约。");
            yield return secondPool.DisposeAsync().ToCoroutine();
            Assert.IsTrue(_backend.GetLatest("handles").Released);
            Assert.AreEqual(0, _module.Count);
        }

        /// <summary>
        /// 闲置容量只限制缓存；超额实例与清空的实例应在帧末真实销毁。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator CapacityAndClear_DestroyObjectsAfterFrame()
        {
            var pool = CreatePool("capacity", CreateTemplate("capacity"), new PrefabPoolOptions(0, 1));
            PrefabInstance first = pool.Spawn();
            PrefabInstance second = pool.Spawn();
            GameObject firstObject = first.GameObject;
            GameObject secondObject = second.GameObject;
            first.Dispose();
            second.Dispose();
            Assert.AreEqual(1, pool.CountInactive);
            Assert.AreEqual(1, pool.CountAll);
            yield return null;
            Assert.IsTrue(secondObject == null);
            Assert.IsTrue(firstObject != null);
            pool.ClearInactive();
            Assert.AreEqual(0, pool.CountAll);
            yield return null;
            Assert.IsTrue(firstObject == null);
            yield return pool.DisposeAsync().ToCoroutine();
            Assert.IsTrue(_backend.GetLatest("capacity").Released);
        }

        /// <summary>
        /// 池关闭应销毁外部父节点下的活跃实例及闲置实例，再释放底层资源操作。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator DisposeAsync_WaitsForExternalParentChildrenAndIsIdempotent()
        {
            GameObject template = CreateTemplate("dispose");
            new GameObject("detachable").transform.SetParent(template.transform, false);
            var pool = CreatePool("dispose", template, new PrefabPoolOptions(1, 2));
            GameObject parent = Track(new GameObject("PrefabPoolTest-external-parent"));
            PrefabInstance active = pool.Spawn(parent.transform);
            GameObject activeObject = active.GameObject;
            Assert.AreSame(parent.transform, active.Transform.parent);
            GameObject detached = active.Transform.Find("detachable").gameObject;
            detached.transform.SetParent(parent.transform, false);
            PrefabInstance idle = pool.Spawn();
            GameObject idleObject = idle.GameObject;
            idle.Dispose();
            UniTask first = pool.DisposeAsync();
            UniTask second = pool.DisposeAsync();
            Assert.IsTrue(pool.IsDisposed);
            Assert.IsNull(pool.Spawn());
            Assert.IsFalse(active.IsValid);
            Assert.IsFalse(_backend.GetLatest("dispose").Released);
            Assert.AreEqual(0, _module.Count);
            yield return first.ToCoroutine();
            yield return second.ToCoroutine();
            Assert.IsTrue(activeObject == null);
            Assert.IsTrue(detached == null, "原始子节点脱离根对象后仍由池负责销毁。");
            Assert.IsTrue(idleObject == null);
            Assert.AreEqual(2, PrefabPoolProbe.DespawnCount);
            Assert.IsTrue(_backend.GetLatest("dispose").Released);
        }

        /// <summary>
        /// 外部销毁活跃或闲置实例后，计数与后续生成应自动恢复。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator ExternalDestroy_PrunesStaleEntries()
        {
            GameObject template = CreateTemplate("external");
            new GameObject("detachable").transform.SetParent(template.transform, false);
            var pool = CreatePool("external", template, new PrefabPoolOptions(1, 2));
            PrefabInstance active = pool.Spawn();
            GameObject destroyedActive = active.GameObject;
            GameObject detached = active.Transform.Find("detachable").gameObject;
            detached.transform.SetParent(null, false);
            UnityEngine.Object.Destroy(destroyedActive);
            yield return null;
            Assert.IsFalse(active.IsValid);
            Assert.AreEqual(0, pool.CountActive);
            Assert.IsFalse(pool.Despawn(active));
            yield return null;
            Assert.IsTrue(detached == null, "外部销毁根对象后仍需清理脱离的原始子节点。");
            PrefabInstance next = pool.Spawn();
            Assert.IsNotNull(next);
            Assert.AreNotSame(destroyedActive, next.GameObject);
            GameObject destroyedIdle = next.GameObject;
            next.Dispose();
            UnityEngine.Object.Destroy(destroyedIdle);
            yield return null;
            Assert.AreEqual(0, pool.CountInactive);
            Assert.AreEqual(1, pool.Prewarm(1));
            yield return pool.DisposeAsync().ToCoroutine();
        }

        /// <summary>
        /// 同地址并发创建应共享一次底层加载，但每位调用方收到独立池与独立取消结果。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator ConcurrentCreate_SharedLoadIndependentCancellation()
        {
            GameObject template = CreateTemplate("concurrent");
            using var cancellation = new CancellationTokenSource();
            UniTask<global::GameFramework.PrefabPool> canceled = _module.CreatePoolAsync("concurrent", ct: cancellation.Token);
            UniTask<global::GameFramework.PrefabPool> kept = _module.CreatePoolAsync("concurrent");
            Assert.AreEqual(1, _backend.LoadCount);
            cancellation.Cancel();
            Assert.Throws<OperationCanceledException>(() => canceled.GetAwaiter().GetResult());
            _backend.GetLatest("concurrent").Succeed(template);
            var pool = kept.GetAwaiter().GetResult();
            Assert.IsNotNull(pool);
            Assert.AreEqual(1, _module.Count);
            Assert.IsNotNull(pool.Spawn());
            yield return pool.DisposeAsync().ToCoroutine();
            Assert.IsTrue(_backend.GetLatest("concurrent").Released);
        }

        /// <summary>
        /// 模块关闭取消在途加载，迟到结果不可建池；资源失败后另一次请求可重试。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator FailureRetry_AndShutdownCancelDoNotCreateLatePool()
        {
            UniTask<global::GameFramework.PrefabPool> failed = _module.CreatePoolAsync("retry");
            _backend.GetLatest("retry").Fail("expected failure");
            Assert.Throws<ResourceLoadException>(() => failed.GetAwaiter().GetResult());
            UniTask<global::GameFramework.PrefabPool> retry = _module.CreatePoolAsync("retry");
            Assert.AreEqual(2, _backend.LoadCount);
            _backend.GetLatest("retry").Succeed(CreateTemplate("retry"));
            var pool = retry.GetAwaiter().GetResult();
            Assert.IsNotNull(pool);
            yield return pool.DisposeAsync().ToCoroutine();

            UniTask<global::GameFramework.PrefabPool> pending = _module.CreatePoolAsync("late");
            FakeResourceOperation operation = _backend.GetLatest("late");
            GameEntry.StopFramework();
            Assert.Throws<OperationCanceledException>(() => pending.GetAwaiter().GetResult());
            operation.Succeed(CreateTemplate("late"));
            yield return null;
            Assert.AreEqual(0, _module.Count);
            Assert.IsTrue(operation.Released);
        }

        /// <summary>
        /// 回收应清除物理速度、粒子和拖尾残留，且不改变模板内容。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator Despawn_ResetsPhysicsAndEffects()
        {
            GameObject template = CreateTemplate("effects");
            GameObject physics3D = new GameObject("physics3D");
            physics3D.transform.SetParent(template.transform, false);
            physics3D.AddComponent<Rigidbody>().useGravity = false;
            GameObject physics2D = new GameObject("physics2D");
            physics2D.transform.SetParent(template.transform, false);
            physics2D.AddComponent<Rigidbody2D>().gravityScale = 0;
            var particle = template.AddComponent<ParticleSystem>();
            particle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            template.AddComponent<TrailRenderer>();
            var pool = CreatePool("effects", template);
            PrefabInstance instance = pool.Spawn();
            GameObject gameObject = instance.GameObject;
            Rigidbody body = gameObject.transform.Find("physics3D").GetComponent<Rigidbody>();
            Rigidbody2D body2D = gameObject.transform.Find("physics2D").GetComponent<Rigidbody2D>();
            ParticleSystem system = gameObject.GetComponent<ParticleSystem>();
            TrailRenderer trail = gameObject.GetComponent<TrailRenderer>();
            body.velocity = Vector3.one * 5;
            body.angularVelocity = Vector3.one * 2;
            body2D.velocity = Vector2.one * 3;
            body2D.angularVelocity = 4;
            system.Play();
            system.Emit(5);
            trail.AddPosition(Vector3.zero);
            trail.AddPosition(Vector3.one);
            Assert.Greater(system.particleCount, 0);
            Assert.GreaterOrEqual(trail.positionCount, 2);
            instance.Dispose();
            Assert.AreEqual(Vector3.zero, body.velocity);
            Assert.AreEqual(Vector3.zero, body.angularVelocity);
            Assert.AreEqual(Vector2.zero, body2D.velocity);
            Assert.AreEqual(0f, body2D.angularVelocity);
            Assert.AreEqual(0, system.particleCount);
            Assert.AreEqual(0, trail.positionCount);
            Assert.AreSame(gameObject, pool.Spawn().GameObject);
            yield return pool.DisposeAsync().ToCoroutine();
        }

        /// <summary>
        /// 回调异常须丢弃脏实例；回调中关闭池不能把实例放回闲置队列。
        /// </summary>
        /// <returns>逐帧执行的测试迭代器。</returns>
        [UnityTest]
        public IEnumerator CallbackFaultAndReentrantDispose_DoNotReuseDirtyObject()
        {
            var pool = CreatePool("callback", CreateTemplate("callback"));
            PrefabPoolProbe.OnSpawnAction = _ => throw new InvalidOperationException("expected spawn fault");
            Assert.IsNull(pool.Spawn());
            Assert.AreEqual(0, pool.CountInactive);
            PrefabPoolProbe.OnSpawnAction = null;
            PrefabPoolProbe.OnDespawnAction = () => throw new InvalidOperationException("expected callback fault");
            PrefabInstance first = pool.Spawn();
            GameObject dirty = first.GameObject;
            Assert.IsTrue(pool.Despawn(first));
            Assert.AreEqual(0, pool.CountInactive);
            PrefabPoolProbe.OnDespawnAction = null;
            PrefabInstance clean = pool.Spawn();
            Assert.AreNotSame(dirty, clean.GameObject);
            PrefabPoolProbe.OnDespawnAction = pool.Dispose;
            clean.Dispose();
            Assert.IsTrue(pool.IsDisposed);
            Assert.AreEqual(0, pool.CountInactive);
            yield return pool.DisposeAsync().ToCoroutine();
            Assert.IsTrue(_backend.GetLatest("callback").Released);
        }

        /// <summary>
        /// 停止整个框架时池应同步失效并释放资源租约。
        /// </summary>
        [Test]
        public void Shutdown_InvalidatesInstancesAndReleasesLease()
        {
            var pool = CreatePool("shutdown", CreateTemplate("shutdown"), new PrefabPoolOptions(1, 2));
            PrefabInstance instance = pool.Spawn();
            GameObject gameObject = instance.GameObject;
            GameEntry.StopFramework();
            Assert.IsTrue(pool.IsDisposed);
            Assert.IsFalse(instance.IsValid);
            Assert.IsFalse(gameObject.activeSelf);
            Assert.IsTrue(_backend.GetLatest("shutdown").Released);
        }

        /// <summary>
        /// 在关闭的父容器下创建默认激活的测试 Prefab，避免模板本身触发探针。
        /// </summary>
        /// <param name="location">测试资源地址。</param>
        /// <returns>包含生命周期探针的模板对象。</returns>
        private GameObject CreateTemplate(string location)
        {
            GameObject container = Track(new GameObject($"PrefabPoolTest-container-{location}"));
            container.SetActive(false);
            GameObject template = Track(new GameObject($"PrefabPoolTest-template-{location}"));
            template.transform.SetParent(container.transform, false);
            template.AddComponent<PrefabPoolProbe>();
            PrefabPoolProbe.ResetCounters();
            return template;
        }

        /// <summary>
        /// 通过可控后端完成一次真实资源获取。
        /// </summary>
        /// <param name="location">测试资源地址。</param>
        /// <param name="template">资源模板。</param>
        /// <param name="options">池选项。</param>
        /// <returns>创建完成的实例池。</returns>
        private global::GameFramework.PrefabPool CreatePool(string location, GameObject template, PrefabPoolOptions options = null)
        {
            UniTask<global::GameFramework.PrefabPool> creating = _module.CreatePoolAsync(location, options);
            _backend.GetLatest(location).Succeed(template);
            return creating.GetAwaiter().GetResult();
        }

        /// <summary>
        /// 登记仅由当前测试持有的 Unity 对象。
        /// </summary>
        /// <param name="gameObject">待登记对象。</param>
        /// <returns>同一个对象。</returns>
        private GameObject Track(GameObject gameObject)
        {
            _objects.Add(gameObject);
            return gameObject;
        }

        private sealed class FakeResourceBackend : IResourceAssetBackend
        {
            private readonly Dictionary<string, List<FakeResourceOperation>> _operations = new(StringComparer.Ordinal);

            /// <summary>
            /// 已启动的底层加载次数。
            /// </summary>
            public int LoadCount { get; private set; }

            /// <summary>
            /// 判断测试资源包是否就绪。
            /// </summary>
            /// <param name="packageName">资源包名。</param>
            /// <returns>是否为测试资源包。</returns>
            public bool IsPackageReady(string packageName) => packageName == PackageName;

            /// <summary>
            /// 创建由测试控制完成时刻的资源操作。
            /// </summary>
            /// <typeparam name="T">资源类型。</typeparam>
            /// <param name="packageName">资源包名。</param>
            /// <param name="location">资源地址。</param>
            /// <returns>待完成的资源操作。</returns>
            public IResourceAssetOperation LoadAsync<T>(string packageName, string location) where T : UnityEngine.Object
            {
                var operation = new FakeResourceOperation();
                if (!_operations.TryGetValue(location, out List<FakeResourceOperation> operations))
                {
                    operations = new List<FakeResourceOperation>();
                    _operations.Add(location, operations);
                }
                operations.Add(operation);
                LoadCount++;
                return operation;
            }

            /// <summary>
            /// 模拟卸载未使用资源。
            /// </summary>
            /// <param name="packageName">资源包名。</param>
            /// <param name="ct">取消令牌。</param>
            /// <returns>已完成的任务。</returns>
            public UniTask UnloadUnusedAssetsAsync(string packageName, CancellationToken ct) => UniTask.CompletedTask;

            /// <summary>
            /// 获取指定地址的最新底层操作。
            /// </summary>
            /// <param name="location">资源地址。</param>
            /// <returns>最新操作。</returns>
            public FakeResourceOperation GetLatest(string location) => _operations[location][_operations[location].Count - 1];
        }

        private sealed class FakeResourceOperation : IResourceAssetOperation
        {
            private readonly UniTaskCompletionSource _completion = new();

            /// <summary>
            /// 操作释放前是否有效。
            /// </summary>
            public bool IsValid => !Released;

            /// <summary>
            /// 操作是否完成。
            /// </summary>
            public bool IsDone { get; private set; }

            /// <summary>
            /// 加载进度。
            /// </summary>
            public float Progress => IsDone ? 1f : 0f;

            /// <summary>
            /// 操作状态。
            /// </summary>
            public EOperationStatus Status { get; private set; }

            /// <summary>
            /// 失败信息。
            /// </summary>
            public string Error { get; private set; } = string.Empty;

            /// <summary>
            /// 已加载资源。
            /// </summary>
            public UnityEngine.Object Asset { get; private set; }

            /// <summary>
            /// 操作是否已释放。
            /// </summary>
            public bool Released { get; private set; }

            /// <summary>
            /// 等待外部完成或取消。
            /// </summary>
            /// <param name="ct">取消令牌。</param>
            /// <returns>操作完成任务。</returns>
            public UniTask WaitAsync(CancellationToken ct) => ct.CanBeCanceled ? _completion.Task.AttachExternalCancellation(ct) : _completion.Task;

            /// <summary>
            /// 释放底层操作。
            /// </summary>
            public void Release() => Released = true;

            /// <summary>
            /// 成功完成底层加载。
            /// </summary>
            /// <param name="asset">加载的模板。</param>
            public void Succeed(UnityEngine.Object asset)
            {
                Asset = asset;
                Status = EOperationStatus.Succeeded;
                IsDone = true;
                _completion.TrySetResult();
            }

            /// <summary>
            /// 以指定信息失败。
            /// </summary>
            /// <param name="error">失败信息。</param>
            public void Fail(string error)
            {
                Error = error;
                Status = EOperationStatus.Failed;
                IsDone = true;
                _completion.TrySetResult();
            }
        }
    }

    /// <summary>
    /// 记录实例激活顺序和可控回调行为的测试组件。
    /// </summary>
    public sealed class PrefabPoolProbe : MonoBehaviour, IPrefabPoolCallbacks
    {
        /// <summary>
        /// 激活次数。
        /// </summary>
        public static int EnableCount { get; private set; }

        /// <summary>
        /// 生成回调次数。
        /// </summary>
        public static int SpawnCount { get; private set; }

        /// <summary>
        /// 回收回调次数。
        /// </summary>
        public static int DespawnCount { get; private set; }

        /// <summary>
        /// 最近一次激活时的世界坐标。
        /// </summary>
        public static Vector3 LastEnabledPosition { get; private set; }

        /// <summary>
        /// 最近一次生成回调是否收到有效句柄。
        /// </summary>
        public static bool SpawnSawValidHandle { get; private set; }

        /// <summary>
        /// 生成时执行的测试动作。
        /// </summary>
        public static Action<PrefabInstance> OnSpawnAction { get; set; }

        /// <summary>
        /// 回收时执行的测试动作。
        /// </summary>
        public static Action OnDespawnAction { get; set; }

        /// <summary>
        /// 清空计数和观察值。
        /// </summary>
        public static void ResetCounters()
        {
            EnableCount = 0;
            SpawnCount = 0;
            DespawnCount = 0;
            LastEnabledPosition = default;
            SpawnSawValidHandle = false;
        }

        /// <summary>
        /// 记录 Unity 激活回调发生时的坐标。
        /// </summary>
        private void OnEnable()
        {
            EnableCount++;
            LastEnabledPosition = transform.position;
        }

        /// <summary>
        /// 记录生成回调并执行测试注入动作。
        /// </summary>
        /// <param name="instance">本轮实例句柄。</param>
        public void OnSpawned(PrefabInstance instance)
        {
            SpawnCount++;
            SpawnSawValidHandle = instance.IsValid;
            OnSpawnAction?.Invoke(instance);
        }

        /// <summary>
        /// 记录回收回调并执行测试注入动作。
        /// </summary>
        public void OnDespawned()
        {
            DespawnCount++;
            OnDespawnAction?.Invoke();
        }
    }
}



