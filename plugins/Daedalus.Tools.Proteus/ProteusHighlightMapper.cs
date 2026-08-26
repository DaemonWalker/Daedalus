namespace Daedalus.Tools.Proteus;

/// <summary>编辑区语法高亮种类（proteus.md §5：映射表由 Proteus 维护，格式化器不参与）。</summary>
public enum ProteusHighlightKind
{
    /// <summary>不高亮（未知格式）。</summary>
    None,

    /// <summary>JSON：自定义高亮规则。</summary>
    Json,

    /// <summary>XML：FastColoredTextBox 内置 XML 规则。</summary>
    Xml,
}

/// <summary>格式 id → 语法高亮种类映射（大小写不敏感；未知格式不高亮）。</summary>
public static class ProteusHighlightMapper
{
    /// <summary>按格式 id 取高亮种类。</summary>
    public static ProteusHighlightKind Map(string? formatId)
    {
        return formatId?.ToLowerInvariant() switch
        {
            "json" => ProteusHighlightKind.Json,
            "xml" => ProteusHighlightKind.Xml,
            _ => ProteusHighlightKind.None,
        };
    }
}
