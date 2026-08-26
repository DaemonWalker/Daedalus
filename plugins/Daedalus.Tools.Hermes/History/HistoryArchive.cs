using System.Globalization;
using System.IO.Compression;

using Serilog;
using Serilog.Core;

namespace Daedalus.Tools.Hermes.History;

/// <summary>一次归档检查的结果。</summary>
/// <param name="ArchivedMonths">成功归档的月份（yyyy-MM）。</param>
/// <param name="Compressor">实际使用的压缩器（"7z" / "zip"）；无可归档文件时为 null。</param>
/// <param name="SkippedMonths">归档包已存在而跳过的月份（原文件保留，等待人工处理）。</param>
/// <param name="FailedMonths">归档失败的月份（原文件保留，详见日志）。</param>
public sealed record HistoryArchiveResult(
    IReadOnlyList<string> ArchivedMonths,
    string? Compressor,
    IReadOnlyList<string> SkippedMonths,
    IReadOnlyList<string> FailedMonths)
{
    /// <summary>无可归档文件时的空结果。</summary>
    public static HistoryArchiveResult Empty { get; } = new([], null, [], []);
}

/// <summary>
/// 历史归档（hermes.md §10.2，FR-HERMES-053）：30 天前（含第 30 天）的日文件按月打包到
/// history/archive/yyyy-MM.7z|zip——PATH 探测到 7z 时用 <c>7z a -mx=9</c> 压缩、<c>7z t</c> 校验，
/// 否则用内置 System.IO.Compression 写 zip（SmallestSize）并回读校验条目。
/// 校验通过后才删除原 jsonl 文件；任何一步失败保留原文件并记日志。
/// </summary>
public sealed class HistoryArchive
{
    /// <summary>归档阈值天数：文件名日期早于等于"今天 - 30 天"的日文件纳入归档。</summary>
    public const int ArchiveAfterDays = 30;

    private readonly string _historyDirectory;
    private readonly string _archiveDirectory;
    private readonly ISevenZipRunner _sevenZip;
    private readonly Func<DateTimeOffset> _nowProvider;
    private readonly ILogger _logger;

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    /// <param name="logger">日志器；不传则不记日志。</param>
    public HistoryArchive(string dataDirectory, ILogger? logger = null)
        : this(dataDirectory, new SevenZipRunner(), null, logger)
    {
    }

    // 测试注入：假 7z 桩与固定时钟（构造跨月样本）
    internal HistoryArchive(string dataDirectory, ISevenZipRunner sevenZipRunner, Func<DateTimeOffset>? nowProvider, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(sevenZipRunner);
        _historyDirectory = Path.Combine(dataDirectory, "history");
        _archiveDirectory = Path.Combine(_historyDirectory, "archive");
        _sevenZip = sevenZipRunner;
        _nowProvider = nowProvider ?? (() => DateTimeOffset.Now);
        _logger = logger ?? Logger.None;
    }

    /// <summary>归档目录路径（history/archive/）。</summary>
    public string ArchiveDirectory => _archiveDirectory;

