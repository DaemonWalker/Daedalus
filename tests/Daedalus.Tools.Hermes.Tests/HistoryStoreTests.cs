using System.Text;
using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// HistoryStore 测试（hermes.md §10.1/§12）：按天落盘、追加、响应体上限截断标记、
/// 读取往返、损坏行行级容错、并发追加。
/// </summary>
public sealed class HistoryStoreTests : IDisposable
{
    private const long DefaultLimit = Hermes.Settings.HermesSettings.DefaultResponseBodyLimitBytes;

    private readonly string _directory = Path.Combine(Path.GetTempPath(), "daedalus-hermes-tests-" + Guid.NewGuid().ToString("N"));

    public HistoryStoreTests()
    {
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    private static HistoryEntry CreateEntry(DateTimeOffset timestamp, string url = "http://localhost:8080/api/login", string body = "{\"token\":\"abc\"}") => new()
    {
        Id = IdGenerator.NewId(),
        Timestamp = timestamp,
        Request = new HistoryRequest("POST", url, [new NameValuePair("Content-Type", "application/json")], "{\"user\":\"a\"}"),
        Response = new HistoryResponse(200, 42, [new NameValuePair("Content-Type", "application/json")], body, BodyTruncated: false),
        RedirectHops = 0,
    };

    [Fact]
    public async Task AppendAsync_任意记录_按时间戳本机日期写入对应jsonl文件且单行一条()
    {
        var store = new HistoryStore(_directory);
        HistoryEntry entry = CreateEntry(new DateTimeOffset(2026, 8, 26, 13, 0, 0, TimeSpan.FromHours(8)));

        await store.AppendAsync(entry, DefaultLimit);

        string filePath = store.GetFilePath(DateOnly.FromDateTime(entry.Timestamp.LocalDateTime));
        Assert.True(File.Exists(filePath));
        string[] lines = await File.ReadAllLinesAsync(filePath);
        string line = Assert.Single(lines);
        Assert.DoesNotContain('\n', line);
        Assert.Contains("\"version\":1", line);
        Assert.Contains("\"redirectHops\":0", line);
    }

    [Fact]
    public async Task AppendAsync再ReadDayAsync_已追加记录_往返一致()
    {
        var store = new HistoryStore(_directory);
        DateTimeOffset now = DateTimeOffset.Now;
        HistoryEntry first = CreateEntry(now, url: "http://localhost/a");
        HistoryEntry second = CreateEntry(now.AddMinutes(1), url: "http://localhost/b") with { RedirectHops = 2 };

        await store.AppendAsync(first, DefaultLimit);
        await store.AppendAsync(second, DefaultLimit);
        HistoryDayReadResult result = await store.ReadDayAsync(DateOnly.FromDateTime(now.LocalDateTime));

        Assert.Equal(0, result.SkippedLines);
        Assert.Equal(2, result.Entries.Count);
        TestJson.Equal(first, result.Entries[0]);
        TestJson.Equal(second, result.Entries[1]);
    }

    [Fact]
    public async Task AppendAsync_响应体超上限_截断并置截断标记()
    {
        var store = new HistoryStore(_directory);
        // 中文字符 UTF-8 占 3 字节，验证截断不产生乱码（不切断字符）
        HistoryEntry entry = CreateEntry(DateTimeOffset.Now, body: "汉字abc汉字abc");

        await store.AppendAsync(entry, responseBodyLimitBytes: 8);
        HistoryDayReadResult result = await store.ReadDayAsync(DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime));

        HistoryEntry loaded = Assert.Single(result.Entries);
        Assert.True(loaded.Response.BodyTruncated);
        Assert.NotNull(loaded.Response.Body);
        Assert.True(Encoding.UTF8.GetByteCount(loaded.Response.Body) <= 8);
        Assert.StartsWith("汉", loaded.Response.Body);
    }

    [Fact]
    public async Task AppendAsync_响应体未超上限_原样保存不置截断标记()
    {
        var store = new HistoryStore(_directory);
        HistoryEntry entry = CreateEntry(DateTimeOffset.Now);

        await store.AppendAsync(entry, DefaultLimit);
        HistoryDayReadResult result = await store.ReadDayAsync(DateOnly.FromDateTime(DateTimeOffset.Now.LocalDateTime));

        HistoryEntry loaded = Assert.Single(result.Entries);
        Assert.False(loaded.Response.BodyTruncated);
        Assert.Equal(entry.Response.Body, loaded.Response.Body);
    }

    [Fact]
    public async Task ReadDayAsync_文件不存在_返回空结果()
    {
        var store = new HistoryStore(_directory);

        HistoryDayReadResult result = await store.ReadDayAsync(new DateOnly(2026, 1, 1));

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.SkippedLines);
    }

    [Fact]
    public async Task ReadDayAsync_含损坏行_跳过该行并计数且不影响其余行()
    {
        var store = new HistoryStore(_directory);
        DateTimeOffset now = DateTimeOffset.Now;
        await store.AppendAsync(CreateEntry(now), DefaultLimit);
        string filePath = store.GetFilePath(DateOnly.FromDateTime(now.LocalDateTime));
        await File.AppendAllTextAsync(filePath, "{ 这不是合法 JSON" + Environment.NewLine);
        await store.AppendAsync(CreateEntry(now), DefaultLimit);

        HistoryDayReadResult result = await store.ReadDayAsync(DateOnly.FromDateTime(now.LocalDateTime));

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1, result.SkippedLines);
        // 损坏行原样留在文件中，不做整文件备份
        Assert.True(File.Exists(filePath));
    }

    [Fact]
    public async Task AppendAsync_并发追加_全部记录落盘且无交错()
    {
        var store = new HistoryStore(_directory);
        DateTimeOffset now = DateTimeOffset.Now;
        const int count = 20;

        await Task.WhenAll(Enumerable.Range(0, count)
            .Select(i => store.AppendAsync(CreateEntry(now, url: $"http://localhost/{i}"), DefaultLimit)));
        HistoryDayReadResult result = await store.ReadDayAsync(DateOnly.FromDateTime(now.LocalDateTime));

        Assert.Equal(0, result.SkippedLines);
        Assert.Equal(count, result.Entries.Count);
        Assert.Equal(
            Enumerable.Range(0, count).Select(i => $"http://localhost/{i}").Order(StringComparer.Ordinal),
            result.Entries.Select(e => e.Request.Url).Order(StringComparer.Ordinal));
    }
}
