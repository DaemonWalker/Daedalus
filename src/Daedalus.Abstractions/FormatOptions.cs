namespace Daedalus.Abstractions;

/// <summary>格式化选项。</summary>
/// <param name="Minify">true = 压缩，false = 美化。</param>
/// <param name="IndentSize">美化时的缩进宽度。</param>
public sealed record FormatOptions(bool Minify, int IndentSize);
