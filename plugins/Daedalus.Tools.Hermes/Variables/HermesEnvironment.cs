namespace Daedalus.Tools.Hermes.Variables;

/// <summary>一套环境（hermes.md §11.2）：若干键值对，同一时间仅启用一套（FR-HERMES-020）。</summary>
public sealed record HermesEnvironment
{
    /// <summary>环境 id。</summary>
    public required string Id { get; init; }

    /// <summary>环境显示名。</summary>
    public required string Name { get; init; }

    /// <summary>变量清单。setter 供环境管理窗口整表替换（step8），持久化序列化不受影响。</summary>
    public List<EnvironmentVariable> Variables { get; set; } = [];
}
