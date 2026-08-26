using System.Security.Cryptography;

namespace Daedalus.Tools.Hermes;

/// <summary>
/// 生成集合 / 历史记录使用的 id（hermes.md §11 示例 "01J..."）：ULID 格式，
/// 26 字符 Crockford base32，高 48 位为 Unix 毫秒时间戳（字典序近似按时间排序）、
/// 低 80 位为加密随机数。项目未引入第三方 ULID 包，此处按规范自实现。
/// </summary>
public static class IdGenerator
{
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    /// <summary>生成一个新 id。</summary>
    public static string NewId()
    {
        Span<byte> bytes = stackalloc byte[16];
        long timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        for (int i = 5; i >= 0; i--)
        {
            bytes[i] = (byte)(timestamp & 0xFF);
            timestamp >>= 8;
        }

        RandomNumberGenerator.Fill(bytes[6..]);

        // 128 位按 5 位一组切成 26 个 base32 字符（高位在前）
        var chars = new char[26];
        for (int charIndex = 0; charIndex < 26; charIndex++)
        {
            int value = 0;
            for (int bit = 0; bit < 5; bit++)
            {
                int absoluteBit = charIndex * 5 + bit;
                int byteIndex = absoluteBit / 8;
                int bitOffset = 7 - absoluteBit % 8;
                if (byteIndex < 16 && ((bytes[byteIndex] >> bitOffset) & 1) == 1)
                {
                    value |= 1 << 4 - bit;
                }
            }

            chars[charIndex] = Alphabet[value];
        }

        return new string(chars);
    }
}
