namespace Daedalus.Abstractions;

/// <summary>格式化器插件：无界面的格式化能力，被 Proteus 和 Hermes 等工具复用。</summary>
public interface IFormatter
{
    /// <summary>格式 id，如 "json"、"xml"，全小写。</summary>
    string FormatId { get; }

    /// <summary>显示名，如 "JSON"。</summary>
    string DisplayName { get; }

    /// <summary>校验输入是否合法；失败时 <paramref name="error"/> 包含尽可能准确的行列信息。</summary>
    bool TryValidate(string input, out string? error);

    /// <summary>格式化。输入非法时抛出 <see cref="FormatException"/>。</summary>
    /// <exception cref="FormatException">输入不是合法的对应格式。</exception>
    string Format(string input, FormatOptions options);
}
