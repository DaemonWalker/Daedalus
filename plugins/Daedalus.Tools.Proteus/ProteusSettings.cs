namespace Daedalus.Tools.Proteus;

/// <summary>Proteus 的用户设置（proteus.md §6）：记住上次选择的格式与缩进宽度。</summary>
/// <param name="Version">设置文件格式版本，当前为 1。</param>
/// <param name="LastFormatId">上次选择的格式 id；尚未选择过（首次启动）为 null。</param>
/// <param name="IndentSize">美化时的缩进宽度，必须为正数。</param>
public sealed record ProteusSettings(int Version, string? LastFormatId, int IndentSize)
{
    /// <summary>当前设置文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>默认缩进宽度。</summary>
    public const int DefaultIndentSize = 4;

    /// <summary>首次启动或设置文件损坏恢复时使用的默认设置。</summary>
    public static ProteusSettings Default { get; } = new(CurrentVersion, null, DefaultIndentSize);
}
