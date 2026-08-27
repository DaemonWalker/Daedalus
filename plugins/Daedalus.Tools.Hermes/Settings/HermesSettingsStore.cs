using Daedalus.Tools.Hermes.Persistence;

using Serilog;

namespace Daedalus.Tools.Hermes.Settings;

/// <summary>设置读取结果。</summary>
/// <param name="Settings">读出的设置；文件不存在或损坏恢复时为默认值。</param>
/// <param name="RecoveredFromCorruption">true 表示原文件损坏、已按 DR-003 备份并以默认值启动。</param>
/// <param name="BackupFilePath">损坏文件的备份路径；未发生恢复时为 null。</param>
public sealed record HermesSettingsLoadResult(
    HermesSettings Settings,
    bool RecoveredFromCorruption,
    string? BackupFilePath);

/// <summary>
/// Hermes 设置的持久化（hermes.md §11.4）：数据目录下的 settings.json，修改即保存（FR-HERMES-061）。
/// 读取损坏（JSON 解析失败或上限值非法）按 DR-003 备份原文件并以默认值启动。
/// </summary>
public sealed class HermesSettingsStore
{
    private readonly string _filePath;
    private readonly ILogger? _logger;

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    /// <param name="logger">插件日志器；为 null 时不写日志（主要用于测试）。</param>
    public HermesSettingsStore(string dataDirectory, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "settings.json");
        _logger = logger;
    }

    /// <summary>读取设置；文件不存在返回默认值（不视为损坏），损坏时备份原文件并返回默认值。</summary>
    public async Task<HermesSettingsLoadResult> LoadAsync()
    {
        JsonDataFileLoadResult<HermesSettings> result =
            await JsonDataFile.LoadAsync<HermesSettings>(_filePath, static s => s.IsValid).ConfigureAwait(false);

        // Debug 记录加载来源（默认/文件/损坏恢复）：设置"改了不生效"类问题的排查入口
        if (result.BackupFilePath is not null)
        {
            _logger?.Debug("设置文件损坏，已备份到 {BackupPath}，以默认值启动", result.BackupFilePath);
            return new HermesSettingsLoadResult(HermesSettings.Default, true, result.BackupFilePath);
        }

        _logger?.Debug(result.Value is null ? "设置文件不存在，使用默认值" : "从 {FilePath} 加载设置", _filePath);
        return new HermesSettingsLoadResult(result.Value ?? HermesSettings.Default, false, null);
    }

    /// <summary>保存设置（覆盖写）。</summary>
    public async Task SaveAsync(HermesSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        await JsonDataFile.SaveAsync(_filePath, settings).ConfigureAwait(false);
        _logger?.Debug("设置已保存到 {FilePath}", _filePath);
    }
}
