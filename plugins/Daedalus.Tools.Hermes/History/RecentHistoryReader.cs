namespace Daedalus.Tools.Hermes.History;

/// <summary>
/// 最近历史读取（FR-HERMES-052）：从今日起向前逐日读取 jsonl，汇总为新→旧排序的清单。
/// 归档包内的更早历史由下一步的分层搜索覆盖（FR-HERMES-054/055）。
/// </summary>
/// <param name="store">历史存储。</param>
public sealed class RecentHistoryReader(HistoryStore store)
{
    /// <summary>历史列表默认回看天数。</summary>
    public const int DefaultDays = 7;

    /// <summary>读取最近 <paramref name="days"/> 天（含今天）的历史，新→旧排序；逐日行级容错由 <see cref="HistoryStore"/> 保证。</summary>
    public async Task<IReadOnlyList<HistoryEntry>> ReadRecentAsync(int days = DefaultDays, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(days, 0);

        var entries = new List<HistoryEntry>();
        DateOnly today = DateOnly.FromDateTime(DateTime.Now);
        for (int offset = 0; offset < days; offset++)
        {
            HistoryDayReadResult day = await store.ReadDayAsync(today.AddDays(-offset), cancellationToken).ConfigureAwait(false);
            // 日文件内为追加顺序（旧→新），反转后拼接出整体新→旧
            for (int i = day.Entries.Count - 1; i >= 0; i--)
            {
                entries.Add(day.Entries[i]);
            }
        }

        return entries;
    }
}
