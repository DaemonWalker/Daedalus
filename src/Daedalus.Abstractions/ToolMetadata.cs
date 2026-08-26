namespace Daedalus.Abstractions;

/// <summary>工具元数据。</summary>
/// <param name="Id">工具 id，形如 "daedalus.tools.hermes"，全小写。</param>
/// <param name="DisplayName">界面显示名。</param>
/// <param name="Description">工具描述。</param>
/// <param name="Version">工具版本。</param>
public sealed record ToolMetadata(string Id, string DisplayName, string Description, Version Version);
