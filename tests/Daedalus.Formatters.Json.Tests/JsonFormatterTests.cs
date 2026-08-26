using System.Text;
using System.Text.Json;

using Daedalus.Abstractions;

namespace Daedalus.Formatters.Json.Tests;

/// <summary>覆盖 docs/plugins/proteus/json.md §4 测试要点。</summary>
public class JsonFormatterTests
{
    private readonly JsonFormatter _formatter = new();

    [Fact]
    public void Metadata_格式标识_符合设计文档()
    {
        Assert.Equal("json", _formatter.FormatId);
        Assert.Equal("JSON", _formatter.DisplayName);
    }

    [Fact]
    public void TryValidate_合法输入_返回True且无错误()
    {
        bool valid = _formatter.TryValidate("{\"a\": 1, \"b\": [true, null]}", out string? error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_非法输入_返回False且行列准确()
    {
        // "b" 的值缺失，第 4 行的 "}" 位置即出错位置
        string input = "{\n  \"a\": 1,\n  \"b\": \n}";

        bool valid = _formatter.TryValidate(input, out string? error);

        Assert.False(valid);
        Assert.Contains("第 4 行第 1 列", error);
    }

    [Fact]
    public void TryValidate_多字节字符行_列按字符而非字节计算()
    {
        // 单行："["中", bad]"，bad 在第 7 个字符处（若按字节算会偏到第 9 列）
        string input = "[\"中\", bad]";

        bool valid = _formatter.TryValidate(input, out string? error);

        Assert.False(valid);
        Assert.Contains("第 1 行第 7 列", error);
    }

    [Fact]
    public void TryValidate_空输入_返回False()
    {
        bool valid = _formatter.TryValidate("", out string? error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_含注释_严格拒绝()
    {
        bool valid = _formatter.TryValidate("{ /* 注释 */ \"a\": 1 }", out _);

        Assert.False(valid);
    }

    [Fact]
    public void TryValidate_尾随逗号_严格拒绝()
    {
        bool valid = _formatter.TryValidate("{\"a\": 1,}", out _);

        Assert.False(valid);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Format_美化_缩进宽度参数生效(int indentSize)
    {
        string formatted = _formatter.Format("{\"a\":{\"b\":1}}", new FormatOptions(Minify: false, IndentSize: indentSize));

        string expectedIndent = new string(' ', indentSize);
        Assert.Contains($"\n{expectedIndent}\"a\"", formatted);
        Assert.Contains($"\n{expectedIndent}{expectedIndent}\"b\"", formatted);
    }

    [Fact]
    public void Format_压缩_输出单行无多余空白()
    {
        string minified = _formatter.Format("{\n  \"a\": 1,\n  \"b\": [ true, null ]\n}", new FormatOptions(Minify: true, IndentSize: 4));

        Assert.Equal("{\"a\":1,\"b\":[true,null]}", minified);
    }

    [Fact]
    public void Format_美化往返_语义等价()
    {
        string input = "{\"name\":\"测试\",\"nums\":[1,2.5,-3],\"obj\":{\"x\":true,\"y\":null}}";

        string formatted = _formatter.Format(input, new FormatOptions(Minify: false, IndentSize: 4));

        AssertSemanticEquals(input, formatted);
    }

    [Fact]
    public void Format_压缩往返_语义等价()
    {
        string input = "{ \"name\" : \"测试\", \"nums\" : [ 1, 2.5, -3 ] }";

        string minified = _formatter.Format(input, new FormatOptions(Minify: true, IndentSize: 4));

        AssertSemanticEquals(input, minified);
    }

    [Fact]
    public void Format_非法输入_抛FormatException且含行列()
    {
        FormatException ex = Assert.Throws<FormatException>(
            () => _formatter.Format("{\n  bad\n}", new FormatOptions(Minify: false, IndentSize: 4)));

        Assert.Contains("第 2 行", ex.Message);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
    }

    [Fact]
    public void Format_超大数字_字面量保持不变()
    {
        string input = "{\"big\": 123456789012345678901234567890}";

        string minified = _formatter.Format(input, new FormatOptions(Minify: true, IndentSize: 4));

        using JsonDocument document = JsonDocument.Parse(minified);
        Assert.Equal("123456789012345678901234567890", document.RootElement.GetProperty("big").GetRawText());
    }

    [Fact]
    public void Format_Unicode字符_值不变()
    {
        string minified = _formatter.Format("{ \"text\" : \"中文🚀\" }", new FormatOptions(Minify: true, IndentSize: 4));

        using JsonDocument document = JsonDocument.Parse(minified);
        Assert.Equal("中文🚀", document.RootElement.GetProperty("text").GetString());
    }

    [Fact]
    public void Format_深嵌套_正常往返()
    {
        var builder = new StringBuilder();
        for (int i = 0; i < 100; i++)
        {
            builder.Append("{\"n\":");
        }

        builder.Append('1');
        builder.Append('}', 100);
        string input = builder.ToString();

        string formatted = _formatter.Format(input, new FormatOptions(Minify: false, IndentSize: 2));

        AssertSemanticEquals(input, formatted);
    }

    private static void AssertSemanticEquals(string expected, string actual)
    {
        // 与被测实现一致放开深度上限，避免测试自身在深嵌套用例上先倒下
        var options = new JsonDocumentOptions { MaxDepth = int.MaxValue };
        using JsonDocument expectedDoc = JsonDocument.Parse(expected, options);
        using JsonDocument actualDoc = JsonDocument.Parse(actual, options);
        Assert.True(JsonElement.DeepEquals(expectedDoc.RootElement, actualDoc.RootElement),
            $"语义不等价：\n期望：{expected}\n实际：{actual}");
    }
}
