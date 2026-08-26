namespace Daedalus.Tools.Hermes.Collections;

/// <summary>
/// 集合树节点（hermes.md §11.1）：<see cref="Type"/> 为 Folder 时使用 <see cref="Items"/>，
/// 为 Request 时使用其余请求字段。
/// </summary>
public sealed record CollectionNode
{
    /// <summary>节点种类。</summary>
    public required CollectionNodeType Type { get; init; }

    /// <summary>显示名。</summary>
    public required string Name { get; init; }

    /// <summary>子节点（仅 Folder）。</summary>
    public List<CollectionNode>? Items { get; init; }

    /// <summary>HTTP 方法（仅 Request），允许自定义方法名（FR-HERMES-001）。</summary>
    public string? Method { get; init; }

    /// <summary>请求 URL（仅 Request），可含 <c>{{变量}}</c> 引用。</summary>
    public string? Url { get; init; }

    /// <summary>请求头（仅 Request）。</summary>
    public List<KeyValueEntry>? Headers { get; init; }

    /// <summary>请求体（仅 Request）；无请求体时为 null。</summary>
    public RequestBody? Body { get; init; }

    /// <summary>请求级选项覆盖（仅 Request）；全部为 null 或本身为 null 表示完全继承全局设置。</summary>
    public RequestOptions? Options { get; init; }

    /// <summary>后事件脚本（仅 Request，FR-HERMES-040）；无脚本时为 null。</summary>
    public string? PostResponseScript { get; init; }
}
