using Daedalus.Tools.Hermes.Collections;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>CollectionStore 测试：读写往返、文件格式、删除、逐文件损坏备份恢复（DR-003）。</summary>
public sealed class CollectionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-hermes-tests-" + Guid.NewGuid().ToString("N"));

    public CollectionStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static HermesCollection CreateSampleCollection(string id = "01JEXAMPLE0000000000000000") => new()
    {
        Id = id,
        Name = "示例集合",
        Items =
        [
            new CollectionNode
            {
                Type = CollectionNodeType.Folder,
                Name = "用户模块",
                Items =
                [
                    new CollectionNode
                    {
                        Type = CollectionNodeType.Request,
                        Name = "登录",
                        Method = "POST",
                        Url = "{{host}}/api/login",
                        Headers = [new KeyValueEntry("Content-Type", "application/json")],
                        Body = new RequestBody
                        {
                            Kind = RequestBodyKind.Raw,
                            ContentType = "application/json",
                            Text = "{\"user\":\"a\"}",
                        },
                        Options = new RequestOptions(FollowRedirect: null, UseCookies: false),
                        PostResponseScript = "pm.environment.set('token', pm.response.json().token);",
                    },
                ],
            },
            new CollectionNode
            {
                Type = CollectionNodeType.Request,
                Name = "表单",
                Method = "POST",
                Url = "{{host}}/api/form",
                Body = new RequestBody
                {
                    Kind = RequestBodyKind.UrlEncoded,
                    Fields = [new KeyValueEntry("a", "1"), new KeyValueEntry("b", "2", Enabled: false)],
                },
            },
        ],
    };

    [Fact]
    public async Task LoadAllAsync_目录不存在_返回空且不视为损坏()
    {
        var store = new CollectionStore(_directory);

        CollectionStoreLoadResult result = await store.LoadAllAsync();

        Assert.Empty(result.Collections);
        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public async Task SaveAsync再LoadAllAsync_已保存集合_往返一致()
    {
        var store = new CollectionStore(_directory);
        HermesCollection collection = CreateSampleCollection();

        await store.SaveAsync(collection);
        CollectionStoreLoadResult result = await store.LoadAllAsync();

        HermesCollection loaded = Assert.Single(result.Collections);
        TestJson.Equal(collection, loaded);
        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public async Task SaveAsync_文件格式_符合hermes文档的小写枚举与camelCase()
    {
        var store = new CollectionStore(_directory);

        await store.SaveAsync(CreateSampleCollection());
        string json = await File.ReadAllTextAsync(Path.Combine(_directory, "collections", "01JEXAMPLE0000000000000000.json"));

        Assert.Contains("\"version\": 1", json);
        Assert.Contains("\"type\": \"folder\"", json);
        Assert.Contains("\"type\": \"request\"", json);
        Assert.Contains("\"kind\": \"raw\"", json);
        Assert.Contains("\"kind\": \"urlEncoded\"", json);
        Assert.Contains("\"followRedirect\": null", json);
        Assert.Contains("\"postResponseScript\":", json);
    }

    [Fact]
    public async Task SaveAsync_多个集合_各自一文件全部读出()
    {
        var store = new CollectionStore(_directory);
        await store.SaveAsync(CreateSampleCollection("01JAAAAAAAAAAAAAAAAAAAAA"));
        await store.SaveAsync(CreateSampleCollection("01JBBBBBBBBBBBBBBBBBBBBB"));

        CollectionStoreLoadResult result = await store.LoadAllAsync();

        Assert.Equal(2, result.Collections.Count);
    }

    [Fact]
    public async Task SaveAsync_id含路径分隔符_抛ArgumentException()
    {
        var store = new CollectionStore(_directory);
        HermesCollection collection = CreateSampleCollection() with { Id = "../evil" };

        await Assert.ThrowsAsync<ArgumentException>(() => store.SaveAsync(collection));
    }

    [Fact]
    public async Task LoadAllAsync_单个文件损坏_备份该文件并返回其余集合()
    {
        var store = new CollectionStore(_directory);
        await store.SaveAsync(CreateSampleCollection("01JAAAAAAAAAAAAAAAAAAAAA"));
        string brokenPath = Path.Combine(_directory, "collections", "01JBBBBBBBBBBBBBBBBBBBBB.json");
        await File.WriteAllTextAsync(brokenPath, "{ 这不是合法 JSON");

        CollectionStoreLoadResult result = await store.LoadAllAsync();

        Assert.Single(result.Collections);
        CorruptedFileRecovery recovery = Assert.Single(result.Recoveries);
        Assert.Equal(brokenPath, recovery.FilePath);
        Assert.True(File.Exists(recovery.BackupFilePath));
        // 备份保留原内容，原路径已移除
        Assert.Equal("{ 这不是合法 JSON", await File.ReadAllTextAsync(recovery.BackupFilePath));
        Assert.False(File.Exists(brokenPath));
    }

    [Fact]
    public async Task LoadAllAsync_文件缺必填字段_按损坏备份恢复()
    {
        var store = new CollectionStore(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "collections"));
        string brokenPath = Path.Combine(_directory, "collections", "01JAAAAAAAAAAAAAAAAAAAAA.json");
        await File.WriteAllTextAsync(brokenPath, """{"version": 1, "name": "缺 id 的集合", "items": []}""");

        CollectionStoreLoadResult result = await store.LoadAllAsync();

        Assert.Empty(result.Collections);
        Assert.Single(result.Recoveries);
    }

    [Fact]
    public async Task LoadAllAsync_含未知字段_忽略并正常读取()
    {
        var store = new CollectionStore(_directory);
        Directory.CreateDirectory(Path.Combine(_directory, "collections"));
        string filePath = Path.Combine(_directory, "collections", "01JAAAAAAAAAAAAAAAAAAAAA.json");
        await File.WriteAllTextAsync(filePath,
            """{"version": 1, "id": "01JAAAAAAAAAAAAAAAAAAAAA", "name": "集合", "items": [], "futureField": true}""");

        CollectionStoreLoadResult result = await store.LoadAllAsync();

        HermesCollection loaded = Assert.Single(result.Collections);
        Assert.Equal("集合", loaded.Name);
        Assert.Empty(result.Recoveries);
    }

    [Fact]
    public async Task DeleteAsync_集合存在_删除对应文件()
    {
        var store = new CollectionStore(_directory);
        HermesCollection collection = CreateSampleCollection();
        await store.SaveAsync(collection);

        await store.DeleteAsync(collection.Id);

        CollectionStoreLoadResult result = await store.LoadAllAsync();
        Assert.Empty(result.Collections);
    }

    [Fact]
    public async Task DeleteAsync_集合不存在_不报错()
    {
        var store = new CollectionStore(_directory);

        await store.DeleteAsync("01JNOTEXIST000000000000000");
    }
}
