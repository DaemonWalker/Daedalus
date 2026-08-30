using System.Text;

namespace Daedalus.Tools.Iris.Tests;

/// <summary>IrisAesOperations 测试：各模式 round-trip、口令派生、密钥/IV 校验、打包格式、NIST 向量、GCM 篡改。</summary>
public class IrisAesOperationsTests
{
    private static IrisAesOptions PasswordOptions(
        IrisAesCipherMode mode,
        int keyBits = 256,
        IrisAesIvSource ivSource = IrisAesIvSource.Auto,
        IrisBytesEncoding cipherFormat = IrisBytesEncoding.Base64)
    {
        return new IrisAesOptions(mode, keyBits, IrisAesKeySource.Password, IrisBytesEncoding.Base64, ivSource, IrisBytesEncoding.Base64, cipherFormat);
    }

    private static IrisAesOptions RawKeyOptions(
        IrisAesCipherMode mode,
        int keyBits,
        IrisBytesEncoding keyFormat,
        IrisAesIvSource ivSource = IrisAesIvSource.Auto,
        IrisBytesEncoding cipherFormat = IrisBytesEncoding.Base64)
    {
        return new IrisAesOptions(mode, keyBits, IrisAesKeySource.RawKey, keyFormat, ivSource, IrisBytesEncoding.Base64, cipherFormat);
    }

    /// <summary>确定性的测试密钥字节（不使用随机数，保证用例可重复）。</summary>
    private static byte[] KeyBytes(int length)
    {
        return Enumerable.Range(0, length).Select(i => (byte)(i * 7 + 1)).ToArray();
    }

    public static IEnumerable<object[]> AllModesAndKeyBits()
    {
        foreach (IrisAesCipherMode mode in new[] { IrisAesCipherMode.Ecb, IrisAesCipherMode.Cbc, IrisAesCipherMode.Gcm })
        {
            foreach (int keyBits in new[] { 128, 192, 256 })
            {
                yield return [mode, keyBits];
            }
        }
    }

    [Theory]
    [MemberData(nameof(AllModesAndKeyBits))]
    public void Encrypt再Decrypt_各模式各密钥长度_密钥模式RoundTrip还原(IrisAesCipherMode mode, int keyBits)
    {
        byte[] keyBytes = KeyBytes(keyBits / 8);
        IrisAesOptions options = RawKeyOptions(mode, keyBits, IrisBytesEncoding.Hex);
        string keyHex = Convert.ToHexString(keyBytes);
        const string plaintext = "Hello, Iris! 中文内容 ✅";

        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, plaintext, null, keyHex, null);
        IrisOperationResult decrypted = IrisAesOperations.Decrypt(options, encrypted.Output!, null, keyHex, null);

