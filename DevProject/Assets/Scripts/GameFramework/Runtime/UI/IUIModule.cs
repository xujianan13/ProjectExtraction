using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace GameFramework
{
    /// <summary>
    /// 游戏 UI 的统一入口，负责窗口、弹窗、提示和全屏等待界面。
    /// </summary>
    public interface IUIModule : IModule
    {
        /// <summary>
        /// UI 根节点是否已建立。
        /// </summary>
        bool IsReady { get; }

        /// <summary>
        /// 当前 UI 是否需要阻断玩法输入。
        /// </summary>
        bool BlocksGameplayInput { get; }

        /// <summary>
        /// 当前已打开的业务窗口。
        /// </summary>
        IReadOnlyList<GameWindow> OpenedWindows { get; }

        /// <summary>
        /// 配置业务窗口；由热更程序在内容包激活后调用一次。
        /// </summary>
        /// <param name="configs">窗口配置集合。</param>
        /// <param name="register">窗口类型显式注册回调。</param>
        void ConfigureWindows(IEnumerable<GameWindowConfig> configs, Action<GameWindowRegistry> register);

        /// <summary>
        /// 按配置 ID 打开业务窗口。
        /// </summary>
        /// <param name="id">窗口配置 ID。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="options">加载反馈选项。</param>
        /// <returns>已打开的窗口。</returns>
        UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args = null, CancellationToken ct = default, UILoadOptions options = null);

        /// <summary>
        /// 使用原有方法签名按配置 ID 打开业务窗口。
        /// </summary>
        /// <param name="id">窗口配置 ID。</param>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已打开的窗口。</returns>
        UniTask<GameWindow> OpenWindowAsync(int id, GameWindowOpenArgs args, CancellationToken ct);

        /// <summary>
        /// 按注册类型打开业务窗口。
        /// </summary>
        /// <typeparam name="TWindow">窗口逻辑类型。</typeparam>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <param name="options">加载反馈选项。</param>
        /// <returns>已打开的窗口。</returns>
        UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args = null, CancellationToken ct = default, UILoadOptions options = null) where TWindow : GameWindow;

        /// <summary>
        /// 使用原有方法签名按注册类型打开业务窗口。
        /// </summary>
        /// <typeparam name="TWindow">窗口逻辑类型。</typeparam>
        /// <param name="args">窗口打开参数。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>已打开的窗口。</returns>
        UniTask<TWindow> OpenWindowAsync<TWindow>(GameWindowOpenArgs args, CancellationToken ct) where TWindow : GameWindow;

        /// <summary>
        /// 运行带有等待提示和可恢复资源失败重试的异步操作。
        /// </summary>
        /// <typeparam name="T">操作结果类型。</typeparam>
        /// <param name="operation">接收取消令牌、响应取消并自行清理晚到结果的异步操作。</param>
        /// <param name="options">加载反馈选项。</param>
        /// <param name="ct">调用方取消令牌。</param>
        /// <returns>异步操作结果；取消失败对话框时抛出取消异常。</returns>
        UniTask<T> RunWithLoadingAsync<T>(Func<CancellationToken, UniTask<T>> operation, UILoadOptions options = null, CancellationToken ct = default);

        /// <summary>
        /// 关闭指定业务窗口。
        /// </summary>
        /// <param name="id">窗口配置 ID。</param>
        /// <param name="reason">关闭原因。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否关闭了一个已打开的窗口。</returns>
        UniTask<bool> CloseWindowAsync(int id, GameWindowCloseReason reason = GameWindowCloseReason.User, CancellationToken ct = default);

        /// <summary>
        /// 关闭当前最上层的业务窗口。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>是否关闭了一个已打开的窗口。</returns>
        UniTask<bool> CloseTopWindowAsync(CancellationToken ct = default);

        /// <summary>
        /// 关闭全部已打开的业务窗口。
        /// </summary>
        /// <param name="ct">取消令牌。</param>
        /// <returns>关闭操作。</returns>
        UniTask CloseAllWindowsAsync(CancellationToken ct = default);

        /// <summary>
        /// 销毁指定逻辑作用域的业务窗口及其资源租约。
        /// </summary>
        /// <param name="scope">需要退出的逻辑作用域。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>作用域销毁操作。</returns>
        UniTask DestroyWindowScopeAsync(GameWindowScope scope, CancellationToken ct = default);

        /// <summary>
        /// 排队显示一条非模态提示。
        /// </summary>
        /// <param name="message">提示内容。</param>
        /// <param name="durationSeconds">显示时长，单位为秒。</param>
        void ShowToast(string message, float durationSeconds = 2.5f);

        /// <summary>
        /// 显示模态确认框；多个请求按调用顺序显示。
        /// </summary>
        /// <param name="title">标题。</param>
        /// <param name="message">正文。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="cancelText">取消按钮文字。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>玩家是否选择确认。</returns>
        UniTask<bool> ConfirmAsync(string title, string message, string confirmText = "确定", string cancelText = "取消", CancellationToken ct = default);

        /// <summary>
        /// 显示只有确认按钮的模态提示框。
        /// </summary>
        /// <param name="title">标题。</param>
        /// <param name="message">正文。</param>
        /// <param name="confirmText">确认按钮文字。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>玩家关闭提示框后的操作。</returns>
        UniTask AlertAsync(string title, string message, string confirmText = "确定", CancellationToken ct = default);

        /// <summary>
        /// 显示全屏输入阻断等待层，释放返回的句柄后隐藏。
        /// </summary>
        /// <param name="message">等待层文字。</param>
        /// <returns>可更新文字的等待层句柄。</returns>
        IUIBusyHandle BeginBusy(string message = "请稍候…");
    }

    /// <summary>
    /// 一次全屏等待层的独立持有句柄。
    /// </summary>
    public interface IUIBusyHandle : IDisposable
    {
        /// <summary>
        /// 更新本次等待操作的说明。
        /// </summary>
        /// <param name="message">等待层文字。</param>
        void SetMessage(string message);
    }
}