    /// <summary>
    /// 检查并归档超期日文件。逐月独立：单月失败不影响其余月份。
    /// 归档包已存在的月份跳过（保留原文件），不合并进已有包。
    /// </summary>
    public async Task<HistoryArchiveResult> ArchiveOldFilesAsync(CancellationToken cancellationToken = default)
    {
        Dictionary<string, List<string>> byMonth = CollectArchivableFiles();
        if (byMonth.Count == 0)
        {
            return HistoryArchiveResult.Empty;
        }

        string? sevenZipPath = _sevenZip.FindExecutable();
        string extension = sevenZipPath is not null ? ".7z" : ".zip";

        var archived = new List<string>();
        var skipped = new List<string>();
        var failed = new List<string>();
        Directory.CreateDirectory(_archiveDirectory);

        foreach (KeyValuePair<string, List<string>> month in byMonth.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string archivePath = Path.Combine(_archiveDirectory, month.Key + extension);
            if (File.Exists(archivePath))
            {
                // 不与已有包合并（7z/zip 追加语义不一致，且说明历史曾被部分归档）：保留原文件等人工处理
                _logger.Warning("归档包 {ArchivePath} 已存在，跳过 {Month} 的 {Count} 个日文件", archivePath, month.Key, month.Value.Count);
                skipped.Add(month.Key);
                continue;
            }

            try
            {
                if (sevenZipPath is not null)
                {
                    await CompressWithSevenZipAsync(sevenZipPath, archivePath, month.Value, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await CompressWithZipAsync(archivePath, month.Value, cancellationToken).ConfigureAwait(false);
                }

                // 校验通过才删除原文件（hermes.md §10.2 原子性要求）
                foreach (string file in month.Value)
                {
                    File.Delete(file);
                }

                archived.Add(month.Key);
                _logger.Information("历史归档完成：{Month}（{Count} 个日文件 → {ArchivePath}）", month.Key, month.Value.Count, archivePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                _logger.Error(ex, "历史归档失败：{Month}，原文件已保留", month.Key);
                failed.Add(month.Key);
                TryDeletePartialArchive(archivePath);
            }
        }

        return new HistoryArchiveResult(archived, sevenZipPath is not null ? "7z" : "zip", skipped, failed);
    }

    // 收集超期日文件并按月份分组；文件名不是 yyyy-MM-dd.jsonl 的忽略（含 archive/ 子目录不在枚举范围）
    private Dictionary<string, List<string>> CollectArchivableFiles()
    {
        var byMonth = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        if (!Directory.Exists(_historyDirectory))
        {
            return byMonth;
        }

        DateOnly cutoff = DateOnly.FromDateTime(_nowProvider().LocalDateTime).AddDays(-ArchiveAfterDays);
        foreach (string file in Directory.EnumerateFiles(_historyDirectory, "*.jsonl", SearchOption.TopDirectoryOnly))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (!DateOnly.TryParseExact(name, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date)
                || date > cutoff)
            {
                continue;
            }

            string month = name[..7];
            if (!byMonth.TryGetValue(month, out List<string>? files))
            {
                files = [];
                byMonth[month] = files;
            }

            files.Add(file);
        }

        foreach (List<string> files in byMonth.Values)
        {
            files.Sort(StringComparer.Ordinal);
        }

        return byMonth;
    }

    private async Task CompressWithSevenZipAsync(string sevenZipPath, string archivePath, List<string> files, CancellationToken cancellationToken)
    {
        // 工作目录设为 history/ 并传裸文件名，包内保持平铺的日文件名（不带目录层级）
        var arguments = new List<string> { "a", "-mx=9", archivePath };
        arguments.AddRange(files.Select(Path.GetFileName)!);
        await _sevenZip.RunAsync(sevenZipPath, arguments, _historyDirectory, cancellationToken).ConfigureAwait(false);

        // 校验归档可读（7z t 非零退出码视为损坏）
        await _sevenZip.RunAsync(sevenZipPath, ["t", archivePath], null, cancellationToken).ConfigureAwait(false);
    }

    private async Task CompressWithZipAsync(string archivePath, List<string> files, CancellationToken cancellationToken)
    {
        await using (FileStream stream = new(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                archive.CreateEntryFromFile(file, Path.GetFileName(file), CompressionLevel.SmallestSize);
            }
        }

        // 回读校验：条目可枚举且与预期文件名一一对应
        using (var verify = ZipFile.OpenRead(archivePath))
        {
            string[] actual = [.. verify.Entries.Select(entry => entry.Name).Order(StringComparer.Ordinal)];
            string[] expected = [.. files.Select(Path.GetFileName).Order(StringComparer.Ordinal)!];
            if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            {
                throw new InvalidDataException($"归档校验失败：{archivePath} 条目与预期不一致");
            }
        }
    }

    private void TryDeletePartialArchive(string archivePath)
    {
        try
        {
            if (File.Exists(archivePath))
            {
                File.Delete(archivePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 残留半成品包会让下轮检查误判"已存在"而永久跳过，必须让人看到
            _logger.Warning(ex, "清理失败的归档半成品 {ArchivePath} 失败，请手工删除", archivePath);
        }
    }
}
