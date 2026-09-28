using System;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 将单个内容包的版本事务保存在一个 PlayerPrefs 键中。
    /// </summary>
    public sealed class PlayerPrefsVersionStateStore : IVersionStateStore
    {
        private const string KeyPrefix = "GameFramework.UpdateVersionState.";
        private readonly string _scope;

        /// <summary>
        /// 创建指定宿主、渠道和运行模式范围的版本状态仓库。
        /// </summary>
        /// <param name="scope">隔离状态的范围标识；为空时使用当前应用标识。</param>
        public PlayerPrefsVersionStateStore(string scope = null)
        {
            _scope = string.IsNullOrWhiteSpace(scope) ? ApplicationIdentity.GameId : scope;
        }

        [Serializable]
        private sealed class StateData
        {
            public string currentVersion;
            public string lastGoodVersion;
            public string pendingVersion;
            public int pendingAttemptCount;
            public int lastFailureCode;
            public string rejectedVersion;
        }

        /// <summary>
        /// 读取指定内容包版本状态。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <returns>当前持久化的版本状态。</returns>
        public PackageVersionState Load(string packageName)
        {
            StateData data = Read(packageName);
            return Snapshot(data);
        }

        /// <summary>
        /// 首次接入事务时保存当前活动版本，已有可用版本不被覆盖。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="version">当前活动版本。</param>
        /// <returns>初始化后的版本状态。</returns>
        public PackageVersionState InitializeCurrent(string packageName, string version)
        {
            RequireVersion(version);
            StateData data = Read(packageName);
            if (!string.IsNullOrEmpty(data.lastGoodVersion)) return Snapshot(data);
            data.currentVersion = version;
            data.lastGoodVersion = version;
            Write(packageName, data);
            return Snapshot(data);
        }

        /// <summary>
        /// 写入等待游戏启动确认的目标版本。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="version">候选内容版本。</param>
        public void StagePending(string packageName, string version)
        {
            RequireVersion(version);
            StateData data = Read(packageName);
            if (string.Equals(data.rejectedVersion, version, StringComparison.Ordinal))
                throw new InvalidOperationException("该候选版本此前启动失败，不能重复激活。");
            data.pendingVersion = version;
            data.pendingAttemptCount = 0;
            data.lastFailureCode = (int)UpdateErrorCode.None;
            data.rejectedVersion = string.Empty;
            Write(packageName, data);
        }

        /// <summary>
        /// 增加候选版本的启动尝试次数。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <returns>更新后的版本状态。</returns>
        public PackageVersionState IncrementPendingAttempt(string packageName)
        {
            StateData data = Read(packageName);
            if (string.IsNullOrEmpty(data.pendingVersion)) return Snapshot(data);
            if (data.pendingAttemptCount < int.MaxValue) data.pendingAttemptCount++;
            Write(packageName, data);
            return Snapshot(data);
        }

        /// <summary>
        /// 游戏启动成功后提交候选版本。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="version">成功启动的内容版本。</param>
        /// <returns>提交后的版本状态。</returns>
        public PackageVersionState CommitReady(string packageName, string version)
        {
            RequireVersion(version);
            StateData data = Read(packageName);
            if (!string.Equals(data.pendingVersion, version, StringComparison.Ordinal))
                throw new InvalidOperationException("成功启动的版本与等待确认的候选版本不一致。");
            data.currentVersion = version;
            data.lastGoodVersion = version;
            data.pendingVersion = string.Empty;
            data.pendingAttemptCount = 0;
            data.lastFailureCode = (int)UpdateErrorCode.None;
            Write(packageName, data);
            return Snapshot(data);
        }

        /// <summary>
        /// 清除失败候选版本并恢复上次可用版本标记。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="errorCode">候选版本失败原因。</param>
        /// <param name="rejectPending">是否禁止本次失败的候选版本再次激活。</param>
        /// <returns>回退后的版本状态。</returns>
        public PackageVersionState Rollback(string packageName, UpdateErrorCode errorCode, bool rejectPending = false)
        {
            StateData data = Read(packageName);
            data.currentVersion = data.lastGoodVersion;
            if (rejectPending && !string.IsNullOrEmpty(data.pendingVersion)) data.rejectedVersion = data.pendingVersion;
            data.pendingVersion = string.Empty;
            data.pendingAttemptCount = 0;
            data.lastFailureCode = (int)errorCode;
            Write(packageName, data);
            return Snapshot(data);
        }

        /// <summary>
        /// 读取 PlayerPrefs 中的单键状态。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <returns>可修改的状态数据。</returns>
        private StateData Read(string packageName)
        {
            string key = GetKey(packageName);
            string json = PlayerPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json)) return new StateData();
            StateData data = JsonUtility.FromJson<StateData>(json);
            return data ?? new StateData();
        }

        /// <summary>
        /// 将完整事务状态保存为单个 PlayerPrefs 值。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <param name="data">待保存状态。</param>
        private void Write(string packageName, StateData data)
        {
            PlayerPrefs.SetString(GetKey(packageName), JsonUtility.ToJson(data));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// 将内部数据转换为只读状态快照。
        /// </summary>
        /// <param name="data">内部状态数据。</param>
        /// <returns>只读版本状态。</returns>
        private static PackageVersionState Snapshot(StateData data)
        {
            return new PackageVersionState(data.currentVersion, data.lastGoodVersion, data.pendingVersion,
                data.pendingAttemptCount, (UpdateErrorCode)data.lastFailureCode, data.rejectedVersion);
        }

        /// <summary>
        /// 创建内容包版本事务键。
        /// </summary>
        /// <param name="packageName">内容包名称。</param>
        /// <returns>PlayerPrefs 键名。</returns>
        private string GetKey(string packageName)
        {
            if (string.IsNullOrWhiteSpace(packageName)) throw new ArgumentException("内容包名称不能为空。", nameof(packageName));
            return KeyPrefix + _scope + "." + packageName;
        }

        /// <summary>
        /// 检查待保存的版本非空。
        /// </summary>
        /// <param name="version">待保存的版本。</param>
        private static void RequireVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version)) throw new ArgumentException("内容版本不能为空。", nameof(version));
        }
    }
}
