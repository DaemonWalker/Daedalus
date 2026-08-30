using Daedalus.App;

using Serilog.Events;

namespace Daedalus.App.Tests;

public sealed class LoggingBootstrapTests : IDisposable
{
    private readonly string _directory;

    public LoggingBootstrapTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "daedalus-app-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SaveAsync_文件不存在_写入配置并可被Load读回()
    {
        string filePath = Path.Combine(_directory, LoggingBootstrap.ConfigFileName);
        var settings = new LoggingSettings(
            LogEventLevel.Debug,
            new Dictionary<string, LogEventLevel> { ["daedalus.tools.hermes"] = LogEventLevel.Verbose });

        await LoggingBootstrap.SaveAsync(filePath, settings);

        var warnings = new List<string>();
        LoggingSettings loaded = LoggingBootstrap.Load(filePath, warnings);
        Assert.Equal(LogEventLevel.Debug, loaded.DefaultLevel);
        Assert.Equal(LogEventLevel.Verbose, loaded.Overrides["daedalus.tools.hermes"]);
        Assert.Empty(warnings);
    }

    [Fact]
    public async Task SaveAsync_文件含其他节_保留其他节()
    {
        string filePath = Path.Combine(_directory, LoggingBootstrap.ConfigFileName);
        await File.WriteAllTextAsync(filePath, """{ "other": { "key": "value" } }""");

        await LoggingBootstrap.SaveAsync(filePath, LoggingBootstrap.Default);

        string json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"other\"", json);
        Assert.Contains("\"key\"", json);
        Assert.Contains("\"logging\"", json);
    }

    [Fact]
    public async Task SaveAsync_原文件损坏_备份后重写()
    {
        string filePath = Path.Combine(_directory, LoggingBootstrap.ConfigFileName);
        await File.WriteAllTextAsync(filePath, "{ 这不是合法 JSON");

        await LoggingBootstrap.SaveAsync(filePath, LoggingBootstrap.Default);

        string[] backups = Directory.GetFiles(_directory, LoggingBootstrap.ConfigFileName + ".broken-*");
        Assert.Single(backups);
        var warnings = new List<string>();
        LoggingSettings loaded = LoggingBootstrap.Load(filePath, warnings);
        Assert.Equal(LoggingBootstrap.Default.DefaultLevel, loaded.DefaultLevel);
    }

    [Fact]
    public async Task SaveAsync_overrides为空_不写overrides节()
    {
        string filePath = Path.Combine(_directory, LoggingBootstrap.ConfigFileName);
        await File.WriteAllTextAsync(filePath, """{ "logging": { "default": "Debug", "overrides": { "daedalus.tools.hermes": "Verbose" } } }""");

        await LoggingBootstrap.SaveAsync(filePath, new LoggingSettings(LogEventLevel.Warning, new Dictionary<string, LogEventLevel>()));

        string json = await File.ReadAllTextAsync(filePath);
        Assert.Contains("\"Warning\"", json);
        Assert.DoesNotContain("overrides", json);
    }
}
