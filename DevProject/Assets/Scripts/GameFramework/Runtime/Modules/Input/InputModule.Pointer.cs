using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 输入模块的鼠标、触摸、UI 捕获和手势实现。
    /// </summary>
    public sealed partial class InputModule
    {
        private const float TapDistance = 25f;
        private const float SwipeDistance = 80f;
        private const float LongPressSeconds = 0.5f;
        private const float DoubleTapSeconds = 0.3f;
        private readonly Dictionary<int, float> _lastTapTimes = new();
        private readonly Dictionary<int, Vector2> _lastTapPositions = new();

        /// <summary>
        /// 更新鼠标和触摸指针。
        /// </summary>
        /// <param name="blocked">玩法是否被阻断。</param>
        private void UpdatePointers(bool blocked)
        {
            bool hasTouches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches.Count > 0;
            if (hasTouches)
            {
                foreach (UnityEngine.InputSystem.EnhancedTouch.Touch touch in UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches)
                {
                    int id = touch.touchId + 1;
                    bool began = touch.phase == UnityEngine.InputSystem.TouchPhase.Began;
                    bool canceled = touch.phase == UnityEngine.InputSystem.TouchPhase.Canceled;
                    bool ended = touch.phase == UnityEngine.InputSystem.TouchPhase.Ended || canceled;
                    UpdatePointer(id, touch.screenPosition, !ended, began, ended, canceled, blocked);
                    if (began) DetectDevice(Touchscreen.current);
                }
            }
            else if (Mouse.current != null)
            {
                Mouse mouse = Mouse.current;
                UpdatePointer(0, mouse.position.ReadValue(), mouse.leftButton.isPressed, mouse.leftButton.wasPressedThisFrame,
                    mouse.leftButton.wasReleasedThisFrame, false, blocked);
                if (mouse.delta.ReadValue().sqrMagnitude > 0.01f || mouse.leftButton.wasPressedThisFrame) DetectDevice(mouse);
            }
            UpdateTwoFingerGesture(blocked);
        }

        /// <summary>
        /// 更新单个指针及其捕获状态。
        /// </summary>
        /// <param name="id">指针 ID。</param>
        /// <param name="position">屏幕像素位置。</param>
        /// <param name="pressed">当前是否按住。</param>
        /// <param name="began">本帧是否按下。</param>
        /// <param name="ended">本帧是否结束。</param>
        /// <param name="canceled">是否由设备取消。</param>
        /// <param name="blocked">玩法是否被阻断。</param>
        private void UpdatePointer(int id, Vector2 position, bool pressed, bool began, bool ended, bool canceled, bool blocked)
        {
            _pointerTracks.TryGetValue(id, out PointerTrack track);
            Vector2 delta = track == null ? Vector2.zero : position - track.Position;
            var pointer = new GamePointer(id, position, delta, pressed);
            if (began)
            {
                bool captured = blocked || _activeMap == null || IsUIHit(position);
                track = new PointerTrack(position, captured);
                _pointerTracks[id] = track;
                if (!captured)
                {
                    _pointers.Add(pointer);
                    PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Pressed));
                }
                return;
            }
            if (track == null)
            {
                if (id == 0 && !pressed && !blocked && _activeMap != null && !IsUIHit(position)) _pointers.Add(pointer);
                return;
            }
            track.Position = position;
            if (!track.Captured && blocked)
            {
                track.Captured = true;
                PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Canceled));
            }
            if (canceled)
            {
                _pointerTracks.Remove(id);
                if (!track.Captured) PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Canceled));
                return;
            }
            if (!track.Captured) _pointers.Add(pointer);
            if (pressed && !track.Captured && delta.sqrMagnitude > 0.01f)
                PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Dragged));
            if (pressed && !track.Captured && !track.LongSent &&
                Time.unscaledTime - track.PressTime >= LongPressSeconds &&
                Vector2.Distance(position, track.Start) <= TapDistance)
            {
                track.LongSent = true;
                GestureEvent?.Invoke(new GameGestureEvent(GameGestureKind.LongPress, position, Vector2.zero, id));
            }
            if (!ended) return;
            _pointerTracks.Remove(id);
            if (track.Captured) return;
            string mapAtRelease = _activeMap;
            PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Released));
            if (_activeMap != mapAtRelease || GameplayBlocked) return;
            float distance = Vector2.Distance(position, track.Start);
            if (!track.LongSent && distance <= TapDistance && Time.unscaledTime - track.PressTime >= LongPressSeconds)
            {
                GestureEvent?.Invoke(new GameGestureEvent(GameGestureKind.LongPress, position, Vector2.zero, id));
                track.LongSent = true;
            }
            if (!track.LongSent && distance <= TapDistance && Time.unscaledTime - track.PressTime < LongPressSeconds)
            {
                PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Clicked));
                bool recent = _lastTapTimes.TryGetValue(id, out float lastTime) && Time.unscaledTime - lastTime <= DoubleTapSeconds;
                bool nearby = _lastTapPositions.TryGetValue(id, out Vector2 lastPosition) &&
                    Vector2.Distance(position, lastPosition) <= TapDistance;
                GameGestureKind kind = recent && nearby ? GameGestureKind.DoubleTap : GameGestureKind.Tap;
                GestureEvent?.Invoke(new GameGestureEvent(kind, position, Vector2.zero, id));
                _lastTapTimes[id] = kind == GameGestureKind.DoubleTap ? -10f : Time.unscaledTime;
                _lastTapPositions[id] = position;
            }
            else if (!track.LongSent && distance >= SwipeDistance)
                GestureEvent?.Invoke(new GameGestureEvent(GameGestureKind.Swipe, position, position - track.Start, id));
        }

        /// <summary>
        /// 用当前 UGUI 射线检查按下位置。
        /// </summary>
        /// <param name="position">屏幕像素位置。</param>
        /// <returns>是否命中 UGUI。</returns>
        private bool IsUIHit(Vector2 position)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null) return false;
            _uiHits.Clear();
            eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = position }, _uiHits);
            foreach (RaycastResult hit in _uiHits)
                if (hit.module is GraphicRaycaster) return true;
            return false;
        }

        /// <summary>
        /// 更新双指缩放和旋转手势。
        /// </summary>
        /// <param name="blocked">玩法是否被阻断。</param>
        private void UpdateTwoFingerGesture(bool blocked)
        {
            if (blocked || _activeMap == null) { _hasGesturePair = false; return; }
            GamePointer first = default;
            GamePointer second = default;
            int found = 0;
            foreach (GamePointer pointer in _pointers)
            {
                if (!pointer.IsPressed || !_pointerTracks.TryGetValue(pointer.Id, out PointerTrack track) || track.Captured) continue;
                if (found++ == 0) first = pointer;
                else { second = pointer; break; }
            }
            if (found < 2) { _hasGesturePair = false; return; }
            Vector2 difference = second.Position - first.Position;
            float distance = difference.magnitude;
            float angle = Mathf.Atan2(difference.y, difference.x) * Mathf.Rad2Deg;
            if (_hasGesturePair)
            {
                Vector2 center = (first.Position + second.Position) * 0.5f;
                float scale = _previousPinchDistance > 0.01f ? distance / _previousPinchDistance : 1f;
                if (Mathf.Abs(scale - 1f) > 0.001f)
                    GestureEvent?.Invoke(new GameGestureEvent(GameGestureKind.Pinch, center, new Vector2(scale, 0f), first.Id));
                float rotation = Mathf.DeltaAngle(_previousRotation, angle);
                if (Mathf.Abs(rotation) > 0.1f)
                    GestureEvent?.Invoke(new GameGestureEvent(GameGestureKind.Rotate, center, new Vector2(rotation, 0f), first.Id));
            }
            _hasGesturePair = true;
            _previousPinchDistance = distance;
            _previousRotation = angle;
        }

        /// <summary>
        /// 取消正在进行的玩法指针和双指手势。
        /// </summary>
        private void CancelGameplayPointers()
        {
            var snapshot = new List<KeyValuePair<int, PointerTrack>>(_pointerTracks);
            foreach (KeyValuePair<int, PointerTrack> item in snapshot)
            {
                if (!_pointerTracks.TryGetValue(item.Key, out PointerTrack current) || current != item.Value || current.Captured) continue;
                current.Captured = true;
                var pointer = new GamePointer(item.Key, current.Position, Vector2.zero, false);
                PointerEvent?.Invoke(new GamePointerEvent(pointer, GamePointerPhase.Canceled));
            }
            _hasGesturePair = false;
        }
        private sealed class PointerTrack
        {
            internal readonly Vector2 Start;
            internal readonly float PressTime;
            internal Vector2 Position;
            internal bool Captured;
            internal bool LongSent;
            /// <summary>
            /// 创建指针按下跟踪状态。
            /// </summary>
            /// <param name="position">按下位置。</param>
            /// <param name="captured">是否被 UI 或阻断层捕获。</param>
            internal PointerTrack(Vector2 position, bool captured)
            {
                Start = position;
                Position = position;
                Captured = captured;
                PressTime = Time.unscaledTime;
            }
        }
    }
}
