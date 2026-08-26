namespace Daedalus.Tools.Hermes.Collections;

/// <summary>请求头 / 表单字段等使用的键值项（hermes.md §11.1）。</summary>
/// <param name="Key">键。</param>
/// <param name="Value">值。</param>
/// <param name="Enabled">false 表示发送时该项不生效（界面上可临时停用）。</param>
public sealed record KeyValueEntry(string Key, string Value, bool Enabled = true);
