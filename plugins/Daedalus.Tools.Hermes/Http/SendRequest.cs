using Daedalus.Tools.Hermes.Collections;

namespace Daedalus.Tools.Hermes.Http;

/// <summary>
/// HttpEngine 的发送输入：变量替换完成后的最终请求内容。
/// 选项生效值（跟随重定向 / Cookie）由引擎按 <paramref name="Options"/> ?? 全局设置解析（hermes.md §5.1）。
/// </summary>
/// <param name="Method">HTTP 方法，允许自定义方法名（FR-HERMES-001）。</param>
/// <param name="Url">实际请求的 URL。</param>
/// <param name="Headers">请求头；<see cref="KeyValueEntry.Enabled"/> 为 false 的项不发送。</param>
/// <param name="Body">请求体；无请求体时为 null。</param>
/// <param name="Options">请求级选项覆盖；null 或字段为 null 表示继承全局设置。</param>
public sealed record SendRequest(
    string Method,
    string Url,
    IReadOnlyList<KeyValueEntry> Headers,
    RequestBody? Body,
    RequestOptions? Options);
