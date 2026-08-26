using Daedalus.Tools.Hermes.Settings;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>HermesSettingsStore 测试：读写往返、缺省、损坏备份恢复（DR-003）、非法值与未知字段。</summary>
public sealed class HermesSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-hermes-tests-" + Guid.NewGuid().ToString("N"));

    public HermesSettingsStoreTests()
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
        var store = new HermesSettingsStore(_directory);

        HermesSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(HermesSettings.Default, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.BackupFilePath);
    }

    [Fact]
    public async Task SaveAsync再LoadAsync_已保存设置_往返一致()
    {
        var store = new HermesSettingsStore(_directory);
        var settings = new HermesSettings(HermesSettings.CurrentVersion, false, false, true, 1024, 500, 2048);

        await store.SaveAsync(settings);
        HermesSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(settings, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_文件损坏_备份原文件并返回默认值()
    {
        var store = new HermesSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, "{ 这不是合法 JSON");

        HermesSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(HermesSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.BackupFilePath);
        Assert.Equal("{ 这不是合法 JSON", await File.ReadAllTextAsync(result.BackupFilePath));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task LoadAsync_上限值非法_按损坏备份恢复()
    {
        var store = new HermesSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath,
            """{"version": 1, "followRedirects": true, "useCookies": true, "ignoreServerCertificate": false, "scriptMemoryLimitBytes": 4194304, "scriptTimeoutMs": 0, "responseBodyLimitBytes": 10485760}""");

        HermesSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(HermesSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_含未知字段_忽略并正常读取()
    {
        var store = new HermesSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath,
            """{"version": 1, "followRedirects": false, "useCookies": true, "ignoreServerCertificate": false, "scriptMemoryLimitBytes": 4194304, "scriptTimeoutMs": 2000, "responseBodyLimitBytes": 10485760, "futureField": true}""");

        HermesSettingsLoadResult result = await store.LoadAsync();

        Assert.False(result.RecoveredFromCorruption);
        Assert.False(result.Settings.FollowRedirects);
        Assert.Equal(HermesSettings.DefaultScriptTimeoutMs, result.Settings.ScriptTimeoutMs);
    }
}
