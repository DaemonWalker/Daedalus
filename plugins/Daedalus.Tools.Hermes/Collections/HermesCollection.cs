namespace Daedalus.Tools.Hermes.Collections;

/// <summary>集合（hermes.md §11.1）：一组请求的树形组织，持久化为 collections/&lt;id&gt;.json。</summary>
public sealed record HermesCollection
{
    /// <summary>当前集合文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>文件格式版本（DR-004）。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>集合 id（ULID），同时是 collections/ 下的文件名。</summary>
    public required string Id { get; init; }

    /// <summary>集合名。</summary>
    public required string Name { get; init; }

    /// <summary>树根节点（文件夹 / 请求）。</summary>
    public List<CollectionNode> Items { get; init; } = [];
}
