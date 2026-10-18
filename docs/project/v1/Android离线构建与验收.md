# Android 离线构建与验收

> **v1 历史归档**：正文中的“当前”“已确认”和验收结果均指 v1 时期。当前设计采用 [V3](../v3/README.md)；本页不要求保留或恢复 V1 运行代码。

本项目新增 Android 的两阶段构建入口，目标为 **IL2CPP / ARM64 / APK**。构建通过与设备运行验收分开记录；当前没有连接 Android 设备。

## 构建入口

1. 在 Unity 2022.3.62f2 将活动平台切换到 Android，等待导入和编译。
2. 执行 **Tools → Unity Mini Game Framework → Prepare Android Player**。该入口设置 Android IL2CPP、仅 ARM64，并关闭 App Bundle 与 Gradle 工程导出；使用当前开发构建开关生成对应桥接代码及 AOT 元数据。
3. 退出 Unity，重新打开同一工程，执行 **Tools → Unity Mini Game Framework → Build Android Offline Player**。
4. 默认产物为 `DevProject/Builds/Android/Game.apk`，同目录保存 `build-report.json`。可通过 `MINIGAME_PLAYER_OUTPUT` 指定另一个 `.apk` 绝对路径。

批处理分别使用 `MiniGame.Editor.ProjectBuildPipeline.PrepareAndroid` 和 `MiniGame.Editor.ProjectBuildPipeline.BuildAndroidOffline`；首个 Unity 进程带 `-buildTarget Android`，两个阶段分别运行独立进程。原有 Windows 入口继续使用其自己的准备记录。

构建包括全部启用宿主场景（Main 必须居首且包含 Loading）、Android 专用热更 DLL、离线 GamePackage 首包和宿主 AOT 元数据。准备记录校验 Unity/HybridCLR 版本、目标与开发模式；跨平台或切换开发模式后应重新 Prepare。

## HybridCLR 工具链

本工程 HybridCLR 包版本为 **8.12.0**。若项目本地工具链尚未安装，Android 准备入口要求设置 `MINIGAME_HYBRIDCLR_SOURCE`，其值为带 `hybridclr` 子目录的完整 `libil2cpp` 路径。

本次使用包内版本清单指定的官方源码：

- `focus-creative-games/hybridclr` 的 `v8.12.0`，提交 `4c9a53517d8bcc2200cf9b73bafdda5e31d9f376`。
- `focus-creative-games/il2cpp_plus` 的 `v2022-8.11.0`，提交 `e00d1d96b1795eb744eb02cd743f702f1cf584be`。

将前者仓库内的 `hybridclr/` 子目录放入后者的 `libil2cpp/`，再将该 `libil2cpp` 路径作为本地源。安装后的项目工具链位于忽略目录 `DevProject/HybridCLRData`；更换包版本需重新准备匹配工具链。

## 平台产物与工作区

Android AOT 元数据和 StreamingAssets 使用项目内的共享生成目录；切回 Windows 后应重新 Prepare 当前平台内容。不可将 Android 的 `GameLogic.dll.bytes` 或元数据直接沿用到其他平台。DLL、构建产物、AOT 资源和本地工具链均沿用项目现有忽略规则。

当前宿主没有正式签名配置，此入口用于本地可安装包验收；正式发布仍须单独配置发布签名与渠道。

## 本轮实际验收

- 已确认 AndroidPlayer、SDK、NDK 23.1 和内置 OpenJDK 可用。
- Android Prepare 通过，生成 7 个 AOT 程序集的元数据及 Android 桥接代码。
- 2026-10-04（本地时间）Android APK 构建成功，0 错误、2 条构建警告。使用本机现有 targetSdk 36、minSdk 22；Gradle 提示随 Unity 提供的 Android Gradle Plugin 7.4.2 测试范围止于 API 33，构建成功不替代设备兼容验证。
- 产物：`DevProject/Builds/Android/Game.apk`，实际文件 **28,647,535 字节（27.3 MiB）**，包名 `com.minigamestudio.projectextraction`，版本 1.0 / versionCode 1。
- APK SHA-256：`ad5f7e06f37bbbe69832bb7e66c314f8a9a25f153460a61d06ccc7bb6bcbd1f2`。
- `apksigner verify` 通过，使用本机 Android Debug 签名；ZIP 完整性校验通过，仅包含 `arm64-v8a`，包含 `libil2cpp.so` 和 57 个离线 GamePackage 文件。
- 7 个打包 AOT 资源的哈希均与最终 Android 裁剪产物一致；Android 热更 DLL 与内容源 DLL 哈希一致。
- 构建后已恢复原 ProjectSettings，清理共享 Android AOT/首包生成资产，切回 Windows 并重新 Prepare GamePackage；项目检查及启动 PlayMode 1/1 通过。项目本地 HybridCLR 工具链保留供后续构建使用。
- `adb devices -l` 未发现连接设备；未执行安装、启动、触控、横竖屏、安全区、后台恢复或性能实测。

本轮证据在 `verification/interaction-rewards-history`。玩法侧回归与当前数值见[交互、奖励与战绩完善](交互奖励与战绩完善.md)。
