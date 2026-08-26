namespace Daedalus.Tools.Hermes.History;

/// <summary>历史记录中的请求快照（hermes.md §11.3）：变量替换后的实际发送内容。</summary>
/// <param name="Method">HTTP 方法。</param>
/// <param name="Url">实际请求的 URL。</param>
/// <param name="Headers">请求头。</param>
/// <param name="Body">请求体文本；无请求体时为 null。</param>
public sealed record HistoryRequest(string Method, string Url, List<NameValuePair> Headers, string? Body);
