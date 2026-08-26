using System.IO.Compression;
using System.Text.Json;

using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// HistorySearch 测试（hermes.md §10.3/§12）：jsonl 直搜（全文子串、新→旧）、
/// zip 流式搜索、7z 临时目录搜索与清理、逐包推进与中途停止。
/// </summary>
public sealed class HistorySearchTests : IDisposable
{
    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "hermes-search-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    private static DateTimeOffset LocalNoon(int year, int month, int day) =>
        new(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Local));

    private HistorySearch CreateSearch(FakeSevenZipRunner? runner = null) =>
        new(_dataDirectory, runner ?? new FakeSevenZipRunner(), logger: null);

    private async Task WriteDayFileAsync(DateTimeOffset timestamp, string url, string? body = null)
    {
        var store = new HistoryStore(_dataDirectory);
        var entry = new HistoryEntry
        {
            Id = IdGenerator.NewId(),
            Timestamp = timestamp,
            Request = new HistoryRequest("GET", url, [], null),
            Response = new HistoryResponse(200, 1, [], body ?? "ok", false),
        };
        await store.AppendAsync(entry, Hermes.Settings.HermesSettings.DefaultResponseBodyLimitBytes);
    }

    // 直接以 zip 格式在归档目录造一个包（条目内容 = 日文件原文），驱动"搜索更久"链路
    private async Task<string> WriteZipArchiveAsync(string month, params (string DayFile, string Url)[] days)
    {
        string archiveDirectory = Path.Combine(_dataDirectory, "history", "archive");
        Directory.CreateDirectory(archiveDirectory);
        string archivePath = Path.Combine(archiveDirectory, month + ".zip");
        await using (var stream = new FileStream(archivePath, FileMode.CreateNew))
        using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach ((string dayFile, string url) in days)
            {
                ZipArchiveEntry entry = zip.CreateEntry(dayFile);
                await using var writer = new StreamWriter(entry.Open());
                var record = new HistoryEntry
                {
                    Id = IdGenerator.NewId(),
                    Timestamp = LocalNoon(2000, 1, 1),
                    Request = new HistoryRequest("GET", url, [], null),
                    Response = new HistoryResponse(200, 1, [], "ok", false),
                };
                await writer.WriteLineAsync(JsonSerializer.Serialize(record));
            }
        }

        return archivePath;
    }

    [Fact]
    public async Task SearchRecent_直搜未压缩jsonl_全文子串匹配且整体新到旧()
    {
        await WriteDayFileAsync(LocalNoon(2026, 8, 25), "http://a/orders?page=1");
        await WriteDayFileAsync(LocalNoon(2026, 8, 26), "http://a/users");
        await WriteDayFileAsync(LocalNoon(2026, 8, 26), "http://a/ORDERS?page=2"); // 同日后追加
        await WriteDayFileAsync(LocalNoon(2026, 8, 26), "http://a/no-hit");

        HistorySearchResult result = await CreateSearch().SearchRecentAsync("orders");

        Assert.Equal(0, result.SkippedLines);
        // 跨日新→旧；同日内后追加的在前
        Assert.Equal(["http://a/ORDERS?page=2", "http://a/orders?page=1"], result.Entries.Select(e => e.Request.Url).ToArray());
    }

    [Fact]
    public async Task SearchRecent_关键词命中响应体_也算命中()
    {
        await WriteDayFileAsync(LocalNoon(2026, 8, 26), "http://a/users", body: "{\"token\":\"SECRET-XYZ\"}");

        HistorySearchResult result = await CreateSearch().SearchRecentAsync("secret-xyz");

        Assert.Single(result.Entries);
    }

    [Fact]
    public async Task SearchRecent_无命中_返回空()
    {
        await WriteDayFileAsync(LocalNoon(2026, 8, 26), "http://a/users");

        HistorySearchResult result = await CreateSearch().SearchRecentAsync("不存在的关键词");

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.SkippedLines);
    }

    [Fact]
    public async Task SearchRecent_命中行损坏_计数跳过不影响其余命中()
    {
        await WriteDayFileAsync(LocalNoon(2026, 8, 26), "http://a/users");
        string file = new HistoryStore(_dataDirectory).GetFilePath(new DateOnly(2026, 8, 26));
        await File.AppendAllTextAsync(file, "{ 损坏但含关键词 users" + Environment.NewLine);

        HistorySearchResult result = await CreateSearch().SearchRecentAsync("users");

        Assert.Single(result.Entries);
        Assert.Equal(1, result.SkippedLines);
    }

    [Fact]
    public async Task HasArchives_归档目录有无包_反映按钮可见性()
    {
        HistorySearch search = CreateSearch();
        Assert.False(search.HasArchives());

        await WriteZipArchiveAsync("2026-06", ("2026-06-05.jsonl", "http://a/old"));

        Assert.True(search.HasArchives());
    }

    [Fact]
    public async Task SearchArchives_zip包_流式搜索命中且批次带包名()
    {
        await WriteZipArchiveAsync("2026-06",
            ("2026-06-05.jsonl", "http://a/legacy-orders"),
            ("2026-06-06.jsonl", "http://a/other"));

        List<HistorySearchBatch> batches = [];
        await foreach (HistorySearchBatch batch in CreateSearch().SearchArchivesAsync("legacy-orders"))
        {
            batches.Add(batch);
        }

        HistorySearchBatch only = Assert.Single(batches);
        Assert.Equal("2026-06.zip", only.ArchiveName);
        HistoryEntry hit = Assert.Single(only.Entries);
        Assert.Equal("http://a/legacy-orders", hit.Request.Url);
    }

    [Fact]
    public async Task SearchArchives_多个包_按月份新到旧逐包推进()
    {
        await WriteZipArchiveAsync("2026-06", ("2026-06-05.jsonl", "http://a/june-hit"));
        await WriteZipArchiveAsync("2026-07", ("2026-07-02.jsonl", "http://a/july-hit"));

        List<HistorySearchBatch> batches = [];
        await foreach (HistorySearchBatch batch in CreateSearch().SearchArchivesAsync("hit"))
        {
            batches.Add(batch);
        }

        Assert.Equal(["2026-07.zip", "2026-06.zip"], batches.Select(b => b.ArchiveName).ToArray());
        Assert.All(batches, batch => Assert.Single(batch.Entries));
    }

    [Fact]
    public async Task SearchArchives_中途取消_停止处理后续包()
    {
        await WriteZipArchiveAsync("2026-06", ("2026-06-05.jsonl", "http://a/june-hit"));
        await WriteZipArchiveAsync("2026-07", ("2026-07-02.jsonl", "http://a/july-hit"));
        using var cts = new CancellationTokenSource();

        List<HistorySearchBatch> batches = [];
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (HistorySearchBatch batch in CreateSearch().SearchArchivesAsync("hit", cts.Token))
            {
                batches.Add(batch);
                await cts.CancelAsync();
            }
        });

        // 只处理了最新的一个包就停下
        Assert.Single(batches);
        Assert.Equal("2026-07.zip", batches[0].ArchiveName);
    }

    [Fact]
    public async Task SearchArchives_损坏的包_跳过不中断后续包()
    {
        string archiveDirectory = Path.Combine(_dataDirectory, "history", "archive");
        Directory.CreateDirectory(archiveDirectory);
        await File.WriteAllTextAsync(Path.Combine(archiveDirectory, "2026-07.zip"), "这不是 zip");
        await WriteZipArchiveAsync("2026-06", ("2026-06-05.jsonl", "http://a/june-hit"));

        List<HistorySearchBatch> batches = [];
        await foreach (HistorySearchBatch batch in CreateSearch().SearchArchivesAsync("hit"))
        {
            batches.Add(batch);
        }

        HistorySearchBatch only = Assert.Single(batches);
        Assert.Equal("2026-06.zip", only.ArchiveName);
    }

    [Fact]
    public async Task SearchArchives_7z包_解压临时目录搜索且结束后清理()
    {
        // 假 7z 桩内部用真 zip 模拟；先造 zip 再改名 .7z，使包内容可被桩解压
        string zipPath = await WriteZipArchiveAsync("2026-06", ("2026-06-05.jsonl", "http://a/seven-z-hit"));
        string sevenZipPath = Path.Combine(Path.GetDirectoryName(zipPath)!, "2026-06.7z");
        File.Move(zipPath, sevenZipPath);
        var runner = new FakeSevenZipRunner { ExecutablePath = "fake-7z.exe" };

        List<HistorySearchBatch> batches = [];
        await foreach (HistorySearchBatch batch in CreateSearch(runner).SearchArchivesAsync("seven-z-hit"))
        {
            batches.Add(batch);
        }

        HistorySearchBatch only = Assert.Single(batches);
        Assert.Equal("2026-06.7z", only.ArchiveName);
        Assert.Single(only.Entries);

        // e 子命令解压到临时目录；枚举结束后临时目录已清理（FR-HERMES-055）
        IReadOnlyList<string> extract = Assert.Single(runner.Invocations, args => args[0] == "e");
        Assert.StartsWith("-o", extract[2], StringComparison.Ordinal);
        string tempDirectory = Assert.Single(runner.ExtractedTempDirectories);
        Assert.False(Directory.Exists(tempDirectory));
    }

    [Fact]
    public async Task SearchArchives_有7z包但本机无7z_跳过该包()
    {
        string zipPath = await WriteZipArchiveAsync("2026-06", ("2026-06-05.jsonl", "http://a/hit"));
        File.Move(zipPath, Path.Combine(Path.GetDirectoryName(zipPath)!, "2026-06.7z"));
        var runner = new FakeSevenZipRunner { ExecutablePath = null };

        List<HistorySearchBatch> batches = [];
        await foreach (HistorySearchBatch batch in CreateSearch(runner).SearchArchivesAsync("hit"))
        {
            batches.Add(batch);
        }

        Assert.Empty(batches);
    }
}
