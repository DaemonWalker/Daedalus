using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Variables;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>PostmanImporter 测试（hermes.md §9.1 / §12），样本驱动（TestData/）。</summary>
public sealed class PostmanImporterTests
{
    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static PostmanImportResult ImportCollection(IReadOnlyCollection<string>? existingNames = null) =>
        new PostmanImporter().Import(Sample("postman-collection-v2.1.json"), existingCollectionNames: existingNames);

    [Fact]
    public void Import_CollectionV21样本_集合与树结构映射正确()
    {
        PostmanImportResult result = ImportCollection();

        Assert.NotNull(result.Collection);
        Assert.Null(result.Environment);
        Assert.Equal("示例 API", result.Collection.Name);
        Assert.False(string.IsNullOrWhiteSpace(result.Collection.Id));

        // 顶层：文件夹「用户」+ 三个请求
        Assert.Equal(4, result.Collection.Items.Count);
        CollectionNode folder = result.Collection.Items[0];
        Assert.Equal(CollectionNodeType.Folder, folder.Type);
        Assert.Equal("用户", folder.Name);
        Assert.Single(folder.Items!);
    }

    [Fact]
    public void Import_CollectionV21样本_请求字段与脚本映射正确()
    {
        CollectionNode login = ImportCollection().Collection!.Items[0].Items![0];

        Assert.Equal("登录", login.Name);
        Assert.Equal("POST", login.Method);
        Assert.Equal("{{host}}/api/login", login.Url);
        Assert.Equal(2, login.Headers!.Count);
        Assert.Equal(new KeyValueEntry("Content-Type", "application/json"), login.Headers[0]);
        // disabled: true → Enabled = false
        Assert.Equal(new KeyValueEntry("X-Debug", "1", Enabled: false), login.Headers[1]);
        Assert.Equal(RequestBodyKind.Raw, login.Body!.Kind);
        Assert.Equal("application/json", login.Body.ContentType);
        Assert.Equal("{\"user\":\"a\",\"pwd\":\"b\"}", login.Body.Text);
        // test 事件多行 exec 以换行连接（FR-HERMES-033 原样保留）
        Assert.Equal("const json = pm.response.json();\npm.environment.set('token', json.token);", login.PostResponseScript);
    }

    [Fact]
    public void Import_CollectionV21样本_忽略项全部汇总()
    {
        PostmanImportResult result = ImportCollection();

        // prerequest、url 路径变量、formdata、请求级 auth、集合级 auth / variable
        Assert.Contains(result.IgnoredItems, i => i.Contains("登录") && i.Contains("prerequest"));
        Assert.Contains(result.IgnoredItems, i => i.Contains("查询订单") && i.Contains("路径变量"));
        Assert.Contains(result.IgnoredItems, i => i.Contains("上传") && i.Contains("form-data"));
        Assert.Contains(result.IgnoredItems, i => i.Contains("上传") && i.Contains("auth"));
        Assert.Contains(result.IgnoredItems, i => i.Contains("示例 API") && i.Contains("auth"));
        Assert.Contains(result.IgnoredItems, i => i.Contains("示例 API") && i.Contains("变量"));
    }

    [Fact]
    public void Import_CollectionV21样本_路径变量与urlencoded映射正确()
    {
        HermesCollection collection = ImportCollection().Collection!;

        // :id 路径变量按普通文本保留在 URL 中
        Assert.Equal("{{host}}/api/orders/:id", collection.Items[1].Url);

        RequestBody body = collection.Items[2].Body!;
        Assert.Equal(RequestBodyKind.UrlEncoded, body.Kind);
        Assert.Equal(new KeyValueEntry("a", "1"), body.Fields![0]);
        Assert.Equal(new KeyValueEntry("b", "2", Enabled: false), body.Fields[1]);
    }

    [Fact]
    public void Import_V20样本_明确拒绝并说明版本()
    {
        PostmanImportException ex = Assert.Throws<PostmanImportException>(
            () => new PostmanImporter().Import(Sample("postman-collection-v2.0.json")));

        Assert.Contains("v2.0", ex.Message);
        Assert.Contains("v2.1", ex.Message);
    }

    [Fact]
    public void Import_无法识别的结构_报错()
    {
        Assert.Throws<PostmanImportException>(() => new PostmanImporter().Import("{\"foo\": 1}"));
        Assert.Throws<PostmanImportException>(() => new PostmanImporter().Import("[1, 2]"));
        Assert.Throws<PostmanImportException>(() => new PostmanImporter().Import("not json"));
    }

    [Fact]
    public void Import_EnvironmentV1样本_变量映射正确()
    {
        PostmanImportResult result = new PostmanImporter().Import(Sample("postman-environment-v1.json"));

        Assert.Null(result.Collection);
        HermesEnvironment environment = result.Environment!;
        Assert.Equal("开发环境", environment.Name);
        Assert.False(string.IsNullOrWhiteSpace(environment.Id));
        Assert.Equal(3, environment.Variables.Count);
        Assert.Equal(new EnvironmentVariable("host", "http://localhost:8080"), environment.Variables[0]);
        // type: secret → Secret 标记
        Assert.Equal(new EnvironmentVariable("token", "abc123", Secret: true), environment.Variables[1]);
        Assert.Equal(new EnvironmentVariable("off", "x", Enabled: false), environment.Variables[2]);
    }

    [Fact]
    public void Import_名称冲突_自动追加序号()
    {
        PostmanImportResult result = ImportCollection(["示例 API"]);
        Assert.Equal("示例 API (2)", result.Collection!.Name);

        PostmanImportResult again = ImportCollection(["示例 API", "示例 API (2)"]);
        Assert.Equal("示例 API (3)", again.Collection!.Name);
    }

    [Fact]
    public void Import_集合id为合法Ulid()
    {
        string id = ImportCollection().Collection!.Id;

        Assert.Equal(26, id.Length);
        Assert.All(id, c => Assert.True(char.IsLetterOrDigit(c)));
    }
}