        Assert.True(encrypted.Success, encrypted.StatusText);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal(plaintext, decrypted.Output);
    }

    [Theory]
    [MemberData(nameof(AllModesAndKeyBits))]
    public void Encrypt再Decrypt_各模式各密钥长度_口令模式RoundTrip还原(IrisAesCipherMode mode, int keyBits)
    {
        IrisAesOptions options = PasswordOptions(mode, keyBits);
        const string plaintext = "口令模式 round-trip 测试";

        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, plaintext, "口令-123", null, null);
        IrisOperationResult decrypted = IrisAesOperations.Decrypt(options, encrypted.Output!, "口令-123", null, null);

        Assert.True(encrypted.Success, encrypted.StatusText);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal(plaintext, decrypted.Output);
    }

    [Fact]
    public void Decrypt_口令错误_收敛为错误状态()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Gcm);
        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, "秘密", "正确口令", null, null);

        IrisOperationResult result = IrisAesOperations.Decrypt(options, encrypted.Output!, "错误口令", null, null);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解密失败", result.StatusText);
    }

    [Fact]
    public void Encrypt_口令为空_收敛为错误状态()
    {
        IrisOperationResult result = IrisAesOperations.Encrypt(PasswordOptions(IrisAesCipherMode.Cbc), "input", string.Empty, null, null);

        Assert.False(result.Success);
        Assert.Contains("口令不能为空", result.StatusText);
    }

    [Fact]
    public void Encrypt_密钥长度不符_收敛为错误状态()
    {
        // AES-128 需要 16 字节，提供 32 字节
        IrisAesOptions options = RawKeyOptions(IrisAesCipherMode.Cbc, 128, IrisBytesEncoding.Hex);
        string keyHex = Convert.ToHexString(KeyBytes(32));

        IrisOperationResult result = IrisAesOperations.Encrypt(options, "input", null, keyHex, null);

        Assert.False(result.Success);
        Assert.Contains("密钥长度不符", result.StatusText);
    }

    [Fact]
    public void Encrypt再Decrypt_密钥格式Base64_RoundTrip还原()
    {
        IrisAesOptions options = RawKeyOptions(IrisAesCipherMode.Cbc, 256, IrisBytesEncoding.Base64);
        string keyBase64 = Convert.ToBase64String(KeyBytes(32));

        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, "Base64 密钥", null, keyBase64, null);
        IrisOperationResult decrypted = IrisAesOperations.Decrypt(options, encrypted.Output!, null, keyBase64, null);

        Assert.True(encrypted.Success, encrypted.StatusText);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal("Base64 密钥", decrypted.Output);
    }

    [Fact]
    public void Encrypt_密钥文本非法_收敛为错误状态()
    {
        IrisAesOptions options = RawKeyOptions(IrisAesCipherMode.Cbc, 128, IrisBytesEncoding.Hex);

        IrisOperationResult result = IrisAesOperations.Encrypt(options, "input", null, "这不是HEX!", null);

        Assert.False(result.Success);
        Assert.Contains("密钥", result.StatusText);
    }

    [Fact]
    public void Encrypt_自动IV_同一明文两次密文不同()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Cbc);

        IrisOperationResult first = IrisAesOperations.Encrypt(options, "同一明文", "口令", null, null);
        IrisOperationResult second = IrisAesOperations.Encrypt(options, "同一明文", "口令", null, null);

        Assert.True(first.Success && second.Success);
        // 随机盐 + 随机 IV：两次输出必然不同
        Assert.NotEqual(first.Output, second.Output);
    }

    [Fact]
    public void Encrypt_自动IV打包格式_Gcm口令模式为盐加Nonce加密文加Tag()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Gcm);
        byte[] plaintext = Encoding.UTF8.GetBytes("打包格式验证");

        IrisOperationResult result = IrisAesOperations.Encrypt(options, "打包格式验证", "口令", null, null);

        Assert.True(result.Success);
        byte[] package = Convert.FromBase64String(result.Output!);
        // salt(16) || nonce(12) || ciphertext(=明文长度) || tag(16)
        Assert.Equal(16 + 12 + plaintext.Length + 16, package.Length);
    }

    [Fact]
    public void Encrypt_自动IV打包格式_Cbc密钥模式为IV加密文()
    {
        IrisAesOptions options = RawKeyOptions(IrisAesCipherMode.Cbc, 128, IrisBytesEncoding.Hex);
        string keyHex = Convert.ToHexString(KeyBytes(16));

        IrisOperationResult result = IrisAesOperations.Encrypt(options, "1234567890123456", null, keyHex, null);

        Assert.True(result.Success);
        byte[] package = Convert.FromBase64String(result.Output!);
        // 密钥模式无盐头：IV(16) || ciphertext(16 明文字节 + PKCS7 填充 = 32)
        Assert.Equal(16 + 32, package.Length);
    }

    [Fact]
    public void Encrypt再Decrypt_手动IV_输出仅密文本体且RoundTrip还原()
    {
        IrisAesOptions options = new(
            IrisAesCipherMode.Cbc, 128, IrisAesKeySource.RawKey, IrisBytesEncoding.Hex,
            IrisAesIvSource.Manual, IrisBytesEncoding.Hex, IrisBytesEncoding.Base64);
        string keyHex = Convert.ToHexString(KeyBytes(16));
        string ivHex = Convert.ToHexString(KeyBytes(16));

        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, "手动 IV 测试", null, keyHex, ivHex);

        Assert.True(encrypted.Success, encrypted.StatusText);
        // 手动 IV：密钥模式下输出仅密文本体（无盐无 IV 头）；明文 18 字节经 PKCS7 补齐为 32 字节
        Assert.Equal(32, Convert.FromBase64String(encrypted.Output!).Length);

        IrisOperationResult decrypted = IrisAesOperations.Decrypt(options, encrypted.Output!, null, keyHex, ivHex);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal("手动 IV 测试", decrypted.Output);
    }

    [Fact]
    public void Encrypt_手动IV长度不符_收敛为错误状态()
    {
        IrisAesOptions options = new(
            IrisAesCipherMode.Cbc, 128, IrisAesKeySource.RawKey, IrisBytesEncoding.Hex,
            IrisAesIvSource.Manual, IrisBytesEncoding.Hex, IrisBytesEncoding.Base64);
        string keyHex = Convert.ToHexString(KeyBytes(16));

        IrisOperationResult result = IrisAesOperations.Encrypt(options, "input", null, keyHex, Convert.ToHexString(KeyBytes(8)));

        Assert.False(result.Success);
        Assert.Contains("IV", result.StatusText);
        Assert.Contains("长度不符", result.StatusText);
    }

    [Fact]
    public void EncryptBytes_NistAes128Ecb向量_首分组匹配()
    {
        // FIPS-197 / SP800-38A F.1.1：AES-128-ECB 第一分组
        byte[] key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        byte[] plaintext = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172a");
        IrisAesOptions options = RawKeyOptions(IrisAesCipherMode.Ecb, 128, IrisBytesEncoding.Hex);

        byte[] ciphertext = IrisAesOperations.EncryptBytes(options, plaintext, key, null);

        // PKCS7 会在尾部补一个分组，官方向量只比对第一分组
        Assert.Equal("3ad77bb40d7a3660a89ecaf32466ef97", Convert.ToHexString(ciphertext[..16]).ToLowerInvariant());
        Assert.Equal(plaintext, IrisAesOperations.DecryptBytes(options, ciphertext, key, null));
    }

    [Fact]
    public void EncryptBytes_NistAes128Cbc向量_首分组匹配()
    {
        // SP800-38A F.2.1：AES-128-CBC 第一分组
        byte[] key = Convert.FromHexString("2b7e151628aed2a6abf7158809cf4f3c");
        byte[] iv = Convert.FromHexString("000102030405060708090a0b0c0d0e0f");
        byte[] plaintext = Convert.FromHexString("6bc1bee22e409f96e93d7e117393172a");
        IrisAesOptions options = RawKeyOptions(IrisAesCipherMode.Cbc, 128, IrisBytesEncoding.Hex);

        byte[] ciphertext = IrisAesOperations.EncryptBytes(options, plaintext, key, iv);

        Assert.Equal("7649abac8119b246cee98e9b12e9197d", Convert.ToHexString(ciphertext[..16]).ToLowerInvariant());
        Assert.Equal(plaintext, IrisAesOperations.DecryptBytes(options, ciphertext, key, iv));
    }

    [Fact]
    public void Decrypt_Gcm密文被篡改_认证失败收敛为错误状态()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Gcm);
        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, "完整性验证", "口令", null, null);
        byte[] package = Convert.FromBase64String(encrypted.Output!);
        package[^1] ^= 0xFF; // 翻转 tag 最后一字节的全部位
        string tampered = Convert.ToBase64String(package);

        IrisOperationResult result = IrisAesOperations.Decrypt(options, tampered, "口令", null, null);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解密失败", result.StatusText);
    }

    [Fact]
    public void Decrypt_密文格式非法_收敛为错误状态()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Cbc);

        IrisOperationResult result = IrisAesOperations.Decrypt(options, "!!!这不是Base64!!!", "口令", null, null);

        Assert.False(result.Success);
        Assert.Contains("密文", result.StatusText);
    }

    [Fact]
    public void Decrypt_密文太短缺盐头_收敛为错误状态()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Cbc);
        string shortPackage = Convert.ToBase64String(KeyBytes(8));

        IrisOperationResult result = IrisAesOperations.Decrypt(options, shortPackage, "口令", null, null);

        Assert.False(result.Success);
        Assert.Contains("盐", result.StatusText);
    }

    [Fact]
    public void Encrypt再Decrypt_密文格式Hex_RoundTrip还原()
    {
        IrisAesOptions options = PasswordOptions(IrisAesCipherMode.Gcm, cipherFormat: IrisBytesEncoding.Hex);
        const string plaintext = "HEX 密文 round-trip";

        IrisOperationResult encrypted = IrisAesOperations.Encrypt(options, plaintext, "口令", null, null);

        Assert.True(encrypted.Success, encrypted.StatusText);
        // 输出确为 HEX（能被 FromHexString 解析）
        _ = Convert.FromHexString(encrypted.Output!);

        IrisOperationResult decrypted = IrisAesOperations.Decrypt(options, encrypted.Output!, "口令", null, null);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal(plaintext, decrypted.Output);
    }

    [Fact]
    public void Decrypt_Ecb错误密钥但填充巧合通过_非UTF8收敛为错误状态()
    {
        // ECB 无认证：构造一份密钥 A 的密文，用密钥 B 解密；多数情况填充错，此用例直接对核心层验证 UTF-8 收敛
        IrisAesOptions optionsA = RawKeyOptions(IrisAesCipherMode.Ecb, 128, IrisBytesEncoding.Hex);
        byte[] keyA = KeyBytes(16);
        byte[] keyB = KeyBytes(16).Select(b => (byte)(b + 1)).ToArray();
        byte[] ciphertext = IrisAesOperations.EncryptBytes(optionsA, Encoding.UTF8.GetBytes("一些明文内容"), keyA, null);

        // 无论填充错（CryptographicException）还是巧合通过但非 UTF-8（DecoderFallbackException），都必须收敛而非抛出
        IrisOperationResult result = IrisAesOperations.Decrypt(optionsA, Convert.ToBase64String(ciphertext), null, Convert.ToHexString(keyB), null);

        Assert.False(result.Success);
        Assert.Contains("解密失败", result.StatusText);
    }
}
