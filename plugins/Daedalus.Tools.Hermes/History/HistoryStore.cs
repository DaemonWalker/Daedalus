using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daedalus.Tools.Hermes.History;

/// <summary>按天读取历史的结果。</summary>
/// <param name="Entries">成功解析的记录，按文件内顺序（即追加顺序）。</param>
/// <param name="SkippedLines">解析失败被跳过的行数（行级容错，见 hermes.md §10.1）。</param>
public sealed record HistoryDayReadResult(IReadOnlyList<HistoryEntry> Entries, int SkippedLines);

/// <summary>
/// 历史记录持久化（hermes.md §10.1/§11.3）：按天一个 JSON Lines 文件
/// history/yyyy-MM-dd.jsonl（以记录时间戳的本机时区自然日为准），异步追加写入，
/// 同一文件追加由信号量保护。响应体超出上限时截断并置 BodyTruncated（FR-HERMES-050）。
/// </summary>
public sealed class HistoryStore
{
    // jsonl 必须单行写出，不能用 JsonDataFile.Options（带缩进）；读入容忍大小写
    private static readonly JsonSerializerOptions LineOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly string _historyDirectory;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    public HistoryStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _historyDirectory = Path.Combine(dataDirectory, "history");
    }

    /// <summary>指定日期对应的历史文件路径。</summary>
    public string GetFilePath(DateOnly date) =>
        Path.Combine(_historyDirectory, date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".jsonl");

    /// <summary>
    /// 追加一条历史记录（写入记录时间戳对应的日文件）。
    /// 响应体 UTF-8 字节数超过 <paramref name="responseBodyLimitBytes"/> 时截断并置 BodyTruncated=true。
    /// </summary>
    public async Task AppendAsync(HistoryEntry entry, long responseBodyLimitBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(responseBodyLimitBytes, 0L);

        HistoryEntry effective = Truncate(entry, responseBodyLimitBytes);
        string line = JsonSerializer.Serialize(effective, LineOptions);

        Directory.CreateDirectory(_historyDirectory);
        string filePath = GetFilePath(DateOnly.FromDateTime(entry.Timestamp.LocalDateTime));

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var stream = new FileStream(filePath, FileMode.Append, FileAccess.Write, FileShare.Read);
            await using (stream.ConfigureAwait(false))
            {
                var writer = new StreamWriter(stream);
                await using (writer.ConfigureAwait(false))
                {
                    await writer.WriteLineAsync(line.AsMemory(), cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 读取指定日期的全部记录；文件不存在返回空结果。
    /// 单行损坏只跳过该行并计数，不做整文件备份（追加式日志整文件备份会丢历史，hermes.md §10.1）。
    /// </summary>
    public async Task<HistoryDayReadResult> ReadDayAsync(DateOnly date, CancellationToken cancellationToken = default)
    {
        string filePath = GetFilePath(date);
        if (!File.Exists(filePath))
        {
            return new HistoryDayReadResult([], 0);
        }

        var entries = new List<HistoryEntry>();
        int skipped = 0;
        foreach (string line in await File.ReadAllLinesAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            HistoryEntry? entry = TryDeserializeLine(line);
            if (entry is null)
            {
                skipped++;
            }
            else
            {
                entries.Add(entry);
            }
        }

        return new HistoryDayReadResult(entries, skipped);
    }

    private static HistoryEntry Truncate(HistoryEntry entry, long limitBytes)
    {
        string? body = entry.Response.Body;
        if (body is null || Encoding.UTF8.GetByteCount(body) <= limitBytes)
        {
            return entry;
        }

        return entry with
        {
            Response = entry.Response with
            {
                Body = TruncateToUtf8Bytes(body, limitBytes),
                BodyTruncated = true,
            },
        };
    }

    // 按 UTF-8 字节预算截断字符串，不切断代理项对（截出半个会产生替换字符）
    internal static string TruncateToUtf8Bytes(string text, long maxBytes)
    {
        long budget = maxBytes;
        int i = 0;
        while (i < text.Length && budget > 0)
        {
            int charCount = char.IsHighSurrogate(text[i]) && i + 1 < text.Length ? 2 : 1;
            int byteCount = charCount == 2
                ? Encoding.UTF8.GetByteCount(text, i, 2)
                : text[i] < 0x80 ? 1 : Encoding.UTF8.GetByteCount(text, i, 1);
            if (byteCount > budget)
            {
                break;
            }

            budget -= byteCount;
            i += charCount;
        }

        return text[..i];
    }

    // internal：HistorySearch 复用同一反序列化口径（行级容错）
    internal static HistoryEntry? TryDeserializeLine(string line)
    {
        try
        {
            return JsonSerializer.Deserialize<HistoryEntry>(line, LineOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
