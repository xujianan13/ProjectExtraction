namespace GameFramework
{
    /// <summary>内容包完成下载后的激活时机。</summary>
    public enum UpdateActivationMode
    {
        /// <summary>在当前进程立即激活目标版本。</summary>
        Immediate = 0,

        /// <summary>仅准备目标版本，由下次冷启动激活。</summary>
        NextBoot = 1,

        /// <summary>仅准备目标版本，由 BootKernel 在当前启动中通过通用激活器切换。</summary>
        Deferred = 2
    }
}
