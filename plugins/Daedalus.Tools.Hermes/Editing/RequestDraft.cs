using System.Text.Json;

using Daedalus.Tools.Hermes.Collections;
using Daedalus.Tools.Hermes.Persistence;

namespace Daedalus.Tools.Hermes.Editing;

/// <summary>
/// 请求编辑区的内容快照（hermes.md §3）：方法 / URL（Params 页合并后的完整 URL）/ 请求头 /
/// 请求体 / 请求级选项 / 后事件脚本。名称属于集合树节点，不属于草稿。
/// 脏标记（FR-HERMES-012）通过与已保存草稿的内容比较得出。
/// </summary>
public sealed record RequestDraft
{
    /// <summary>HTTP 方法，允许自定义方法名（FR-HERMES-001）。</summary>
    public string Method { get; init; } = "GET";

    /// <summary>请求 URL，可含 <c>{{变量}}</c> 引用；query 段即 Params 页的内容。</summary>
    public string Url { get; init; } = string.Empty;

    /// <summary>请求头。</summary>
    public List<KeyValueEntry> Headers { get; init; } = [];

    /// <summary>请求体；无请求体时为 null。</summary>
    public RequestBody? Body { get; init; }

    /// <summary>请求级选项覆盖；全部为 null 或本身为 null 表示完全继承全局设置。</summary>
    public RequestOptions? Options { get; init; }

    /// <summary>后事件脚本；无脚本时为 null。</summary>
    public string? PostResponseScript { get; init; }

    /// <summary>空草稿（新建请求 / 清空编辑区）。</summary>
    public static RequestDraft Empty => new();

    /// <summary>从集合树的请求节点载入草稿。</summary>
    public static RequestDraft FromNode(CollectionNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return new RequestDraft
        {
            Method = node.Method ?? "GET",
            Url = node.Url ?? string.Empty,
            Headers = node.Headers is null ? [] : [.. node.Headers],
            Body = node.Body,
            Options = node.Options,
            PostResponseScript = node.PostResponseScript,
        };
    }

    /// <summary>草稿写回为请求节点（FR-HERMES-012），节点名由 <paramref name="name"/> 保留。</summary>
    public CollectionNode ToNode(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new CollectionNode
        {
            Type = CollectionNodeType.Request,
            Name = name,
            Method = Method,
            Url = Url,
            Headers = [.. Headers],
            Body = Body,
            Options = Options,
            PostResponseScript = PostResponseScript,
        };
    }

    /// <summary>与另一份草稿做内容比较（列表按值逐项比），相等表示编辑区无未保存修改。</summary>
    public bool ContentEquals(RequestDraft? other) =>
        other is not null
        && JsonSerializer.Serialize(this, JsonDataFile.Options) == JsonSerializer.Serialize(other, JsonDataFile.Options);
}
