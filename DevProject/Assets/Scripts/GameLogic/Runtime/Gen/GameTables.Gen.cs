using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using GameFramework;

namespace GameLogic.Tables
{
    /// <summary>
    /// 全部配置表的加载入口。
    /// </summary>
    public static partial class GameTables
    {
        /// <summary>
        /// 加载当前内容包中的全部配置表。
        /// </summary>
        /// <param name="resources">资源模块。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>加载完成后的异步操作。</returns>
        public static UniTask LoadAsync(IResourceModule resources, CancellationToken ct)
        {
            if (resources == null) throw new ArgumentNullException(nameof(resources));
            ct.ThrowIfCancellationRequested();
            return UniTask.CompletedTask;
        }
    }
}
