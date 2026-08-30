using System.Text.Json;

namespace Daedalus.Tools.Iris;

/// <summary>设置读取结果。</summary>
/// <param name="Settings">读出的设置；文件不存在或损坏恢复时为默认值。</param>
/// <param name="RecoveredFromCorruption">true 表示原文件损坏、已按 DR-003 备份并以默认值启动。</param>
/// <param name="BackupFilePath">损坏文件的备份路径；未发生恢复时为 null。</param>
public sealed record IrisSettingsLoadResult(
    IrisSettings Settings,
    bool RecoveredFromCorruption,
    string? BackupFilePath);

/// <summary>
/// Iris 设置的持久化（iris.md §6）：数据目录下的 settings.json。
/// 读取时文件损坏（JSON 解析失败）按 DR-003 处理：原文件备份为
/// <c>settings.json.broken-时间戳</c>，以默认值启动并告知调用方提示用户。
/// </summary>
public sealed class IrisSettingsStore
{
    // 写出 camelCase 与 iris.md §6 的文件格式一致；读入容忍大小写差异（含手工编辑的文件）
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    public IrisSettingsStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "settings.json");
    }

    /// <summary>读取设置；文件不存在返回默认值（不视为损坏），损坏时备份原文件并返回默认值。</summary>
    public async Task<IrisSettingsLoadResult> LoadAsync()
    {
        if (!File.Exists(_filePath))
        {
            return new IrisSettingsLoadResult(IrisSettings.Default, false, null);
        }

        string json = await File.ReadAllTextAsync(_filePath).ConfigureAwait(false);
        IrisSettings? settings = TryDeserialize(json);
        if (settings is not null)
        {
            return new IrisSettingsLoadResult(settings, false, null);
        }

        string backupPath = _filePath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
        File.Move(_filePath, backupPath);
        return new IrisSettingsLoadResult(IrisSettings.Default, true, backupPath);
    }

    /// <summary>保存设置（覆盖写）。</summary>
    public async Task SaveAsync(IrisSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string json = JsonSerializer.Serialize(settings, SerializerOptions);
        await File.WriteAllTextAsync(_filePath, json).ConfigureAwait(false);
    }

    // 字段缺失容忍（反序列化后即为 null，由 ResolveInitialMethod / ToOptions 回落默认）；
    // 值本身无需校验——未知方式 id 与未知枚举值在解析时回落默认
    private static IrisSettings? TryDeserialize(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<IrisSettings>(json, SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
