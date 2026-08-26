namespace Daedalus.Tools.Proteus.Tests;

/// <summary>ProteusSettingsStore 测试：读写往返、缺省、损坏备份恢复（DR-003）。</summary>
public sealed class ProteusSettingsStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-proteus-tests-" + Guid.NewGuid().ToString("N"));

    public ProteusSettingsStoreTests()
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
        var store = new ProteusSettingsStore(_directory);

        ProteusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ProteusSettings.Default, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.BackupFilePath);
    }

    [Fact]
    public async Task SaveAsync再LoadAsync_已保存设置_往返一致()
    {
        var store = new ProteusSettingsStore(_directory);
        var settings = new ProteusSettings(ProteusSettings.CurrentVersion, "xml", 8);

        await store.SaveAsync(settings);
        ProteusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(settings, result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_文件损坏_备份原文件并返回默认值()
    {
        var store = new ProteusSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, "{ 这不是合法 JSON");

        ProteusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ProteusSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.BackupFilePath);
        Assert.True(File.Exists(result.BackupFilePath));
        // 备份保留原内容，原路径已移除（下次启动走默认值路径，不再反复报损坏）
        Assert.Equal("{ 这不是合法 JSON", await File.ReadAllTextAsync(result.BackupFilePath));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task LoadAsync_缩进值非法_按损坏备份恢复()
    {
        var store = new ProteusSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, """{"version": 1, "lastFormatId": "json", "indentSize": 0}""");

        ProteusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(ProteusSettings.Default, result.Settings);
        Assert.True(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_含未知字段_忽略并正常读取()
    {
        var store = new ProteusSettingsStore(_directory);
        string filePath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(filePath, """{"version": 1, "lastFormatId": "xml", "indentSize": 2, "futureField": true}""");

        ProteusSettingsLoadResult result = await store.LoadAsync();

        Assert.Equal(new ProteusSettings(1, "xml", 2), result.Settings);
        Assert.False(result.RecoveredFromCorruption);
    }
}
