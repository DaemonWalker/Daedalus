using Daedalus.Abstractions;

using Daedalus.Tools.Hermes.Response;

using Serilog;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>ResponseBeautifier：FindFormatter 美化、缺失/非法内容回退纯文本。</summary>
public sealed class ResponseBeautifierTests
{
    /// <summary>测试用 IToolHost：只实现 FindFormatter 语义，其余成员不应被触碰。</summary>
    private sealed class StubHost(IReadOnlyList<IFormatter> formatters) : IToolHost
    {
        public IReadOnlyList<IFormatter> Formatters => formatters;

        public IFormatter? FindFormatter(string formatId) =>
            formatters.FirstOrDefault(f => f.FormatId == formatId);

        public string GetDataDirectory(string toolId) => throw new NotSupportedException();

        public ILogger GetLogger(string pluginId) => throw new NotSupportedException();
    }

    /// <summary>测试用 JSON 格式化器：固定输出以区别于原文。</summary>
    private sealed class StubJsonFormatter(bool throwOnFormat = false) : IFormatter
    {
        public string FormatId => "json";

        public string DisplayName => "JSON";

        public bool TryValidate(string input, out string? error)
        {
            error = null;
            return true;
        }

        public string Format(string input, FormatOptions options) =>
            throwOnFormat ? throw new FormatException("非法 JSON") : $"[美化]{input}";
    }

    [Fact]
    public void Beautify_格式化器已安装_返回美化文本()
    {
        var beautifier = new ResponseBeautifier(new StubHost([new StubJsonFormatter()]));

        BeautifyResult result = beautifier.Beautify("{\"a\":1}", "application/json");

        Assert.True(result.Beautified);
        Assert.Equal("json", result.FormatId);
        Assert.Equal("[美化]{\"a\":1}", result.Text);
    }

    [Fact]
    public void Beautify_格式化器未安装_退化为纯文本不报错()
    {
        var beautifier = new ResponseBeautifier(new StubHost([]));

        BeautifyResult result = beautifier.Beautify("{\"a\":1}", "application/json");

        Assert.False(result.Beautified);
        Assert.Equal("json", result.FormatId);
        Assert.Equal("{\"a\":1}", result.Text);
    }

    [Fact]
    public void Beautify_内容非法抛FormatException_退化为纯文本不报错()
    {
        var beautifier = new ResponseBeautifier(new StubHost([new StubJsonFormatter(throwOnFormat: true)]));

        BeautifyResult result = beautifier.Beautify("不是json", "application/json");

        Assert.False(result.Beautified);
        Assert.Equal("不是json", result.Text);
    }

    [Fact]
    public void Beautify_ContentType无映射_纯文本展示()
    {
        var beautifier = new ResponseBeautifier(new StubHost([new StubJsonFormatter()]));

        BeautifyResult result = beautifier.Beautify("<p>hi</p>", "text/html");

        Assert.False(result.Beautified);
        Assert.Null(result.FormatId);
        Assert.Equal("<p>hi</p>", result.Text);
    }
}
