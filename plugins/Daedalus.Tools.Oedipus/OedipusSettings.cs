namespace Daedalus.Tools.Oedipus;

/// <summary>Oedipus 的用户设置（oedipus.md §6）：记住上次选择的解码方式。</summary>
/// <param name="Version">设置文件格式版本，当前为 1。</param>
/// <param name="LastDecoding">上次选择的解码方式 id；尚未选择过（首次启动）为 null。</param>
public sealed record OedipusSettings(int Version, string? LastDecoding)
{
    /// <summary>当前设置文件格式版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>首次启动或设置文件损坏恢复时使用的默认设置。</summary>
    public static OedipusSettings Default { get; } = new(CurrentVersion, null);
}
