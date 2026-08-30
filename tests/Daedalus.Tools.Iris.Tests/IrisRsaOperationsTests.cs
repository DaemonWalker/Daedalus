namespace Daedalus.Tools.Iris.Tests;

/// <summary>IrisRsaOperations 测试：两种填充 round-trip、PEM 往返、交叉验证、超长/非法输入收敛。</summary>
public class IrisRsaOperationsTests
{
    private static IrisRsaOptions Options(
        IrisRsaPadding padding = IrisRsaPadding.OaepSha256,
        int keyBits = 2048,
        IrisBytesEncoding cipherFormat = IrisBytesEncoding.Base64)
    {
        return new IrisRsaOptions(padding, keyBits, cipherFormat);
    }

    [Theory]
    [InlineData(IrisRsaPadding.OaepSha256)]
    [InlineData(IrisRsaPadding.Pkcs1)]
    public void Encrypt再Decrypt_两种填充_RoundTrip还原(IrisRsaPadding padding)
    {
        IrisRsaOptions options = Options(padding);
        (string publicKeyPem, string privateKeyPem) = IrisRsaOperations.GenerateKeyPairCore(options);
        const string plaintext = "RSA round-trip 测试，中文内容";

        IrisOperationResult encrypted = IrisRsaOperations.Encrypt(options, plaintext, publicKeyPem);
        IrisOperationResult decrypted = IrisRsaOperations.Decrypt(options, encrypted.Output!, privateKeyPem);

        Assert.True(encrypted.Success, encrypted.StatusText);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal(plaintext, decrypted.Output);
    }

    [Fact]
    public void GenerateKeyPair_输出格式_含公钥与私钥Pem块()
    {
        IrisOperationResult result = IrisRsaOperations.GenerateKeyPair(Options());

        Assert.True(result.Success, result.StatusText);
        Assert.NotNull(result.Output);
        Assert.Contains("--- 公钥 (PEM) ---", result.Output);
        Assert.Contains("-----BEGIN PUBLIC KEY-----", result.Output);
        Assert.Contains("--- 私钥 (PEM", result.Output);
        Assert.Contains("-----BEGIN PRIVATE KEY-----", result.Output);
        Assert.Contains("RSA-2048", result.StatusText);
    }

    [Fact]
    public void GenerateKeyPair_Pem导出导入往返_各密钥长度可用()
    {
        foreach (int keyBits in new[] { 2048, 3072, 4096 })
        {
            IrisRsaOptions options = Options(keyBits: keyBits);
            (string publicKeyPem, string privateKeyPem) = IrisRsaOperations.GenerateKeyPairCore(options);

            IrisOperationResult encrypted = IrisRsaOperations.Encrypt(options, "长度 " + keyBits, publicKeyPem);
            IrisOperationResult decrypted = IrisRsaOperations.Decrypt(options, encrypted.Output!, privateKeyPem);

            Assert.True(encrypted.Success, encrypted.StatusText);
            Assert.True(decrypted.Success, decrypted.StatusText);
            Assert.Equal("长度 " + keyBits, decrypted.Output);
        }
    }

    [Fact]
    public void Decrypt_用另一密钥对的私钥_收敛为错误状态()
    {
        IrisRsaOptions options = Options();
        (string publicKeyPemA, _) = IrisRsaOperations.GenerateKeyPairCore(options);
        (_, string privateKeyPemB) = IrisRsaOperations.GenerateKeyPairCore(options);
        IrisOperationResult encrypted = IrisRsaOperations.Encrypt(options, "交叉验证", publicKeyPemA);

        IrisOperationResult result = IrisRsaOperations.Decrypt(options, encrypted.Output!, privateKeyPemB);

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("解密失败", result.StatusText);
    }

    [Theory]
    [InlineData(IrisRsaPadding.OaepSha256, 190)]  // 2048 位 - 66
    [InlineData(IrisRsaPadding.Pkcs1, 245)]       // 2048 位 - 11
    public void Encrypt_明文超长_收敛为明确错误(IrisRsaPadding padding, int maxLength)
    {
        IrisRsaOptions options = Options(padding);
        (string publicKeyPem, _) = IrisRsaOperations.GenerateKeyPairCore(options);
        string plaintext = new('a', maxLength + 1);

        IrisOperationResult result = IrisRsaOperations.Encrypt(options, plaintext, publicKeyPem);

        Assert.False(result.Success);
        Assert.Contains("明文超长", result.StatusText);
        Assert.Contains(maxLength.ToString(), result.StatusText);
    }

