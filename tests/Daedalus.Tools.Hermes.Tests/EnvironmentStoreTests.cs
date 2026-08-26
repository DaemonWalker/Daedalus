using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>EnvironmentStore 测试：读写往返、损坏备份恢复（DR-003）、Set/Unset 变量立即持久化。</summary>
public sealed class EnvironmentStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-hermes-tests-" + Guid.NewGuid().ToString("N"));

    public EnvironmentStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static EnvironmentData CreateSampleData() => new()
    {
        ActiveId = "dev",
        Environments =
        [
            new HermesEnvironment
            {
                Id = "dev",
                Name = "开发环境",
                Variables = [new EnvironmentVariable("host", "http://localhost:8080")],
            },
            new HermesEnvironment { Id = "prod", Name = "生产环境" },
        ],
    };

    [Fact]
    public async Task LoadAsync_文件不存在_返回空数据且不视为损坏()
    {
        var store = new EnvironmentStore(_directory);

        EnvironmentLoadResult result = await store.LoadAsync();

        Assert.Empty(result.Data.Environments);
        Assert.Null(result.Data.ActiveId);
        Assert.False(result.RecoveredFromCorruption);
        Assert.Null(result.BackupFilePath);
    }

    [Fact]
    public async Task SaveAsync再LoadAsync_已保存数据_往返一致()
    {
        var store = new EnvironmentStore(_directory);
        EnvironmentData data = CreateSampleData();

        await store.SaveAsync(data);
        EnvironmentLoadResult result = await store.LoadAsync();

        TestJson.Equal(data, result.Data);
        Assert.False(result.RecoveredFromCorruption);
    }

    [Fact]
    public async Task LoadAsync_文件损坏_备份原文件并返回空数据()
    {
        var store = new EnvironmentStore(_directory);
        string filePath = Path.Combine(_directory, "environments.json");
        await File.WriteAllTextAsync(filePath, "{ 这不是合法 JSON");

        EnvironmentLoadResult result = await store.LoadAsync();

        Assert.Empty(result.Data.Environments);
        Assert.True(result.RecoveredFromCorruption);
        Assert.NotNull(result.BackupFilePath);
        Assert.Equal("{ 这不是合法 JSON", await File.ReadAllTextAsync(result.BackupFilePath));
        Assert.False(File.Exists(filePath));
    }

    [Fact]
    public async Task LoadAsync_含未知字段_忽略并正常读取()
    {
        var store = new EnvironmentStore(_directory);
        string filePath = Path.Combine(_directory, "environments.json");
        await File.WriteAllTextAsync(filePath,
            """{"version": 1, "activeId": null, "environments": [], "futureField": 42}""");

        EnvironmentLoadResult result = await store.LoadAsync();

        Assert.False(result.RecoveredFromCorruption);
        Assert.Empty(result.Data.Environments);
    }

    [Fact]
    public async Task SetVariableAsync_新变量_追加并立即持久化()
    {
        var store = new EnvironmentStore(_directory);
        await store.SaveAsync(CreateSampleData());

        EnvironmentData data = await store.SetVariableAsync("dev", "token", "abc");

        EnvironmentVariable variable = Assert.Single(data.Environments[0].Variables, v => v.Key == "token");
        Assert.Equal("abc", variable.Value);
        // 立即持久化：重新加载能读到
        EnvironmentLoadResult reloaded = await store.LoadAsync();
        Assert.Contains(reloaded.Data.Environments[0].Variables, v => v.Key == "token" && v.Value == "abc");
    }

    [Fact]
    public async Task SetVariableAsync_已有变量_只更新值并保留secret与enabled标记()
    {
        var store = new EnvironmentStore(_directory);
        EnvironmentData data = CreateSampleData();
        data.Environments[0].Variables.Add(new EnvironmentVariable("token", "old", Secret: true, Enabled: false));
        await store.SaveAsync(data);

        EnvironmentData updated = await store.SetVariableAsync("dev", "token", "new");

        EnvironmentVariable variable = Assert.Single(updated.Environments[0].Variables, v => v.Key == "token");
        Assert.Equal(new EnvironmentVariable("token", "new", Secret: true, Enabled: false), variable);
    }

    [Fact]
    public async Task SetVariableAsync_环境不存在_抛InvalidOperationException()
    {
        var store = new EnvironmentStore(_directory);
        await store.SaveAsync(CreateSampleData());

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SetVariableAsync("no-such-env", "k", "v"));
    }

    [Fact]
    public async Task UnsetVariableAsync_变量存在_删除并立即持久化()
    {
        var store = new EnvironmentStore(_directory);
        await store.SaveAsync(CreateSampleData());

        EnvironmentData data = await store.UnsetVariableAsync("dev", "host");

        Assert.DoesNotContain(data.Environments[0].Variables, v => v.Key == "host");
        EnvironmentLoadResult reloaded = await store.LoadAsync();
        Assert.DoesNotContain(reloaded.Data.Environments[0].Variables, v => v.Key == "host");
    }

    [Fact]
    public async Task UnsetVariableAsync_变量不存在_不报错()
    {
        var store = new EnvironmentStore(_directory);
        await store.SaveAsync(CreateSampleData());

        EnvironmentData data = await store.UnsetVariableAsync("dev", "no-such-key");

        Assert.Single(data.Environments[0].Variables);
    }
}
