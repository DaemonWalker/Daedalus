using Daedalus.Tools.Hermes.Persistence;

namespace Daedalus.Tools.Hermes.Variables;

/// <summary>环境读取结果。</summary>
/// <param name="Data">读出的环境数据；文件不存在或损坏恢复时为空数据。</param>
/// <param name="RecoveredFromCorruption">true 表示原文件损坏、已按 DR-003 备份并以空数据启动。</param>
/// <param name="BackupFilePath">损坏文件的备份路径；未发生恢复时为 null。</param>
public sealed record EnvironmentLoadResult(
    EnvironmentData Data,
    bool RecoveredFromCorruption,
    string? BackupFilePath);

/// <summary>
/// 环境持久化（hermes.md §11.2）：数据目录下的 environments.json。
/// 读取损坏时按 DR-003 备份原文件并以空数据启动。
/// Set/Unset 变量立即持久化，供悬浮编辑与后事件脚本共用（FR-HERMES-024/044）。
/// </summary>
public sealed class EnvironmentStore
{
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <param name="dataDirectory">工具数据目录（由 <c>IToolHost.GetDataDirectory</c> 分配）。</param>
    public EnvironmentStore(string dataDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dataDirectory);
        _filePath = Path.Combine(dataDirectory, "environments.json");
    }

    /// <summary>读取环境数据；文件不存在返回空数据（不视为损坏），损坏时备份原文件并返回空数据。</summary>
    public async Task<EnvironmentLoadResult> LoadAsync()
    {
        JsonDataFileLoadResult<EnvironmentData> result = await JsonDataFile.LoadAsync<EnvironmentData>(_filePath).ConfigureAwait(false);
        if (result.BackupFilePath is not null)
        {
            return new EnvironmentLoadResult(EnvironmentData.Empty, true, result.BackupFilePath);
        }

        return new EnvironmentLoadResult(result.Value ?? EnvironmentData.Empty, false, null);
    }

    /// <summary>保存环境数据（覆盖写）。</summary>
    public async Task SaveAsync(EnvironmentData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        await JsonDataFile.SaveAsync(_filePath, data).ConfigureAwait(false);
    }

    /// <summary>
    /// 写入变量并立即持久化（hermes.md §5.1/§6.1）。变量已存在时只更新值，
    /// 保留原有 secret/enabled 标记；返回持久化后的完整环境数据。
    /// </summary>
    /// <exception cref="InvalidOperationException">指定的环境不存在。</exception>
    public async Task<EnvironmentData> SetVariableAsync(string environmentId, string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(value);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnvironmentData data = (await LoadAsync().ConfigureAwait(false)).Data;
            HermesEnvironment environment = data.Environments.FirstOrDefault(e => e.Id == environmentId)
                ?? throw new InvalidOperationException($"环境不存在：{environmentId}");
            int index = environment.Variables.FindIndex(v => v.Key == key);
            if (index >= 0)
            {
                environment.Variables[index] = environment.Variables[index] with { Value = value };
            }
            else
            {
                environment.Variables.Add(new EnvironmentVariable(key, value));
            }

            await SaveAsync(data).ConfigureAwait(false);
            return data;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>删除变量并立即持久化；变量不存在时不报错。返回持久化后的完整环境数据。</summary>
    /// <exception cref="InvalidOperationException">指定的环境不存在。</exception>
    public async Task<EnvironmentData> UnsetVariableAsync(string environmentId, string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(environmentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            EnvironmentData data = (await LoadAsync().ConfigureAwait(false)).Data;
            HermesEnvironment environment = data.Environments.FirstOrDefault(e => e.Id == environmentId)
                ?? throw new InvalidOperationException($"环境不存在：{environmentId}");
            environment.Variables.RemoveAll(v => v.Key == key);
            await SaveAsync(data).ConfigureAwait(false);
            return data;
        }
        finally
        {
            _gate.Release();
        }
    }
}