    [Fact]
    public void Encrypt_明文达上限_成功()
    {
        IrisRsaOptions options = Options();
        (string publicKeyPem, string privateKeyPem) = IrisRsaOperations.GenerateKeyPairCore(options);
        string plaintext = new('a', 190);

        IrisOperationResult encrypted = IrisRsaOperations.Encrypt(options, plaintext, publicKeyPem);
        IrisOperationResult decrypted = IrisRsaOperations.Decrypt(options, encrypted.Output!, privateKeyPem);

        Assert.True(encrypted.Success, encrypted.StatusText);
        Assert.Equal(plaintext, decrypted.Output);
    }

    [Fact]
    public void Encrypt_公钥Pem非法_收敛为错误状态()
    {
        IrisOperationResult result = IrisRsaOperations.Encrypt(Options(), "input", "这不是 PEM");

        Assert.False(result.Success);
        Assert.Contains("公钥 PEM 无法解析", result.StatusText);
    }

    [Fact]
    public void Encrypt_公钥Pem为空_收敛为错误状态()
    {
        IrisOperationResult result = IrisRsaOperations.Encrypt(Options(), "input", string.Empty);

        Assert.False(result.Success);
        Assert.Contains("公钥 PEM 不能为空", result.StatusText);
    }

    [Fact]
    public void Decrypt_私钥Pem非法_收敛为错误状态()
    {
        IrisOperationResult result = IrisRsaOperations.Decrypt(Options(), "AAAA", "这不是 PEM");

        Assert.False(result.Success);
        Assert.Contains("私钥 PEM 无法解析", result.StatusText);
    }

    [Fact]
    public void Decrypt_密文长度不符_收敛为明确错误()
    {
        IrisRsaOptions options = Options();
        (_, string privateKeyPem) = IrisRsaOperations.GenerateKeyPairCore(options);
        string shortCiphertext = Convert.ToBase64String(new byte[10]);

        IrisOperationResult result = IrisRsaOperations.Decrypt(options, shortCiphertext, privateKeyPem);

        Assert.False(result.Success);
        Assert.Contains("密文长度不符", result.StatusText);
        Assert.Contains("256", result.StatusText);
    }

    [Fact]
    public void Decrypt_密文格式非法_收敛为错误状态()
    {
        IrisRsaOptions options = Options();
        (_, string privateKeyPem) = IrisRsaOperations.GenerateKeyPairCore(options);

        IrisOperationResult result = IrisRsaOperations.Decrypt(options, "!!!这不是Base64!!!", privateKeyPem);

        Assert.False(result.Success);
        Assert.Contains("密文", result.StatusText);
    }

    [Fact]
    public void Encrypt再Decrypt_密文格式Hex_RoundTrip还原()
    {
        IrisRsaOptions options = Options(cipherFormat: IrisBytesEncoding.Hex);
        (string publicKeyPem, string privateKeyPem) = IrisRsaOperations.GenerateKeyPairCore(options);

        IrisOperationResult encrypted = IrisRsaOperations.Encrypt(options, "HEX 密文", publicKeyPem);

        Assert.True(encrypted.Success, encrypted.StatusText);
        _ = Convert.FromHexString(encrypted.Output!);

        IrisOperationResult decrypted = IrisRsaOperations.Decrypt(options, encrypted.Output!, privateKeyPem);
        Assert.True(decrypted.Success, decrypted.StatusText);
        Assert.Equal("HEX 密文", decrypted.Output);
    }

    [Fact]
    public void GenerateKeyPair_密钥长度不支持_收敛为错误状态()
    {
        IrisOperationResult result = IrisRsaOperations.GenerateKeyPair(Options(keyBits: 1024));

        Assert.False(result.Success);
        Assert.Contains("不支持的密钥长度", result.StatusText);
    }
}
