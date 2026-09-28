# 小游戏平台存档 SDK 接入模板

小游戏即使导出为 WebGL，也应将 `HotUpdateBootSettings.SaveBackend` 设为 `PlatformSdk`，并在 Boot 启动前设置 `HotUpdateBootstrap.PlatformSaveStore = new WeChatSaveStore()` 或 `new DouyinSaveStore()`。Boot 会将它包装为 `new PlatformSaveBackend(store)`。`store` 必须实现 `GameFramework.IPlatformSaveStore`；未注入时初始化返回 `Unsupported`，不会显示保存成功。

## 微信与抖音适配步骤

在具体 SDK 安装后，分别创建 `WeChatSaveStore` 或 `DouyinSaveStore` 类。将 SDK 的异步本地键值 API 映射到以下端口：

```csharp
public interface IPlatformSaveStore
{
    string StorageIdentity { get; }
    int MaxRecordBytes { get; }
    UniTask<SaveResult> InitializeAsync();
    UniTask<SaveReadResult> GetAsync(string key);
    UniTask<SaveResult> SetAsync(string key, string record);
}
```

- `MaxRecordBytes` 按所用 SDK 的单键实际容量设置；`StorageIdentity` 可采用 `wechat://app-id/prod/local` 这样的稳定标识。
- `GetAsync` 仅在 SDK 明确报告键不存在时返回 `SaveReadResult.Missing()`；权限、网络或介质失败应返回 `Failed(ReadFailed)`。
- `SetAsync` 应等待 SDK 对写入的最终完成回调；底层操作未结束时必须保持任务在途，由会话超时返回 `Uncertain`。只有确定底层操作已结束但无法判断提交结果时，适配器才能返回 `Uncertain`；不能把发起调用视为成功。
- SDK 配额错误映射为 `QuotaExceeded`，单条超限映射为 `TooLarge`。记录以原始字符串传递，不解析或重排 JSON。
- 回调只完成一次；异常分支也要保留明确的失败结果。不要用内存字典、PlayerPrefs 或同步 API 的返回值伪装 SDK 已提交。
- `StorageIdentity` 需在重启后稳定，同一物理存储的不同适配器实例应返回相同身份。不同小游戏和环境应隔离。

适配器放在对应项目的 SDK 条件编译目录，直接调用已安装 SDK 的实际方法。安装前不添加对微信或抖音程序集的强依赖；完成后需分别在开发者工具和真机验证缺档、写入、重启读回、配额及失败回调。
