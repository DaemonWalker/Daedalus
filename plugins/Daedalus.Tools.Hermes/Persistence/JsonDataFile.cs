using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Daedalus.Tools.Hermes.Persistence;

/// <summary>JSON 数据文件读取结果。</summary>
/// <typeparam name="T">数据类型。</typeparam>
/// <param name="Value">读出的数据；文件不存在或损坏恢复时为 null。</param>
/// <param name="BackupFilePath">损坏文件的备份路径（DR-003）；未发生恢复时为 null。</param>
internal sealed record JsonDataFileLoadResult<T>(T? Value, string? BackupFilePath) where T : class;

/// <summary>
/// Hermes 各 JSON 数据文件的公共读写助手：统一序列化选项（camelCase、读入容忍大小写、
/// 枚举写小写字符串），读取损坏时按 DR-003 备份原文件（追加 .broken-时间戳 后缀）。
/// </summary>
internal static class JsonDataFile
{
    // 写出 camelCase 与 hermes.md §11 的文件格式一致；读入容忍大小写差异（含手工编辑的文件）
    internal static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// 读取并反序列化 JSON 数据文件。文件不存在返回 Value=null 且不视为损坏；
    /// 解析失败或 <paramref name="isValid"/> 判定非法时备份原文件并返回 BackupFilePath。
    /// </summary>
    internal static async Task<JsonDataFileLoadResult<T>> LoadAsync<T>(string filePath, Func<T, bool>? isValid = null)
        where T : class
    {
        if (!File.Exists(filePath))
        {
            return new JsonDataFileLoadResult<T>(null, null);
        }

        string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);
        T? value = TryDeserialize<T>(json);
        if (value is not null && (isValid is null || isValid(value)))
        {
            return new JsonDataFileLoadResult<T>(value, null);
        }

        string backupPath = filePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        File.Move(filePath, backupPath);
        return new JsonDataFileLoadResult<T>(null, backupPath);
    }

    /// <summary>保存数据文件（覆盖写），所在目录不存在时自动创建。</summary>
    internal static async Task SaveAsync<T>(string filePath, T value)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string json = JsonSerializer.Serialize(value, Options);
        await File.WriteAllTextAsync(filePath, json).ConfigureAwait(false);
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
