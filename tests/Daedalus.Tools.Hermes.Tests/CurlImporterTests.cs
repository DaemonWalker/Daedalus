using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Editing;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>CurlImporter 测试（hermes.md §9.2 / §12），样本驱动（TestData/）。</summary>
public sealed class CurlImporterTests
{
    private static string Sample(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", name));

    private static CurlImportResult Import(string text) => new CurlImporter().Import(text);

    [Fact]
    public void Tokenize_引号转义续行_正确分词()
    {
        List<string> tokens = CurlImporter.Tokenize("curl 'a b' \"c\\\"d\" e\\ f \\\n  --opt 'x'");

        Assert.Equal(["curl", "a b", "c\"d", "e f", "--opt", "x"], tokens);
    }

    [Fact]
    public void Tokenize_单引号内转义不生效()
    {
        List<string> tokens = CurlImporter.Tokenize("'a\\nb'");

        Assert.Equal(["a\\nb"], tokens);
    }

    [Fact]
    public void Import_ChromeGet样本_映射正确()
    {
        CurlImportResult result = Import(Sample("curl-chrome-get.txt"));

        Assert.Equal("GET", result.Draft.Method);
        Assert.Equal("http://localhost:8080/api/orders?page=1", result.Draft.Url);
        Assert.Contains(result.Draft.Headers, h => h.Key == "Accept" && h.Value == "application/json, text/plain, */*");
        Assert.Contains(result.Draft.Headers, h => h.Key == "Referer");
        Assert.Contains(result.Draft.Headers, h => h.Key == "User-Agent" && h.Value.Contains("Mozilla/5.0"));
        // Cookie: 头拆入 Cookie 字段
        Assert.Contains(result.Draft.Headers, h => h.Key == "Cookie" && h.Value == "session=abc123; theme=dark");
        // --compressed 未知，汇总提示
        Assert.Contains(result.IgnoredArguments, a => a.Contains("--compressed"));
        Assert.False(result.HasInsecureFlag);
        Assert.Null(result.Draft.Body);
    }

    [Fact]
    public void Import_ChromePost样本_映射正确()
    {
        CurlImportResult result = Import(Sample("curl-chrome-post.txt"));

        Assert.Equal("POST", result.Draft.Method);
        Assert.Equal("http://localhost:8080/api/login", result.Draft.Url);
        // --data-raw → raw 请求体，Content-Type 沿用已有请求头
        Assert.Equal("{\"user\":\"a\",\"pwd\":\"b\"}", result.Draft.Body!.Text);
        Assert.Equal("application/json", result.Draft.Body.ContentType);
        // -u → Authorization: Basic base64
        string expected = Convert.ToBase64String("admin:secret"u8.ToArray());
        Assert.Contains(result.Draft.Headers, h => h.Key == "Authorization" && h.Value == $"Basic {expected}");
        // -k 不映射为请求属性，仅标记提示
        Assert.True(result.HasInsecureFlag);
        Assert.Contains(result.IgnoredArguments, a => a.Contains("--http2"));
        Assert.Contains(result.IgnoredArguments, a => a.Contains("--compressed"));
    }

    [Fact]
    public void Import_带数据未指定方法_默认POST()
    {
        CurlImportResult result = Import("curl http://a/b --data 'x=1'");

        Assert.Equal("POST", result.Draft.Method);
        // Content-Type 缺省 form-urlencoded（§9.2）
        Assert.Equal("application/x-www-form-urlencoded", result.Draft.Body!.ContentType);
    }

    [Fact]
    public void Import_多个data_以与号连接()
    {
        CurlImportResult result = Import("curl http://a/b --data 'x=1' --data-raw 'y=2'");

        Assert.Equal("x=1&y=2", result.Draft.Body!.Text);
    }

    [Fact]
    public void Import_b参数与Cookie头_合并()
    {
        CurlImportResult result = Import("curl http://a/b -H 'Cookie: a=1' -b 'b=2'");

        Assert.Contains(result.Draft.Headers, h => h.Key == "Cookie" && h.Value == "a=1; b=2");
    }

    [Fact]
    public void Import_短参数连写与长参数等号_均支持()
    {
        CurlImportResult result = Import("curl -XPUT --url=http://a/b -HAccept:text/plain -Aagent/1.0");

        Assert.Equal("PUT", result.Draft.Method);
        Assert.Equal("http://a/b", result.Draft.Url);
        Assert.Contains(result.Draft.Headers, h => h.Key == "Accept" && h.Value == "text/plain");
        Assert.Contains(result.Draft.Headers, h => h.Key == "User-Agent" && h.Value == "agent/1.0");
    }

    [Fact]
    public void Import_缺URL_报错()
    {
        Assert.Throws<FormatException>(() => Import("curl -X POST"));
        Assert.Throws<FormatException>(() => Import("   "));
    }

    [Fact]
    public void Import_结果不自动入集合_仅产出草稿()
    {
        // 契约层面：返回值只有 RequestDraft + 提示，没有集合写入路径（FR-HERMES-034）
        CurlImportResult result = Import("curl http://a/b");

        Assert.IsType<RequestDraft>(result.Draft);
        Assert.Null(result.Draft.PostResponseScript);
        Assert.Null(result.Draft.Options);
    }
}
