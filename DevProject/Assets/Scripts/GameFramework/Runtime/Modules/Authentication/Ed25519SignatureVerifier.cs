using System;
using System.Numerics;
using System.Security.Cryptography;

namespace GameFramework
{
    /// <summary>
    /// RFC 8032 Ed25519 验签实现。仅处理公开数据，执行规范点编码、素数阶子群与标量边界门禁；
    /// 不持有私钥，也不提供签名能力。
    /// </summary>
    public sealed class Ed25519SignatureVerifier : IEd25519SignatureVerifier
    {
        private static readonly BigInteger Prime =
            (BigInteger.One << 255) - 19;
        private static readonly BigInteger GroupOrder = BigInteger.Parse(
            "7237005577332262213973186563042994240857116359379907606001950938285454250989");
        private static readonly BigInteger CurveD = Mod(
            -121665 * Invert(new BigInteger(121666)));
        private static readonly BigInteger SquareRootOfMinusOne =
            BigInteger.ModPow(new BigInteger(2), (Prime - 1) / 4, Prime);
        private static readonly Point BasePoint = DecodePoint(CreateBasePointEncoding());

        /// <inheritdoc />
        public bool Verify(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32)
                throw new ArgumentException("Ed25519 公钥必须是 32 字节。", nameof(publicKey));
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (signature == null || signature.Length != 64)
                throw new ArgumentException("Ed25519 签名必须是 64 字节。", nameof(signature));

            byte[] encodedR = new byte[32];
            byte[] encodedS = new byte[32];
            Buffer.BlockCopy(signature, 0, encodedR, 0, encodedR.Length);
            Buffer.BlockCopy(signature, encodedR.Length, encodedS, 0, encodedS.Length);
            BigInteger scalarS = FromLittleEndian(encodedS);
            if (scalarS >= GroupOrder) return false;

            Point publicPoint;
            Point signaturePoint;
            try
            {
                publicPoint = DecodePoint(publicKey);
                signaturePoint = DecodePoint(encodedR);
            }
            catch (CryptographicException)
            {
                return false;
            }

            // 只接受素数阶子群中的非单位元，拒绝小阶点与混合阶点造成的可塑性。
            if (IsIdentity(publicPoint) || !IsIdentity(Multiply(publicPoint, GroupOrder)) ||
                IsIdentity(signaturePoint) || !IsIdentity(Multiply(signaturePoint, GroupOrder)))
                return false;

            byte[] hashInput = new byte[64 + message.Length];
            Buffer.BlockCopy(encodedR, 0, hashInput, 0, 32);
            Buffer.BlockCopy(publicKey, 0, hashInput, 32, 32);
            Buffer.BlockCopy(message, 0, hashInput, 64, message.Length);
            byte[] digest;
            using (SHA512 sha = SHA512.Create())
                digest = sha.ComputeHash(hashInput);
            BigInteger challenge = FromLittleEndian(digest) % GroupOrder;

            Point left = Multiply(BasePoint, scalarS);
            Point right = Add(signaturePoint, Multiply(publicPoint, challenge));
            return Equal(left, right);
        }

        private static Point DecodePoint(byte[] encoded)
        {
            if (encoded == null || encoded.Length != 32)
                throw new CryptographicException("Ed25519 点编码长度无效。");
            byte[] yBytes = (byte[])encoded.Clone();
            int expectedXParity = yBytes[31] >> 7;
            yBytes[31] &= 0x7f;
            BigInteger y = FromLittleEndian(yBytes);
            if (y >= Prime) throw new CryptographicException("Ed25519 点编码不是规范表示。");

            BigInteger ySquared = Mod(y * y);
            BigInteger xSquared = Mod((ySquared - 1) * Invert(CurveD * ySquared + 1));
            BigInteger x = BigInteger.ModPow(xSquared, (Prime + 3) / 8, Prime);
            if (Mod(x * x - xSquared) != BigInteger.Zero)
                x = Mod(x * SquareRootOfMinusOne);
            if (Mod(x * x - xSquared) != BigInteger.Zero)
                throw new CryptographicException("Ed25519 点不在曲线上。");
            if (x.IsZero && expectedXParity != 0)
                throw new CryptographicException("Ed25519 点使用了非规范符号位。");
            if ((!x.IsEven ? 1 : 0) != expectedXParity) x = Prime - x;
            return new Point(x, y, BigInteger.One, Mod(x * y));
        }

        private static Point Add(Point left, Point right)
        {
            BigInteger a = Mod((left.Y - left.X) * (right.Y - right.X));
            BigInteger b = Mod((left.Y + left.X) * (right.Y + right.X));
            BigInteger c = Mod(2 * CurveD * left.T * right.T);
            BigInteger d = Mod(2 * left.Z * right.Z);
            BigInteger e = Mod(b - a);
            BigInteger f = Mod(d - c);
            BigInteger g = Mod(d + c);
            BigInteger h = Mod(b + a);
            return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
        }

        private static Point Double(Point value)
        {
            BigInteger a = Mod(value.X * value.X);
            BigInteger b = Mod(value.Y * value.Y);
            BigInteger c = Mod(2 * value.Z * value.Z);
            BigInteger d = Mod(-a);
            BigInteger e = Mod((value.X + value.Y) * (value.X + value.Y) - a - b);
            BigInteger g = Mod(d + b);
            BigInteger f = Mod(g - c);
            BigInteger h = Mod(d - b);
            return new Point(Mod(e * f), Mod(g * h), Mod(f * g), Mod(e * h));
        }

        private static Point Multiply(Point value, BigInteger scalar)
        {
            Point result = Point.Identity;
            Point addend = value;
            while (scalar > BigInteger.Zero)
            {
                if (!scalar.IsEven) result = Add(result, addend);
                addend = Double(addend);
                scalar >>= 1;
            }
            return result;
        }

        private static bool Equal(Point left, Point right)
        {
            return Mod(left.X * right.Z - right.X * left.Z).IsZero &&
                   Mod(left.Y * right.Z - right.Y * left.Z).IsZero;
        }

        private static bool IsIdentity(Point value)
        {
            return Mod(value.X).IsZero && Mod(value.Y - value.Z).IsZero;
        }

        private static BigInteger Invert(BigInteger value)
        {
            value = Mod(value);
            if (value.IsZero) throw new CryptographicException("Ed25519 域元素不可求逆。");
            return BigInteger.ModPow(value, Prime - 2, Prime);
        }

        private static BigInteger Mod(BigInteger value)
        {
            value %= Prime;
            return value.Sign < 0 ? value + Prime : value;
        }

        private static BigInteger FromLittleEndian(byte[] value)
        {
            var unsigned = new byte[value.Length + 1];
            Buffer.BlockCopy(value, 0, unsigned, 0, value.Length);
            return new BigInteger(unsigned);
        }

        private static byte[] CreateBasePointEncoding()
        {
            var encoded = new byte[32];
            encoded[0] = 0x58;
            for (int index = 1; index < encoded.Length; index++) encoded[index] = 0x66;
            return encoded;
        }

        private readonly struct Point
        {
            internal static readonly Point Identity =
                new Point(BigInteger.Zero, BigInteger.One, BigInteger.One, BigInteger.Zero);

            internal Point(BigInteger x, BigInteger y, BigInteger z, BigInteger t)
            {
                X = x;
                Y = y;
                Z = z;
                T = t;
            }

            internal BigInteger X { get; }
            internal BigInteger Y { get; }
            internal BigInteger Z { get; }
            internal BigInteger T { get; }
        }
    }
}
