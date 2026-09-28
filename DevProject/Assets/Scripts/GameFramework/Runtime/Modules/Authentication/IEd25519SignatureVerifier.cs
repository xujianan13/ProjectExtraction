namespace GameFramework
{
    /// <summary>
    /// 由目标运行时提供的 Ed25519 验签端口。实现必须使用生产级密码学；
    /// 不支持的平台应拒绝启动相关签名制品流程，不得降级为跳过验签。
    /// </summary>
    public interface IEd25519SignatureVerifier
    {
        /// <summary>验证 Ed25519 消息签名。</summary>
        /// <param name="publicKey">32 字节 Ed25519 公钥副本。</param>
        /// <param name="message">需要验证的精确消息字节。</param>
        /// <param name="signature">64 字节 Ed25519 签名。</param>
        /// <returns>签名是否有效。</returns>
        bool Verify(byte[] publicKey, byte[] message, byte[] signature);
    }
}
