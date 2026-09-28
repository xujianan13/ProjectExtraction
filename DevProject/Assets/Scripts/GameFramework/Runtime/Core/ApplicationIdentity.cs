using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace GameFramework
{
    /// <summary>
    /// 提供跨平台稳定的游戏标识；Windows Standalone 运行时可能不提供 Application.identifier。
    /// 回退标识由公司名和产品名组成，修改任一名称会改变存档与内容版本状态的隔离范围。
    /// 超长标识使用完整 SHA-256 摘要，避免截断不同名称时产生冲突。
    /// </summary>
    public static class ApplicationIdentity
    {
        /// <summary>
        /// 获取当前游戏的持久化标识。
        /// </summary>
        public static string GameId => Resolve(Application.identifier, Application.companyName, Application.productName);

        /// <summary>
        /// 优先使用平台应用标识；为空时以公司名和产品名生成 Windows Standalone 回退标识。
        /// 超过存档身份的 128 字节限制时使用完整 SHA-256 摘要。
        /// </summary>
        /// <param name="identifier">运行时提供的平台应用标识。</param>
        /// <param name="companyName">项目公司名。</param>
        /// <param name="productName">项目产品名。</param>
        /// <returns>供存档和内容状态共用的游戏标识。</returns>
        public static string Resolve(string identifier, string companyName, string productName)
        {
            string value = !string.IsNullOrWhiteSpace(identifier) ? identifier : companyName + "." + productName;
            byte[] bytes = Encoding.UTF8.GetBytes(value);
            if (bytes.Length <= 128) return value;
            using var sha = SHA256.Create();
            return "app-" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
        }
    }
}
