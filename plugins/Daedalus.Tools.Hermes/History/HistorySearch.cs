using System.Globalization;
using System.IO.Compression;
using System.Runtime.CompilerServices;

using Serilog;
using Serilog.Core;

namespace Daedalus.Tools.Hermes.History;

/// <summary>一次搜索的结果。</summary>
/// <param name="Entries">命中记录，整体新→旧排序。</param>
/// <param name="SkippedLines">命中关键词但解析失败而无法展示的行数。</param>
public sealed record HistorySearchResult(IReadOnlyList<HistoryEntry> Entries, int SkippedLines);

/// <summary>"搜索更久"每处理完一个归档包产出的批次。</summary>
/// <param name="ArchiveName">归档包文件名（如 2026-06.zip）。</param>
/// <param name="Entries">本包命中记录，新→旧排序。</param>
/// <param name="SkippedLines">本包内命中但解析失败的行数。</param>
public sealed record HistorySearchBatch(string ArchiveName, IReadOnlyList<HistoryEntry> Entries, int SkippedLines);

/// <summary>
/// 历史分层搜索（hermes.md §10.3，FR-HERMES-054/055）：对记录行的原始 JSON 文本做
/// 不区分大小写的子串匹配（覆盖 URL/方法/头/体全部字段）。第一层直搜未压缩的 jsonl
/// （按日期新→旧）；"搜索更久"从最新到最旧逐包处理归档——zip 流式读取、7z 解压到
/// 临时目录后扫描，每包产出一个批次，取消即停，临时目录随清理。
/// </summary>
public sealed class HistorySearch
{
    private readonly string _historyDirectory;
    private readonly string _archiveDirectory;
    private readonly ISevenZipRunner _sevenZip;
    private readonly ILogger _logger;

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    /// <param name="logger">日志器；不传则不记日志。</param>
    public HistorySearch(string dataDirectory, ILogger? logger = null)
        : this(dataDirectory, new SevenZipRunner(), logger)
    {
    }

