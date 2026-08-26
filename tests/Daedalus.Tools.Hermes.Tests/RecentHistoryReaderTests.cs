using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>RecentHistoryReader：跨日汇总与新→旧排序（FR-HERMES-052）。</summary>
public sealed class RecentHistoryReaderTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "hermes-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private static HistoryEntry CreateEntry(DateTimeOffset timestamp, string url) => new()
    {
        Id = IdGenerator.NewId(),
        Timestamp = timestamp,
        Request = new HistoryRequest("GET", url, [], null),
        Response = new HistoryResponse(200, 1, [], "ok", false),
    };

    private async Task AppendAsync(HistoryStore store, params HistoryEntry[] entries)
    {
        foreach (HistoryEntry entry in entries)
        {
            await store.AppendAsync(entry, Hermes.Settings.HermesSettings.DefaultResponseBodyLimitBytes);
        }
    }

    [Fact]
    public async Task ReadRecent_跨多日_整体新到旧排序()
    {
        var store = new HistoryStore(_dataDirectory);
        DateTimeOffset today = DateTimeOffset.Now;
        DateTimeOffset yesterday = today.AddDays(-1);
        await AppendAsync(store,
            CreateEntry(yesterday, "http://a/1"),
            CreateEntry(yesterday, "http://a/2"),
            CreateEntry(today, "http://a/3"));

        var reader = new RecentHistoryReader(store);

        IReadOnlyList<HistoryEntry> entries = await reader.ReadRecentAsync(7);

        // 今日在前；同一日内后追加的在前
        Assert.Equal(["http://a/3", "http://a/2", "http://a/1"], entries.Select(e => e.Request.Url).ToArray());
    }

    [Fact]
    public async Task ReadRecent_超出天数窗口的历史不包含()
    {
        var store = new HistoryStore(_dataDirectory);
        DateTimeOffset today = DateTimeOffset.Now;
        await AppendAsync(store,
            CreateEntry(today.AddDays(-10), "http://a/old"),
            CreateEntry(today, "http://a/new"));

        var reader = new RecentHistoryReader(store);

        IReadOnlyList<HistoryEntry> entries = await reader.ReadRecentAsync(3);

        Assert.Equal(["http://a/new"], entries.Select(e => e.Request.Url).ToArray());
    }

    [Fact]
    public async Task ReadRecent_无历史文件_返回空表()
    {
        var reader = new RecentHistoryReader(new HistoryStore(_dataDirectory));

        Assert.Empty(await reader.ReadRecentAsync(7));
    }
}
