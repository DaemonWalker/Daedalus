using Daedalus.Abstractions;

namespace Daedalus.Tools.Proteus.Tests;

/// <summary>ProteusOperations 测试：操作编排（选项传递、错误处理、初始格式解析）。</summary>
public class ProteusOperationsTests
{
    private static readonly FakeFormatter JsonFormatter = new("json", "JSON");
    private static readonly FakeFormatter XmlFormatter = new("xml", "XML");

    [Fact]
    public void Format_合法输入_传递美化选项并返回输出()
    {
        ProteusOperationResult result = ProteusOperations.Format(JsonFormatter, "input", indentSize: 8);

        Assert.True(result.Success);
        Assert.Equal(new FormatOptions(Minify: false, IndentSize: 8), JsonFormatter.LastOptions);
        Assert.Equal("formatted", result.Output);
        Assert.Contains("格式化", result.StatusText);
    }

    [Fact]
    public void Minify_合法输入_传递压缩选项并返回输出()
    {
        ProteusOperationResult result = ProteusOperations.Minify(JsonFormatter, "input");

        Assert.True(result.Success);
        Assert.Equal(new FormatOptions(Minify: true, IndentSize: 0), JsonFormatter.LastOptions);
        Assert.Equal("formatted", result.Output);
        Assert.Contains("压缩", result.StatusText);
    }

    [Fact]
    public void Format_非法输入_失败且输出为空()
    {
        ProteusOperationResult result = ProteusOperations.Format(JsonFormatter, "bad", indentSize: 4);

        // FormatException 按校验失败处理：状态栏显示错误（含行列），输出区保持不变（proteus.md §5）
        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("第 1 行第 1 列", result.StatusText);
    }

    [Fact]
    public void Validate_合法输入_校验通过且无输出()
    {
        ProteusOperationResult result = ProteusOperations.Validate(JsonFormatter, "input");

        Assert.True(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("校验通过", result.StatusText);
    }

    [Fact]
    public void Validate_非法输入_返回含行列的错误信息()
    {
        ProteusOperationResult result = ProteusOperations.Validate(JsonFormatter, "bad");

        Assert.False(result.Success);
        Assert.Null(result.Output);
        Assert.Contains("校验失败", result.StatusText);
        Assert.Contains("第 1 行第 1 列", result.StatusText);
    }

    [Fact]
    public void ResolveInitialFormatter_匹配上次格式_返回对应格式化器()
    {
        IReadOnlyList<IFormatter> formatters = [JsonFormatter, XmlFormatter];

        IFormatter? result = ProteusOperations.ResolveInitialFormatter(formatters, "XML");

        Assert.Same(XmlFormatter, result);
    }

    [Fact]
    public void ResolveInitialFormatter_上次格式未安装_回落列表第一个()
    {
        IReadOnlyList<IFormatter> formatters = [JsonFormatter, XmlFormatter];

        IFormatter? result = ProteusOperations.ResolveInitialFormatter(formatters, "yaml");

        Assert.Same(JsonFormatter, result);
    }

    [Fact]
    public void ResolveInitialFormatter_首次启动_返回列表第一个()
    {
        IReadOnlyList<IFormatter> formatters = [JsonFormatter, XmlFormatter];

        IFormatter? result = ProteusOperations.ResolveInitialFormatter(formatters, null);

        Assert.Same(JsonFormatter, result);
    }

    [Fact]
    public void ResolveInitialFormatter_未安装任何格式化器_返回空()
    {
        IFormatter? result = ProteusOperations.ResolveInitialFormatter([], "json");

        Assert.Null(result);
    }

    /// <summary>测试用格式化器桩：input 含 "bad" 时视为非法输入。</summary>
    private sealed class FakeFormatter : IFormatter
    {
        public FakeFormatter(string formatId, string displayName)
        {
            FormatId = formatId;
            DisplayName = displayName;
        }

        public string FormatId { get; }

        public string DisplayName { get; }

        public FormatOptions? LastOptions { get; private set; }

        public bool TryValidate(string input, out string? error)
        {
            if (input.Contains("bad", StringComparison.Ordinal))
            {
                error = "第 1 行第 1 列：非法输入";
                return false;
            }

            error = null;
            return true;
        }

        public string Format(string input, FormatOptions options)
        {
            if (!TryValidate(input, out string? error))
            {
                throw new FormatException(error);
            }

            LastOptions = options;
            return "formatted";
        }
    }
}
