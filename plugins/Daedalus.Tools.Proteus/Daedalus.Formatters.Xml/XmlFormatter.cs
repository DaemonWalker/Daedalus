using System.Text;
using System.Xml;
using System.Xml.Linq;

using Daedalus.Abstractions;

namespace Daedalus.Formatters.Xml;

/// <summary>
/// XML 格式化器插件（设计文档：docs/plugins/proteus/xml.md）。
/// 基于 <see cref="XDocument"/> 实现校验（含行列）、美化（缩进可配）与压缩；
/// 显式禁止 DTD 与外部实体（防 XXE），含 DOCTYPE 的输入按非法处理。
/// </summary>
public sealed class XmlFormatter : IFormatter
{
    // 安全基线（xml.md §3）：禁 DTD、禁外部实体解析。XmlReader.Create 会克隆设置，可安全共享
    private static readonly XmlReaderSettings ReaderSettings = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
        // 忽略元素间的纯空白文本节点，否则输入原有的缩进空白会被当作内容保留，压缩/美化都无法生效
        IgnoreWhitespace = true,
    };

    /// <inheritdoc />
    public string FormatId => "xml";

    /// <inheritdoc />
    public string DisplayName => "XML";

    /// <inheritdoc />
    public bool TryValidate(string input, out string? error)
    {
        try
        {
            Parse(input);
            error = null;
            return true;
        }
        catch (XmlException ex)
        {
            error = BuildErrorMessage(ex);
            return false;
        }
    }

    /// <inheritdoc />
    /// <exception cref="FormatException">输入不是合法 XML，消息中含行列信息。</exception>
    public string Format(string input, FormatOptions options)
    {
        XDocument document;
        try
        {
            document = Parse(input);
        }
        catch (XmlException ex)
        {
            throw new FormatException(BuildErrorMessage(ex), ex);
        }

        return options.Minify ? Minify(document) : Beautify(document, options.IndentSize);
    }

    private static XDocument Parse(string input)
    {
        using XmlReader reader = XmlReader.Create(new StringReader(input), ReaderSettings);
        return XDocument.Load(reader, LoadOptions.SetLineInfo);
    }

    private static string Beautify(XDocument document, int indentSize)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = new string(' ', indentSize),
            // 声明头由调用方按原文前置（XmlWriter 会把 encoding 改写为输出目标的 utf-16）
            OmitXmlDeclaration = true,
        };

        var builder = new StringBuilder();
        AppendDeclaration(builder, document, "\r\n");
        using (XmlWriter writer = XmlWriter.Create(builder, settings))
        {
            document.Save(writer);
        }

        return builder.ToString();
    }

    private static string Minify(XDocument document)
    {
        var builder = new StringBuilder();
        // XDocument.ToString 不含声明头，需手动前置以保留（xml.md §2）
        AppendDeclaration(builder, document, string.Empty);
        builder.Append(document.ToString(SaveOptions.DisableFormatting));
        return builder.ToString();
    }

    private static void AppendDeclaration(StringBuilder builder, XDocument document, string suffix)
    {
        if (document.Declaration is not null)
        {
            builder.Append(document.Declaration.ToString()).Append(suffix);
        }
    }

    // XmlException 的行列（LineNumber / LinePosition）本身就是 1 起始
    private static string BuildErrorMessage(XmlException ex)
    {
        return $"第 {ex.LineNumber} 行第 {ex.LinePosition} 列：{ex.Message}";
    }
}
