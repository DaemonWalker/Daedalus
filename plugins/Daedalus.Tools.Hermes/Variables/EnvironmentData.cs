namespace Daedalus.Tools.Hermes.Variables;

/// <summary>environments.json 的整体数据（hermes.md §11.2）。</summary>
public sealed record EnvironmentData
{
    /// <summary>当前环境文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>文件格式版本（DR-004）。</summary>
    public int Version { get; init; } = CurrentVersion;

    /// <summary>当前启用环境的 id；null 表示未启用任何环境。</summary>
    public string? ActiveId { get; init; }

    /// <summary>全部环境。</summary>
    public List<HermesEnvironment> Environments { get; init; } = [];

    /// <summary>空数据（首次启动 / 损坏恢复时使用）。每次返回新实例，避免共享可变状态。</summary>
    public static EnvironmentData Empty => new();

    /// <summary>当前启用的环境；未启用或 ActiveId 指向不存在的环境时返回 null。</summary>
    public HermesEnvironment? FindActive() =>
        ActiveId is null ? null : Environments.FirstOrDefault(e => e.Id == ActiveId);
}
