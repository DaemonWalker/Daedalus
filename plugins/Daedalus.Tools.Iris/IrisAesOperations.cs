using System.Security.Cryptography;
using System.Text;

namespace Daedalus.Tools.Iris;

/// <summary>
/// AES 加解密编排（iris.md §5.1，非 UI 可测）：ECB / CBC / GCM，密钥由口令（PBKDF2-SHA256）
/// 或直接输入（HEX / Base64）获得；自动 IV / nonce 拼在密文头部。解密错误（口令或密钥错、
/// 长度不符、GCM 认证失败、填充错）一律收敛为「解密失败：…」状态文本，不向界面抛异常。
/// </summary>
public static class IrisAesOperations
{
    /// <summary>PBKDF2-SHA256 迭代次数（口令模式派生密钥）。</summary>
    public const int Pbkdf2Iterations = 100_000;

    /// <summary>盐长度（字节，仅口令模式）。</summary>
    internal const int SaltLength = 16;

    /// <summary>GCM nonce 长度（字节）。</summary>
    internal const int GcmNonceLength = 12;

    /// <summary>GCM 认证标签长度（字节，附密文尾部）。</summary>
    internal const int GcmTagLength = 16;

    /// <summary>AES 分组长度（字节，CBC 的 IV 长度）。</summary>
    internal const int BlockLength = 16;

