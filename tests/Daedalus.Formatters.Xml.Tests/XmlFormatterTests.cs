using System.Xml;
using System.Xml.Linq;

using Daedalus.Abstractions;

namespace Daedalus.Formatters.Xml.Tests;

/// <summary>覆盖 docs/plugins/proteus/xml.md §4 测试要点。</summary>
public class XmlFormatterTests
{
    private readonly XmlFormatter _formatter = new();

    [Fact]
    public void Metadata_格式标识_符合设计文档()
    {
        Assert.Equal("xml", _formatter.FormatId);
        Assert.Equal("XML", _formatter.DisplayName);
    }

    [Fact]
    public void TryValidate_合法输入_返回True且无错误()
    {
        bool valid = _formatter.TryValidate("<root><a x=\"1\">文本</a></root>", out string? error);

        Assert.True(valid);
        Assert.Null(error);
    }

    [Fact]
    public void TryValidate_非法输入_返回False且行列准确()
    {
        // 第 2 行开始标签 <a> 未闭合，第 3 行 </root> 处报错
        string input = "<root>\n  <a>\n</root>";

        bool valid = _formatter.TryValidate(input, out string? error);

        Assert.False(valid);
        Assert.Contains("第 3 行", error);
    }

    [Fact]
    public void TryValidate_空输入_返回False()
    {
        bool valid = _formatter.TryValidate("", out string? error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_含内部DTD_拒绝()
    {
        string input = "<!DOCTYPE root [<!ENTITY x \"y\">]><root>&x;</root>";

        bool valid = _formatter.TryValidate(input, out string? error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void TryValidate_含外部实体DOCTYPE_拒绝()
    {
        string input = "<!DOCTYPE root SYSTEM \"https://evil.example/xxe.dtd\"><root/>";

        bool valid = _formatter.TryValidate(input, out string? error);

        Assert.False(valid);
        Assert.NotNull(error);
    }

    [Fact]
    public void Format_非法输入_抛FormatException且含行列()
    {
        FormatException ex = Assert.Throws<FormatException>(
            () => _formatter.Format("<root>\n  <a>\n</root>", new FormatOptions(Minify: false, IndentSize: 4)));

        Assert.Contains("第 3 行", ex.Message);
        Assert.IsType<XmlException>(ex.InnerException);
    }

    [Fact]
    public void Format_含DOCTYPE_抛FormatException()
    {
        Assert.Throws<FormatException>(
            () => _formatter.Format("<!DOCTYPE root><root/>", new FormatOptions(Minify: false, IndentSize: 4)));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(4)]
    public void Format_美化_缩进宽度参数生效(int indentSize)
    {
        string formatted = _formatter.Format("<root><a><b>1</b></a></root>", new FormatOptions(Minify: false, IndentSize: indentSize));

        string expectedIndent = new string(' ', indentSize);
        Assert.Contains($"\r\n{expectedIndent}<a>", formatted);
        Assert.Contains($"\r\n{expectedIndent}{expectedIndent}<b>", formatted);
    }

    [Fact]
    public void Format_美化_声明头按原文保留()
    {
        string input = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root><a>1</a></root>";

        string formatted = _formatter.Format(input, new FormatOptions(Minify: false, IndentSize: 4));

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", formatted);
        Assert.Contains("\r\n<root>", formatted);
    }

    [Fact]
    public void Format_美化_无声明输入不补加()
    {
        string formatted = _formatter.Format("<root><a>1</a></root>", new FormatOptions(Minify: false, IndentSize: 4));

        Assert.DoesNotContain("<?xml", formatted);
    }

    [Fact]
    public void Format_压缩_无格式化空白且保留声明头()
    {
        string input = "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n<root>\n  <a>1</a>\n</root>";

        string minified = _formatter.Format(input, new FormatOptions(Minify: true, IndentSize: 4));

        Assert.Equal("<?xml version=\"1.0\" encoding=\"utf-8\"?><root><a>1</a></root>", minified);
    }

    [Fact]
    public void Format_压缩_无声明输入不补加()
    {
        string minified = _formatter.Format("<root>\n  <a>1</a>\n</root>", new FormatOptions(Minify: true, IndentSize: 4));

        Assert.Equal("<root><a>1</a></root>", minified);
    }

    [Fact]
    public void Format_美化往返_语义等价()
    {
        string input = "<root><a x=\"1\">文本</a><b><c>2</c></b></root>";

        string formatted = _formatter.Format(input, new FormatOptions(Minify: false, IndentSize: 4));

        AssertSemanticEquals(input, formatted);
    }

    [Fact]
    public void Format_CDATA与注释_往返保留()
    {
        string input = "<root><!-- 注释 --><a><![CDATA[x < y && z]]></a></root>";

        string formatted = _formatter.Format(input, new FormatOptions(Minify: false, IndentSize: 2));

        AssertSemanticEquals(input, formatted);
        Assert.Contains("<![CDATA[x < y && z]]>", formatted);
        Assert.Contains("<!-- 注释 -->", formatted);
    }

    [Fact]
    public void Format_命名空间_往返保留()
    {
        string input = "<root xmlns=\"urn:default\" xmlns:p=\"urn:p\"><p:a p:x=\"1\"/></root>";

        string formatted = _formatter.Format(input, new FormatOptions(Minify: true, IndentSize: 4));

        AssertSemanticEquals(input, formatted);
        Assert.Contains("xmlns:p=\"urn:p\"", formatted);
    }

    private static void AssertSemanticEquals(string expected, string actual)
    {
        XDocument expectedDoc = XDocument.Parse(expected);
        XDocument actualDoc = XDocument.Parse(actual);
        Assert.True(XNode.DeepEquals(expectedDoc, actualDoc),
            $"语义不等价：\n期望：{expected}\n实际：{actual}");
    }
}
