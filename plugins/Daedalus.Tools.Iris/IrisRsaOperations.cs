using System.Security.Cryptography;
using System.Text;

namespace Daedalus.Tools.Iris;

/// <summary>
/// RSA 加解密与密钥对生成编排（iris.md §5.2，非 UI 可测）：填充 OAEP-SHA256 / PKCS#1 v1.5，
/// 密钥对以 PEM 展示/导入。PEM 非法、明文超长、密文长度不符、解密失败均收敛为错误状态文本，
/// 不向界面抛异常。
/// </summary>
public static class IrisRsaOperations
{
    /// <summary>生成密钥对：公钥 <c>ExportSubjectPublicKeyInfoPem</c>、私钥 <c>ExportPkcs8PrivateKeyPem</c> 按固定格式展示。</summary>
    public static IrisOperationResult GenerateKeyPair(IrisRsaOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        try
        {
            (string publicKeyPem, string privateKeyPem) = GenerateKeyPairCore(options);

            var builder = new StringBuilder();
            builder.AppendLine("--- 公钥 (PEM) ---");
            builder.AppendLine(publicKeyPem.TrimEnd());
            builder.AppendLine();
            builder.AppendLine("--- 私钥 (PEM，请妥善保管，勿泄露) ---");
            builder.Append(privateKeyPem.TrimEnd());
            return new IrisOperationResult(true, $"密钥对生成完成（RSA-{options.KeyBits}）", builder.ToString());
        }
        catch (InvalidOperationException ex)
        {
            return new IrisOperationResult(false, $"生成失败：{ex.Message}", null);
        }
    }

    /// <summary>RSA 加密：导入公钥 PEM；明文超长（按填充与密钥长度计算上限）报明确错误。</summary>
    public static IrisOperationResult Encrypt(IrisRsaOptions options, string input, string? publicKeyPem)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            using RSA rsa = RSA.Create();
            ImportPem(rsa, publicKeyPem, "公钥");

            byte[] plaintext = Encoding.UTF8.GetBytes(input);
            int maxLength = MaxPlaintextLength(rsa.KeySize / 8, options.Padding);
            if (plaintext.Length > maxLength)
            {
                throw new InvalidOperationException(
                    $"明文超长：RSA-{rsa.KeySize} + {PaddingNameOf(options.Padding)} 最多 {maxLength} 字节（UTF-8），实际 {plaintext.Length} 字节");
            }

            byte[] ciphertext = rsa.Encrypt(plaintext, MapPadding(options.Padding));
            string output = IrisOperations.EncodeBytes(ciphertext, options.CipherFormat);
            return new IrisOperationResult(true, $"加密完成（RSA-{rsa.KeySize}，{PaddingNameOf(options.Padding)}）", output);
        }
        catch (InvalidOperationException ex)
        {
            return new IrisOperationResult(false, $"加密失败：{ex.Message}", null);
        }
        catch (CryptographicException ex)
        {
            return new IrisOperationResult(false, $"加密失败：{ex.Message}", null);
        }
    }

    /// <summary>RSA 解密：导入私钥 PEM；密文按所选格式解析，长度须与密钥长度一致。</summary>
    public static IrisOperationResult Decrypt(IrisRsaOptions options, string input, string? privateKeyPem)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(input);

        try
        {
            using RSA rsa = RSA.Create();
            ImportPem(rsa, privateKeyPem, "私钥");

            byte[] ciphertext = IrisOperations.DecodeField(input, options.CipherFormat, "密文");
            int keyBytes = rsa.KeySize / 8;
            if (ciphertext.Length != keyBytes)
            {
                throw new InvalidOperationException($"密文长度不符：RSA-{rsa.KeySize} 密文应为 {keyBytes} 字节，实际 {ciphertext.Length} 字节");
            }

            byte[] plaintext = rsa.Decrypt(ciphertext, MapPadding(options.Padding));
            string output = IrisOperations.StrictUtf8.GetString(plaintext);
            return new IrisOperationResult(true, $"解密完成（RSA-{rsa.KeySize}，{PaddingNameOf(options.Padding)}）", output);
        }
        catch (InvalidOperationException ex)
        {
            return new IrisOperationResult(false, $"解密失败：{ex.Message}", null);
        }
        catch (FormatException ex)
        {
            // 密文文本非法
            return new IrisOperationResult(false, $"解密失败：{ex.Message}", null);
        }
        catch (CryptographicException)
        {
            return new IrisOperationResult(false, "解密失败：密文损坏或私钥不匹配", null);
        }
        catch (DecoderFallbackException)
        {
            return new IrisOperationResult(false, "解密失败：解密结果不是合法的 UTF-8 文本（私钥可能不匹配）", null);
        }
    }

    /// <summary>密钥对生成核心：返回 (公钥 PEM, 私钥 PEM)，供公开编排与测试共用。</summary>
    internal static (string PublicKeyPem, string PrivateKeyPem) GenerateKeyPairCore(IrisRsaOptions options)
    {
        if (options.KeyBits is not (2048 or 3072 or 4096))
        {
            throw new InvalidOperationException($"不支持的密钥长度：{options.KeyBits}（可选 2048 / 3072 / 4096）");
        }

        using RSA rsa = RSA.Create(options.KeyBits);
        return (rsa.ExportSubjectPublicKeyInfoPem(), rsa.ExportPkcs8PrivateKeyPem());
    }

    /// <summary>明文长度上限（字节）：PKCS#1 v1.5 为 keyBytes - 11，OAEP-SHA256 为 keyBytes - 66。</summary>
    internal static int MaxPlaintextLength(int keyBytes, IrisRsaPadding padding)
    {
        return keyBytes - (padding == IrisRsaPadding.OaepSha256 ? 66 : 11);
    }

    private static RSAEncryptionPadding MapPadding(IrisRsaPadding padding)
    {
        return padding switch
        {
            IrisRsaPadding.OaepSha256 => RSAEncryptionPadding.OaepSHA256,
            IrisRsaPadding.Pkcs1 => RSAEncryptionPadding.Pkcs1,
            // 未知枚举值属编程错误（选项清单固定），抛出让 App 兜底
            _ => throw new InvalidOperationException($"未知填充方式：{padding}"),
        };
    }

    private static string PaddingNameOf(IrisRsaPadding padding)
    {
        return padding == IrisRsaPadding.OaepSha256 ? "OAEP-SHA256" : "PKCS#1 v1.5";
    }

    private static void ImportPem(RSA rsa, string? pem, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(pem))
        {
            throw new InvalidOperationException($"{fieldName} PEM 不能为空");
        }

        try
        {
            rsa.ImportFromPem(pem);
        }
        catch (ArgumentException)
        {
            throw new InvalidOperationException($"{fieldName} PEM 无法解析");
        }
    }
}
