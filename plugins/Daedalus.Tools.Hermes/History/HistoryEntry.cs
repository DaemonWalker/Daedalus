namespace Daedalus.Tools.Hermes.History;

/// <summary>一条历史记录（hermes.md §11.3）：按天追加写入 history/yyyy-MM-dd.jsonl，每行一条。</summary>
public sealed record HistoryEntry
{
    /// <summary>当前历史记录格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>记录格式版本（DR-004）。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>记录 id（ULID）。</summary>
    public required string Id { get; init; }

    /// <summary>发送时间（含时区偏移）。</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>请求快照。</summary>
    public required HistoryRequest Request { get; init; }

    /// <summary>响应快照（最终一跳）。</summary>
    public required HistoryResponse Response { get; init; }

    /// <summary>重定向跳数；未发生跳转时为 0。</summary>
    public int RedirectHops { get; init; }
}
