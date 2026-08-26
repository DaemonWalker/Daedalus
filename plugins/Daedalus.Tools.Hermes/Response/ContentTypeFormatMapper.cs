namespace Daedalus.Tools.Hermes.Response;

/// <summary>
/// 响应 Content-Type → 格式 id 映射（hermes.md §8，FR-HERMES-004）：
/// application/json 与 application/*+json → "json"；application/xml、text/xml、application/*+xml → "xml"；
/// 其他返回 null（不美化，纯文本展示）。
/// </summary>
public static class ContentTypeFormatMapper
{
    /// <summary>映射 Content-Type 头值（可带 charset 等参数）到格式 id；无匹配返回 null。</summary>
    public static string? Map(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType))
        {
            return null;
        }

        // 丢弃 "; charset=utf-8" 等参数，只取媒体类型本体
        string mediaType = contentType.Split(';', 2)[0].Trim();
        if (mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase))
        {
            return "json";
        }

        if (mediaType.Equals("application/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.Equals("text/xml", StringComparison.OrdinalIgnoreCase)
            || mediaType.StartsWith("application/", StringComparison.OrdinalIgnoreCase) && mediaType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase))
        {
            return "xml";
        }

        return null;
    }
}
