using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace GameFramework
{
    /// <summary>
    /// 提供无需业务 Prefab 即可使用的 Toast、确认框和全屏等待层。
    /// </summary>
    internal sealed class UIFeedbackPresenter : IDisposable
    {
        private readonly Queue<ToastRequest> _toastQueue = new();
        private readonly Dictionary<int, string> _busyMessages = new();
        private readonly List<int> _busyOrder = new();
        private readonly SemaphoreSlim _dialogGate = new(1, 1);
        private readonly CancellationTokenSource _lifetime = new();
        private readonly RectTransform _toastRoot;
        private readonly RectTransform _dialogRoot;
        private readonly RectTransform _busyRoot;
        private readonly Text _toastText;
        private readonly Text _busyText;
        private readonly Font _font;
        private UniTaskCompletionSource<bool> _activeDialog;
        private int _nextBusyId;
        private bool _processingToasts;
        private bool _disposed;

        /// <summary>
        /// 在框架 UI Canvas 下创建反馈界面的常驻容器。
        /// </summary>
        /// <param name="parent">全屏 UI 根节点。</param>
        internal UIFeedbackPresenter(RectTransform parent)
        {
            if (parent == null) throw new ArgumentNullException(nameof(parent));
            _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _toastRoot = CreateRect("Toast Layer", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _busyRoot = CreateRect("Busy Layer", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            _dialogRoot = CreateRect("Dialog Layer", parent, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);

            RectTransform toast = CreateRect("Toast", _toastRoot, new Vector2(0.5f, 0.12f), new Vector2(0.5f, 0.12f),
                new Vector2(900f, 82f), Vector2.zero);
            AddImage(toast, new Color(0.04f, 0.06f, 0.1f, 0.88f), false);
            _toastText = CreateText("Message", toast, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 27,
                TextAnchor.MiddleCenter, Color.white);
            _toastText.rectTransform.offsetMin = new Vector2(30f, 8f);
            _toastText.rectTransform.offsetMax = new Vector2(-30f, -8f);
            toast.gameObject.SetActive(false);

            AddImage(_busyRoot, new Color(0f, 0f, 0f, 0.62f), true);
            _busyText = CreateText("Message", _busyRoot, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(900f, 120f), Vector2.zero, 32, TextAnchor.MiddleCenter, Color.white);
            _busyRoot.gameObject.SetActive(false);
        }

        /// <summary>
        /// 将非模态提示加入显示队列。
        /// </summary>
        /// <param name="message">提示内容。</param>
        /// <param name="durationSeconds">显示时长，单位为秒。</param>
        internal void ShowToast(string message, float durationSeconds)
        {
            if (_disposed || string.IsNullOrWhiteSpace(message)) return;
            _toastQueue.Enqueue(new ToastRequest(message, Mathf.Clamp(durationSeconds, 0.5f, 30f)));
            if (_processingToasts) return;
            ProcessToastQueueAsync().Forget(LogAsyncFailure);
        }

        /// <summary>
        /// 按顺序显示带确认和取消按钮的模态弹窗。
        /// </summary>
        /// <param name="title">弹窗标题。</param>
        /// <param name="message">弹窗正文。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="cancelText">取消按钮文字。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>用户是否点击确认。</returns>
        internal UniTask<bool> ConfirmAsync(string title, string message, string confirmText, string cancelText, CancellationToken ct)
        {
            return ShowDialogAsync(title, message, confirmText, cancelText, true, ct);
        }

        /// <summary>
        /// 显示只有确认按钮的模态弹窗。
        /// </summary>
        /// <param name="title">弹窗标题。</param>
        /// <param name="message">弹窗正文。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>弹窗关闭后的异步操作。</returns>
        internal async UniTask AlertAsync(string title, string message, string confirmText, CancellationToken ct)
        {
            await ShowDialogAsync(title, message, confirmText, string.Empty, false, ct);
        }

        /// <summary>
        /// 显示全屏等待层并为调用方建立独立持有句柄。
        /// </summary>
        /// <param name="message">等待说明。</param>
        /// <returns>等待层持有句柄。</returns>
        internal IUIBusyHandle BeginBusy(string message)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIFeedbackPresenter));
            int id = ++_nextBusyId;
            _busyMessages.Add(id, message ?? string.Empty);
            _busyOrder.Add(id);
            RefreshBusy();
            return new BusyHandle(this, id);
        }

        /// <summary>
        /// 当前是否显示等待层或模态反馈。
        /// </summary>
        internal bool IsBlockingGameplay => _busyMessages.Count > 0 || _activeDialog != null;

        /// <summary>
        /// 优先处理等待层和当前模态弹窗的返回键。
        /// </summary>
        /// <returns>返回键是否已由反馈层处理。</returns>
        internal bool TryHandleBack()
        {
            if (_busyMessages.Count > 0) return true;
            if (_activeDialog == null) return false;
            _activeDialog.TrySetResult(false);
            return true;
        }

        /// <summary>
        /// 取消等待中的反馈请求并销毁界面容器。
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _lifetime.Cancel();
            _activeDialog?.TrySetResult(false);
            _toastQueue.Clear();
            _busyMessages.Clear();
            _busyOrder.Clear();
            DestroyUnityObject(_toastRoot.gameObject);
            DestroyUnityObject(_dialogRoot.gameObject);
            DestroyUnityObject(_busyRoot.gameObject);
            _lifetime.Dispose();
        }

        /// <summary>
        /// 顺序播放 Toast 请求，并使用不受 Time.timeScale 影响的时间。
        /// </summary>
        /// <returns>队列处理操作。</returns>
        private async UniTask ProcessToastQueueAsync()
        {
            _processingToasts = true;
            GameObject toast = _toastText.transform.parent.gameObject;
            while (!_disposed && _toastQueue.Count > 0)
            {
                ToastRequest request = _toastQueue.Dequeue();
                _toastText.text = request.Message;
                toast.SetActive(true);
                bool cancelled = await UniTask.Delay(TimeSpan.FromSeconds(request.DurationSeconds), ignoreTimeScale: true,
                    cancellationToken: _lifetime.Token).SuppressCancellationThrow();
                if (cancelled) break;
            }
            if (toast != null) toast.SetActive(false);
            _processingToasts = false;
        }

        /// <summary>
        /// 串行化模态弹窗并等待用户选择。
        /// </summary>
        /// <param name="title">弹窗标题。</param>
        /// <param name="message">弹窗正文。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="cancelText">取消按钮文字。</param>
        /// <param name="showCancel">是否显示取消按钮。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>用户是否确认。</returns>
        private async UniTask<bool> ShowDialogAsync(string title, string message, string confirmText, string cancelText,
            bool showCancel, CancellationToken ct)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(UIFeedbackPresenter));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, _lifetime.Token);
            await _dialogGate.WaitAsync(linked.Token);
            DialogView dialog = null;
            try
            {
                dialog = CreateDialog(title, message, confirmText, cancelText, showCancel);
                var completion = new UniTaskCompletionSource<bool>();
                _activeDialog = completion;
                dialog.Confirm.onClick.AddListener(() => completion.TrySetResult(true));
                if (showCancel) dialog.Cancel.onClick.AddListener(() => completion.TrySetResult(false));
                return await completion.Task.AttachExternalCancellation(linked.Token);
            }
            finally
            {
                _activeDialog = null;
                if (dialog != null) DestroyUnityObject(dialog.Root);
                _dialogGate.Release();
            }
        }

        /// <summary>
        /// 创建覆盖全屏的遮罩和可交互确认面板。
        /// </summary>
        /// <param name="title">弹窗标题。</param>
        /// <param name="message">弹窗正文。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="cancelText">取消按钮文字。</param>
        /// <param name="showCancel">是否显示取消按钮。</param>
        /// <returns>创建完成的弹窗视图。</returns>
        private DialogView CreateDialog(string title, string message, string confirmText, string cancelText, bool showCancel)
        {
            RectTransform backdrop = CreateRect("Modal Dialog", _dialogRoot, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero);
            AddImage(backdrop, new Color(0f, 0f, 0f, 0.68f), true);
            RectTransform panel = CreateRect("Panel", backdrop, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(760f, 400f), Vector2.zero);
            AddImage(panel, new Color(0.11f, 0.15f, 0.22f, 1f), true);
            Text heading = CreateText("Title", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(680f, 72f), new Vector2(0f, 125f), 35, TextAnchor.MiddleCenter, Color.white);
            heading.fontStyle = FontStyle.Bold;
            heading.text = title ?? string.Empty;
            Text body = CreateText("Message", panel, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(660f, 160f), new Vector2(0f, 15f), 26, TextAnchor.MiddleCenter, new Color(0.84f, 0.88f, 0.95f));
            body.text = message ?? string.Empty;
            Button confirm = CreateButton("Confirm", panel, showCancel ? new Vector2(165f, -132f) : new Vector2(0f, -132f),
                confirmText, new Color(0.2f, 0.56f, 0.86f));
            Button cancel = showCancel ? CreateButton("Cancel", panel, new Vector2(-165f, -132f), cancelText,
                new Color(0.29f, 0.34f, 0.43f)) : null;
            return new DialogView(backdrop.gameObject, confirm, cancel);
        }

        /// <summary>
        /// 创建一个普通 UGUI 按钮。
        /// </summary>
        /// <param name="name">按钮名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="position">中心偏移。</param>
        /// <param name="label">按钮文字。</param>
        /// <param name="color">按钮底色。</param>
        /// <returns>按钮组件。</returns>
        private Button CreateButton(string name, Transform parent, Vector2 position, string label, Color color)
        {
            RectTransform rect = CreateRect(name, parent, new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                new Vector2(220f, 65f), position);
            Image image = AddImage(rect, color, true);
            Button button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Text text = CreateText("Label", rect, Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, 26,
                TextAnchor.MiddleCenter, Color.white);
            text.text = label ?? string.Empty;
            return button;
        }

        /// <summary>
        /// 创建铺满或固定大小的 RectTransform。
        /// </summary>
        /// <param name="name">节点名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="anchorMin">最小锚点。</param>
        /// <param name="anchorMax">最大锚点。</param>
        /// <param name="size">尺寸。</param>
        /// <param name="position">锚点偏移。</param>
        /// <returns>创建的矩形节点。</returns>
        private static RectTransform CreateRect(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            return rect;
        }

        /// <summary>
        /// 为矩形节点添加图片并设置输入阻断。
        /// </summary>
        /// <param name="rect">图片节点。</param>
        /// <param name="color">图片颜色。</param>
        /// <param name="raycastTarget">是否拦截指针射线。</param>
        /// <returns>创建的图片组件。</returns>
        private static Image AddImage(RectTransform rect, Color color, bool raycastTarget)
        {
            Image image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = raycastTarget;
            return image;
        }

        /// <summary>
        /// 创建统一的 UGUI 文本。
        /// </summary>
        /// <param name="name">文本节点名称。</param>
        /// <param name="parent">父节点。</param>
        /// <param name="anchorMin">最小锚点。</param>
        /// <param name="anchorMax">最大锚点。</param>
        /// <param name="size">尺寸。</param>
        /// <param name="position">锚点偏移。</param>
        /// <param name="fontSize">字号。</param>
        /// <param name="alignment">文本对齐方式。</param>
        /// <param name="color">文字颜色。</param>
        /// <returns>创建的文本组件。</returns>
        private Text CreateText(string name, Transform parent, Vector2 anchorMin, Vector2 anchorMax, Vector2 size, Vector2 position,
            int fontSize, TextAnchor alignment, Color color)
        {
            Text text = CreateRect(name, parent, anchorMin, anchorMax, size, position).gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        /// <summary>
        /// 更新或释放一个等待层持有者。
        /// </summary>
        /// <param name="id">持有者 ID。</param>
        /// <param name="message">新文字；为空表示释放持有者。</param>
        /// <param name="release">是否释放持有者。</param>
        private void UpdateBusy(int id, string message, bool release)
        {
            if (_disposed || !_busyMessages.ContainsKey(id)) return;
            if (release)
            {
                _busyMessages.Remove(id);
                _busyOrder.Remove(id);
            }
            else
            {
                _busyMessages[id] = message ?? string.Empty;
            }
            RefreshBusy();
        }

        /// <summary>
        /// 将最新等待操作的说明显示在全屏遮罩上。
        /// </summary>
        private void RefreshBusy()
        {
            bool visible = _busyOrder.Count > 0;
            _busyRoot.gameObject.SetActive(visible);
            if (visible) _busyText.text = _busyMessages[_busyOrder[_busyOrder.Count - 1]];
        }

        /// <summary>
        /// 将后台 UI 反馈错误写入框架日志。
        /// </summary>
        /// <param name="exception">异步操作异常。</param>
        private static void LogAsyncFailure(Exception exception)
        {
            if (exception is OperationCanceledException) return;
            GameEntry.Get<ILogModule>()?.Error("UI", exception.ToString());
        }

        /// <summary>
        /// 在运行时或编辑器测试中销毁界面节点。
        /// </summary>
        /// <param name="target">目标对象。</param>
        private static void DestroyUnityObject(UnityEngine.Object target)
        {
            if (target == null) return;
            if (Application.isPlaying) UnityEngine.Object.Destroy(target);
            else UnityEngine.Object.DestroyImmediate(target);
        }

        /// <summary>
        /// 一条待显示的 Toast 消息。
        /// </summary>
        private readonly struct ToastRequest
        {
            /// <summary>
            /// 创建 Toast 请求。
            /// </summary>
            /// <param name="message">提示文字。</param>
            /// <param name="durationSeconds">显示秒数。</param>
            internal ToastRequest(string message, float durationSeconds)
            {
                Message = message;
                DurationSeconds = durationSeconds;
            }

            internal string Message { get; }
            internal float DurationSeconds { get; }
        }

        /// <summary>
        /// 一次模态弹窗的节点和按钮引用。
        /// </summary>
        private sealed class DialogView
        {
            /// <summary>
            /// 保存弹窗视图引用。
            /// </summary>
            /// <param name="root">弹窗根节点。</param>
            /// <param name="confirm">确认按钮。</param>
            /// <param name="cancel">取消按钮。</param>
            internal DialogView(GameObject root, Button confirm, Button cancel)
            {
                Root = root;
                Confirm = confirm;
                Cancel = cancel;
            }

            internal GameObject Root { get; }
            internal Button Confirm { get; }
            internal Button Cancel { get; }
        }

        /// <summary>
        /// 确保每次等待操作只释放自己的遮罩引用。
        /// </summary>
        private sealed class BusyHandle : IUIBusyHandle
        {
            private UIFeedbackPresenter _owner;
            private readonly int _id;

            /// <summary>
            /// 创建一次等待层持有句柄。
            /// </summary>
            /// <param name="owner">等待层所有者。</param>
            /// <param name="id">持有者 ID。</param>
            internal BusyHandle(UIFeedbackPresenter owner, int id)
            {
                _owner = owner;
                _id = id;
            }

            /// <inheritdoc />
            public void SetMessage(string message)
            {
                _owner?.UpdateBusy(_id, message, false);
            }

            /// <inheritdoc />
            public void Dispose()
            {
                UIFeedbackPresenter owner = _owner;
                _owner = null;
                owner?.UpdateBusy(_id, null, true);
            }
        }
    }
}
