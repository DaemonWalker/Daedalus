namespace Daedalus.Tools.Hermes.History;

/// <summary>历史记录中的响应快照（hermes.md §11.3）：跟随重定向时只记最终一跳（FR-HERMES-050）。</summary>
/// <param name="Status">HTTP 状态码。</param>
/// <param name="ElapsedMs">耗时（毫秒）。</param>
/// <param name="Headers">响应头。</param>
/// <param name="Body">响应体文本；超出上限截断后为截断内容。</param>
/// <param name="BodyTruncated">true 表示响应体超出上限（默认 10 MB，可配置）被截断。</param>
public sealed record HistoryResponse(int Status, long ElapsedMs, List<NameValuePair> Headers, string? Body, bool BodyTruncated);
