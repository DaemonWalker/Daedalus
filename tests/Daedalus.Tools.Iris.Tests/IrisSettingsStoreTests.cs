using System.Text.Json;

namespace Daedalus.Tools.Iris.Tests;

/// <summary>IrisSettingsStore 与设置模型测试：读写往返、缺省容忍、损坏备份恢复（DR-003）、不落密钥字段。</summary>
public sealed class IrisSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-iris-tests-" + Guid.NewGuid().ToString("N"));

    public IrisSettingsStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task LoadAsync_文件不存在_返回默认且不视为损坏()
    {
        var store = new IrisSettingsStore(_directory);

        IrisSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(IrisSettings.Default, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.BackupFilePath);
    }

    [Fact]
    public async Task SaveAsync再LoadAsync_已保存设置_往返一致()
    {
        var store = new IrisSettingsStore(_directory);
        var settings = new IrisSettings(
            IrisSettings.CurrentVersion,
            "aes-enc",
            IrisAesSettings.FromOptions(new IrisAesOptions(
                IrisAesCipherMode.Gcm, 192, IrisAesKeySource.RawKey, IrisBytesEncoding.Hex,
                IrisAesIvSource.Manual, IrisBytesEncoding.Hex, IrisBytesEncoding.Hex)),
            IrisRsaSettings.FromOptions(new IrisRsaOptions(IrisRsaPadding.Pkcs1, 4096, IrisBytesEncoding.Hex)));

        await store.SaveAsync(settings);
        IrisSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(settings, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task SaveAsync_AES参数_不落密钥口令IV值字段()
    {
        var store = new IrisSettingsStore(_directory);
        var settings = new IrisSettings(
            IrisSettings.CurrentVersion,
            "aes-enc",
            IrisAesSettings.FromOptions(IrisAesOptions.Default),
            IrisRsaSettings.FromOptions(IrisRsaOptions.Default));

        await store.SaveAsync(settings);
        string json = await File.ReadAllTextAsync(Path.Combine(_directory, "settings.json"));

        // 密钥/口令/IV 是运行时输入，设置文件只允许存在参数选择字段（iris.md §6）
        using JsonDocument document = JsonDocument.Parse(json);
        string[] aesFields = document.RootElement.GetProperty("aes").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["mode", "keyBits", "keySource", "keyFormat", "ivSource", "ivFormat", "cipherFormat"], aesFields);
        string[] rsaFields = document.RootElement.GetProperty("rsa").EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(["padding", "keyBits", "cipherFormat"], rsaFields);
    }

    [Fact]
    public async Task LoadAsync_文件损坏_备份原文件并返回默认值()
    {
        var store = new IrisSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, "{ 这不是合法 JSON");

        IrisSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(IrisSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.BackupFilePath);
        Assert.True(File.Exists(result.BackupFilePath));
        // 备份保留原内容，原路径已移除（下次启动走默认值路径，不再反复报损坏）
        Assert.Equal("{ 这不是合法 JSON", await File.ReadAllTextAsync(result.BackupFilePath));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task LoadAsync_字段缺失_容忍并以默认补齐()
    {
        var store = new IrisSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, """{"version": 1}""");

        IrisSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(new IrisSettings(1, null, null, null), result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_含未知字段_忽略并正常读取()
    {
        var store = new IrisSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, """{"version": 1, "lastMethod": "jwt-dec", "futureField": true}""");

        IrisSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(new IrisSettings(1, "jwt-dec", null, null), result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public void ToOptions_Aes未知枚举名与非法长度_回落默认()
    {
        var settings = new IrisAesSettings("rot13", 512, "??", null, "auto", "base64", "hex");

        IrisAesOptions options = settings.ToOptions();

        Assert.Equal(IrisAesOptions.Default.Mode, options.Mode);
        Assert.Equal(IrisAesOptions.Default.KeyBits, options.KeyBits);
        Assert.Equal(IrisAesOptions.Default.KeySource, options.KeySource);
        // 已知值（含大小写不敏感）正常还原
        Assert.Equal(IrisAesIvSource.Auto, options.IvSource);
        Assert.Equal(IrisBytesEncoding.Base64, options.IvFormat);
        Assert.Equal(IrisBytesEncoding.Hex, options.CipherFormat);
        // 缺失字段回落默认
        Assert.Equal(IrisAesOptions.Default.KeyFormat, options.KeyFormat);
    }

    [Fact]
    public void ToOptions_Rsa非法长度_回落默认2048()
    {
        var settings = new IrisRsaSettings("pkcs1", 1024, null);

        IrisRsaOptions options = settings.ToOptions();

        Assert.Equal(IrisRsaPadding.Pkcs1, options.Padding);
        Assert.Equal(2048, options.KeyBits);
        Assert.Equal(IrisRsaOptions.Default.CipherFormat, options.CipherFormat);
    }

    [Fact]
    public void FromOptions再ToOptions_AES与RSA_往返一致()
    {
        var aes = new IrisAesOptions(
            IrisAesCipherMode.Gcm, 192, IrisAesKeySource.RawKey, IrisBytesEncoding.Hex,
            IrisAesIvSource.Manual, IrisBytesEncoding.Base64, IrisBytesEncoding.Hex);
        var rsa = new IrisRsaOptions(IrisRsaPadding.Pkcs1, 3072, IrisBytesEncoding.Hex);

        Assert.Equal(aes, IrisAesSettings.FromOptions(aes).ToOptions());
        Assert.Equal(rsa, IrisRsaSettings.FromOptions(rsa).ToOptions());
    }
}
