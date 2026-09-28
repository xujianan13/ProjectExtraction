using System;
using System.IO;
using System.Security.Cryptography;
using System.Reflection;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;
using HybridCLR;
using UnityEngine;
using YooAsset;

namespace MiniGame.Boot
{
    /// <summary>
    /// 从已激活的内容包加载热更程序集并调用其静态入口。
    /// </summary>
    public static class HotUpdateLauncher
    {
        /// <summary>
        /// 当前进程是否已加载热更 DLL，或已进入可能修改游戏状态的热更入口。
        /// </summary>
        public static bool RequiresRestart { get; private set; }

        /// <summary>
        /// 每次进入 Play Mode 时重置启动状态。
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetLaunchState()
        {
            RequiresRestart = false;
        }

        /// <summary>
        /// 加载包级配置指定的程序集并启动游戏。
        /// </summary>
        /// <param name="settings">与当前包清单一起发布的运行配置。</param>
        /// <param name="resources">已经绑定 GamePackage 的资源模块。</param>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>热更入口完成后的异步操作。</returns>
        public static async UniTask LaunchAsync(PackageRuntimeSettings settings, IResourceModule resources, CancellationToken ct)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            ct.ThrowIfCancellationRequested();

            Assembly assembly;
#if UNITY_EDITOR
            assembly = FindLoadedAssembly(settings.AssemblyName);
            if (assembly == null) throw new InvalidOperationException($"编辑器中未编译热更程序集：{settings.AssemblyName}");
#else
            if (FindLoadedAssembly(settings.AssemblyName) != null)
            {
                RequiresRestart = true;
                throw new InvalidOperationException($"宿主已加载热更程序集：{settings.AssemblyName}。请检查 Player 构建的 HybridCLR 过滤配置。");
            }

            await LoadHostAotMetadataAsync(ct);

            for (int i = 0; i < settings.AotMetadataLocations.Count; i++)
            {
                string location = settings.AotMetadataLocations[i];
                byte[] metadata = await LoadBytesAsync(resources, location, ct);
                LoadImageErrorCode metadataResult = RuntimeApi.LoadMetadataForAOTAssembly(metadata, HomologousImageMode.SuperSet);
                if (metadataResult != LoadImageErrorCode.OK && metadataResult != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
                    throw new InvalidOperationException($"加载 AOT 元数据失败：{location}, code={metadataResult}");
            }

            string dllLocation = settings.AssemblyName + ".dll";
            byte[] hotBytes = await LoadBytesAsync(resources, dllLocation, ct);
            assembly = Assembly.Load(hotBytes);
            RequiresRestart = true;
            GameEntry.Get<ILogModule>()?.Info("HotUpdate", $"热更程序集已加载：{settings.AssemblyName}, bytes={hotBytes.Length}");
#endif
            Type entryType = assembly.GetType(settings.EntryTypeName);
            if (entryType == null) throw new InvalidOperationException($"热更入口类型不存在：{settings.EntryTypeName}");
            MethodInfo method = entryType.GetMethod(settings.EntryMethodName, BindingFlags.Public | BindingFlags.Static,
                null, new[] { typeof(PackageRuntimeSettings), typeof(CancellationToken) }, null);
            if (method == null) throw new InvalidOperationException($"热更入口方法不存在：{settings.EntryTypeName}.{settings.EntryMethodName}(PackageRuntimeSettings, CancellationToken)");

            ct.ThrowIfCancellationRequested();
            RequiresRestart = true;
            object result = method.Invoke(null, new object[] { settings, ct });
            if (result is not UniTask task)
                throw new InvalidOperationException($"热更入口必须返回 UniTask：{settings.EntryTypeName}.{settings.EntryMethodName}");
            await task;
        }