    /// <summary>
    /// AES 加密：打包格式 <c>[salt（仅口令模式）16B] || [IV/nonce（仅自动且 CBC 16B / GCM 12B）] || ciphertext(+GCM tag 16B)</c>，
    /// 整体按 <see cref="IrisAesOptions.CipherFormat"/> 编码输出；手动 IV 时输出不含 IV。
    /// </summary>
    public static IrisOperationResult Encrypt(IrisAesOptions options, string input, string? password, string? keyText, string? ivText)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            byte[] plaintext = Encoding.UTF8.GetBytes(input);
            byte[] key = ResolveKeyForEncrypt(options, password, keyText, out byte[]? salt);
            byte[]? iv = ResolveIvForEncrypt(options, ivText);
            byte[] ciphertext = EncryptBytes(options, plaintext, key, iv);
            byte[] package = Pack(salt, options.IvSource == IrisAesIvSource.Auto ? iv : null, ciphertext);
            string output = IrisOperations.EncodeBytes(package, options.CipherFormat);
            return new IrisOperationResult(true, $"加密完成（AES-{options.KeyBits}-{options.Mode}）", output);
        }
        catch (InvalidOperationException ex)
        {
            return new IrisOperationResult(false, $"加密失败：{ex.Message}", null);
        }
        catch (FormatException ex)
        {
            // 密钥 / 手动 IV 文本非法
            return new IrisOperationResult(false, $"加密失败：{ex.Message}", null);
        }
    }

    /// <summary>AES 解密：按 <see cref="IrisAesOptions.CipherFormat"/> 解析输入并依打包格式拆分后解密。</summary>
    public static IrisOperationResult Decrypt(IrisAesOptions options, string input, string? password, string? keyText, string? ivText)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            byte[] package = IrisOperations.DecodeField(input, options.CipherFormat, "密文");
            int offset = 0;

            byte[] key;
            if (options.KeySource == IrisAesKeySource.Password)
            {
                if (string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("口令不能为空");
                }

                if (package.Length < SaltLength)
                {
                    throw new InvalidOperationException($"密文太短，缺少 {SaltLength} 字节盐头");
                }

                byte[] salt = package[..SaltLength];
                offset = SaltLength;
                key = DeriveKey(password, salt, options.KeyBytes);
            }
            else
            {
                key = DecodeRawKey(options, keyText);
            }

            byte[]? iv = null;
            if (options.Mode != IrisAesCipherMode.Ecb)
            {
                int ivLength = IvLengthOf(options.Mode);
                if (options.IvSource == IrisAesIvSource.Auto)
                {
                    if (package.Length < offset + ivLength)
                    {
                        throw new InvalidOperationException($"密文太短，缺少 {ivLength} 字节 {IvNameOf(options.Mode)} 头");
                    }

                    iv = package[offset..(offset + ivLength)];
                    offset += ivLength;
                }
                else
                {
                    iv = DecodeManualIv(options, ivText);
                }
            }

            byte[] ciphertext = package[offset..];
            byte[] plaintext = DecryptBytes(options, ciphertext, key, iv);
            string output = IrisOperations.StrictUtf8.GetString(plaintext);
            return new IrisOperationResult(true, $"解密完成（AES-{options.KeyBits}-{options.Mode}）", output);
        }
        catch (InvalidOperationException ex)
        {
            return new IrisOperationResult(false, $"解密失败：{ex.Message}", null);
        }
        catch (FormatException ex)
        {
            // 密文 / 密钥 / 手动 IV 文本非法
            return new IrisOperationResult(false, $"解密失败：{ex.Message}", null);
        }
        catch (CryptographicException)
        {
            // 口令或密钥错、GCM 认证失败（密文被篡改）、PKCS7 填充错都落在这里，细节不向外暴露
            return new IrisOperationResult(false, "解密失败：密文损坏或口令/密钥/IV 不正确", null);
        }
        catch (DecoderFallbackException)
        {
            return new IrisOperationResult(false, "解密失败：解密结果不是合法的 UTF-8 文本（口令/密钥可能不正确）", null);
        }
    }

    /// <summary>字节级加密核心（无打包/派生）：供公开编排与测试（NIST 向量对拍）共用。</summary>
    internal static byte[] EncryptBytes(IrisAesOptions options, byte[] plaintext, byte[] key, byte[]? iv)
    {
        if (options.Mode == IrisAesCipherMode.Gcm)
        {
            ArgumentNullException.ThrowIfNull(iv);
            using var gcm = new AesGcm(key, GcmTagLength);
            byte[] ciphertext = new byte[plaintext.Length + GcmTagLength];
            // 必须用 AsSpan 切片：数组的范围索引会复制出新数组，写进去原数组仍是全零
            gcm.Encrypt(iv, plaintext, ciphertext.AsSpan(..^GcmTagLength), ciphertext.AsSpan(^GcmTagLength..));
            return ciphertext;
        }

        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = options.Mode == IrisAesCipherMode.Cbc ? CipherMode.CBC : CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        if (iv is not null)
        {
            aes.IV = iv;
        }

        using ICryptoTransform encryptor = aes.CreateEncryptor();
        return encryptor.TransformFinalBlock(plaintext, 0, plaintext.Length);
    }

    /// <summary>字节级解密核心（无拆分/派生）：供公开编排与测试共用；失败抛 <see cref="CryptographicException"/>。</summary>
    internal static byte[] DecryptBytes(IrisAesOptions options, byte[] ciphertext, byte[] key, byte[]? iv)
    {
        if (options.Mode == IrisAesCipherMode.Gcm)
        {
            ArgumentNullException.ThrowIfNull(iv);
            if (ciphertext.Length < GcmTagLength)
            {
                throw new CryptographicException("密文长度不足以容纳认证标签");
            }

            using var gcm = new AesGcm(key, GcmTagLength);
            byte[] plaintext = new byte[ciphertext.Length - GcmTagLength];
            gcm.Decrypt(iv, ciphertext.AsSpan(..^GcmTagLength), ciphertext.AsSpan(^GcmTagLength..), plaintext);
            return plaintext;
        }

        using Aes aes = Aes.Create();
        aes.Key = key;
        aes.Mode = options.Mode == IrisAesCipherMode.Cbc ? CipherMode.CBC : CipherMode.ECB;
        aes.Padding = PaddingMode.PKCS7;
        if (iv is not null)
        {
            aes.IV = iv;
        }

        using ICryptoTransform decryptor = aes.CreateDecryptor();
        return decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);
    }

    /// <summary>口令模式派生密钥（PBKDF2-SHA256，<see cref="Pbkdf2Iterations"/> 次迭代）。</summary>
    internal static byte[] DeriveKey(string password, byte[] salt, int keyBytes)
    {
        return Rfc2898DeriveBytes.Pbkdf2(password, salt, Pbkdf2Iterations, HashAlgorithmName.SHA256, keyBytes);
    }

    /// <summary>CBC 的 IV / GCM 的 nonce 长度（ECB 无 IV，不得调用）。</summary>
    internal static int IvLengthOf(IrisAesCipherMode mode)
    {
        return mode == IrisAesCipherMode.Gcm ? GcmNonceLength : BlockLength;
    }

    private static string IvNameOf(IrisAesCipherMode mode)
    {
        return mode == IrisAesCipherMode.Gcm ? "nonce" : "IV";
    }

    private static byte[] ResolveKeyForEncrypt(IrisAesOptions options, string? password, string? keyText, out byte[]? salt)
    {
        if (options.KeySource == IrisAesKeySource.Password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new InvalidOperationException("口令不能为空");
            }

            salt = RandomNumberGenerator.GetBytes(SaltLength);
            return DeriveKey(password, salt, options.KeyBytes);
        }

        salt = null;
        return DecodeRawKey(options, keyText);
    }

    private static byte[] DecodeRawKey(IrisAesOptions options, string? keyText)
    {
        byte[] key = IrisOperations.DecodeField(keyText, options.KeyFormat, "密钥");
        if (key.Length != options.KeyBytes)
        {
            throw new InvalidOperationException($"密钥长度不符：AES-{options.KeyBits} 需要 {options.KeyBytes} 字节，实际 {key.Length} 字节");
        }

        return key;
    }

    private static byte[]? ResolveIvForEncrypt(IrisAesOptions options, string? ivText)
    {
        if (options.Mode == IrisAesCipherMode.Ecb)
        {
            return null;
        }

        if (options.IvSource == IrisAesIvSource.Auto)
        {
            return RandomNumberGenerator.GetBytes(IvLengthOf(options.Mode));
        }

        return DecodeManualIv(options, ivText);
    }

    private static byte[] DecodeManualIv(IrisAesOptions options, string? ivText)
    {
        string name = IvNameOf(options.Mode);
        byte[] iv = IrisOperations.DecodeField(ivText, options.IvFormat, name);
        int expected = IvLengthOf(options.Mode);
        if (iv.Length != expected)
        {
            throw new InvalidOperationException($"{name} 长度不符：{options.Mode} 需要 {expected} 字节，实际 {iv.Length} 字节");
        }

        return iv;
    }

    // 打包：salt（仅口令模式） || IV/nonce（仅自动） || ciphertext(+GCM tag)
    // 手动 IV 不打 IV 头，但口令模式的盐必须始终在头部——否则解密无法派生同一密钥
    private static byte[] Pack(byte[]? salt, byte[]? iv, byte[] ciphertext)
    {
        byte[] package = new byte[(salt?.Length ?? 0) + (iv?.Length ?? 0) + ciphertext.Length];
        int offset = 0;
        if (salt is not null)
        {
            salt.CopyTo(package, offset);
            offset += salt.Length;
        }

        if (iv is not null)
        {
            iv.CopyTo(package, offset);
            offset += iv.Length;
        }

        ciphertext.CopyTo(package, offset);
        return package;
    }
}
