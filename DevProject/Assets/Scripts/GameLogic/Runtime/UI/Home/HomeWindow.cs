using Cysharp.Threading.Tasks;
using GameFramework;
using UnityEngine.UI;

namespace GameLogic.UI.Home
{
    /// <summary>
    /// Home窗口逻辑；显示组件保存在资源 Prefab 中。
    /// </summary>
    public sealed class HomeWindow : GameWindow
    {
        private Text title;
        private Button closeButton;

        /// <summary>
        /// 绑定窗口必需组件。
        /// </summary>
        protected override void BindComponents()
        {
            title = FindRequired<Text>("Panel/Title");
            closeButton = FindRequired<Button>("Panel/CloseButton");
        }

        /// <summary>
        /// 每轮打开登记按钮监听。
        /// </summary>
        /// <param name="args">打开参数。</param>
        protected override void Open(GameWindowOpenArgs args)
        {
            closeButton.onClick.AddListener(OnCloseClicked);
            OpenScope.Defer(() => closeButton.onClick.RemoveListener(OnCloseClicked));
        }

        /// <summary>
        /// 刷新本次显示。
        /// </summary>
        /// <param name="args">显示参数。</param>
        protected override void Show(GameWindowOpenArgs args)
        {
            title.text = args.UserData as string ?? "Home";
        }

        /// <summary>
        /// 用户点击关闭按钮。
        /// </summary>
        private void OnCloseClicked() => CloseSelfAsync().Forget();
    }
}