        /// <summary>
        /// 从随宿主发布的 Resources 加载当前构建的 AOT 补充元数据。
        /// </summary>
        /// <param name="ct">启动取消令牌。</param>
        /// <returns>全部宿主元数据加载完成后的异步操作。</returns>
        private static async UniTask LoadHostAotMetadataAsync(CancellationToken ct)
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                ResourceRequest request = Resources.LoadAsync<TextAsset>("HotUpdateMetadata/Manifest");
                await request.ToUniTask();
                TextAsset manifestAsset = request.asset as TextAsset;
                if (manifestAsset == null)
                    throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible,
                        "宿主缺少 AOT 元数据清单，请通过 Prepare Windows Player 重新构建安装包。", false);
                string manifestJson = manifestAsset.text;
                Resources.UnloadAsset(manifestAsset);
                ct.ThrowIfCancellationRequested();
                HostAotMetadataManifest manifest = JsonUtility.FromJson<HostAotMetadataManifest>(manifestJson);
                if (manifest?.assemblies == null || manifest.hashes == null ||
                    manifest.assemblies.Length != manifest.hashes.Length)
                    throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible, "宿主 AOT 元数据清单无效，请重新构建安装包。", false);

                for (int index = 0; index < manifest.assemblies.Length; index++)
                {
                    ct.ThrowIfCancellationRequested();
                    string name = manifest.assemblies[index];
                    if (string.IsNullOrWhiteSpace(name) || name != Path.GetFileName(name) || !name.EndsWith(".dll", StringComparison.Ordinal))
                        throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible, $"宿主 AOT 元数据名称无效：{name}", false);
                    request = Resources.LoadAsync<TextAsset>("HotUpdateMetadata/" + name);
                    await request.ToUniTask();
                    TextAsset dllAsset = request.asset as TextAsset;
                    if (dllAsset == null)
                        throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible, $"宿主缺少 AOT 元数据：{name}", false);
                    byte[] bytes = dllAsset.bytes;
                    Resources.UnloadAsset(dllAsset);
                    ct.ThrowIfCancellationRequested();
                    using var sha = SHA256.Create();
                    string hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
                    if (bytes.Length == 0 || !string.Equals(hash, manifest.hashes[index], StringComparison.Ordinal))
                        throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible, $"宿主 AOT 元数据校验失败：{name}", false);
                    LoadImageErrorCode result = RuntimeApi.LoadMetadataForAOTAssembly(bytes, HomologousImageMode.SuperSet);
                    if (result != LoadImageErrorCode.OK && result != LoadImageErrorCode.HOMOLOGOUS_ASSEMBLY_HAS_LOADED)
                        throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible, $"宿主 AOT 元数据加载失败：{name}, code={result}", false);
                }
                GameEntry.Get<ILogModule>()?.Info("HotUpdate", $"宿主 AOT 元数据已加载：{manifest.assemblies.Length}");
            }
            catch (Exception exception) when (exception is not OperationCanceledException && exception is not UpdateException)
            {
                throw new UpdateException(UpdateErrorCode.HostApplicationIncompatible,
                    "宿主 AOT 元数据加载异常，请重新构建安装包。", false, exception);
            }
        }
        [Serializable]
        private sealed class HostAotMetadataManifest
        {
            public string[] assemblies;
            public string[] hashes;
        }
        /// <summary>
        /// 从当前 AppDomain 查找程序集。
        /// </summary>
        /// <param name="assemblyName">不含扩展名的程序集名称。</param>
        /// <returns>已加载的程序集；不存在时返回空值。</returns>
        private static Assembly FindLoadedAssembly(string assemblyName)
        {
            Assembly[] loaded = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < loaded.Length; i++)
            {
                if (string.Equals(loaded[i].GetName().Name, assemblyName, StringComparison.Ordinal)) return loaded[i];
            }

            return null;
        }

        /// <summary>
        /// 从已激活的包中读取 DLL 字节。
        /// </summary>
        /// <param name="resources">默认包已经绑定的资源模块。</param>
        /// <param name="location">YooAsset 逻辑地址。</param>
        /// <param name="ct">加载取消令牌。</param>
        /// <returns>已复制的 DLL 字节。</returns>
        private static async UniTask<byte[]> LoadBytesAsync(IResourceModule resources, string location, CancellationToken ct)
        {
            ResourceHandle<TextAsset> handle = await resources.LoadAsync<TextAsset>(location, ct);
            try
            {
                if (handle.Status != EOperationStatus.Succeeded || handle.Asset == null || handle.Asset.bytes.Length == 0)
                    throw new InvalidOperationException($"加载热更字节失败：{location}, status={handle.Status}");
                return handle.Asset.bytes;
            }
            finally
            {
                handle.Release();
            }
        }
    }
}
