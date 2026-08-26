using System.IO.Compression;

using Daedalus.Tools.Hermes.History;

namespace Daedalus.Tools.Hermes.Tests;

/// <summary>
/// HistoryArchive 测试（hermes.md §10.2/§12）：按月打包、30 天边界、7z 存在/缺失两条路径
/// （7z 用假命令桩）、压缩成功后删除原文件、失败保留、已存在包跳过。
/// </summary>
public sealed class HistoryArchiveTests : IDisposable
{
    // 固定时钟：2026-08-26 本机正午 → 归档截止日为 2026-07-27（含）
    private static readonly DateTimeOffset FixedNow = LocalNoon(2026, 8, 26);

    private readonly string _dataDirectory = Path.Combine(Path.GetTempPath(), "hermes-archive-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dataDirectory))
        {
            Directory.Delete(_dataDirectory, recursive: true);
        }
    }

    // 本机正午构造时间戳：避免时区换算把文件名日期顶到前一天/后一天
    private static DateTimeOffset LocalNoon(int year, int month, int day) =>
        new(new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Local));

    private HistoryArchive CreateArchive(FakeSevenZipRunner runner) =>
        new(_dataDirectory, runner, () => FixedNow, logger: null);

    private async Task<string> WriteDayFileAsync(DateTimeOffset timestamp, string url)
    {
        var store = new HistoryStore(_dataDirectory);
        var entry = new HistoryEntry
        {
            Id = IdGenerator.NewId(),
            Timestamp = timestamp,
            Request = new HistoryRequest("GET", url, [], null),
            Response = new HistoryResponse(200, 1, [], "ok", false),
        };
        await store.AppendAsync(entry, Hermes.Settings.HermesSettings.DefaultResponseBodyLimitBytes);
        return store.GetFilePath(DateOnly.FromDateTime(timestamp.LocalDateTime));
    }

    [Fact]
    public async Task ArchiveOldFiles_无超期文件_返回空结果且不创建归档目录()
    {
        await WriteDayFileAsync(LocalNoon(2026, 8, 25), "http://a/recent");
        var archive = CreateArchive(new FakeSevenZipRunner());

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Empty(result.ArchivedMonths);
        Assert.Empty(result.SkippedMonths);
        Assert.Empty(result.FailedMonths);
        Assert.Null(result.Compressor);
        Assert.False(Directory.Exists(archive.ArchiveDirectory));
    }

    [Fact]
    public async Task ArchiveOldFiles_无7z回退zip_按月打包且成功后删除原文件()
    {
        string june5 = await WriteDayFileAsync(LocalNoon(2026, 6, 5), "http://a/0605");
        string june20 = await WriteDayFileAsync(LocalNoon(2026, 6, 20), "http://a/0620");
        string july2 = await WriteDayFileAsync(LocalNoon(2026, 7, 2), "http://a/0702");
        var archive = CreateArchive(new FakeSevenZipRunner { ExecutablePath = null });

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Equal("zip", result.Compressor);
        Assert.Equal(["2026-06", "2026-07"], result.ArchivedMonths);
        Assert.False(File.Exists(june5));
        Assert.False(File.Exists(june20));
        Assert.False(File.Exists(july2));

        // 包内为当月各日 jsonl 平铺文件，内容可读
        string juneArchive = Path.Combine(archive.ArchiveDirectory, "2026-06.zip");
        using (var zip = ZipFile.OpenRead(juneArchive))
        {
            Assert.Equal(["2026-06-05.jsonl", "2026-06-20.jsonl"],
                zip.Entries.Select(e => e.Name).Order(StringComparer.Ordinal).ToArray());
            using var reader = new StreamReader(zip.GetEntry("2026-06-05.jsonl")!.Open());
            Assert.Contains("http://a/0605", await reader.ReadToEndAsync());
        }

        Assert.True(File.Exists(Path.Combine(archive.ArchiveDirectory, "2026-07.zip")));
    }

    [Fact]
    public async Task ArchiveOldFiles_边界_恰好30天前的文件归档_29天的保留()
    {
        string cutoff = await WriteDayFileAsync(LocalNoon(2026, 7, 27), "http://a/cutoff");
        string inside = await WriteDayFileAsync(LocalNoon(2026, 7, 28), "http://a/inside");
        var archive = CreateArchive(new FakeSevenZipRunner());

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Equal(["2026-07"], result.ArchivedMonths);
        Assert.False(File.Exists(cutoff));
        Assert.True(File.Exists(inside));
    }

    [Fact]
    public async Task ArchiveOldFiles_文件名无法解析为日期_忽略不归档()
    {
        string historyDirectory = Path.Combine(_dataDirectory, "history");
        Directory.CreateDirectory(historyDirectory);
        string stray = Path.Combine(historyDirectory, "notes.jsonl");
        await File.WriteAllTextAsync(stray, "{}");

        HistoryArchiveResult result = await CreateArchive(new FakeSevenZipRunner()).ArchiveOldFilesAsync();

        Assert.Empty(result.ArchivedMonths);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public async Task ArchiveOldFiles_探测到7z_调用a带mx9与裸文件名并校验后删原文件()
    {
        string june5 = await WriteDayFileAsync(LocalNoon(2026, 6, 5), "http://a/0605");
        var runner = new FakeSevenZipRunner { ExecutablePath = "fake-7z.exe" };
        var archive = CreateArchive(runner);

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Equal("7z", result.Compressor);
        Assert.Equal(["2026-06"], result.ArchivedMonths);
        string archivePath = Path.Combine(archive.ArchiveDirectory, "2026-06.7z");
        Assert.True(File.Exists(archivePath));
        Assert.False(File.Exists(june5));

        // a 子命令：-mx=9 最大压缩，传裸文件名（工作目录为 history/）
        IReadOnlyList<string> compress = Assert.Single(runner.Invocations, args => args[0] == "a");
        Assert.Equal("-mx=9", compress[1]);
        Assert.Equal(archivePath, compress[2]);
        Assert.Equal("2026-06-05.jsonl", Assert.Single(compress.Skip(3)));
        // t 子命令：压缩后校验归档可读
        IReadOnlyList<string> verify = Assert.Single(runner.Invocations, args => args[0] == "t");
        Assert.Equal(archivePath, verify[1]);
    }

    [Fact]
    public async Task ArchiveOldFiles_7z压缩失败_原文件保留且半成品包被清理()
    {
        string june5 = await WriteDayFileAsync(LocalNoon(2026, 6, 5), "http://a/0605");
        var runner = new FakeSevenZipRunner { ExecutablePath = "fake-7z.exe", FailOnCompress = _ => true };
        var archive = CreateArchive(runner);

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Empty(result.ArchivedMonths);
        Assert.Equal(["2026-06"], result.FailedMonths);
        Assert.True(File.Exists(june5));
        Assert.False(File.Exists(Path.Combine(archive.ArchiveDirectory, "2026-06.7z")));
    }

    [Fact]
    public async Task ArchiveOldFiles_7z校验失败_原文件保留()
    {
        string june5 = await WriteDayFileAsync(LocalNoon(2026, 6, 5), "http://a/0605");
        var runner = new FakeSevenZipRunner { ExecutablePath = "fake-7z.exe", FailOnTest = _ => true };
        var archive = CreateArchive(runner);

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Empty(result.ArchivedMonths);
        Assert.Equal(["2026-06"], result.FailedMonths);
        Assert.True(File.Exists(june5));
    }

    [Fact]
    public async Task ArchiveOldFiles_单月压缩失败_不影响其余月份()
    {
        string june5 = await WriteDayFileAsync(LocalNoon(2026, 6, 5), "http://a/0605");
        string july2 = await WriteDayFileAsync(LocalNoon(2026, 7, 2), "http://a/0702");
        var runner = new FakeSevenZipRunner
        {
            ExecutablePath = "fake-7z.exe",
            FailOnCompress = path => path.Contains("2026-06", StringComparison.Ordinal),
        };
        var archive = CreateArchive(runner);

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Equal(["2026-07"], result.ArchivedMonths);
        Assert.Equal(["2026-06"], result.FailedMonths);
        Assert.True(File.Exists(june5));
        Assert.False(File.Exists(july2));
    }

    [Fact]
    public async Task ArchiveOldFiles_归档包已存在_跳过该月并保留原文件()
    {
        string june5 = await WriteDayFileAsync(LocalNoon(2026, 6, 5), "http://a/0605");
        var archive = CreateArchive(new FakeSevenZipRunner());
        Directory.CreateDirectory(archive.ArchiveDirectory);
        await File.WriteAllTextAsync(Path.Combine(archive.ArchiveDirectory, "2026-06.zip"), "既有归档包");

        HistoryArchiveResult result = await archive.ArchiveOldFilesAsync();

        Assert.Empty(result.ArchivedMonths);
        Assert.Equal(["2026-06"], result.SkippedMonths);
        Assert.True(File.Exists(june5));
    }
}
