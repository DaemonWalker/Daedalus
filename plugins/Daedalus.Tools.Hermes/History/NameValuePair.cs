namespace Daedalus.Tools.Hermes.History;

/// <summary>历史记录中的头键值对（hermes.md §11.3；历史只存生效后的快照，不含 enabled）。</summary>
/// <param name="Key">头名。</param>
/// <param name="Value">头值。</param>
public sealed record NameValuePair(string Key, string Value);
