using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 最近一次产生有效输入的设备类别。
    /// </summary>
    public enum InputDeviceKind { Unknown, KeyboardMouse, Touch, Gamepad }

    /// <summary>
    /// 经框架路由的动作阶段。
    /// </summary>
    public enum GameActionPhase { Started, Performed, Canceled }

    /// <summary>
    /// 按钮在当前帧的状态。
    /// </summary>
    public readonly struct InputButtonState
    {
        /// <summary>
        /// 构造按钮状态。
        /// </summary>
        /// <param name="pressedThisFrame">本帧是否按下。</param>
        /// <param name="isPressed">当前是否按住。</param>
        /// <param name="releasedThisFrame">本帧是否松开。</param>
        public InputButtonState(bool pressedThisFrame, bool isPressed, bool releasedThisFrame)
        {
            PressedThisFrame = pressedThisFrame;
            IsPressed = isPressed;
            ReleasedThisFrame = releasedThisFrame;
        }

        /// <summary>
        /// 本帧是否按下。
        /// </summary>
        public bool PressedThisFrame { get; }

        /// <summary>
        /// 当前是否按住。
        /// </summary>
        public bool IsPressed { get; }

        /// <summary>
        /// 本帧是否松开。
        /// </summary>
        public bool ReleasedThisFrame { get; }
    }

    /// <summary>
    /// 经过上下文路由的动作事件。
    /// </summary>
    public readonly struct GameActionEvent
    {
        /// <summary>
        /// 创建动作事件。
        /// </summary>
        /// <param name="id">动作 ID。</param>
        /// <param name="phase">动作阶段。</param>
        public GameActionEvent(string id, GameActionPhase phase) { Id = id; Phase = phase; }

        /// <summary>
        /// Map/Action 形式的动作 ID。
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// 动作阶段。
        /// </summary>
        public GameActionPhase Phase { get; }
    }

    /// <summary>
    /// 指针当前状态。
    /// </summary>
    public readonly struct GamePointer
    {
        /// <summary>
        /// 创建指针状态。
        /// </summary>
        /// <param name="id">稳定指针 ID。</param>
        /// <param name="position">屏幕像素位置。</param>
        /// <param name="delta">本帧位置变化。</param>
        /// <param name="isPressed">当前是否按住。</param>
        public GamePointer(int id, Vector2 position, Vector2 delta, bool isPressed)
        { Id = id; Position = position; Delta = delta; IsPressed = isPressed; }

        /// <summary>
        /// 指针 ID，鼠标为零。
        /// </summary>
        public int Id { get; }

        /// <summary>
        /// 屏幕像素位置。
        /// </summary>
        public Vector2 Position { get; }

        /// <summary>
        /// 视口坐标。
        /// </summary>
        public Vector2 ViewportPosition => new(Position.x / Mathf.Max(1, Screen.width), Position.y / Mathf.Max(1, Screen.height));

        /// <summary>
        /// 本帧位置变化。
        /// </summary>
        public Vector2 Delta { get; }

        /// <summary>
        /// 当前是否按住。
        /// </summary>
        public bool IsPressed { get; }
    }

    /// <summary>
    /// 玩法指针事件类型。
    /// </summary>
    public enum GamePointerPhase { Pressed, Dragged, Released, Clicked, Canceled }

    /// <summary>
    /// 玩法指针事件。
    /// </summary>
    public readonly struct GamePointerEvent
    {
        /// <summary>
        /// 创建指针事件。
        /// </summary>
        /// <param name="pointer">指针快照。</param>
        /// <param name="phase">事件类型。</param>
        public GamePointerEvent(GamePointer pointer, GamePointerPhase phase) { Pointer = pointer; Phase = phase; }

        /// <summary>
        /// 指针快照。
        /// </summary>
        public GamePointer Pointer { get; }

        /// <summary>
        /// 事件类型。
        /// </summary>
        public GamePointerPhase Phase { get; }
    }

    /// <summary>
    /// 手势类型。
    /// </summary>
    public enum GameGestureKind { Tap, DoubleTap, LongPress, Swipe, Pinch, Rotate }

    /// <summary>
    /// 玩法手势事件。
    /// </summary>
    public readonly struct GameGestureEvent
    {
        /// <summary>
        /// 创建手势事件。
        /// </summary>
        /// <param name="kind">手势类型。</param>
        /// <param name="position">屏幕像素位置。</param>
        /// <param name="delta">拖动位移、缩放比例变化或旋转角度。</param>
        /// <param name="pointerId">主要指针 ID。</param>
        public GameGestureEvent(GameGestureKind kind, Vector2 position, Vector2 delta, int pointerId)
        { Kind = kind; Position = position; Delta = delta; PointerId = pointerId; }

        /// <summary>
        /// 手势类型。
        /// </summary>
        public GameGestureKind Kind { get; }

        /// <summary>
        /// 屏幕像素位置。
        /// </summary>
        public Vector2 Position { get; }

        /// <summary>
        /// 手势变化值。
        /// </summary>
        public Vector2 Delta { get; }

        /// <summary>
        /// 主要指针 ID。
        /// </summary>
        public int PointerId { get; }
    }
}
