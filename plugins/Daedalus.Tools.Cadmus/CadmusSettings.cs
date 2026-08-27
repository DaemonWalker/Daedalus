namespace Daedalus.Tools.Cadmus;

/// <summary>Cadmus 的用户设置（cadmus.md §6）：记住上次选择的编码方式。</summary>
/// <param name="Version">设置文件格式版本，当前为 1。</param>
/// <param name="LastEncoding">上次选择的编码方式 id；尚未选择过（首次启动）为 null。</param>
public sealed record CadmusSettings(int Version, string? LastEncoding)
{
    /// <summary>当前设置文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>首次启动或设置文件损坏恢复时使用的默认设置。</summary>
    public static CadmusSettings Default { get; } = new(CurrentVersion, null);
}
