namespace Daedalus.Tools.Oedipus.Tests;

/// <summary>OedipusSettingsStore 测试：读写往返、缺省容忍、损坏备份恢复（DR-003）。</summary>
public sealed class OedipusSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-oedipus-tests-" + Guid.NewGuid().ToString("N"));

    public OedipusSettingsStoreTests()
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
        var store = new OedipusSettingsStore(_directory);

        OedipusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(OedipusSettings.Default, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.BackupFilePath);
    }

    [Fact]
    public async Task SaveAsync再LoadAsync_已保存设置_往返一致()
    {
        var store = new OedipusSettingsStore(_directory);
        var settings = new OedipusSettings(OedipusSettings.CurrentVersion, "jwt");

        await store.SaveAsync(settings);
        OedipusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(settings, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_文件损坏_备份原文件并返回默认值()
    {
        var store = new OedipusSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, "{ 这不是合法 JSON");

        OedipusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(OedipusSettings.Default, result.Settings);
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
        var store = new OedipusSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, """{"version": 1}""");

        OedipusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(new OedipusSettings(1, null), result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_含未知字段_忽略并正常读取()
    {
        var store = new OedipusSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, """{"version": 1, "lastDecoding": "base64", "futureField": true}""");

        OedipusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(new OedipusSettings(1, "base64"), result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }
}
