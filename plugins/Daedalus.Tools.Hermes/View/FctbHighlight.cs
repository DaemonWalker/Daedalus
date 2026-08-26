using FastColoredTextBoxNS;

namespace Daedalus.Tools.Hermes.View;

/// <summary>
/// FastColoredTextBox 高亮助手：响应展示区按格式 id 着色（json → 自定义规则，xml → 内置 XML，
/// 与 Proteus 面板同一套口径）；null/未知格式清除残留样式按纯文本展示。
/// </summary>
internal static class FctbHighlight
{
    // JSON 自定义高亮：先数字/关键字后字符串，字符串样式覆盖前者（同 ProteusPanel）
    private static readonly Style JsonNumberStyle = new TextStyle(Brushes.MediumPurple, null, FontStyle.Regular);
    private static readonly Style JsonKeywordStyle = new TextStyle(Brushes.Blue, null, FontStyle.Regular);
    private static readonly Style JsonStringStyle = new TextStyle(Brushes.Brown, null, FontStyle.Regular);

    /// <summary>按格式 id 应用高亮并立即整篇着色一次。</summary>
    public static void Apply(FastColoredTextBox box, string? formatId)
    {
        if (formatId == "xml")
        {
            box.Language = Language.XML;
        }
        else if (formatId == "json")
        {
            box.Language = Language.Custom;
            HighlightJson(box);
        }
        else
        {
            box.Language = Language.Custom;
            box.Range.ClearStyle(StyleIndex.All);
        }
    }

    private static void HighlightJson(FastColoredTextBox box)
    {
        FastColoredTextBoxNS.Range range = box.Range;
        range.ClearStyle(StyleIndex.All);
        range.SetStyle(JsonNumberStyle, @"(?<![\w""])-?\d+(\.\d+)?([eE][+-]?\d+)?(?![\w""])");
        range.SetStyle(JsonKeywordStyle, @"\b(true|false|null)\b");
        range.SetStyle(JsonStringStyle, @"""([^""\\]|\\.)*""");
    }
}
