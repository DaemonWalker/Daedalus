namespace Daedalus.Tools.Hermes.Collections;

/// <summary>请求体（hermes.md §11.1）：按 <see cref="Kind"/> 取用对应字段。</summary>
public sealed record RequestBody
{
    /// <summary>请求体种类。</summary>
    public RequestBodyKind Kind { get; init; }

    /// <summary>raw 文本的 Content-Type（如 application/json）；仅 <see cref="RequestBodyKind.Raw"/> 有效。</summary>
    public string? ContentType { get; init; }

    /// <summary>raw 文本内容；仅 <see cref="RequestBodyKind.Raw"/> 有效。</summary>
    public string? Text { get; init; }

    /// <summary>urlencoded 字段表；仅 <see cref="RequestBodyKind.UrlEncoded"/> 有效。</summary>
    public List<KeyValueEntry>? Fields { get; init; }
}
