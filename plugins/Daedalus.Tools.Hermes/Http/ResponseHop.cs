using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.Http;

/// <summary>跳转链中一跳的请求快照（hermes.md §5.3：记录方法/URL/头）。</summary>
/// <param name="Method">实际发送的 HTTP 方法（重定向改写后的值）。</param>
/// <param name="Url">实际请求的 URL（相对 Location 解析后的绝对地址）。</param>
/// <param name="Headers">实际发送的请求头（不含禁用项）。</param>
/// <param name="Body">实际发送的请求体文本；无请求体或 303/301/302 改写丢弃后为 null。</param>
public sealed record HopRequest(string Method, string Url, IReadOnlyList<NameValuePair> Headers, string? Body);

/// <summary>跳转链中一跳的响应快照（hermes.md §5.3：记录状态码/头/体/耗时）。</summary>
/// <param name="Status">HTTP 状态码。</param>
/// <param name="ReasonPhrase">状态描述文本（如 "Found"）。</param>
/// <param name="Headers">响应头（含内容头）。</param>
/// <param name="Body">响应体文本。</param>
/// <param name="ElapsedMs">本跳耗时（毫秒，仅本跳，不含其他跳）。</param>
public sealed record HopResponse(int Status, string? ReasonPhrase, IReadOnlyList<NameValuePair> Headers, string Body, long ElapsedMs);

/// <summary>跳转链中的一跳（FR-HERMES-006：每跳一个 tab，含本跳的请求与响应）。</summary>
/// <param name="Index">跳序号，从 1 开始。</param>
/// <param name="Request">本跳请求快照。</param>
/// <param name="Response">本跳响应快照。</param>
public sealed record ResponseHop(int Index, HopRequest Request, HopResponse Response);
