using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;

namespace GameFramework
{
    /// <summary>
    /// 统一读取系统与玩法输入，并管理热更动作、输入焦点和指针归属。
    /// </summary>
    public interface IInputModule : IModule
    {
        /// <summary>
        /// 固定的 System/UI 动作资产，供唯一的 UGUI 事件模块共享。
        /// </summary>
        InputActionAsset FrameworkActions { get; }

        /// <summary>
        /// 当前主要输入设备类型。
        /// </summary>
        InputDeviceKind ActiveDevice { get; }

        /// <summary>
        /// 当前可供玩法使用的指针快照。
        /// </summary>
        IReadOnlyList<GamePointer> Pointers { get; }

        /// <summary>
        /// 主要输入设备改变时触发。
        /// </summary>
        event Action<InputDeviceKind> DeviceChanged;

        /// <summary>
        /// 玩法指针发生点击、拖拽或释放时触发。
        /// </summary>
        event Action<GamePointerEvent> PointerEvent;

        /// <summary>
        /// 玩法指针产生手势时触发。
        /// </summary>
        event Action<GameGestureEvent> GestureEvent;

        /// <summary>
        /// 从默认资源包加载玩法动作资产并持有租约。
        /// </summary>
        /// <param name="location">GamePackage 中的动作资产地址。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>资产是否成功加载并启用。</returns>
        UniTask<bool> LoadGameplayActionsAsync(string location, CancellationToken ct = default);

        /// <summary>
        /// 禁用玩法动作并释放克隆与资源租约。
        /// </summary>
        void UnloadGameplayActions();

        /// <summary>
        /// 创建指定动作映射的玩法输入焦点。
        /// </summary>
        /// <param name="map">玩法资产中的动作映射名称。</param>
        /// <param name="priority">焦点优先级，相同时后创建者优先。</param>
        /// <returns>释放后退出焦点的句柄。</returns>
        IDisposable PushContext(string map, int priority = 0);

        /// <summary>
        /// 临时阻断所有玩法动作与指针。
        /// </summary>
        /// <param name="reason">阻断原因。</param>
        /// <returns>释放后解除本次阻断的句柄。</returns>
        IDisposable BlockGameplay(string reason);

        /// <summary>
        /// 读取按钮的本帧快照。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <returns>按钮状态。</returns>
        InputButtonState GetButton(string id);

        /// <summary>
        /// 读取一维动作的本帧值。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <returns>动作值，无效或阻断时为零。</returns>
        float GetAxis(string id);

        /// <summary>
        /// 读取二维动作的本帧值。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <returns>动作值，无效或阻断时为零向量。</returns>
        Vector2 GetVector2(string id);

        /// <summary>
        /// 订阅经过焦点与 UI 路由的动作事件。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <param name="handler">动作事件处理器。</param>
        /// <returns>取消订阅的句柄。</returns>
        IDisposable Subscribe(string id, Action<GameActionEvent> handler);

        /// <summary>
        /// 注册优先处理系统返回动作的回调。
        /// </summary>
        /// <param name="handler">处理并消费时返回 true 的回调。</param>
        /// <param name="priority">处理优先级，较大值优先。</param>
        /// <returns>取消注册的句柄。</returns>
        IDisposable RegisterBackHandler(Func<bool> handler, int priority = 0);

        /// <summary>
        /// 消费本帧动作，防止后续玩法读取。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <returns>动作本帧是否有效且尚未被消费。</returns>
        bool TryConsumeAction(string id);

        /// <summary>
        /// 获取指定指针的本帧状态。
        /// </summary>
        /// <param name="pointerId">鼠标为零，触指使用 Input System 触指 ID。</param>
        /// <param name="pointer">指针状态。</param>
        /// <returns>是否存在该指针。</returns>
        bool TryGetPointer(int pointerId, out GamePointer pointer);

        /// <summary>
        /// 从指针的屏幕位置创建世界射线。
        /// </summary>
        /// <param name="camera">目标相机。</param>
        /// <param name="pointerId">指针 ID。</param>
        /// <param name="ray">创建的射线。</param>
        /// <returns>相机和指针是否有效。</returns>
        bool TryGetWorldRay(Camera camera, int pointerId, out Ray ray);

        /// <summary>
        /// 对指定动作的一个绑定执行交互重绑定。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <param name="bindingId">绑定的稳定 GUID。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否完成重绑定。</returns>
        UniTask<bool> BeginRebindAsync(string id, Guid bindingId, CancellationToken ct = default);

        /// <summary>
        /// 清除指定动作的绑定覆盖。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        void ResetBinding(string id);

        /// <summary>
        /// 获取指定动作的有效绑定显示文字。
        /// </summary>
        /// <param name="id">Map/Action 形式的稳定动作 ID。</param>
        /// <returns>绑定描述，未知动作返回空字符串。</returns>
        string GetBindingDisplayText(string id);
    }
}
