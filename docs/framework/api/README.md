# 框架 API 任务索引

由 Unity Editor 中已编译的 `GameFramework` 接口与辅助类型生成；用途与生命周期说明由 `FrameworkApiIndex` 人工维护。运行 `FrameworkApiIndex.Check()` 可检查受版本管理的签名快照。

## 记录运行日志 · ILogModule

取得：`GameEntry.Get<ILogModule>()` · 命名空间：`GameFramework`

源码：[ILogModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Log/ILogModule.cs) · 指南：[使用约定](../../../docs/framework/运行诊断.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：模块持有日志 Sink；取消：同步调用无取消；失败：按日志等级与 Sink 配置输出；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
LogLevel MinLevel { get; set; }
bool IsEnabled(LogLevel level);
void AddSink(ILogSink sink);
void Debug(string tag, string message);
void Error(string tag, string message);
void Fatal(string tag, string message);
void Flush();
void Info(string tag, string message);
void RemoveSink(ILogSink sink);
void Warn(string tag, string message);
```

## 检查资源、输入与模块状态 · IDiagnosticsModule

取得：`GameEntry.Get<IDiagnosticsModule>()` · 命名空间：`GameFramework`

源码：[IDiagnosticsModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Diagnostics/IDiagnosticsModule.cs) · 指南：[使用约定](../../../docs/framework/运行诊断.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方释放 RegisterProvider 返回的订阅；取消：同步快照无取消；失败：快照反映调用时状态；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
DiagnosticSnapshot CaptureSnapshot();
IDisposable RegisterProvider(IDiagnosticProvider provider);
bool IsLogCaptureEnabled { get; set; }
int LogCapacity { get; set; }
string ExportJson(bool prettyPrint = true);
void ClearLogs();
```

## 发布和订阅业务事件 · IEventModule

取得：`GameEntry.Get<IEventModule>()` · 命名空间：`GameFramework`

源码：[IEventModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Event/IEventModule.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：订阅者在作用域结束时退订；取消：作用域结束后不再处理；失败：回调错误按事件模块实现记录；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
void Clear(EventId id);
void ClearAll();
void Publish(EventId id);
void Publish<T>(EventId id, in T evt) where T : struct;
void Subscribe(EventId id, Action handler);
void Subscribe<T>(EventId id, Action<T> handler) where T : struct;
void Unsubscribe(EventId id, Action handler);
void Unsubscribe<T>(EventId id, Action<T> handler) where T : struct;
```

## 延迟、循环及下一帧任务 · ITimerModule

取得：`GameEntry.Get<ITimerModule>()` · 命名空间：`GameFramework`

源码：[ITimerModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Timer/ITimerModule.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：作用域持有 TimerHandle；取消：取消句柄或传入 CancellationToken；失败：取消后不执行回调；线程：Unity 主线程；暂停：缩放时间计时随玩法暂停；可选 unscaledTime。

```csharp
TimerHandle Loop(float interval, Action callback, int repeatCount = -1);
TimerHandle Loop(float interval, Action callback, int repeatCount, bool unscaledTime);
TimerHandle NextFrame(Action callback);
TimerHandle NextFrame(Action callback, bool unscaledTime);
TimerHandle Once(float delay, Action callback);
TimerHandle Once(float delay, Action callback, bool unscaledTime);
UniTask DelayAsync(float delay, CancellationToken ct = default);
UniTask DelayAsync(float delay, CancellationToken ct, bool unscaledTime);
UniTask NextFrameAsync(CancellationToken ct = default);
UniTask NextFrameAsync(CancellationToken ct, bool unscaledTime);
bool Cancel(TimerHandle handle);
void CancelAll();
```

## 播放属性动画 · ITweenModule

取得：`GameEntry.Get<ITweenModule>()` · 命名空间：`GameFramework`

源码：[ITweenModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Tween/ITweenModule.cs) · 指南：[使用约定](../../../docs/framework/通用Tween.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：传入的 LifetimeScope 持有 TweenHandle；取消：作用域结束或令牌取消时停止；失败：目标消失时动画停止；线程：Unity 主线程；暂停：默认缩放时间；UI 可指定 unscaledTime。

```csharp
TweenHandle Play(TweenClip clip, LifetimeScope scope = null, CancellationToken cancellationToken = default, bool unscaledTime = false);
int ActiveCount { get; }
void CancelAll();
```

## 保存轻量偏好 · IStorageModule

取得：`GameEntry.Get<IStorageModule>()` · 命名空间：`GameFramework`

源码：[IStorageModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Storage/IStorageModule.cs) · 指南：[使用约定](../../../docs/framework/正式存档使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：StorageModule 管理命名空间；取消：同步 API 无取消；失败：持久化依赖底层存储；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
IStorageSpaceFactory GetFactory();
IStorageSpaceFactory SetFactory(IStorageSpaceFactory factory);
StorageSpace Add(string name);
StorageSpace Get(string name);
StorageSpace GetGlobal();
bool Delete(string name);
void DeleteAll();
void SaveAll();
```

## 保存正式进度 · ISaveModule

取得：`GameEntry.Get<ISaveModule>()` · 命名空间：`GameFramework`

源码：[ISaveModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Save/Contracts/ISaveModule.cs) · 指南：[使用约定](../../../docs/framework/正式存档使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：项目持有 SaveSession 并等待 CloseAsync；取消：保存操作使用独立清理令牌；失败：检查 SaveResult 而非假定成功；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
ISaveBackend Backend { get; }
SaveResult ConfigureBackend(ISaveBackend backend);
UniTask<SaveOpenResult<T>> OpenAsync<T>(SaveIdentity identity, SaveDefinition<T> definition, SavePolicy policy = null, CancellationToken ct = default);
UniTask<SaveOpenResult<T>> RestoreToNewSlotAsync<T>(SaveRecoveryCandidate candidate, SaveIdentity targetIdentity, SaveDefinition<T> definition, SavePolicy policy = null, CancellationToken ct = default);
UniTask<SaveRecoveryInspection> InspectRecoveryAsync<T>(SaveIdentity identity, SaveDefinition<T> definition, CancellationToken ct = default);
```

## 复用普通对象 · IPoolModule

取得：`GameEntry.Get<IPoolModule>()` · 命名空间：`GameFramework`

源码：[IPoolModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Pool/IPoolModule.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：借用方归还对象；取消：同步归还无取消；失败：无效归还按池实现处理；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
ObjectPool<T> GetOrCreatePool<T>() where T : class, new();
ObjectPool<T> GetOrCreatePoolConfigured<T>() where T : class;
ObjectPool<T> GetPool<T>(string name) where T : class;
T Rent<T>() where T : class, new();
T Rent<T>(string name) where T : class;
bool IsRegistered<T>(string name = null) where T : class;
bool TryGetPool<T>(out ObjectPool<T> pool, string name = null) where T : class;
void ConfigureDefault<T>(PoolDescriptor<T> descriptor) where T : class;
void Register<T>(ObjectPool<T> pool, string name = null) where T : class;
void Register<T>(PoolDescriptor<T> descriptor, string name = null) where T : class;
void Return<T>(T item) where T : class;
void Return<T>(T item, string name) where T : class;
void ReturnRuntime(object item);
```

## 加载资产、内容场景及下载资源包 · IResourceModule

取得：`GameEntry.Get<IResourceModule>()` · 命名空间：`GameFramework`

源码：[IResourceModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Resource/IResourceModule.cs) · 指南：[使用约定](../../../docs/framework/按需资源加载.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方或作用域释放 ResourceLease；场景显式卸载；取消：异步调用传入本轮令牌；失败：检查租约、下载状态和错误；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
ResourceCacheSettings CacheSettings { get; }
ResourceHandle<T> LoadSync<T>(string location) where T : Object;
ResourceHandle<T> LoadSync<T>(string packageName, string location) where T : Object;
ResourceLoadTicket<T> Load<T>(string location, Action<ResourceLease<T>> onCompleted, Action<ResourceLoadError> onFailed, Action<float> onProgress = null, ResourceLoadOptions options = default, CancellationToken ct = default) where T : Object;
UniTask UnloadUnusedAssetsAsync();
UniTask UnloadUnusedAssetsAsync(string packageName);
UniTask<ResourceDownloadResult> DownloadAsync(ResourceDownloadRequest request, IProgress<ResourceDownloadProgress> progress = null, CancellationToken ct = default);
UniTask<ResourceGroupLease> AcquireGroupAsync(ResourceGroupDefinition definition, IProgress<float> progress = null, CancellationToken ct = default);
UniTask<ResourceHandle<T>> LoadAsync<T>(string location, CancellationToken ct = default) where T : Object;
UniTask<ResourceHandle<T>> LoadAsync<T>(string packageName, string location, CancellationToken ct = default) where T : Object;
UniTask<ResourceLease<T>> AcquireAsync<T>(string location, ResourceLoadOptions options = default, IProgress<float> progress = null, CancellationToken ct = default) where T : Object;
UniTask<ResourceLease<T>> AcquireAsync<T>(string packageName, string location, ResourceLoadOptions options = default, IProgress<float> progress = null, CancellationToken ct = default) where T : Object;
UniTask<SceneHandle> LoadSceneAsync(string location, LoadSceneMode mode = LoadSceneMode.Single, CancellationToken ct = default);
UniTask<bool> InitializePackageAsync(string packageName, Func<ResourcePackage, UniTask> initializer, bool asDefault = true);
UniTask<int> TrimAsync(bool evictAllEligible, CancellationToken ct = default);
bool BindDefaultPackage(string packageName);
bool IsInitialized { get; }
bool TryAcquire<T>(string location, out ResourceLease<T> lease) where T : Object;
bool TryEvict<T>(string location) where T : Object;
bool TryGetPackage(string packageName, out ResourcePackage package);
string DefaultPackageName { get; }
void ConfigureDownloadSupport(string packageName, bool supported);
```

## 创建可复用 Unity 实例 · IPrefabPoolModule

取得：`GameEntry.Get<IPrefabPoolModule>()` · 命名空间：`GameFramework`

源码：[IPrefabPoolModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/PrefabPool/IPrefabPoolModule.cs) · 指南：[使用约定](../../../docs/framework/Prefab实例池.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方持有 PrefabPool 并等待 DisposeAsync；取消：创建池传入本轮令牌；失败：创建或 Spawn 可返回空；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
UniTask<PrefabPool> CreatePoolAsync(string location, PrefabPoolOptions options = null, string packageName = null, CancellationToken ct = default);
int Count { get; }
```

## 读取动作与管理输入上下文 · IInputModule

取得：`GameEntry.Get<IInputModule>()` · 命名空间：`GameFramework`

源码：[IInputModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Input/IInputModule.cs) · 指南：[使用约定](../../../docs/framework/游戏输入系统使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：作用域释放上下文与订阅；取消：重绑定使用本轮令牌；失败：检查加载及重绑定结果；线程：Unity 主线程；暂停：玩法暂停时输入由上下文和业务控制。

```csharp
IDisposable BlockGameplay(string reason);
IDisposable PushContext(string map, int priority = 0);
IDisposable RegisterBackHandler(Func<bool> handler, int priority = 0);
IDisposable Subscribe(string id, Action<GameActionEvent> handler);
IReadOnlyList<GamePointer> Pointers { get; }
InputActionAsset FrameworkActions { get; }
InputButtonState GetButton(string id);
InputDeviceKind ActiveDevice { get; }
UniTask<bool> BeginRebindAsync(string id, Guid bindingId, CancellationToken ct = default);
UniTask<bool> LoadGameplayActionsAsync(string location, CancellationToken ct = default);
Vector2 GetVector2(string id);
bool TryConsumeAction(string id);
bool TryGetPointer(int pointerId, out GamePointer pointer);
bool TryGetWorldRay(Camera camera, int pointerId, out Ray ray);
event Action<GameGestureEvent> GestureEvent;
event Action<GamePointerEvent> PointerEvent;
event Action<InputDeviceKind> DeviceChanged;
float GetAxis(string id);
string GetBindingDisplayText(string id);
void ResetBinding(string id);
void UnloadGameplayActions();
```

## 运行通用流程 · IFlowModule

取得：`GameEntry.Get<IFlowModule>()` · 命名空间：`GameFramework`

源码：[IFlowModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Flow/IFlowModule.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方保存 FlowHandle 并取消；取消：Cancel 结束运行；失败：检查句柄和状态；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
FlowHandle Start(IFlow flow);
FlowState GetState(FlowHandle handle);
bool Cancel(FlowHandle handle);
bool IsRunning(FlowHandle handle);
bool Pause(FlowHandle handle);
bool Resume(FlowHandle handle);
bool TryGetFlow(FlowHandle handle, out IFlow flow);
void CancelAll();
```

## 发送 HTTP 请求 · IHttpModule

取得：`GameEntry.Get<IHttpModule>()` · 命名空间：`GameFramework`

源码：[IHttpModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Http/IHttpModule.cs) · 指南：[使用约定](../../../docs/framework/AI开发入口.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方持有请求任务；取消：传入请求令牌；失败：检查 HttpResponse；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
UniTask<HttpResponse> GetAsync(string url, CancellationToken ct = default);
UniTask<HttpResponse> PostJsonAsync(string url, string json, CancellationToken ct = default);
UniTask<HttpResponse> SendAsync(HttpRequest request, CancellationToken ct = default);
void Get(string url, Action<HttpResponse> onComplete, CancellationToken ct = default);
void PostJson(string url, string json, Action<HttpResponse> onComplete, CancellationToken ct = default);
void Send(HttpRequest request, Action<HttpResponse> onComplete, CancellationToken ct = default);
```

## 管理网络连接 · INetworkModule

取得：`GameEntry.Get<INetworkModule>()` · 命名空间：`GameFramework`

源码：[INetworkModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Network/INetworkModule.cs) · 指南：[使用约定](../../../docs/framework/AI开发入口.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方 Destroy 对应连接；取消：连接自己的操作负责取消；失败：创建和查找可能返回空；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
INetworkConnection Create(string name);
INetworkConnection Get(NetworkConnectionId id);
INetworkConnection GetByName(string name);
IReadOnlyList<INetworkConnection> Connections { get; }
bool Destroy(NetworkConnectionId id);
int DefaultMaxReceiveMessagesPerUpdate { get; set; }
int DefaultMaxReceiveQueueSize { get; set; }
int DefaultMaxSendQueueSize { get; set; }
```

## 播放音乐与音效 · IAudioModule

取得：`GameEntry.Get<IAudioModule>()` · 命名空间：`GameFramework`

源码：[IAudioModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Audio/IAudioModule.cs) · 指南：[使用约定](../../../docs/framework/音频系统.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方保存 AudioHandle 并停止循环音频；取消：加载时传入令牌；退出停止句柄；失败：检查句柄及播放状态；线程：Unity 主线程；暂停：模块响应应用后台；业务暂停策略由项目决定。

```csharp
AudioMixerGroup GetChannelMixerGroup(AudioChannel channel);
UniTask<AudioHandle> PlayAsync(string address, AudioChannel channel, AudioPlayOptions options, CancellationToken ct = default);
UniTask<AudioHandle> PlayAsync(string address, AudioChannel channel, bool loop = false, CancellationToken ct = default);
bool FadeTo(AudioHandle handle, float volume, float duration, bool stopWhenComplete = false);
bool IsApplicationPaused { get; }
bool IsPaused { get; }
bool IsPlaying(AudioHandle handle);
bool SetVoiceLimit(AudioChannel channel, int maxVoices, AudioVoiceOverflowPolicy policy = AudioVoiceOverflowPolicy.StealOldest);
float GetChannelVolume(AudioChannel channel);
float MasterVolume { get; set; }
int GetActiveCount(AudioChannel channel);
int GetVoiceLimit(AudioChannel channel);
void PauseAll();
void ResumeAll();
void SetApplicationPaused(bool paused);
void SetChannelMixerGroup(AudioChannel channel, AudioMixerGroup group);
void SetChannelVolume(AudioChannel channel, float volume);
void Stop(AudioHandle handle);
void Stop(AudioHandle handle, float fadeOutSeconds);
void StopAll();
void StopChannel(AudioChannel channel);
```

## 重置项目模型 · IModelModule

取得：`GameEntry.Get<IModelModule>()` · 命名空间：`GameFramework`

源码：[IModelModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Model/IModelModule.cs) · 指南：[使用约定](../../../docs/framework/AI开发入口.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：模块管理模型生命周期；取消：同步操作无取消；失败：模型实现负责自身错误；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
T Get<T>() where T : ModelBase;
bool IsInitialized { get; }
void InitAll();
void RegisterModel<T>(T model) where T : ModelBase;
void ResetAll(ModelScope scope, ModelResetReason reason);
void ShutdownAll();
```

## 切换逻辑场景 · ISceneModule

取得：`GameEntry.Get<ISceneModule>()` · 命名空间：`GameFramework`

源码：[ISceneModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Scene/ISceneModule.cs) · 指南：[使用约定](../../../docs/framework/关卡与单局流程.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：场景本轮内容由 SceneScope 持有；取消：切换传入进入令牌；退出使用清理令牌；失败：进入失败需清理部分内容；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
IScene CurrentScene { get; }
IScene GetScene(string name);
UniTask ChangeSceneAsync(IScene scene, SceneEnterData enterData = null, CancellationToken ct = default);
UniTask ChangeSceneAsync(string name, SceneEnterData enterData = null, CancellationToken ct = default);
UniTask ExitCurrentSceneAsync(CancellationToken ct = default);
UniTask PopSceneAsync(CancellationToken ct = default);
UniTask PreloadSceneAsync(IScene scene, CancellationToken ct = default);
UniTask PreloadSceneAsync(string name, CancellationToken ct = default);
UniTask PushSceneAsync(IScene scene, SceneEnterData enterData = null, CancellationToken ct = default);
UniTask PushSceneAsync(string name, SceneEnterData enterData = null, CancellationToken ct = default);
bool IsChanging { get; }
int StackDepth { get; }
void ChangeScene(IScene scene, SceneEnterData enterData = null);
void ChangeScene(string name, SceneEnterData enterData = null);
void ExitCurrentScene();
void PopScene();
void PushScene(IScene scene, SceneEnterData enterData = null);
void PushScene(string name, SceneEnterData enterData = null);
void RegisterScene(IScene scene);
void UnregisterScene(IScene scene);
void UnregisterScene(string name);
```

## 启动及停止玩法 · IGameplayModule

取得：`GameEntry.Get<IGameplayModule>()` · 命名空间：`GameFramework`

源码：[IGameplayModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/IGameplayModule.cs) · 指南：[使用约定](../../../docs/framework/关卡与单局流程.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方保存 GameplayHandle 并等待 StopAsync；取消：进入传本轮令牌；退出用独立令牌；失败：启动失败不可继续使用句柄；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
GameplayHandle Start(IGameplay gameplay, GameplayEnterData enterData = null);
UniTask StopAllAsync(CancellationToken ct = default);
UniTask StopAsync(GameplayHandle handle, CancellationToken ct = default);
UniTask<GameplayHandle> StartAsync(IGameplay gameplay, GameplayEnterData enterData = null, CancellationToken ct = default);
bool Contains(GameplayHandle handle);
bool IsApplicationPaused { get; }
bool IsPaused { get; }
bool TryGetGameplay(GameplayHandle handle, out IGameplay gameplay);
int Count { get; }
void SetApplicationPaused(bool paused);
void SetPaused(bool paused);
void Stop(GameplayHandle handle);
void StopAll();
```

## 管理项目服务 · IServiceModule

取得：`GameEntry.Get<IServiceModule>()` · 命名空间：`GameFramework`

源码：[IServiceModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Service/IServiceModule.cs) · 指南：[使用约定](../../../docs/framework/AI开发入口.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：模块初始化和关闭服务；取消：同步操作无取消；失败：服务实现负责自身错误；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
T Get<T>() where T : class, IAppService;
bool IsInitialized { get; }
void InitAll();
void RegisterService<T>(T service) where T : class, IAppService;
void ShutdownAll();
```

## 配置、打开和关闭窗口 · IUIModule

取得：`GameEntry.Get<IUIModule>()` · 命名空间：`GameFramework`

源码：[IUIModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/UI/IUIModule.cs) · 指南：[使用约定](../../../docs/framework/UI系统.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：UI 管理器持有窗口租约；OpenScope 持有本轮订阅；取消：打开和关闭传入对应令牌；失败：检查窗口或 bool 返回值；线程：Unity 主线程；暂停：窗口交互和非缩放动画可继续。

```csharp
IReadOnlyList<GameWindow> OpenedWindows { get; }
IUIBusyHandle BeginBusy(string message = "请稍候…");
UniTask AlertAsync(string title, string message, string confirmText = "确定", CancellationToken ct = default);
UniTask CloseAllWindowsAsync(CancellationToken ct = default);
UniTask DestroyWindowScopeAsync(GameWindowScope scope, CancellationToken ct = default);
UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args = null, CancellationToken ct = default, UILoadOptions options = null);
UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args, CancellationToken ct);
UniTask<T> RunWithLoadingAsync<T>(Func<CancellationToken, UniTask<T>> operation, UILoadOptions options = null, CancellationToken ct = default);
UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args = null, CancellationToken ct = default, UILoadOptions options = null) where TWindow : GameWindow;
UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args, CancellationToken ct) where TWindow : GameWindow;
UniTask<bool> CloseTopWindowAsync(CancellationToken ct = default);
UniTask<bool> CloseWindowAsync(int id, GameWindowCloseReason reason = GameWindowCloseReason.User, CancellationToken ct = default);
UniTask<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消", CancellationToken ct = default);
bool BlocksGameplayInput { get; }
bool IsReady { get; }
void ConfigureWindows(IEnumerable<GameWindowConfig> configs, Action<GameWindowRegistry> register);
void ShowToast(string message, float durationSeconds = 2.5f);
```

## 托管本轮生命周期 · LifetimeScope

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[LifetimeScope](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Core/Lifetime/LifetimeScope.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：创建者持有并释放作用域；取消：Stop 取消本轮；Dispose 完成同步清理；失败：失活后登记立即清理；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
CancellationToken Token { get; }
LifetimeScopeState State { get; }
bool IsActive { get; }
int ChildCount { get; }
int RegistrationCount { get; }
public LifetimeScope CreateChild(string name = null);
public LifetimeScope(string name = null, CancellationToken cancellationToken = default);
public ScopeRegistration Defer(Action cleanup);
public bool TryOwn(IDisposable disposable);
public bool TryOwn(IDisposable disposable, out ScopeRegistration registration);
public void Dispose();
public void Stop();
string Name { get; }
```

## 将事件、输入、计时器和资源绑定到作用域 · LifetimeScopeExtensions

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[LifetimeScopeExtensions](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Core/Lifetime/LifetimeScopeExtensions.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：调用方的 LifetimeScope 持有登记；取消：作用域停止后回调失活；失败：迟到资源自动释放；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public ScopeRegistration BlockGameplay(LifetimeScope scope, IInputModule input, string reason);
public ScopeRegistration Loop(LifetimeScope scope, ITimerModule timers, float interval, Action callback, int repeatCount = -1, bool unscaledTime = false);
public ScopeRegistration NextFrame(LifetimeScope scope, ITimerModule timers, Action callback, bool unscaledTime = false);
public ScopeRegistration Once(LifetimeScope scope, ITimerModule timers, float delay, Action callback, bool unscaledTime = false);
public ScopeRegistration PushContext(LifetimeScope scope, IInputModule input, string map, int priority = 0);
public ScopeRegistration RegisterBackHandler(LifetimeScope scope, IInputModule input, Func<bool> handler, int priority = 0);
public ScopeRegistration Subscribe(LifetimeScope scope, IEventModule events, EventId id, Action handler);
public ScopeRegistration Subscribe<T>(LifetimeScope scope, IEventModule events, EventId id, Action<T> handler) where T : struct;
public ScopeRegistration SubscribeGesture(LifetimeScope scope, IInputModule input, Action<GameGestureEvent> handler);
public ScopeRegistration SubscribeInput(LifetimeScope scope, IInputModule input, string id, Action<GameActionEvent> handler);
public ScopeRegistration SubscribePointer(LifetimeScope scope, IInputModule input, Action<GamePointerEvent> handler);
public UniTask<ResourceGroupLease> AcquireGroupAsync(LifetimeScope scope, IResourceModule resources, ResourceGroupDefinition definition, IProgress<float> progress = null, CancellationToken ct = default);
public UniTask<ResourceLease<T>> AcquireAsync<T>(LifetimeScope scope, IResourceModule resources, string location, ResourceLoadOptions options = default, IProgress<float> progress = null, CancellationToken ct = default) where T : Object;
public UniTask<ResourceLease<T>> AcquireAsync<T>(LifetimeScope scope, IResourceModule resources, string packageName, string location, ResourceLoadOptions options = default, IProgress<float> progress = null, CancellationToken ct = default) where T : Object;
```

## 实现逻辑场景生命周期 · SceneBase

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[SceneBase](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Scene/SceneBase.cs) · 指南：[使用约定](../../../docs/framework/关卡与单局流程.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：SceneScope 持有本轮工作；取消：退出使用独立令牌；失败：进入失败时释放本轮作用域；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
CancellationToken SceneToken { get; }
LifetimeScope SceneScope { get; set; }
SceneState State { get; }
bool IsActive { get; set; }
bool IsEntering { get; set; }
bool IsExiting { get; set; }
bool IsInited { get; set; }
bool IsPreloaded { get; set; }
protected SceneBase(string name);
protected UniTask OnEnterSceneAsync(SceneEnterData enterData, CancellationToken ct);
protected UniTask OnExitSceneAsync(CancellationToken ct);
protected UniTask OnInitSceneAsync(CancellationToken ct);
protected UniTask OnPreloadSceneAsync(CancellationToken ct);
protected void OnForceExit();
protected void OnUpdate(float deltaTime);
public UniTask EnterSceneAsync(SceneEnterData enterData = null, CancellationToken ct = default);
public UniTask ExitSceneAsync(CancellationToken ct = default);
public UniTask InitSceneAsync(CancellationToken ct = default);
public UniTask PreloadSceneAsync(CancellationToken ct = default);
public void ForceExit();
public void Update(float deltaTime);
string Name { get; }
```

## 实现玩法根节点 · GameplayRoot

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[GameplayRoot](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/GameplayRoot.cs) · 指南：[使用约定](../../../docs/framework/关卡与单局流程.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：GameplayScope 持有本轮工作；取消：退出令牌独立于 GameplayToken；失败：进入失败时清理本轮工作；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
CancellationToken GameplayToken { get; }
GameplayState State { get; set; }
LifetimeScope GameplayScope { get; set; }
bool IsActive { get; set; }
bool IsDestroyed { get; set; }
bool IsEntering { get; set; }
bool IsExiting { get; set; }
bool IsInited { get; set; }
protected GameplayRoot(string name = null);
protected UniTask OnEnterGameplayAsync(GameplayEnterData enterData, CancellationToken ct);
protected UniTask OnExitGameplayAsync(CancellationToken ct);
protected UniTask OnInitGameplayAsync(CancellationToken ct);
protected void OnDestroyGameplay();
protected void OnFixedUpdateGameplay(float deltaTime);
protected void OnForceExit();
protected void OnLateUpdateGameplay(float deltaTime);
protected void OnUpdateGameplay(float deltaTime);
public T AddFeature<T>() where T : GameplayFeature, new();
public T AddFeature<T>(T feature) where T : GameplayFeature;
public T AddModel<T>() where T : GameplayModel, new();
public T AddModel<T>(T model) where T : GameplayModel;
public T GetFeature<T>() where T : GameplayFeature;
public T GetModel<T>() where T : GameplayModel;
public UniTask EnterGameplayAsync(GameplayEnterData enterData = null, CancellationToken ct = default);
public UniTask ExitGameplayAsync(CancellationToken ct = default);
public UniTask InitGameplayAsync(CancellationToken ct = default);
public void DestroyGameplay();
public void FixedUpdate(float deltaTime);
public void ForceExit();
public void LateUpdate(float deltaTime);
public void Update(float deltaTime);
string Name { get; }
```

## 实现玩法功能组件 · GameplayFeature

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[GameplayFeature](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/GameplayFeature.cs) · 指南：[使用约定](../../../docs/framework/生命周期作用域使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：所属 GameplayRoot 管理 Feature；取消：Enter 后使用 GameplayScope；失败：由玩法根节点管理失败清理；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
protected GameplayFeature();
public UniTask EnterAsync(GameplayEnterData enterData = null, CancellationToken ct = default);
public UniTask ExitAsync(CancellationToken ct = default);
public UniTask InitAsync(CancellationToken ct = default);
public void Destroy();
public void FixedUpdate(float deltaTime);
public void ForceExit();
public void LateUpdate(float deltaTime);
public void Update(float deltaTime);
```

## 接入固定步长模块更新 · IFixedUpdateModule

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[IFixedUpdateModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Core/IFixedUpdateModule.cs) · 指南：[使用约定](../../../docs/framework/玩法更新阶段.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：ModuleManager 管理注册模块；取消：同步阶段无取消；失败：异常由模块派发边界处理；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public void OnFixedUpdate(float deltaTime);
```

## 接入帧末模块更新 · ILateUpdateModule

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[ILateUpdateModule](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Core/ILateUpdateModule.cs) · 指南：[使用约定](../../../docs/framework/玩法更新阶段.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：ModuleManager 管理注册模块；取消：同步阶段无取消；失败：异常由模块派发边界处理；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public void OnLateUpdate(float deltaTime);
```

## 接入玩法固定步长更新 · IFixedUpdateGameplay

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[IFixedUpdateGameplay](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/IFixedUpdateGameplay.cs) · 指南：[使用约定](../../../docs/framework/玩法更新阶段.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：玩法生命周期控制派发；取消：暂停或退出停止后续回调；失败：旧玩法无需实现该接口；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public void FixedUpdate(float deltaTime);
```

## 接入玩法帧末更新 · ILateUpdateGameplay

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[ILateUpdateGameplay](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/ILateUpdateGameplay.cs) · 指南：[使用约定](../../../docs/framework/玩法更新阶段.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：玩法生命周期控制派发；取消：暂停或退出停止后续回调；失败：旧玩法无需实现该接口；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public void LateUpdate(float deltaTime);
```

## 显示大量固定尺寸列表项 · GameVirtualList

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[GameVirtualList](../../../DevProject/Assets/Scripts/GameFramework/Runtime/UI/Controls/GameVirtualList.cs) · 指南：[使用约定](../../../docs/framework/UI虚拟列表.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：列表持有可见项及缓冲实例；取消：禁用或销毁结束条目作用域；失败：配置无效时拒绝初始化；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
int Count { get; }
int InstanceCount { get; }
int VisibleCount { get; }
public GameVirtualList();
public GameVirtualListItem GetVisibleItem(int index);
public void Configure(ScrollRect scroll, GameVirtualListItem prefab, float extent, float gap = 0f, bool isHorizontal = false, int buffer = 1);
public void Refresh();
public void RefreshItem(int index);
public void ScrollTo(int index);
public void SetCount(int count);
```

## 实现可复用列表项 · GameVirtualListItem

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[GameVirtualListItem](../../../DevProject/Assets/Scripts/GameFramework/Runtime/UI/Controls/GameVirtualListItem.cs) · 指南：[使用约定](../../../docs/framework/UI虚拟列表.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：BindingScope 持有本次绑定工作；取消：异步资源使用绑定令牌并检查绑定轮次；失败：回收后迟到结果不得写入；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
LifetimeScope BindingScope { get; set; }
int Index { get; set; }
protected void OnDestroy();
public GameVirtualListItem();
public void Bind(int index, LifetimeScope scope);
public void Unbind();
```

## 实现窗口生命周期 · GameWindow

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[GameWindow](../../../DevProject/Assets/Scripts/GameFramework/Runtime/UI/Windows/GameWindow.cs) · 指南：[使用约定](../../../docs/framework/UI系统.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：OpenScope 持有本轮监听；取消：关闭使 OpenScope 失活；失败：重复 Show 只刷新显示；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
GameObject Root { get; set; }
GameWindowConfig Config { get; set; }
GameWindowState State { get; set; }
LifetimeScope InstanceScope { get; set; }
LifetimeScope OpenScope { get; set; }
RectTransform RootTransform { get; set; }
int Id { get; }
protected GameWindow();
protected T FindRequired<T>(string relativePath) where T : Component;
protected UniTask<bool> CloseSelfAsync(GameWindowCloseReason reason = GameWindowCloseReason.User);
protected void BindComponents();
protected void Close(GameWindowCloseReason reason);
protected void Destroy();
protected void Init();
protected void Open(GameWindowOpenArgs args);
protected void Show(GameWindowOpenArgs args);
```

## 创建串行与并行动画片段 · Tween

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[Tween](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Tween/TweenClip.cs) · 指南：[使用约定](../../../docs/framework/通用Tween.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：ITweenModule.Play 返回播放句柄；取消：作用域或句柄结束播放；失败：目标销毁后不再写入；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public TweenClip Delay(float duration);
public TweenClip Loop(TweenClip clip, int count = -1, bool yoyo = false);
public TweenClip Parallel(TweenClip[] clips);
public TweenClip Sequence(TweenClip[] clips);
public TweenClip To(Color from, Color to, float duration, Action<Color> setter, TweenEase ease = TweenEase.Linear, Object target = null);
public TweenClip To(Quaternion from, Quaternion to, float duration, Action<Quaternion> setter, TweenEase ease = TweenEase.Linear, Object target = null);
public TweenClip To(Vector2 from, Vector2 to, float duration, Action<Vector2> setter, TweenEase ease = TweenEase.Linear, Object target = null);
public TweenClip To(Vector3 from, Vector3 to, float duration, Action<Vector3> setter, TweenEase ease = TweenEase.Linear, Object target = null);
public TweenClip To(float from, float to, float duration, Action<float> setter, TweenEase ease = TweenEase.Linear, Object target = null);
```

## 绑定动画目标属性 · TweenTargets

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[TweenTargets](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Tween/TweenTargets.cs) · 指南：[使用约定](../../../docs/framework/通用Tween.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：目标对象由业务持有；取消：作用域取消动画；失败：目标销毁后停止写入；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
public TweenClip AnchorTo(RectTransform target, Vector2 to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip ColorTo(Graphic target, Color to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip FadeTo(CanvasGroup target, float to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip MoveLocalTo(Transform target, Vector3 to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip MoveTo(Transform target, Vector3 to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip RotateLocalTo(Transform target, Quaternion to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip ScaleTo(Transform target, Vector3 to, float duration, TweenEase ease = TweenEase.Linear);
public TweenClip VolumeTo(AudioSource target, float to, float duration, TweenEase ease = TweenEase.Linear);
```

## 生成、回收并释放 Prefab 实例 · PrefabPool

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[PrefabPool](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/PrefabPool/PrefabPool.cs) · 指南：[使用约定](../../../docs/framework/Prefab实例池.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：场景或玩法持有池；取消：等待 DisposeAsync 完成租约释放；失败：Spawn 失败时返回空；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
bool IsDisposed { get; }
int CountActive { get; }
int CountAll { get; }
int CountInactive { get; }
public PrefabInstance Spawn(Transform parent = null);
public PrefabInstance Spawn(Vector3 position, Quaternion rotation, Transform parent = null);
public UniTask DisposeAsync();
public bool Despawn(PrefabInstance instance);
public int Prewarm(int count);
public void ClearInactive();
public void Dispose();
string Location { get; }
string PackageName { get; }
```

## 管理一次 Prefab 生成轮次 · PrefabInstance

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[PrefabInstance](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/PrefabPool/PrefabInstance.cs) · 指南：[使用约定](../../../docs/framework/Prefab实例池.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：PrefabPool 持有实例；取消：Despawn 结束 SpawnScope；失败：失效句柄不可继续使用；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
GameObject GameObject { get; }
LifetimeScope SpawnScope { get; }
Transform Transform { get; }
bool IsValid { get; }
public void Dispose();
```

## 激活、写入并关闭正式存档会话 · SaveSession<T>

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[SaveSession<T>](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Save/Session/SaveSession.cs) · 指南：[使用约定](../../../docs/framework/正式存档使用指南.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：项目持有会话并等待 CloseAsync；取消：关闭用独立清理令牌；失败：检查 SaveResult 和会话状态；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
SaveLoadResult<T> LoadResult { get; }
event Action<SaveSessionStatus> StatusChanged;
public SaveResult AcceptRecovery();
public SaveResult Activate();
public SaveResult BindSnapshot(Func<T> snapshot);
public SaveResult MarkDirty();
public SaveSessionStatus GetStatus();
public UniTask<SaveFlushResult> FlushAsync(CancellationToken ct = default);
public UniTask<SaveFlushResult> RetryAsync(CancellationToken ct = default);
public UniTask<SaveResult> CloseAsync(CancellationToken ct = default);
public void Dispose();
public void OnBackground(bool background);
public void Tick();
```

## 管理单局进入、结算和返回菜单 · GameRoundFlow

辅助类型，不经 `GameEntry.Get<T>()` 获取。命名空间：`GameFramework`

源码：[GameRoundFlow](../../../DevProject/Assets/Scripts/GameFramework/Runtime/Modules/Gameplay/Flow/GameRoundFlow.cs) · 指南：[使用约定](../../../docs/framework/关卡与单局流程.md) · [可编译示例](../../../DevProject/Assets/Tests/PlayMode/ApiRecipes.cs)

所有者：项目持有流程并等待 CloseAsync；取消：CancelLoading 取消进入；失败：检查状态、错误和 bool 结果；线程：Unity 主线程；暂停：按具体调用及模块文档。

```csharp
GameRoundState State { get; set; }
Nullable<GameRoundResult> Result { get; set; }
bool HasNextLevel { get; }
bool IsBusy { get; }
event Action<GameRoundState> StateChanged;
int CurrentLevelId { get; set; }
public GameRoundFlow(IGameRoundFactory factory, Func<GameRoundResult, CancellationToken, UniTask> saveResult = null);
public UniTask<bool> CloseAsync(CancellationToken ct = default);
public UniTask<bool> NextLevelAsync(CancellationToken ct = default);
public UniTask<bool> RetryAsync(CancellationToken ct = default);
public UniTask<bool> ReturnToMenuAsync(CancellationToken ct = default);
public UniTask<bool> StartLevelAsync(int levelId, CancellationToken ct = default);
public bool Pause();
public bool Resume();
public void CancelLoading();
public void Dispose();
string CurrentRoundId { get; set; }
string Error { get; set; }
```

