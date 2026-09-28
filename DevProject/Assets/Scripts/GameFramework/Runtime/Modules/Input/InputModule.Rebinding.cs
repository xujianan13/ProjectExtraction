using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine.InputSystem;

namespace GameFramework
{
    /// <summary>
    /// 输入模块的交互重绑定与持久化实现。
    /// </summary>
    public sealed partial class InputModule
    {
        /// <inheritdoc />
        public async UniTask<bool> BeginRebindAsync(string id, Guid bindingId, CancellationToken ct = default)
        {
            id = ResolveId(id);
            if (string.IsNullOrWhiteSpace(id) || id == BackId || _rebind != null || !_actions.TryGetValue(id, out InputAction action)) return false;
            int index = FindBindingIndex(action, bindingId);
            if (index < 0 || action.bindings[index].isComposite || ct.IsCancellationRequested) return false;
            string previous = action.bindings[index].overridePath;
            action.Disable();
            var completion = new UniTaskCompletionSource<bool>();
            _rebindCompletion = completion;
            _rebind = action.PerformInteractiveRebinding(index).WithCancelingThrough("<Keyboard>/escape")
                .OnComplete(operation =>
                {
                    bool valid = !HasBindingConflict(action, index);
                    if (valid)
                    {
                        try { SaveOverrides(action.actionMap.asset); }
                        catch (Exception exception)
                        {
                            valid = false;
                            LogError($"重绑定保存失败：{exception.Message}");
                        }
                    }
                    if (!valid)
                    {
                        if (previous == null) action.RemoveBindingOverride(index);
                        else action.ApplyBindingOverride(index, previous);
                    }
                    FinishRebind(operation, action, valid);
                })
                .OnCancel(operation =>
                {
                    _suppressBackOnce = true;
                    FinishRebind(operation, action, false);
                });
            _rebind.Start();
            using (ct.Register(() => UniTask.Post(CancelRebind))) return await completion.Task;
        }

        /// <inheritdoc />
        public void ResetBinding(string id)
        {
            id = ResolveId(id);
            if (string.IsNullOrWhiteSpace(id) || id == BackId || !_actions.TryGetValue(id, out InputAction action)) return;
            action.RemoveAllBindingOverrides();
            SaveOverrides(action.actionMap.asset);
        }

        /// <inheritdoc />
        public string GetBindingDisplayText(string id)
        {
            id = ResolveId(id);
            return !string.IsNullOrWhiteSpace(id) && _actions.TryGetValue(id, out InputAction action) ? action.GetBindingDisplayString() : string.Empty;
        }

        /// <summary>
        /// 查找稳定 GUID 对应的绑定索引。
        /// </summary>
        /// <param name="action">目标动作。</param>
        /// <param name="bindingId">绑定 GUID。</param>
        /// <returns>找到的索引，找不到返回负一。</returns>
        private static int FindBindingIndex(InputAction action, Guid bindingId)
        {
            for (int i = 0; i < action.bindings.Count; i++)
                if (action.bindings[i].id == bindingId) return i;
            return -1;
        }

        /// <summary>
        /// 检测新绑定是否与同映射的其他动作冲突。
        /// </summary>
        /// <param name="action">目标动作。</param>
        /// <param name="index">绑定索引。</param>
        /// <returns>是否冲突。</returns>
        private bool HasBindingConflict(InputAction action, int index)
        {
            string path = action.bindings[index].effectivePath;
            if (string.IsNullOrEmpty(path)) return false;
            foreach (InputAction other in action.actionMap.actions)
                for (int i = 0; i < other.bindings.Count; i++)
                {
                    if (other == action && i == index) continue;
                    if (other.bindings[i].isComposite) continue;
                    if (string.Equals(other.bindings[i].effectivePath, path, StringComparison.OrdinalIgnoreCase)) return true;
                }
            return false;
        }

        /// <summary>
        /// 完成重绑定并恢复动作。
        /// </summary>
        /// <param name="operation">交互重绑定操作。</param>
        /// <param name="action">目标动作。</param>
        /// <param name="success">是否成功。</param>
        private void FinishRebind(InputActionRebindingExtensions.RebindingOperation operation, InputAction action, bool success)
        {
            _rebind = null;
            action.Enable();
            operation.Dispose();
            UniTaskCompletionSource<bool> completion = _rebindCompletion;
            _rebindCompletion = null;
            completion?.TrySetResult(success);
        }

        /// <summary>
        /// 取消当前交互重绑定。
        /// </summary>
        private void CancelRebind()
        {
            _rebind?.Cancel();
        }

        /// <summary>
        /// 保存动作资产的绑定覆盖。
        /// </summary>
        /// <param name="asset">目标动作资产。</param>
        private void SaveOverrides(InputActionAsset asset)
        {
            IStorageModule storage = GameEntry.Get<IStorageModule>();
            if (storage == null) return;
            StorageSpace space = storage.Add(StorageSpaceName);
            space.SetString(asset == _frameworkActions ? FrameworkStorageKey : GameplayStorageKey, asset.SaveBindingOverridesAsJson());
            space.Save();
        }

        /// <summary>
        /// 恢复动作资产的绑定覆盖。
        /// </summary>
        /// <param name="asset">目标动作资产。</param>
        /// <param name="key">持久化键。</param>
        private static void RestoreOverrides(InputActionAsset asset, string key)
        {
            StorageSpace space = GameEntry.Get<IStorageModule>()?.Add(StorageSpaceName);
            string json = space?.GetString(key);
            if (string.IsNullOrEmpty(json)) return;
            try { asset.LoadBindingOverridesFromJson(json); }
            catch (Exception exception)
            {
                asset.RemoveAllBindingOverrides();
                space.Delete(key);
                LogError($"忽略损坏的输入绑定覆盖：{exception.Message}");
            }
        }
    }
}