    // 测试注入：假 7z 桩（本机未必安装 7z）
    internal HistorySearch(string dataDirectory, ISevenZipRunner sevenZipRunner, ILogger? logger)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        ArgumentNullException.ThrowIfNull(sevenZipRunner);
        _historyDirectory = Path.Combine(dataDirectory, "history");
        _archiveDirectory = Path.Combine(_historyDirectory, "archive");
        _sevenZip = sevenZipRunner;
        _logger = logger ?? Logger.None;
    }

    /// <summary>是否存在归档包（决定"搜索更久"按钮是否出现）。</summary>
    public bool HasArchives() =>
        Directory.Exists(_archiveDirectory)
        && Directory.EnumerateFiles(_archiveDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Any(file => file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".7z", StringComparison.OrdinalIgnoreCase));

    /// <summary>第一层：直搜全部未压缩的 jsonl 文件（按文件名日期新→旧）。</summary>
    public async Task<HistorySearchResult> SearchRecentAsync(string keyword, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        var entries = new List<HistoryEntry>();
        int skipped = 0;
        foreach (string file in ListDayFilesNewestFirst())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
            (List<HistoryEntry> fileMatches, int fileSkipped) = await ScanStreamAsync(stream, keyword, cancellationToken).ConfigureAwait(false);
            entries.AddRange(fileMatches);
            skipped += fileSkipped;
        }

        return new HistorySearchResult(entries, skipped);
    }

    /// <summary>
    /// 第二层（"搜索更久"）：按月份新→旧逐个处理归档包，每包产出一个批次。
    /// zip 流式读取无需落盘；7z 解压到临时目录再扫描，每包处理完（或停止/失败时）清理临时目录。
    /// 调用方取消即停止推进。
    /// </summary>
    public async IAsyncEnumerable<HistorySearchBatch> SearchArchivesAsync(
        string keyword, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(keyword);

        string? sevenZipPath = _sevenZip.FindExecutable();
        foreach (string archivePath in ListArchivesNewestFirst())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string archiveName = Path.GetFileName(archivePath);
            HistorySearchBatch? batch;
            try
            {
                if (archiveName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    batch = await ScanZipAsync(archivePath, archiveName, keyword, cancellationToken).ConfigureAwait(false);
                }
                else if (sevenZipPath is not null)
                {
                    batch = await ScanSevenZipAsync(sevenZipPath, archivePath, archiveName, keyword, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    // 有 7z 包而本机无 7z：无法解压，跳过该包并记日志（不中断其余包）
                    _logger.Warning("本机未探测到 7z，跳过归档包 {ArchivePath}", archivePath);
                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
            {
                // 单包损坏/读取失败不中断其余包的推进（hermes.md §10.3 逐包处理语义）
                _logger.Warning(ex, "归档包 {ArchivePath} 搜索失败，已跳过", archivePath);
                continue;
            }

            yield return batch;
        }
    }

    // history/ 顶层 yyyy-MM-dd.jsonl，按日期新→旧（文件名无法解析的忽略）
    private List<string> ListDayFilesNewestFirst()
    {
        if (!Directory.Exists(_historyDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_historyDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
            .Where(file => DateOnly.TryParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    // history/archive/ 下 yyyy-MM.zip|.7z，按月份新→旧
    private List<string> ListArchivesNewestFirst()
    {
        if (!Directory.Exists(_archiveDirectory))
        {
            return [];
        }

        return Directory.EnumerateFiles(_archiveDirectory, "*.*", SearchOption.TopDirectoryOnly)
            .Where(file =>
            {
                string name = Path.GetFileNameWithoutExtension(file);
                bool supported = file.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)
                    || file.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
                return supported && DateOnly.TryParseExact(name + "-01", "yyyy-MM-dd",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
            })
            .OrderByDescending(Path.GetFileName, StringComparer.Ordinal)
            .ToList();
    }

    private async Task<HistorySearchBatch> ScanZipAsync(string archivePath, string archiveName, string keyword, CancellationToken cancellationToken)
    {
        var entries = new List<HistoryEntry>();
        int skipped = 0;
        using var archive = ZipFile.OpenRead(archivePath);
        // 包内为平铺的日文件名，按日期新→旧扫描
        foreach (ZipArchiveEntry entry in archive.Entries.OrderByDescending(e => e.Name, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using Stream stream = entry.Open();
            (List<HistoryEntry> fileMatches, int fileSkipped) = await ScanStreamAsync(stream, keyword, cancellationToken).ConfigureAwait(false);
            entries.AddRange(fileMatches);
            skipped += fileSkipped;
        }

        return new HistorySearchBatch(archiveName, entries, skipped);
    }

    private async Task<HistorySearchBatch> ScanSevenZipAsync(
        string sevenZipPath, string archivePath, string archiveName, string keyword, CancellationToken cancellationToken)
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "daedalus-hermes-search-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDirectory);
            await _sevenZip.RunAsync(sevenZipPath, ["e", "-y", "-o" + tempDirectory, archivePath], null, cancellationToken).ConfigureAwait(false);

            var entries = new List<HistoryEntry>();
            int skipped = 0;
            IEnumerable<string> files = Directory.EnumerateFiles(tempDirectory, "*.jsonl", SearchOption.TopDirectoryOnly)
                .OrderByDescending(Path.GetFileName, StringComparer.Ordinal);
            foreach (string file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using FileStream stream = new(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                (List<HistoryEntry> fileMatches, int fileSkipped) = await ScanStreamAsync(stream, keyword, cancellationToken).ConfigureAwait(false);
                entries.AddRange(fileMatches);
                skipped += fileSkipped;
            }

            return new HistorySearchBatch(archiveName, entries, skipped);
        }
        finally
        {
            // 结束、取消或失败都必须清理临时目录（FR-HERMES-055）
            TryDeleteTempDirectory(tempDirectory);
        }
    }

    // 逐行流式扫描：命中关键词的行尝试反序列化为记录；单文件内按追加顺序（旧→新），返回前反转为新→旧
    private static async Task<(List<HistoryEntry> Matches, int Skipped)> ScanStreamAsync(
        Stream stream, string keyword, CancellationToken cancellationToken)
    {
        var matches = new List<HistoryEntry>();
        int skipped = 0;
        using var reader = new StreamReader(stream);
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0 || !line.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            HistoryEntry? entry = HistoryStore.TryDeserializeLine(line);
            if (entry is null)
            {
                skipped++;
            }
            else
            {
                matches.Add(entry);
            }
        }

        matches.Reverse();
        return (matches, skipped);
    }

    private void TryDeleteTempDirectory(string tempDirectory)
    {
        try
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning(ex, "清理搜索临时目录 {TempDirectory} 失败", tempDirectory);
        }
    }
}
